using System.Security.Cryptography;

namespace RecruitmentModule.Infrastructure.Entra;

/// <summary>
/// The first password of a new staff account, which the administrator hands over and Entra makes the user change at
/// the first sign-in. Meets Entra's complexity rule (all four character classes here, three are required) and leaves out
/// characters that are easy to misread when copied by hand (<c>I l 1 O 0</c>).
/// </summary>
internal static class TemporaryPassword
{
    /// <summary>The generated length; Entra accepts 8 to 256.</summary>
    public const int Length = 16;

    private const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
    private const string Lower = "abcdefghijkmnopqrstuvwxyz";
    private const string Digits = "23456789";
    private const string Symbols = "!#$%&*+-=?@_";

    /// <summary>A new random password with at least one character of every class.</summary>
    public static string Generate()
    {
        char[] password = new char[Length];
        password[0] = Pick(Upper);
        password[1] = Pick(Lower);
        password[2] = Pick(Digits);
        password[3] = Pick(Symbols);
        RandomNumberGenerator.GetItems<char>(Upper + Lower + Digits + Symbols, password.AsSpan(4));
        RandomNumberGenerator.Shuffle(password.AsSpan());
        return new string(password);
    }

    private static char Pick(string set) => set[RandomNumberGenerator.GetInt32(set.Length)];
}
