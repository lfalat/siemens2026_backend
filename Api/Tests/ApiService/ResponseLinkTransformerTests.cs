using ApiService.OpenAPI;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;

namespace Tests.ApiService;

/// <summary>
/// A link is the only part of the document that describes a sequence, and the only thing Schemathesis's stateful
/// phase chains (ADR-009). These tests pin that a <see cref="ResponseLink"/> declared with <c>ProducesLink</c> reaches
/// the generated document with its target and runtime expressions intact, and that a declaration hanging off a status
/// the endpoint never declares is skipped rather than taking the whole document down.
/// </summary>
public class ResponseLinkTransformerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<OpenApiDocument> DocumentAsync(Action<WebApplication> map)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenApi(static o => o.AddOperationTransformer<ResponseLinkTransformer>());
        WebApplication app = builder.Build();
        map(app);

        app.Urls.Add("http://127.0.0.1:0"); // ApiExplorer only sees the routes once the app has started
        await app.StartAsync(Ct);
        OpenApiDocument document = await app.Services
            .GetRequiredKeyedService<IOpenApiDocumentProvider>("v1")
            .GetOpenApiDocumentAsync(Ct);
        await app.StopAsync(Ct);
        return document;
    }

    private static void MapOffers(WebApplication app)
    {
        app.MapGet("/job-offers", static () => Results.Ok())
           .WithName("ListJobOffers")
           .Produces<object>(StatusCodes.Status200OK)
           .ProducesLink(StatusCodes.Status200OK, "GetJobOffer", ("jobOfferId", "$response.body#/items/0/id"));
        app.MapGet("/job-offers/{jobOfferId}", static (string jobOfferId) => Results.Ok())
           .WithName("GetJobOffer")
           .Produces<object>(StatusCodes.Status200OK)
           .ProducesLink(StatusCodes.Status200OK, "ListApplications", ("jobOfferId", "$request.path.jobOfferId"))
           .ProducesLink(StatusCodes.Status200OK, "GetApplication",
               ("jobOfferId", "$request.path.jobOfferId"), ("applicationId", "$response.body#/applications/0/id"));
        app.MapGet("/job-offers/{jobOfferId}/applications", static (string jobOfferId) => Results.Ok())
           .WithName("ListApplications");
        app.MapGet("/job-offers/{jobOfferId}/applications/{applicationId}",
                static (string jobOfferId, string applicationId) => Results.Ok())
           .WithName("GetApplication");
    }

    [Fact]
    public async Task A_declared_link_reaches_the_generated_document()
    {
        OpenApiDocument document = await DocumentAsync(MapOffers);

        IOpenApiResponse response = document.Paths["/job-offers"].Operations![HttpMethod.Get].Responses!["200"];
        OpenApiLink link = Assert.IsType<OpenApiLink>(Assert.Contains("GetJobOffer", response.Links!));
        Assert.Equal("GetJobOffer", link.OperationId);
        Assert.Equal("$response.body#/items/0/id", link.Parameters!["jobOfferId"].Expression!.Expression);
    }

    [Fact]
    public async Task Several_links_on_one_response_are_named_after_their_targets()
    {
        // A nested target carries every parent id through, or the caller invents an offer and gets a 404.
        OpenApiDocument document = await DocumentAsync(MapOffers);

        IOpenApiResponse response = document.Paths["/job-offers/{jobOfferId}"].Operations![HttpMethod.Get].Responses!["200"];
        Assert.Equal(["GetApplication", "ListApplications"], response.Links!.Keys.Order(StringComparer.Ordinal));

        OpenApiLink nested = (OpenApiLink)response.Links["GetApplication"];
        Assert.Equal("$request.path.jobOfferId", nested.Parameters!["jobOfferId"].Expression!.Expression);
        Assert.Equal("$response.body#/applications/0/id", nested.Parameters["applicationId"].Expression!.Expression);
    }

    [Fact]
    public async Task A_link_on_an_undeclared_status_is_skipped_rather_than_thrown_on()
    {
        // A transformer that throws takes the whole document down at runtime for what is a static mistake.
        OpenApiDocument document = await DocumentAsync(static app =>
        {
            app.MapPost("/job-offers", static () => Results.Ok())
               .Produces<object>(StatusCodes.Status200OK)
               .ProducesLink(StatusCodes.Status201Created, "GetJobOffer", ("jobOfferId", "$response.body#/id"));
            app.MapGet("/job-offers/{jobOfferId}", static (string jobOfferId) => Results.Ok()).WithName("GetJobOffer");
        });

        OpenApiOperation post = document.Paths["/job-offers"].Operations![HttpMethod.Post];
        Assert.False(post.Responses!.ContainsKey("201"));
        Assert.True(post.Responses["200"].Links is null or { Count: 0 });
    }
}
