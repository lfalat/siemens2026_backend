namespace RecruitmentModule.Infrastructure.Entra;

/// <summary>
/// Where new staff accounts are created (iam.md §2.4), bound from the same <c>Entra</c> section the host's sign-in reads.
/// None of these is a secret: Graph is called with the VM's managed identity.
/// </summary>
/// <remarks>
/// Required outside Development, where startup fails without them. In Development they may be left empty: the API
/// runs, and creating a staff user fails.
/// </remarks>
internal sealed class StaffDirectoryOptions
{
    /// <summary>The configuration section these are bound from.</summary>
    public const string SectionName = "Entra";

    /// <summary>
    /// The Application (client) id of the HealthyHires <b>API</b> registration, whose enterprise application defines the
    /// app roles a new user is assigned.
    /// </summary>
    public string? ApiClientId { get; set; }

    /// <summary>A verified domain of the tenant; new accounts sign in as <c>username@UserDomain</c>.</summary>
    public string? UserDomain { get; set; }

    /// <summary>Whether both values are set and well-formed.</summary>
    public bool IsConfigured => Guid.TryParse(ApiClientId, out _) && !string.IsNullOrWhiteSpace(UserDomain);
}
