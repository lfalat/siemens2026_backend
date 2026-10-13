using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text.Json.Nodes;
using ApiService.OpenAPI;
using Kernel.CQRS;
using Kernel.Errors;
using Kernel.Idempotency;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Tests.ApiService.ErrorCodes;

/// <summary>
/// Tests that <see cref="ErrorCodeTransformer"/> lists each operation's error codes under the response they are
/// answered with, and that <see cref="ErrorCodeCatalogTransformer"/> publishes them as enums a client can generate its
/// translation keys from (ADR-A017).
/// </summary>
public class ErrorCodeTransformerTests
{
    private static readonly ErrorCode OfferClosed = new(StatusCodes.Status409Conflict, "job_offer.closed", "The offer is closed.");

    private static readonly FieldErrorCode TitleTaken = new("job_offer.title_taken", "The title is taken.");

    private sealed class SampleCommand : ICommand
    {
        public static string EventName => "sample";

        [Required]
        public string? Name { get; init; }
    }

    /// <summary>Stands in for an endpoint delegate: what the transformer reads the dispatched command from.</summary>
    private static Task Handler(ICommandHandler<SampleCommand> handler) => Task.CompletedTask;

    private static MethodInfo HandlerMethod =>
        typeof(ErrorCodeTransformerTests).GetMethod(nameof(Handler), BindingFlags.NonPublic | BindingFlags.Static)!;

    private static async Task<OpenApiOperation> TransformAsync(
        string method, OpenApiOperation? operation = null, params object[] endpointMetadata)
    {
        operation ??= new OpenApiOperation();
        ErrorCodeTransformer transformer = new(Options.Create(new IdempotencyOptions()));

        await transformer.TransformAsync(operation, new OpenApiOperationTransformerContext
        {
            DocumentName = "v1",
            Description = new ApiDescription
            {
                HttpMethod = method,
                ActionDescriptor = new ActionDescriptor { EndpointMetadata = [.. endpointMetadata] },
            },
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        }, TestContext.Current.CancellationToken);

        return operation;
    }

    private static string[] Codes(OpenApiOperation operation, string status, string extension) =>
        operation.Responses![status] is OpenApiResponse { Extensions: { } extensions }
        && extensions.TryGetValue(extension, out IOpenApiExtension? value)
            ? [.. ((JsonNodeExtension)value).Node.AsArray().Select(static n => n!.GetValue<string>())]
            : [];

    [Fact]
    public async Task A_declared_code_is_listed_under_its_status_with_its_meaning()
    {
        OpenApiOperation operation = await TransformAsync(
            "GET", null, new ErrorCodeDeclaration([OfferClosed, CommonErrors.NotFound]));

        Assert.Equal(["job_offer.closed"], Codes(operation, "409", ErrorCodeTransformer.CodesExtension));
        Assert.Equal(["request.not_found"], Codes(operation, "404", ErrorCodeTransformer.CodesExtension));
        Assert.Contains("| `job_offer.closed` | The offer is closed. |", operation.Responses!["409"].Description);
    }

    [Fact]
    public async Task Field_codes_go_under_the_400_apart_from_the_problem_codes()
    {
        OpenApiOperation operation = await TransformAsync(
            "GET", null, new ErrorCodeDeclaration([TitleTaken, CommonErrors.Malformed]));

        Assert.Equal(["job_offer.title_taken"], Codes(operation, "400", ErrorCodeTransformer.FieldCodesExtension));
        Assert.Equal(
            ["request.malformed", "request.validation_failed"],
            Codes(operation, "400", ErrorCodeTransformer.CodesExtension));
    }

    [Fact]
    public async Task The_dispatched_commands_rule_codes_are_listed()
    {
        // The validation decorator answers them with errorCodes, so the document lists them under the 400.
        OpenApiOperation operation = await TransformAsync("GET", null, HandlerMethod);

        Assert.Equal(["required"], Codes(operation, "400", ErrorCodeTransformer.FieldCodesExtension));
        Assert.Equal(["request.validation_failed"], Codes(operation, "400", ErrorCodeTransformer.CodesExtension));
    }

    [Fact]
    public async Task A_write_answers_concurrency_conflict_and_the_idempotency_rejections()
    {
        OpenApiOperation operation = await TransformAsync("POST");

        Assert.Equal(
            ["concurrency.conflict", "idempotency.in_progress"],
            Codes(operation, "409", ErrorCodeTransformer.CodesExtension));
        Assert.Equal(["idempotency.payload_mismatch"], Codes(operation, "422", ErrorCodeTransformer.CodesExtension));
        Assert.Equal(
            ["idempotency.key_missing", "idempotency.key_too_long"],
            Codes(operation, "400", ErrorCodeTransformer.CodesExtension));
    }

