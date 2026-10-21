using System.Globalization;
using Kernel.Validation;

namespace Tests.Shared.Validation;

/// <summary>
/// <see cref="DecimalPrecisionAttribute"/> keeps a decimal within its PostgreSQL <c>numeric(precision, scale)</c> column, so the
/// database never rounds away decimal places silently or fails an insert with a 500 on a value too large.
/// </summary>
public class DecimalPrecisionAttributeTests
{
    private static readonly DecimalPrecisionAttribute Numeric18Scale6 = new(18, 6);

    [Theory]
    [InlineData("0.000001")]
    [InlineData("1.159")]
    [InlineData("1.1590000000")] // trailing zeros don't count
    [InlineData("999999999999.999999")] // 12 integer digits, 6 decimal places: the largest the column holds
    [InlineData("-999999999999.999999")]
    public void Accepts_a_value_the_column_holds(string value) =>
        Assert.True(Numeric18Scale6.IsValid(decimal.Parse(value, CultureInfo.InvariantCulture)));

    [Theory]
    [InlineData("0.0000001")]
    [InlineData("1.1590004")]
    [InlineData("1000000000000")] // 13 integer digits
    [InlineData("-1000000000000")]
    public void Rejects_a_value_the_column_would_round_or_overflow(string value) =>
        Assert.False(Numeric18Scale6.IsValid(decimal.Parse(value, CultureInfo.InvariantCulture)));

    [Fact]
    public void Leaves_null_to_Required() => Assert.True(Numeric18Scale6.IsValid(null));

    [Fact]
    public void Names_the_limits_in_its_message() =>
        Assert.Equal(
            "Číslo môže mať najviac 6 desatinných miest a 12 číslic pred desatinnou čiarkou.",
            Numeric18Scale6.FormatErrorMessage("FxRate"));
}
