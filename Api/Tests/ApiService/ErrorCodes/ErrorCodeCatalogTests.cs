using System.Reflection;
using System.Text.RegularExpressions;
using Kernel.Errors;
using Microsoft.AspNetCore.Http;

namespace Tests.ApiService.ErrorCodes;

/// <summary>
/// The rules every code catalog keeps, because a client keys its translations on the values (ADR-A017): no value
/// shared by two meanings, every problem code dotted <c>area.reason</c>, a description for the document, and an
/// error status. Add each module's <c>&lt;Module&gt;Errors</c> catalog to <see cref="Catalogs"/> when it is created.
/// </summary>
public class ErrorCodeCatalogTests
{
    /// <summary>Every catalog of codes. Named rather than discovered, so a catalog that drops out fails here.</summary>
    private static readonly Type[] Catalogs = [typeof(CommonErrors), typeof(ValidationRuleErrors)];

    /// <summary>The DataAnnotations rule codes are one word each (<c>required</c>), the same for every field.</summary>
    private static readonly HashSet<IErrorCode> RuleCodes = [.. Catalog(typeof(ValidationRuleErrors))];

    private static IEnumerable<IErrorCode> Catalog(Type catalog) => catalog
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Where(static f => typeof(IErrorCode).IsAssignableFrom(f.FieldType))
        .Select(static f => (IErrorCode)f.GetValue(null)!);

    [Fact]
    public void The_catalogs_are_not_empty()
    {
        // Every assertion below passes vacuously on nothing.
        Assert.All(Catalogs, static c => Assert.NotEmpty(Catalog(c)));
    }

    [Fact]
    public void Wire_values_are_unique_per_kind_and_well_formed()
    {
        // The client keys its translations on the value, so two meanings must never share one.
        List<IErrorCode> all = [.. Catalogs.SelectMany(Catalog)];
        Regex dotted = new(@"^[a-z][a-z0-9_]*(\.[a-z][a-z0-9_]*)+$");
        Regex word = new(@"^[a-z][a-z0-9_]*$");

        Assert.Empty(all.GroupBy(static c => (c.GetType(), c.Value)).Where(static g => g.Count() > 1).Select(static g => g.Key.Value));
        Assert.Empty(all.Where(static c => !RuleCodes.Contains(c)).Where(c => !dotted.IsMatch(c.Value)).Select(static c => c.Value));
        Assert.Empty(RuleCodes.Where(c => !word.IsMatch(c.Value)).Select(static c => c.Value));
        Assert.Empty(all.Where(static c => string.IsNullOrWhiteSpace(c.Description)).Select(static c => c.Value));
    }

    [Fact]
    public void Every_problem_code_is_answered_with_an_error_status()
    {
        Assert.Empty(Catalogs.SelectMany(Catalog).OfType<ErrorCode>()
            .Where(static c => c.Status is < 400 or > 599).Select(static c => c.Value));
    }

    [Theory]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status401Unauthorized)]
    [InlineData(StatusCodes.Status403Forbidden)]
    [InlineData(StatusCodes.Status404NotFound)]
    [InlineData(StatusCodes.Status405MethodNotAllowed)]
    [InlineData(StatusCodes.Status409Conflict)]
    [InlineData(StatusCodes.Status413PayloadTooLarge)]
    [InlineData(StatusCodes.Status415UnsupportedMediaType)]
    [InlineData(StatusCodes.Status429TooManyRequests)]
    [InlineData(StatusCodes.Status500InternalServerError)]
    [InlineData(StatusCodes.Status502BadGateway)]
    [InlineData(StatusCodes.Status503ServiceUnavailable)]
    public void The_default_code_for_a_status_is_answered_with_that_status(int status)
    {
        // A problem the framework wrote is given this code; one listed under another status would contradict it.
        Assert.Equal(status, CommonErrors.ForStatus(status).Status);
    }

    [Fact]
    public void Any_endpoint_declares_nothing_only_a_signed_in_caller_can_meet()
    {
        // Applicants never sign in, so a 401 on the shared group would be a status their endpoints never answer.
        Assert.DoesNotContain(CommonErrors.Unauthenticated, CommonErrors.AnyEndpoint);
        Assert.DoesNotContain(CommonErrors.Forbidden, CommonErrors.AnyEndpoint);
    }
}
