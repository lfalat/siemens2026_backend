using Kernel.Errors;

namespace Kernel.Validation;

/// <summary>
/// Implemented by a <see cref="System.ComponentModel.DataAnnotations.ValidationAttribute"/> that wants to
/// name the stable <c>errorCodes</c> value it produces. The client translates on this code, so it is part of the API contract.
/// </summary>
/// <remarks>
/// The built-in attributes are mapped in <see cref="ValidationRuleErrors.For"/>. A custom attribute implements this,
/// or fails with the generic <see cref="ValidationRuleErrors.Invalid"/>.
/// </remarks>
public interface IValidationErrorCode
{
    /// <summary>The stable code this attribute contributes to <c>errorCodes</c> when it fails.</summary>
    FieldErrorCode ErrorCode { get; }
}
