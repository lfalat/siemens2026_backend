using System.ComponentModel.DataAnnotations;
using System.Reflection;
using Kernel.CQRS;
using Kernel.Errors;
using Kernel.Idempotency;

namespace ApiService.OpenAPI;

/// <summary>
/// Every code one operation can answer with, as the OpenAPI document lists it (ADR-A017): what the endpoint and its
/// groups declare, the codes the host adds by rule (<see cref="ErrorCodeDeclarationExtensions.Declared"/>), the
/// DataAnnotations rule codes of the command it dispatches, and the idempotency middleware's rejections where that
/// middleware applies.
/// </summary>
internal static class OperationErrorCodes
{
    /// <summary>The codes of the endpoint with <paramref name="metadata"/>.</summary>
    /// <param name="metadata">The endpoint's metadata.</param>
    /// <param name="httpMethod">The endpoint's HTTP method.</param>
    /// <param name="idempotency">The host's idempotency settings — whether a missing key is refused.</param>
    public static IReadOnlyList<IErrorCode> For(
        IReadOnlyList<object> metadata, string? httpMethod, IdempotencyOptions idempotency)
    {
        List<IErrorCode> codes = [.. ErrorCodeDeclarationExtensions.Declared(metadata, httpMethod)];

        List<FieldErrorCode> rules = [.. RuleCodes(metadata)];
        if (rules.Count > 0)
        {
            codes.AddRange(rules);
            codes.Add(CommonErrors.ValidationFailed);
        }

        if (httpMethod is not null && IdempotencyMiddleware.IsCoveredMethod(httpMethod)
            && !metadata.OfType<IdempotencyDisabled>().Any())
        {
            if (idempotency.RequireKey)
                codes.Add(CommonErrors.IdempotencyKeyMissing);
            codes.AddRange([
                CommonErrors.IdempotencyKeyTooLong, CommonErrors.IdempotencyInProgress,
                CommonErrors.IdempotencyPayloadMismatch,
            ]);
        }

        return [.. codes.Distinct()];
    }

    /// <summary>
    /// The rule codes the validation decorator can answer for the command this endpoint dispatches — read off the
    /// <c>ICommandHandler</c> its delegate takes, since the command, not the request contract, carries the attributes.
    /// </summary>
    private static IEnumerable<FieldErrorCode> RuleCodes(IReadOnlyList<object> metadata)
    {
        if (metadata.OfType<MethodInfo>().FirstOrDefault() is not { } handler)
            return [];

        return handler.GetParameters()
            .Select(static p => p.ParameterType)
            .Where(static t => t.IsGenericType
                               && (t.GetGenericTypeDefinition() == typeof(ICommandHandler<>)
                                   || t.GetGenericTypeDefinition() == typeof(ICommandHandler<,>)))
            .Select(static t => t.GetGenericArguments()[0])
            .SelectMany(static command => command.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .SelectMany(static p => p.GetCustomAttributes<ValidationAttribute>(inherit: true))
            .Select(ValidationRuleErrors.For)
            .Distinct();
    }
}
