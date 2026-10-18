using RecruitmentModule.Infrastructure.Entra;

namespace Tests.RecruitmentModule.Infrastructure.Entra;

/// <summary>The first password of a new staff account always passes Entra's complexity rule.</summary>
public class TemporaryPasswordTests
{
    [Fact]
    public void Has_every_character_class_at_full_length()
    {
        // Random output: many draws, so a generator that only sometimes misses a class is caught.
        for (int i = 0; i < 1000; i++)
        {
            string password = TemporaryPassword.Generate();

            Assert.Equal(TemporaryPassword.Length, password.Length);
            Assert.Contains(password, char.IsAsciiLetterUpper);
            Assert.Contains(password, char.IsAsciiLetterLower);
            Assert.Contains(password, char.IsAsciiDigit);
            Assert.Contains(password, static c => !char.IsAsciiLetterOrDigit(c));
        }
    }

    [Fact]
    public void Leaves_out_characters_that_are_misread_when_copied()
    {
        string many = string.Concat(Enumerable.Range(0, 1000).Select(static _ => TemporaryPassword.Generate()));

        Assert.DoesNotContain(many, static c => "Il1O0".Contains(c));
    }
}
