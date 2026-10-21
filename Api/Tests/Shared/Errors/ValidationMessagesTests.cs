using System.ComponentModel.DataAnnotations;
using Kernel.Errors;
using Kernel.Validation;

namespace Tests.Shared.Errors;

/// <summary>
/// <see cref="ValidationMessages.For"/> is the one map from a failing DataAnnotations attribute to the Slovak message the
/// frontend shows under <c>errors[field]</c>: a built-in attribute gets a Slovak wording instead of the framework's
/// English one, and an author's own message or a custom attribute's message is used as written.
/// </summary>
public class ValidationMessagesTests
{
    private sealed class CustomAttribute() : ValidationAttribute("Vlastná správa.");

    public static TheoryData<ValidationAttribute, object?, string> BuiltIns => new()
    {
        { new RequiredAttribute(), null, "Toto pole je povinné." },
        { new StringLengthAttribute(64), "x", "Text môže mať najviac 64 znakov." },
        { new StringLengthAttribute(4) { MinimumLength = 2 }, "x", "Text musí mať aspoň 2 znaky a najviac 4 znaky." },
        { new MinLengthAttribute(1), Array.Empty<int>(), "Musí mať aspoň 1 položku." },
        { new MaxLengthAttribute(1), "xx", "Môže mať najviac 1 znak." },
        { new RangeAttribute(1.5, 2.5), 3.0, "Hodnota musí byť aspoň 1,5 a najviac 2,5." },
        { new RangeAttribute(0, 10) { MinimumIsExclusive = true }, 0, "Hodnota musí byť väčšia ako 0 a najviac 10." },
        { new EmailAddressAttribute(), "x", "Zadajte platnú e-mailovú adresu." },
    };

    [Theory]
    [MemberData(nameof(BuiltIns))]
    public void A_built_in_attribute_is_worded_in_slovak(ValidationAttribute attribute, object? value, string expected) =>
        Assert.Equal(expected, ValidationMessages.For(attribute, value, "Field"));

    [Fact]
    public void An_authors_message_wins() =>
        Assert.Equal("Iba číslice.",
            ValidationMessages.For(new RegularExpressionAttribute("^[0-9]+$") { ErrorMessage = "Iba číslice." }, "x", "Field"));

    [Fact]
    public void A_custom_attribute_answers_its_own_message()
    {
        Assert.Equal("Vlastná správa.", ValidationMessages.For(new CustomAttribute(), "x", "Field"));
        Assert.Equal("Toto pole je povinné.", ValidationMessages.For(new NotEmptyAttribute(), Guid.Empty, "Field"));
    }
}
