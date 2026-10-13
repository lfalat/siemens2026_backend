using ApiService.OpenAPI;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;

namespace Tests.ApiService;

/// <summary>
/// A header an endpoint refuses to run without, enforced by a filter and absent from the document, is the
/// quietest way for the API to reject a request that satisfies its own contract: a generated client cannot
/// know to send it, and a Schemathesis run reports the API rejecting its own contract (ADR-009). These tests pin
/// that <see cref="RequiredHeader"/> reaches the document — including from a route group, which is where the
/// enforcement usually lives.
/// </summary>
public class RequiredHeaderTransformerTests
{
    private const string Header = "X-Client-Instance";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<OpenApiDocument> DocumentAsync(CancellationToken ct)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenApi(static o => o.AddOperationTransformer<RequiredHeaderTransformer>());
        WebApplication app = builder.Build();

        RouteGroupBuilder group = app.MapGroup("/drafts");
        group.RequiresHeader(Header, "Identifies the browser tab the draft belongs to.", format: "uuid");
        group.MapGet("/{draftId}", static (string draftId) => Results.Ok());
        group.MapPost("/{draftId}/autosave", static (string draftId) => Results.Ok());

        app.MapGet("/elsewhere", static () => Results.Ok()); // outside the group — must stay untouched

        app.Urls.Add("http://127.0.0.1:0"); // ApiExplorer only sees the routes once the app has started
        await app.StartAsync(ct);

        OpenApiDocument document = await app.Services
            .GetRequiredKeyedService<IOpenApiDocumentProvider>("v1")
            .GetOpenApiDocumentAsync(ct);

        await app.StopAsync(ct);
        return document;
    }

    private static IOpenApiParameter? DeclaredHeader(OpenApiOperation operation) =>
        operation.Parameters?.FirstOrDefault(p => p.Name == Header);

    [Fact]
    public async Task A_group_declaration_reaches_every_endpoint_in_the_group()
    {
        OpenApiDocument document = await DocumentAsync(Ct);

        foreach (string path in (string[])["/drafts/{draftId}", "/drafts/{draftId}/autosave"])
        {
            OpenApiOperation operation = document.Paths[path].Operations!.Values.Single();

            IOpenApiParameter? parameter = DeclaredHeader(operation);
            Assert.NotNull(parameter);
            Assert.Equal(ParameterLocation.Header, parameter!.In);
            Assert.True(parameter.Required);
            Assert.Equal("uuid", parameter.Schema!.Format);
        }
    }

    [Fact]
    public async Task The_rejection_it_implies_is_documented()
    {
        // The filter answers 400 when the header is missing or unparseable. Undeclared, that is a status
        // the specification never mentioned — which a contract run fails on in its own right.
        OpenApiDocument document = await DocumentAsync(Ct);
        OpenApiOperation operation = document.Paths["/drafts/{draftId}"].Operations!.Values.Single();

        Assert.Contains("400", operation.Responses!.Keys);
    }

    [Fact]
    public async Task An_endpoint_outside_the_group_declares_nothing()
    {
        OpenApiDocument document = await DocumentAsync(Ct);
        OpenApiOperation operation = document.Paths["/elsewhere"].Operations!.Values.Single();

        Assert.Null(DeclaredHeader(operation));
    }

    [Fact]
    public async Task A_header_already_in_the_document_is_not_declared_twice()
    {
        // The idempotency transformer declares Idempotency-Key itself; a RequiresHeader for the same name, on the
        // same operation, must not add a second parameter the document would then have twice.
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenApi(static o => o.AddOperationTransformer<RequiredHeaderTransformer>());
        WebApplication app = builder.Build();

        app.MapGet("/twice", static ([Microsoft.AspNetCore.Mvc.FromHeader(Name = Header)] string value) => Results.Ok())
           .RequiresHeader(Header, "Bound as a parameter already.");

        app.Urls.Add("http://127.0.0.1:0");
        await app.StartAsync(Ct);
        OpenApiDocument document = await app.Services
            .GetRequiredKeyedService<IOpenApiDocumentProvider>("v1")
            .GetOpenApiDocumentAsync(Ct);
        await app.StopAsync(Ct);

        OpenApiOperation operation = document.Paths["/twice"].Operations!.Values.Single();
        Assert.Single(operation.Parameters!, static p => string.Equals(p.Name, Header, StringComparison.OrdinalIgnoreCase));
    }
}
