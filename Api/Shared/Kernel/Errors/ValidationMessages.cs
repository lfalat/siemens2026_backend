using System.ComponentModel.DataAnnotations;
using System.Globalization;

namespace Kernel.Errors;

/// <summary>
/// The Slovak message a failing DataAnnotations rule on a command answers under <c>errors[field]</c>. The framework's
/// own messages are English and name the C# property, so the built-in attributes are worded here instead.
/// </summary>
/// <remarks>
/// A message the attribute's author wrote (<see cref="ValidationAttribute.ErrorMessage"/>) wins, and a custom attribute
/// answers its own <see cref="ValidationAttribute.FormatErrorMessage"/>: both are written in Slovak.
/// </remarks>
public static class ValidationMessages
{
    private static readonly CultureInfo Slovak = CultureInfo.GetCultureInfo("sk-SK");

    /// <summary>
    /// The English messages some built-in attributes set as their own <see cref="ValidationAttribute.ErrorMessage"/>,
    /// so that only a different one counts as the author's.
    /// </summary>
    private static readonly HashSet<string> FrameworkMessages =
    [
        .. new ValidationAttribute[]
        {
            new EmailAddressAttribute(), new PhoneAttribute(), new UrlAttribute(), new CreditCardAttribute(),
            new AllowedValuesAttribute(), new DeniedValuesAttribute(), new Base64StringAttribute(),
        }.Select(static a => a.ErrorMessage).OfType<string>(),
    ];

    /// <summary>The message <paramref name="attribute"/> fails with on <paramref name="value"/>.</summary>
    /// <param name="attribute">The failing attribute.</param>
    /// <param name="value">The value it rejected; tells a text length apart from a number of items.</param>
    /// <param name="memberName">The property, for an author's message that formats it in.</param>
    public static string For(ValidationAttribute attribute, object? value, string memberName)
    {
        if (attribute.ErrorMessageResourceName is not null
            || (attribute.ErrorMessage is { } message && !FrameworkMessages.Contains(message)))
            return attribute.FormatErrorMessage(memberName);

        return attribute switch
        {
            RequiredAttribute => "Toto pole je povinné.",
            StringLengthAttribute { MinimumLength: > 0 } a =>
                $"Text musí mať aspoň {Count(a.MinimumLength, value)} a najviac {Count(a.MaximumLength, value)}.",
            StringLengthAttribute a => $"Text môže mať najviac {Count(a.MaximumLength, value)}.",
            LengthAttribute a =>
                $"Musí mať aspoň {Count(a.MinimumLength, value)} a najviac {Count(a.MaximumLength, value)}.",
            MinLengthAttribute a => $"Musí mať aspoň {Count(a.Length, value)}.",
            MaxLengthAttribute a => $"Môže mať najviac {Count(a.Length, value)}.",
            RangeAttribute a => Range(a),
            RegularExpressionAttribute => "Hodnota nemá požadovaný formát.",
            EmailAddressAttribute => "Zadajte platnú e-mailovú adresu.",
            PhoneAttribute => "Zadajte platné telefónne číslo.",
            UrlAttribute => "Zadajte platnú webovú adresu začínajúcu https://.",
            CreditCardAttribute => "Zadajte platné číslo karty.",
            CompareAttribute => "Hodnoty sa nezhodujú.",
            AllowedValuesAttribute => "Zvoľte jednu z povolených hodnôt.",
            DeniedValuesAttribute => "Táto hodnota nie je povolená.",
            Base64StringAttribute => "Hodnota nie je platný reťazec Base64.",
            _ => attribute.FormatErrorMessage(memberName),
        };
    }

    private static string Range(RangeAttribute range)
    {
        string min = Convert.ToString(range.Minimum, Slovak) ?? string.Empty;
        string max = Convert.ToString(range.Maximum, Slovak) ?? string.Empty;
        string lower = range.MinimumIsExclusive ? $"väčšia ako {min}" : $"aspoň {min}";
        string upper = range.MaximumIsExclusive ? $"menšia ako {max}" : $"najviac {max}";
        return $"Hodnota musí byť {lower} a {upper}.";
    }

    /// <summary><paramref name="n"/> characters of a text, or items of a collection, in the right Slovak plural.</summary>
    private static string Count(int n, object? value) => value is string
        ? n + (n == 1 ? " znak" : n is >= 2 and <= 4 ? " znaky" : " znakov")
        : n + (n == 1 ? " položku" : n is >= 2 and <= 4 ? " položky" : " položiek");
}
