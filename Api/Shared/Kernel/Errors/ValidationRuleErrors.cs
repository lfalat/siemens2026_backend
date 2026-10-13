using System.ComponentModel.DataAnnotations;
using Kernel.Validation;

namespace Kernel.Errors;

/// <summary>
/// The per-field codes a DataAnnotations rule on a command fails with — one per rule, the same for every field —
/// and the attribute-to-code map the command validator and the OpenAPI transformer share.
/// </summary>
public static class ValidationRuleErrors
{
    /// <summary><see cref="RequiredAttribute"/>.</summary>
    public static readonly FieldErrorCode Required = new("required", "The field is required.");

    /// <summary><see cref="NotEmptyAttribute"/>.</summary>
    public static readonly FieldErrorCode NotEmpty = new("not_empty", "The field must not be the empty id.");

    /// <summary><see cref="StringLengthAttribute"/>.</summary>
    public static readonly FieldErrorCode StringLength = new("string_length", "The text is shorter or longer than allowed.");

    /// <summary><see cref="MinLengthAttribute"/>.</summary>
    public static readonly FieldErrorCode MinLength = new("min_length", "The value has fewer characters or items than allowed.");

    /// <summary><see cref="MaxLengthAttribute"/>.</summary>
    public static readonly FieldErrorCode MaxLength = new("max_length", "The value has more characters or items than allowed.");

    /// <summary><see cref="LengthAttribute"/>.</summary>
    public static readonly FieldErrorCode Length = new("length", "The value's length is outside the allowed range.");

    /// <summary><see cref="RangeAttribute"/>.</summary>
    public static readonly FieldErrorCode Range = new("range", "The number is outside the allowed range.");

    /// <summary><see cref="RegularExpressionAttribute"/>.</summary>
    public static readonly FieldErrorCode Pattern = new("pattern", "The text does not have the required format.");

    /// <summary><see cref="EmailAddressAttribute"/>.</summary>
    public static readonly FieldErrorCode Email = new("email", "The text is not an e-mail address.");

    /// <summary><see cref="PhoneAttribute"/>.</summary>
    public static readonly FieldErrorCode Phone = new("phone", "The text is not a phone number.");

    /// <summary><see cref="UrlAttribute"/>.</summary>
    public static readonly FieldErrorCode Url = new("url", "The text is not an absolute URL.");

    /// <summary><see cref="CreditCardAttribute"/>.</summary>
    public static readonly FieldErrorCode CreditCard = new("credit_card", "The text is not a card number.");

    /// <summary><see cref="CompareAttribute"/>.</summary>
    public static readonly FieldErrorCode Compare = new("compare", "The value does not match the field it must equal.");

    /// <summary><see cref="AllowedValuesAttribute"/>.</summary>
    public static readonly FieldErrorCode AllowedValues = new("allowed_values", "The value is not one of the allowed values.");

    /// <summary><see cref="DeniedValuesAttribute"/>.</summary>
    public static readonly FieldErrorCode DeniedValues = new("denied_values", "The value is not allowed.");

    /// <summary><see cref="Base64StringAttribute"/>.</summary>
    public static readonly FieldErrorCode Base64 = new("base64", "The text is not valid Base64.");

    /// <summary><see cref="DecimalPrecisionAttribute"/>.</summary>
    public static readonly FieldErrorCode DecimalPrecision = new("decimal_precision", "The number has more digits than allowed.");

    /// <summary>A custom attribute that names no code of its own.</summary>
    public static readonly FieldErrorCode Invalid = new("invalid", "The value is invalid.");

    /// <summary>
    /// The code <paramref name="attribute"/> fails with: its own <see cref="IValidationErrorCode"/>, the built-in
    /// attribute's code, else <see cref="Invalid"/>.
    /// </summary>
    /// <param name="attribute">The failing attribute.</param>
    public static FieldErrorCode For(ValidationAttribute attribute) => attribute switch
    {
        IValidationErrorCode custom => custom.ErrorCode,
        RequiredAttribute => Required,
        StringLengthAttribute => StringLength,
        MinLengthAttribute => MinLength,
        MaxLengthAttribute => MaxLength,
        LengthAttribute => Length,
        RangeAttribute => Range,
        RegularExpressionAttribute => Pattern,
        EmailAddressAttribute => Email,
        PhoneAttribute => Phone,
        UrlAttribute => Url,
        CreditCardAttribute => CreditCard,
        CompareAttribute => Compare,
        AllowedValuesAttribute => AllowedValues,
        DeniedValuesAttribute => DeniedValues,
        Base64StringAttribute => Base64,
        _ => Invalid,
    };
}
