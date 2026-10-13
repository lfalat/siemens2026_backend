using System.ComponentModel.DataAnnotations;
using System.Globalization;
using Kernel.Errors;

namespace Kernel.Validation;

/// <summary>
/// Rejects a <see cref="decimal"/> that doesn't fit a PostgreSQL <c>numeric(precision, scale)</c> column: more than
/// <see cref="Scale"/> decimal places, or more than <c>precision − scale</c> digits before the point.
/// </summary>
/// <remarks>
/// Without it the database decides silently: extra decimal places are rounded away on insert, so a value of
/// <c>1.1590004</c> in a <c>numeric(18, 6)</c> column is stored as <c>1.159000</c>, and a value too large for the column fails the insert with a
/// <c>500</c> instead of a <c>400</c>. Trailing zeros don't count (<c>1.1590000</c> has four places), since JSON clients
/// often send them. <c>null</c> passes; that is <see cref="RequiredAttribute"/>'s business.
/// </remarks>
/// <param name="precision">The column's total number of significant digits.</param>
/// <param name="scale">The column's number of digits after the decimal point.</param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class DecimalPrecisionAttribute(int precision, int scale) : ValidationAttribute, IValidationErrorCode
{
    /// <summary>The column's total number of significant digits.</summary>
    public int Precision { get; } = precision;

    /// <summary>The column's number of digits after the decimal point.</summary>
    public int Scale { get; } = scale;

    /// <inheritdoc />
    public FieldErrorCode ErrorCode => ValidationRuleErrors.DecimalPrecision;

    /// <inheritdoc />
    public override bool IsValid(object? value)
    {
        if (value is not decimal number)
            return true;

        // Dividing by 1 with 28 zeros strips trailing zeros, leaving the scale the digits actually need.
        decimal normalised = number / 1.0000000000000000000000000000m;
        int places = (decimal.GetBits(normalised)[3] >> 16) & 0xFF;
        if (places > Scale)
            return false;

        decimal integerLimit = (decimal)Math.Pow(10, Precision - Scale);
        return Math.Abs(decimal.Truncate(number)) < integerLimit;
    }

    /// <inheritdoc />
    public override string FormatErrorMessage(string name) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "Must have at most {0} decimal places and {1} digits before the decimal point.",
            Scale, Precision - Scale);
}
