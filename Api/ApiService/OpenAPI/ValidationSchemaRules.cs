using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace ApiService.OpenAPI;

/// <summary>
/// Copies DataAnnotation constraints onto an OpenAPI schema. Shared by the two places validation reaches
/// the document from: <see cref="CommandValidationSchemaTransformer"/> for a request body's properties, and
/// <see cref="ParameterValidationTransformer"/> for a path or query parameter — which the framework's own
/// generator does not carry across at all.
/// </summary>
/// <remarks>
/// Everything this fails to copy becomes a value the schema calls legal and the API rejects, or the other
/// way round. That is the "API rejected/accepted a schema-compliant request" class of contract finding
/// (ADR-009, <c>docs/architecture/contract-testing.md</c>).
/// </remarks>
internal static class ValidationSchemaRules
{
    internal static void Apply(
        IEnumerable<ValidationAttribute> attributes,
        OpenApiSchema target,
        OpenApiSchema owner,
        string jsonName)
    {
        foreach (ValidationAttribute attr in attributes)
        {
            switch (attr)
            {
                case RequiredAttribute required:
                    (owner.Required ??= new HashSet<string>()).Add(jsonName);

                    // On a string, [Required] means more than "the key is present": by default it also
                    // rejects "" and whitespace. Left unsaid, the schema calls an empty string valid and
                    // the API answers 400 to a request that satisfies it (ADR-009).
                    if (!required.AllowEmptyStrings
                        && target.Type?.HasFlag(JsonSchemaType.String) is true
                        && target.MinLength is null or 0)
                    {
                        target.MinLength = 1;
                    }

                    break;

                case EmailAddressAttribute:
                    target.Format = "email";
                    break;

                case StringLengthAttribute s:
                    target.MaxLength = s.MaximumLength;
                    if (s.MinimumLength > 0) target.MinLength = s.MinimumLength;
                    break;

                case MaxLengthAttribute m when m.Length >= 0:
                    target.MaxLength = m.Length;
                    break;

                case MinLengthAttribute m:
                    target.MinLength = m.Length;
                    break;

                case RegularExpressionAttribute r:
                    target.Pattern = r.Pattern;
                    break;

                case RangeAttribute range:
                    // Exclusive bounds are a separate keyword in JSON Schema, not a flag on the inclusive
                    // one. Mapping "strictly positive" to `minimum: 0` would advertise 0 as acceptable —
                    // which is exactly the kind of schema-legal value the API then rejects (ADR-009).
                    if (range.MinimumIsExclusive)
                        target.ExclusiveMinimum = Convert.ToString(range.Minimum, CultureInfo.InvariantCulture);
                    else
                        target.Minimum = Convert.ToString(range.Minimum, CultureInfo.InvariantCulture);

                    // An open upper bound is written as `double.MaxValue` because RangeAttribute has no way
                    // to say "no maximum". Emitting it would put a 1.79e308 ceiling in the document, which
                    // says nothing true about the field.
                    if (!IsUnbounded(range.Maximum))
                    {
                        if (range.MaximumIsExclusive)
                            target.ExclusiveMaximum = Convert.ToString(range.Maximum, CultureInfo.InvariantCulture);
                        else
                            target.Maximum = Convert.ToString(range.Maximum, CultureInfo.InvariantCulture);
                    }

                    break;

                case AllowedValuesAttribute allowed:
                    target.Enum = allowed.Values
                        .Where(v => v is not null)
                        .Select(v => (JsonNode)JsonValue.Create(v!.ToString())!)
                        .ToList();
                    break;
            }

            // An attribute's message is a reasonable description when the property has none of its own —
            // but only when it reads as prose. Several attributes carry a framework default containing
            // format placeholders ("The {0} field does not equal any of the values specified in
            // AllowedValuesAttribute"), which says nothing and puts a literal {0} in the document.
            if (!string.IsNullOrWhiteSpace(attr.ErrorMessage)
                && !attr.ErrorMessage.Contains('{', StringComparison.Ordinal)
                && string.IsNullOrEmpty(target.Description))
            {
                target.Description = attr.ErrorMessage;
            }
        }
    }

    /// <summary>
    /// Whether a <see cref="RangeAttribute"/> bound is the "no limit" placeholder rather than a real one.
    /// </summary>
    private static bool IsUnbounded(object bound) => bound switch
    {
        double d => d >= double.MaxValue,
        float f => f >= float.MaxValue,
        int i => i == int.MaxValue,
        long l => l == long.MaxValue,
        _ => false,
    };


}
