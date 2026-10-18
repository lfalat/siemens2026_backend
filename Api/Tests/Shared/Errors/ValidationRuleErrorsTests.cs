using System.ComponentModel.DataAnnotations;
using Kernel.Errors;
using Kernel.Validation;

namespace Tests.Shared.Errors;

/// <summary>
/// <see cref="ValidationRuleErrors.For"/> is the one map from a failing DataAnnotations attribute to the per-field code
/// a client translates on: a built-in attribute gets its rule's code, a custom one names its own through
/// <see cref="IValidationErrorCode"/>, and anything else falls back to <c>invalid</c> rather than to nothing.
/// </summary>
public class ValidationRuleErrorsTests
{
    private sealed class NamelessAttribute : ValidationAttribute;

    public static TheoryData<ValidationAttribute, string> BuiltIns => new()
    {
        { new RequiredAttribute(), "required" },
        { new StringLengthAttribute(10), "string_length" },
        { new MinLengthAttribute(1), "min_length" },
        { new MaxLengthAttribute(1), "max_length" },
        { new LengthAttribute(1, 2), "length" },
        { new RangeAttribute(1, 2), "range" },
        { new RegularExpressionAttribute("x"), "pattern" },
        { new EmailAddressAttribute(), "email" },
        { new PhoneAttribute(), "phone" },
        { new UrlAttribute(), "url" },
        { new AllowedValuesAttribute("a"), "allowed_values" },
        { new DeniedValuesAttribute("a"), "denied_values" },
        { new Base64StringAttribute(), "base64" },
    };

    [Theory]
    [MemberData(nameof(BuiltIns))]
    public void A_built_in_attribute_maps_to_its_rule(ValidationAttribute attribute, string expected) =>
        Assert.Equal(expected, ValidationRuleErrors.For(attribute).Value);

    [Fact]
    public void A_custom_attribute_names_its_own_code()
    {
        Assert.Same(ValidationRuleErrors.NotEmpty, ValidationRuleErrors.For(new NotEmptyAttribute()));
        Assert.Same(ValidationRuleErrors.DecimalPrecision, ValidationRuleErrors.For(new DecimalPrecisionAttribute(18, 6)));
    }

    [Fact]
    public void An_attribute_without_a_code_fails_with_invalid() =>
        Assert.Same(ValidationRuleErrors.Invalid, ValidationRuleErrors.For(new NamelessAttribute()));
}
