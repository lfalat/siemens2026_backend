using System.ComponentModel.DataAnnotations;
using ApiService.OpenAPI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;

namespace Tests.ApiService.OpenAPI;

/// <summary>
/// The framework enforces a validation attribute on a minimal-API parameter at runtime but leaves it out of the
/// document, so <c>?status=</c> reads as any string while the API accepts four values. <see cref="ParameterValidationTransformer"/>
/// copies the rules onto the path and query parameters through the same <c>ValidationSchemaRules</c> the request
/// bodies use, so a request the schema permits is one the API accepts (ADR-009).
/// </summary>
public class ParameterValidationTransformerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<OpenApiOperation> OperationAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenApi(static o => o.AddOperationTransformer<ParameterValidationTransformer>());
        WebApplication app = builder.Build();

        app.MapGet("/job-offers/{jobOfferId}/applications", static (
                [StringLength(36, MinimumLength = 36)] string jobOfferId,
                [AllowedValues("new", "screening", "interview", "closed")] string? status,
                [Range(1, 100)] int? pageSize,
                string? search) => Results.Ok());

        app.Urls.Add("http://127.0.0.1:0"); // ApiExplorer only sees the routes once the app has started
        await app.StartAsync(Ct);
        OpenApiDocument document = await app.Services
            .GetRequiredKeyedService<IOpenApiDocumentProvider>("v1")
            .GetOpenApiDocumentAsync(Ct);
        await app.StopAsync(Ct);

        return document.Paths["/job-offers/{jobOfferId}/applications"].Operations![HttpMethod.Get];
    }

    private static IOpenApiSchema Schema(OpenApiOperation operation, string name) =>
        operation.Parameters!.Single(p => p.Name == name).Schema!;

    [Fact]
    public async Task A_fixed_value_set_on_a_query_parameter_becomes_an_enum()
    {
        IOpenApiSchema status = Schema(await OperationAsync(), "status");

        Assert.Equal(["new", "screening", "interview", "closed"], status.Enum!.Select(static v => v!.GetValue<string>()));
    }

    [Fact]
    public async Task A_range_on_a_query_parameter_maps_both_bounds()
    {
        IOpenApiSchema pageSize = Schema(await OperationAsync(), "pageSize");

        Assert.Equal("1", pageSize.Minimum);
        Assert.Equal("100", pageSize.Maximum);
    }

    [Fact]
    public async Task A_length_on_a_path_parameter_carries_over()
    {
        IOpenApiSchema jobOfferId = Schema(await OperationAsync(), "jobOfferId");

        Assert.Equal(36, jobOfferId.MinLength);
        Assert.Equal(36, jobOfferId.MaxLength);
    }

    [Fact]
    public async Task A_parameter_without_rules_is_left_as_generated()
    {
        IOpenApiSchema search = Schema(await OperationAsync(), "search");

        Assert.Null(search.Enum);
        Assert.Null(search.MaxLength);
        Assert.Null(search.Pattern);
    }
}
