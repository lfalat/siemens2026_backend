using System.ComponentModel.DataAnnotations;
using Kernel.Validation;

namespace Tests.Shared.Validation;

/// <summary>
/// <see cref="RequiredAttribute"/> cannot reject the all-zeros <see cref="Guid"/>: it checks for null, and
/// a non-nullable value type is never null. A body that simply omits the property therefore bound to
/// <see cref="Guid.Empty"/> and passes validation — a persisted all-zeros reference, from exactly the <c>{}</c>
/// body a Schemathesis run sends (ADR-009).
/// </summary>
public class NotEmptyAttributeTests
{
    private sealed record Probe
    {
        [Required, NotEmpty]
        public Guid UserId { get; init; }

        [NotEmpty]
        public Guid? Optional { get; init; }
    }

    [Fact]
    public void The_all_zeros_guid_is_rejected() =>
        Assert.False(CommandValidation.IsValid(new Probe { UserId = Guid.Empty }));

    [Fact]
    public void A_real_id_passes() =>
        Assert.True(CommandValidation.IsValid(new Probe { UserId = Guid.CreateVersion7() }));

    [Fact]
    public void Null_is_left_to_Required_so_the_same_problem_is_not_reported_twice() =>
        Assert.True(CommandValidation.IsValid(
            new Probe { UserId = Guid.CreateVersion7(), Optional = null }));

    [Fact]
    public void Required_alone_would_not_have_caught_it()
    {
        // The reason this attribute exists, stated as a test rather than only in a comment.
        Assert.True(new RequiredAttribute().IsValid(Guid.Empty));
        Assert.False(new NotEmptyAttribute().IsValid(Guid.Empty));
    }
}
