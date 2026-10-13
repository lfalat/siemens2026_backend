using ApiService.OpenAPI;
using Kernel.Idempotency;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi;

namespace Tests.ApiService;

/// <summary>
/// Tests that <see cref="IdempotencyHeaderTransformer"/> documents the idempotency contract on exactly the
/// operations the middleware covers. The header and the middleware's rejections have to appear on covered
/// writes and nowhere else: a generated client that sends no key gets a 400 the specification never
/// mentioned, and a contract test run (ADR-009, Schemathesis) fails on the undeclared status.
/// </summary>
public class IdempotencyHeaderTransformerTests
{
    private const string Header = "Idempotency-Key";

    private static async Task<OpenApiOperation> TransformAsync(
        string method, IdempotencyOptions? options = null, params object[] endpointMetadata)
    {
        OpenApiOperation operation = new();
        IdempotencyHeaderTransformer transformer = new(Options.Create(options ?? new IdempotencyOptions()));

        OpenApiOperationTransformerContext context = new()
        {
            DocumentName = "v1",
            Description = new ApiDescription
            {
                HttpMethod = method,
                ActionDescriptor = new ActionDescriptor { EndpointMetadata = [.. endpointMetadata] },
            },
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        };

        await transformer.TransformAsync(operation, context, TestContext.Current.CancellationToken);
        return operation;
    }

    private static IOpenApiParameter? KeyParameter(OpenApiOperation operation) =>
        operation.Parameters?.FirstOrDefault(p => p.Name == Header);

    [Fact]
    public async Task Covered_write_declares_the_key_header_as_required()
    {
        OpenApiOperation operation = await TransformAsync("POST");

        IOpenApiParameter? parameter = KeyParameter(operation);
        Assert.NotNull(parameter);
        Assert.Equal(ParameterLocation.Header, parameter!.In);
        Assert.True(parameter.Required);
        Assert.Equal(128, parameter.Schema!.MaxLength);
    }

    [Fact]
    public async Task Covered_write_declares_the_middleware_rejections()
    {
        OpenApiOperation operation = await TransformAsync("PATCH");

        Assert.Contains("400", operation.Responses!.Keys);
        Assert.Contains("409", operation.Responses.Keys);
        Assert.Contains("422", operation.Responses.Keys);
    }

    [Fact]
    public async Task An_endpoints_own_status_description_is_not_overwritten()
    {
        // An endpoint's own 409 means a domain conflict, not "still processing".
        OpenApiOperation operation = new()
        {
            Responses = new OpenApiResponses { ["409"] = new OpenApiResponse { Description = "The job offer was changed by someone else." } },
        };
        IdempotencyHeaderTransformer transformer = new(Options.Create(new IdempotencyOptions()));

        await transformer.TransformAsync(operation, new OpenApiOperationTransformerContext
        {
            DocumentName = "v1",
            Description = new ApiDescription { HttpMethod = "POST", ActionDescriptor = new ActionDescriptor() },
            ApplicationServices = new ServiceCollection().BuildServiceProvider(),
        }, TestContext.Current.CancellationToken);

        Assert.Equal("The job offer was changed by someone else.", operation.Responses["409"].Description);
    }

    [Fact]
    public async Task An_opted_out_endpoint_declares_nothing()
    {
        // What MapQuery produces, and what a SignalR hub's handshake carries.
        OpenApiOperation operation = await TransformAsync("POST", options: null, IdempotencyDisabled.Instance);

        Assert.Null(KeyParameter(operation));
        Assert.True(operation.Responses is null or { Count: 0 });
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task An_uncovered_method_declares_nothing(string method)
    {
        OpenApiOperation operation = await TransformAsync(method);

        Assert.Null(KeyParameter(operation));
        Assert.True(operation.Responses is null or { Count: 0 });
    }

    [Fact]
    public async Task The_header_is_optional_and_has_no_400_when_the_key_is_not_required()
    {
        OpenApiOperation operation = await TransformAsync("POST", new IdempotencyOptions { RequireKey = false });

        Assert.False(KeyParameter(operation)!.Required);
        Assert.DoesNotContain("400", operation.Responses!.Keys);
    }

    [Fact]
    public async Task A_renamed_header_is_documented_under_its_configured_name()
    {
        OpenApiOperation operation = await TransformAsync(
            "POST", new IdempotencyOptions { HeaderName = "X-Request-Id" });

        Assert.Null(KeyParameter(operation));
        Assert.NotNull(operation.Parameters?.FirstOrDefault(p => p.Name == "X-Request-Id"));
    }

    [Fact]
    public async Task The_generated_document_carries_the_header_on_a_write_but_not_on_a_MapQuery_route()
    {
        // Closes the loop the fakes above leave open: that MapQuery's IdempotencyDisabled metadata survives
        // real endpoint building and reaches the transformer as the ApiDescription it reads.
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Services.AddIdempotency();
        builder.Services.AddOpenApi(static o => o.AddOperationTransformer<IdempotencyHeaderTransformer>());
        WebApplication app = builder.Build();

        app.MapPost("/things", static () => Results.Ok());
        app.MapQuery("/things/search", static () => Results.Ok());
        app.MapGet("/things", static () => Results.Ok());

        app.Urls.Add("http://127.0.0.1:0"); // ApiExplorer only sees the routes once the app has started
        await app.StartAsync(TestContext.Current.CancellationToken);

        OpenApiDocument document = await app.Services
            .GetRequiredKeyedService<IOpenApiDocumentProvider>("v1")
            .GetOpenApiDocumentAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(KeyParameter(document.Paths["/things"].Operations![HttpMethod.Post]));
        Assert.Null(KeyParameter(document.Paths["/things/search"].Operations![HttpMethod.Post]));
        Assert.Null(KeyParameter(document.Paths["/things"].Operations![HttpMethod.Get]));

        await app.StopAsync(TestContext.Current.CancellationToken);
    }
}
