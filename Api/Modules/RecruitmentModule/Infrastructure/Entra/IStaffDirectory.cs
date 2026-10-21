namespace RecruitmentModule.Infrastructure.Entra;

/// <summary>A staff account as the directory created it.</summary>
/// <param name="Id">The account's Entra object id (<c>oid</c>).</param>
/// <param name="UserPrincipalName">The sign-in name, <c>username@domain</c>.</param>
internal sealed record StaffAccount(Guid Id, string UserPrincipalName);

/// <summary>A user holding at least one HealthyHires app role.</summary>
/// <param name="Id">The account's Entra object id (<c>oid</c>).</param>
/// <param name="DisplayName">The name shown in Entra.</param>
/// <param name="Roles">The assigned app roles (<c>Kernel.Auth.StaffRoles</c>); one, unless Entra was given more.</param>
internal sealed record StaffMember(Guid Id, string DisplayName, IReadOnlyList<string> Roles);

/// <summary>
/// Staff accounts in the company's Entra tenant (iam.md §2.4). Failures surface as <c>Kernel.Exceptions</c>:
/// <c>ExternalServiceException</c> when Entra refuses a call, <c>ServiceUnavailableException</c> when it cannot be reached.
/// </summary>
internal interface IStaffDirectory
{
    /// <summary>Whether the tenant already has an account signing in as <paramref name="username"/>.</summary>
    /// <param name="username">The sign-in name before the <c>@</c>.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<bool> UserExistsAsync(string username, CancellationToken ct);

    /// <summary>Creates an enabled account that must change <paramref name="password"/> at its first sign-in.</summary>
    /// <param name="displayName">The name shown in Entra.</param>
    /// <param name="username">The sign-in name before the <c>@</c>.</param>
    /// <param name="password">The temporary password.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<StaffAccount> CreateUserAsync(string displayName, string username, string password, CancellationToken ct);

    /// <summary>Assigns the account one of the API registration's app roles (<c>Kernel.Auth.StaffRoles</c>).</summary>
    /// <param name="userId">The account's object id.</param>
    /// <param name="role">The app role's value, e.g. <c>Recruiter</c>.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task AssignAppRoleAsync(Guid userId, string role, CancellationToken ct);

    /// <summary>
    /// Every user assigned an app role on the API registration, the staff of HealthyHires. Unpaged: one company has
    /// few staff.
    /// </summary>
    /// <param name="ct">Token to cancel the operation.</param>
    Task<IReadOnlyList<StaffMember>> ListStaffAsync(CancellationToken ct);

    /// <summary>Deletes the account. Entra keeps it restorable for 30 days.</summary>
    /// <param name="userId">The account's object id.</param>
    /// <param name="ct">Token to cancel the operation.</param>
    Task DeleteUserAsync(Guid userId, CancellationToken ct);
}
