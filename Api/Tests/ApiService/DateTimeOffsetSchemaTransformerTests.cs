using ApiService.OpenAPI;
using Kernel.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;

namespace Tests.ApiService;

/// <summary>Carries a required and an optional timestamp, the way a search filter does.</summary>
public sealed record TimestampProbeRequest
{
    public DateTimeOffset OccurredAt { get; init; }
    public DateTimeOffset? From { get; init; }
}

/// <summary>
/// Registering a custom <c>DateTimeOffset</c> converter makes the schema generator unable to derive a type for it,
/// leaving every timestamp in the document as bare <c>{"format": "date-time"}</c>. A JSON Schema with no
/// <c>type</c> permits every type, so the document would claim a number or a boolean is an acceptable <c>from</c>
/// filter — Schemathesis would send one and report the 400 as the API rejecting valid data (ADR-009), and the
/// generated frontend client would type it as <c>unknown</c> (ADR-F005). These tests pin the type back on.
/// </summary>
public class DateTimeOffsetSchemaTransformerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<IDictionary<string, IOpenApiSchema>> ProbeAsync(CancellationToken ct)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.ConfigureHttpJsonOptions(static o =>
            o.SerializerOptions.Converters.Add(new UtcDateTimeOffsetConverter()));
        builder.Services.AddOpenApi(static o => o.AddSchemaTransformer<DateTimeOffsetSchemaTransformer>());
        WebApplication app = builder.Build();

        app.MapPost("/probe", static (TimestampProbeRequest _) => Results.Ok());

        app.Urls.Add("http://127.0.0.1:0"); // ApiExplorer only sees the routes once the app has started
        await app.StartAsync(ct);

        OpenApiDocument document = await app.Services
            .GetRequiredKeyedService<IOpenApiDocumentProvider>("v1")
            .GetOpenApiDocumentAsync(ct);

        IDictionary<string, IOpenApiSchema> properties =
            document.Components!.Schemas![nameof(TimestampProbeRequest)].Properties!;

        await app.StopAsync(ct);
        return properties;
    }

    [Fact]
    public async Task A_required_timestamp_is_a_string()
    {
        IOpenApiSchema occurredAt = (await ProbeAsync(Ct))["occurredAt"];

        Assert.Equal(JsonSchemaType.String, occurredAt.Type);
        Assert.Equal("date-time", occurredAt.Format);
    }

    [Fact]
    public async Task An_optional_timestamp_stays_nullable_rather_than_being_narrowed()
    {
        // Getting this wrong the other way would make an omitted filter look invalid.
        IOpenApiSchema from = (await ProbeAsync(Ct))["from"];

        Assert.True(from.Type!.Value.HasFlag(JsonSchemaType.String));
        Assert.True(from.Type!.Value.HasFlag(JsonSchemaType.Null));
    }

    [Fact]
    public async Task No_timestamp_is_left_without_a_type()
    {
        // The actual defect: a schema with no type permits numbers and booleans as well as strings.
        IDictionary<string, IOpenApiSchema> properties = await ProbeAsync(Ct);

        Assert.All(properties.Values, static p => Assert.NotNull(p.Type));
    }
}
