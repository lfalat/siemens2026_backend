using System.ComponentModel.DataAnnotations;

namespace Kernel.Validation;

/// <summary>
/// Rejects the all-zeros <see cref="Guid"/> on a command property that references something.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RequiredAttribute"/> cannot do this. It checks for <c>null</c>, and a non-nullable
/// <see cref="Guid"/> is never null — so a body that simply omits the property binds to
/// <see cref="Guid.Empty"/> and passes validation untouched. On a property whose value is looked up that
/// only turns into a <c>404</c>, but on one that is inserted as a reference nobody verifies it becomes a
/// persisted row pointing at a record that does not exist — a body of <c>{}</c>, which is exactly what a
/// Schemathesis run sends (ADR-009), would be answered <c>200</c> and stored with an all-zeros reference.
/// </para>
/// <para>
/// Keep <see cref="RequiredAttribute"/> alongside it: that is what puts the property in the schema's
/// <c>required</c> list, which is a separate job from rejecting the value.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Parameter | AttributeTargets.Field)]
public sealed class NotEmptyAttribute : ValidationAttribute
{
    /// <summary>Creates the attribute with its default message.</summary>
    public NotEmptyAttribute()
        : base("Toto pole je povinné.")
    {
    }

    /// <inheritdoc />
    public override bool IsValid(object? value) => value switch
    {
        // null is RequiredAttribute's business, not this one — reporting it here too would put the same
        // problem in the response twice.
        null => true,
        Guid guid => guid != Guid.Empty,
        _ => true,
    };
}
