using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Kernel.CQRS;
using Kernel.Errors;
using Kernel.Validation;

namespace Kernel.Decorators;

/// <summary>
/// Validates a command's public properties against their DataAnnotations attributes and throws a
/// <see cref="Exceptions.ValidationException"/> carrying two member-keyed maps when any rule fails: the
/// English messages and the index-aligned stable <c>errorCodes</c> a multilingual client translates on.
/// </summary>
/// <remarks>
/// It validates attribute-by-attribute (rather than through
/// <see cref="Validator.TryValidateObject(object, ValidationContext, ICollection{ValidationResult}, bool)"/>)
/// precisely so it knows <em>which</em> attribute failed and can name its code. Per-property semantics match
/// the framework's: a failing <see cref="RequiredAttribute"/> short-circuits the rest of that property's
/// rules, so a missing value reports <c>required</c> alone rather than also tripping every format rule.
/// Class-level attributes and <see cref="IValidatableObject"/> are not consulted — commands express their
/// rules per property, and the OpenAPI schema mirror does the same.
/// </remarks>
internal static class CommandValidator
{
    /// <summary>Validates all public properties of <paramref name="command"/>; throws on failure.</summary>
    /// <param name="command">The object to validate.</param>
    /// <exception cref="Exceptions.ValidationException">One or more validation rules failed.</exception>
    public static void Validate(object command)
    {
        Dictionary<string, List<string>> messages = [];
        Dictionary<string, List<FieldErrorCode>> codes = [];

        foreach (PropertyInfo property in command.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetIndexParameters().Length > 0)
                continue; // indexers have no single value to validate

            ValidationAttribute[] attributes = [.. property.GetCustomAttributes<ValidationAttribute>(inherit: true)];
            if (attributes.Length == 0)
                continue;

            object? value = property.GetValue(command);
            ValidationContext context = new(command) { MemberName = property.Name, DisplayName = property.Name };

            // Required runs first and short-circuits: a missing value reports `required`, not a pile of
            // format failures on the value it does not have — matching Validator.TryValidateObject.
            RequiredAttribute? required = attributes.OfType<RequiredAttribute>().FirstOrDefault();
            if (required is not null && Record(required, value, context, property.Name, messages, codes))
                continue;

            foreach (ValidationAttribute attribute in attributes)
            {
                if (attribute is RequiredAttribute)
                    continue;
                Record(attribute, value, context, property.Name, messages, codes);
            }
        }

        if (messages.Count == 0)
            return;

        throw new Exceptions.ValidationException(
            messages.ToDictionary(e => e.Key, e => e.Value.ToArray()),
            codes.ToDictionary(e => e.Key, e => e.Value.ToArray()));
    }

    /// <summary>Runs one attribute; on failure records its message and code under each reported member. Returns whether it failed.</summary>
    private static bool Record(
        ValidationAttribute attribute, object? value, ValidationContext context, string propertyName,
        Dictionary<string, List<string>> messages, Dictionary<string, List<FieldErrorCode>> codes)
    {
        ValidationResult? result = attribute.GetValidationResult(value, context);
        if (result is null || result == ValidationResult.Success)
            return false;

        FieldErrorCode code = ValidationRuleErrors.For(attribute);
        foreach (string member in result.MemberNames.DefaultIfEmpty(propertyName))
        {
            Append(messages, member, result.ErrorMessage ?? string.Empty);
            Append(codes, member, code);
        }
        return true;
    }

    private static void Append<T>(Dictionary<string, List<T>> map, string key, T value)
    {
        if (!map.TryGetValue(key, out List<T>? list))
            map[key] = list = [];
        list.Add(value);
    }
}

/// <summary>
/// Decorates a void command handler, running DataAnnotations validation on the command before
/// delegating to the wrapped <paramref name="inner"/> handler. Throws
/// <see cref="Exceptions.ValidationException"/> when validation fails.
/// </summary>
/// <typeparam name="TCommand">The command type being validated.</typeparam>
/// <param name="inner">The handler to invoke once validation passes.</param>
public sealed class ValidationDecorator<TCommand>(
    ICommandHandler<TCommand> inner) : ICommandHandler<TCommand> where TCommand : ICommand
{
    /// <summary>Validates the command, then forwards it to the inner handler.</summary>
    /// <param name="command">The command to validate and handle.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    public Task HandleAsync(TCommand command, CancellationToken ct = default)
    {
        CommandValidator.Validate(command);
        return inner.HandleAsync(command, ct);
    }
}

/// <summary>
/// Decorates a result-returning command handler, running DataAnnotations validation on the command
/// before delegating to the wrapped <paramref name="inner"/> handler. Throws
/// <see cref="Exceptions.ValidationException"/> when validation fails.
/// </summary>
/// <typeparam name="TCommand">The command type being validated.</typeparam>
/// <typeparam name="TResult">The result produced by the command.</typeparam>
/// <param name="inner">The handler to invoke once validation passes.</param>
public sealed class ValidationDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner) : ICommandHandler<TCommand, TResult> where TCommand : ICommand
{
    /// <summary>Validates the command, then forwards it to the inner handler.</summary>
    /// <param name="command">The command to validate and handle.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    /// <returns>The result produced by the inner handler.</returns>
    public Task<TResult> HandleAsync(TCommand command, CancellationToken ct = default)
    {
        CommandValidator.Validate(command);
        return inner.HandleAsync(command, ct);
    }
}

