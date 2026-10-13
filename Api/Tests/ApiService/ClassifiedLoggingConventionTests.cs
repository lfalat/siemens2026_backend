using System.Reflection;
using Microsoft.Extensions.Compliance.Classification;
using Microsoft.Extensions.Logging;

namespace Tests.ApiService;

/// <summary>
/// Guards the half of the redaction pipeline that <see cref="TelemetryRedactionTests"/> cannot reach. Those tests
/// prove a <em>classified</em> argument is erased before export; nothing in them stops someone adding a
/// <c>[LoggerMessage]</c> parameter that carries personal data without a classification, which the pipeline would
/// then export in clear precisely because it was never told to redact it.
/// </summary>
public class ClassifiedLoggingConventionTests
{
    /// <summary>
    /// Every assembly of ours that may declare log methods. Named rather than discovered so that a module dropping
    /// out of the test project's reference graph fails here instead of silently going unchecked. Add each module
    /// here when it is created.
    /// </summary>
    private static readonly string[] ProductionAssemblyNames = ["ApiService", "Kernel", "ServiceDefaults"];

    /// <summary>
    /// Parameter-name fragments that mean the value identifies, or can be linked to, a person. Deliberately short
    /// and unambiguous: a fragment such as "name" would also match a job offer, a blob container and a module, and a
    /// rule that fires on those teaches people to work around it rather than to classify the argument.
    /// </summary>
    private static readonly string[] PersonalFragments =
    [
        "userid", "email", "phone", "firstname", "lastname", "fullname", "address", "personid", "actorid",
        "applicant", "candidate",
    ];

    private static IEnumerable<MethodInfo> LogMethods() =>
        from name in ProductionAssemblyNames
        from type in Assembly.Load(name).GetTypes()
        from method in type.GetMethods(
            BindingFlags.Public | BindingFlags.NonPublic |
            BindingFlags.Static | BindingFlags.Instance | BindingFlags.DeclaredOnly)
        where method.GetCustomAttribute<LoggerMessageAttribute>() is not null
        select method;

    [Fact]
    public void Personal_logger_message_parameters_carry_a_data_classification()
    {
        List<string> unclassified = [];
        int inspected = 0;

        foreach (MethodInfo method in LogMethods())
        {
            inspected++;
            foreach (ParameterInfo parameter in method.GetParameters())
            {
                string name = parameter.Name?.ToLowerInvariant() ?? string.Empty;
                if (!PersonalFragments.Any(fragment => name.Contains(fragment, StringComparison.Ordinal)))
                    continue;

                if (parameter.GetCustomAttributes().OfType<DataClassificationAttribute>().Any())
                    continue;

                unclassified.Add($"{method.DeclaringType?.FullName}.{method.Name}({parameter.Name})");
            }
        }

        // A scan that finds nothing would pass for the wrong reason — assembly names drifting, or the attribute no
        // longer surviving into metadata.
        Assert.True(inspected > 0, "No [LoggerMessage] methods were found; the scan is not looking at anything.");
        Assert.Empty(unclassified);
    }
}
