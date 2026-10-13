using Kernel.Errors;

namespace Kernel.Exceptions;

/// <summary>
/// Accumulates field-keyed validation failures so a handler can gather every problem in one pass —
/// rather than failing on the first — and report them together. Each failure carries a stable
/// <c>module.reason</c> code the client translates on, alongside the English message.
/// Call <see cref="ThrowIfAny"/> at the end to raise a <see cref="ValidationException"/> (HTTP 400) when
/// anything was collected. Shared by handlers across modules so the accumulate-then-throw pattern lives in
/// one place.
/// </summary>
public sealed class ValidationErrors
{
    private readonly Dictionary<string, List<string>> _messages = [];
    private readonly Dictionary<string, List<FieldErrorCode>> _codes = [];

    /// <summary>Whether any error has been recorded.</summary>
    public bool HasErrors => _messages.Count > 0;

    /// <summary>Records <paramref name="message"/> and its <paramref name="code"/> against the field named <paramref name="key"/>.</summary>
    /// <param name="key">The offending member/field name.</param>
    /// <param name="code">The stable <c>module.reason</c> code the client translates on.</param>
    /// <param name="message">The human-readable error message.</param>
    public void Add(string key, FieldErrorCode code, string message)
    {
        if (!_messages.TryGetValue(key, out List<string>? messages))
        {
            _messages[key] = messages = [];
            _codes[key] = [];
        }
        messages.Add(message);
        _codes[key].Add(code);
    }

    /// <summary>Throws a <see cref="ValidationException"/> with the collected errors when there are any.</summary>
    /// <exception cref="ValidationException">One or more errors were recorded.</exception>
    public void ThrowIfAny()
    {
        if (_messages.Count > 0)
            throw new ValidationException(
                _messages.ToDictionary(e => e.Key, e => e.Value.ToArray()),
                _codes.ToDictionary(e => e.Key, e => e.Value.ToArray()));
    }
}
