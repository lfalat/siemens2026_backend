using Kernel.Decorators;
using Kernel.Exceptions;

namespace Tests.Shared;

/// <summary>
/// Runs a command through the <em>real</em> validation the <see cref="ValidationDecorator{TCommand,TResult}"/>
/// (Shared/Kernel) applies — the exact <c>CommandValidator</c> used in production — so command-validation
/// tests reflect the decorator, not a re-implementation. Any object is accepted, so attribute tests can
/// validate a small probe type that is not an <see cref="Kernel.CQRS.ICommand"/>. Returns the validation
/// errors, or <c>null</c> when the command is valid.
/// </summary>
internal static class CommandValidation
{
    public static IReadOnlyDictionary<string, string[]>? Run<TCommand>(TCommand command)
        where TCommand : notnull
    {
        try
        {
            CommandValidator.Validate(command);
            return null;
        }
        catch (ValidationException ex)
        {
            return ex.Errors;
        }
    }

    /// <summary>Convenience: true when the command passes validation.</summary>
    public static bool IsValid<TCommand>(TCommand command) where TCommand : notnull =>
        Run(command) is null;
}
