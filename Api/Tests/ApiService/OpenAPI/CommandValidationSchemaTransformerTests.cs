using System.ComponentModel.DataAnnotations;
using ApiService.OpenAPI;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.OpenApi;

namespace Tests.ApiService.OpenAPI;

/// <summary>Stands in for a feature's command: the type the validation attributes actually live on.</summary>
public sealed record ProbeCommand
{
    [Range(0d, double.MaxValue, MinimumIsExclusive = true, ErrorMessage = "Must be strictly positive.")]
    public decimal Quantity { get; init; }

    [Range(1, 10)]
    public int Bounded { get; init; }

    [AllowedValues(null, "newest", "oldest")]
    public string? Sort { get; init; }

    [Required, StringLength(32, MinimumLength = 2)]
    public string Name { get; init; } = default!;
}

/// <summary>Mirrors <see cref="ProbeCommand"/>, the way every feature's request DTO mirrors its command.</summary>
public sealed record ProbeRequest
{
    public decimal Quantity { get; init; }
    public int Bounded { get; init; }
    public string? Sort { get; init; }
    public string Name { get; init; } = default!;
}

/// <summary>
/// Validation lives on the command, the document describes the request DTO, and this transformer is the
/// only thing joining them. What it fails to copy becomes a value the schema calls legal and the API
/// rejects — the "API rejected a schema-compliant request" class of contract finding (ADR-009). These
/// tests cover the three that produced it: an exclusive bound flattened to an inclusive one, a fixed value
/// set left as a bare string, and an open upper bound written into the document as a real ceiling.
/// </summary>
public class CommandValidationSchemaTransformerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<IDictionary<string, IOpenApiSchema>> ProbeSchemaAsync(CancellationToken ct)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddOpenApi(static o => o.AddSchemaTransformer<CommandValidationSchemaTransformer>());
        WebApplication app = builder.Build();

        app.MapPost("/probe", static (ProbeRequest _) => Results.Ok());

        app.Urls.Add("http://127.0.0.1:0"); // ApiExplorer only sees the routes once the app has started
        await app.StartAsync(ct);

        OpenApiDocument document = await app.Services
            .GetRequiredKeyedService<IOpenApiDocumentProvider>("v1")
            .GetOpenApiDocumentAsync(ct);

        IDictionary<string, IOpenApiSchema> properties =
            document.Components!.Schemas![nameof(ProbeRequest)].Properties!;

        await app.StopAsync(ct);
        return properties;
    }

    [Fact]
    public async Task A_strictly_positive_rule_becomes_exclusiveMinimum_not_minimum()
    {
        // `minimum: 0` advertises 0 as acceptable, which the command rejects. That gap is what a generated
        // client walks into, and what the negative-data phase of a contract run reports.
        IOpenApiSchema quantity = (await ProbeSchemaAsync(Ct))["quantity"];

        Assert.Equal("0", quantity.ExclusiveMinimum);
        Assert.Null(quantity.Minimum);
    }

    [Fact]
    public async Task An_open_upper_bound_is_not_written_as_a_ceiling()
    {
        // RangeAttribute has no way to say "no maximum", so double.MaxValue stands in for it. Copying it
        // would put a 1.79e308 limit in the document that says nothing true about the field.
        IOpenApiSchema quantity = (await ProbeSchemaAsync(Ct))["quantity"];

        Assert.Null(quantity.Maximum);
        Assert.Null(quantity.ExclusiveMaximum);
    }

    [Fact]
    public async Task A_real_range_still_maps_both_bounds()
    {
        IOpenApiSchema bounded = (await ProbeSchemaAsync(Ct))["bounded"];

        Assert.Equal("1", bounded.Minimum);
        Assert.Equal("10", bounded.Maximum);
    }

    [Fact]
    public async Task A_fixed_value_set_becomes_an_enum_with_the_null_placeholder_dropped()
    {
        IOpenApiSchema sort = (await ProbeSchemaAsync(Ct))["sort"];

        Assert.Equal(["newest", "oldest"], sort.Enum!.Select(v => v!.GetValue<string>()));
    }

    [Fact]
    public async Task Length_and_required_carry_over()
    {
        IDictionary<string, IOpenApiSchema> properties = await ProbeSchemaAsync(Ct);

        Assert.Equal(32, properties["name"].MaxLength);
        Assert.Equal(2, properties["name"].MinLength);
    }

    [Fact]
    public async Task A_framework_default_message_does_not_become_the_description()
    {
        // AllowedValuesAttribute carries "The {0} field does not equal any of the values specified in
        // AllowedValuesAttribute" by default. Copied verbatim it puts a literal {0} in the document and
        // tells a reader nothing; a message written by hand is worth keeping.
        IDictionary<string, IOpenApiSchema> properties = await ProbeSchemaAsync(Ct);

        Assert.True(string.IsNullOrEmpty(properties["sort"].Description));
        Assert.Equal("Must be strictly positive.", properties["quantity"].Description);
    }
}