    [Fact]
    public async Task A_query_exposed_as_post_answers_neither()
    {
        OpenApiOperation operation = await TransformAsync(
            "POST", null, ReadOnlyEndpoint.Instance, IdempotencyDisabled.Instance);

        Assert.False(operation.Responses?.ContainsKey("409") ?? false);
    }

    [Fact]
    public async Task An_authorization_policy_adds_the_forbidden_code()
    {
        OpenApiOperation operation = await TransformAsync("GET", null, new AuthorizeAttribute("Admin"));

        Assert.Equal(["auth.forbidden"], Codes(operation, "403", ErrorCodeTransformer.CodesExtension));
    }

    [Fact]
    public async Task An_endpoints_own_response_description_is_kept()
    {
        OpenApiOperation operation = new()
        {
            Responses = new OpenApiResponses { ["409"] = new OpenApiResponse { Description = "Conflict" } },
        };

        await TransformAsync("GET", operation, new ErrorCodeDeclaration([OfferClosed]));

        Assert.StartsWith("Conflict", operation.Responses["409"].Description);
    }

    [Fact]
    public async Task The_catalog_enumerates_every_common_code_and_points_problems_at_it()
    {
        OpenApiDocument document = new()
        {
            Components = new OpenApiComponents
            {
                Schemas = new Dictionary<string, IOpenApiSchema>
                {
                    ["ProblemDetails"] = new OpenApiSchema { Properties = new Dictionary<string, IOpenApiSchema>() },
                    ["HttpValidationProblemDetails"] = new OpenApiSchema
                    {
                        Properties = new Dictionary<string, IOpenApiSchema>(),
                    },
                },
            },
        };
        ErrorCodeCatalogTransformer transformer = new(
            new DefaultEndpointDataSource(), Options.Create(new IdempotencyOptions()));

        await transformer.TransformAsync(document, null!, TestContext.Current.CancellationToken);

        OpenApiSchema codes = (OpenApiSchema)document.Components.Schemas[ErrorCodeCatalogTransformer.ProblemSchema];
        Assert.Contains(codes.Enum!, static n => n.GetValue<string>() == "request.rate_limited");
        OpenApiSchema fields = (OpenApiSchema)document.Components.Schemas[ErrorCodeCatalogTransformer.FieldSchema];
        Assert.Contains(fields.Enum!, static n => n.GetValue<string>() == "required");

        Assert.IsType<OpenApiSchemaReference>(
            ((OpenApiSchema)document.Components.Schemas["ProblemDetails"]).Properties!["code"]);
        Assert.Contains("errorCodes",
            ((OpenApiSchema)document.Components.Schemas["HttpValidationProblemDetails"]).Properties!.Keys);
    }

    [Fact]
    public async Task The_generated_document_lists_a_groups_codes_and_a_writes_rule_codes()
    {
        // Closes the loop the fakes above leave open: declarations made the way Program.cs makes them — on a group,
        // and by rule for a write — reach the document a client generates from.
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddIdempotency();
        builder.Services.AddOpenApi(static o =>
        {
            o.AddOperationTransformer<ErrorCodeTransformer>();
            o.AddDocumentTransformer<ErrorCodeCatalogTransformer>();
        });
        WebApplication app = builder.Build();

        RouteGroupBuilder api = app.MapGroup("/api").ProducesErrors([.. CommonErrors.AnyEndpoint]);
        api.MapPost("/job-offers", static () => Results.Ok()).ProducesErrors(OfferClosed);

        app.Urls.Add("http://127.0.0.1:0"); // ApiExplorer only sees the routes once the app has started
        await app.StartAsync(TestContext.Current.CancellationToken);

        OpenApiDocument document = await app.Services
            .GetRequiredKeyedService<IOpenApiDocumentProvider>("v1")
            .GetOpenApiDocumentAsync(TestContext.Current.CancellationToken);
        OpenApiOperation post = document.Paths["/api/job-offers"].Operations![HttpMethod.Post];

        Assert.Contains("request.rate_limited", Codes(post, "429", ErrorCodeTransformer.CodesExtension));
        Assert.Contains("request.malformed", Codes(post, "400", ErrorCodeTransformer.CodesExtension));
        Assert.Equal(
            ["concurrency.conflict", "idempotency.in_progress", "job_offer.closed"],
            Codes(post, "409", ErrorCodeTransformer.CodesExtension));
        Assert.Contains(
            ((OpenApiSchema)document.Components!.Schemas![ErrorCodeCatalogTransformer.ProblemSchema]).Enum!,
            static n => n.GetValue<string>() == "job_offer.closed");

        await app.StopAsync(TestContext.Current.CancellationToken);
    }
}
