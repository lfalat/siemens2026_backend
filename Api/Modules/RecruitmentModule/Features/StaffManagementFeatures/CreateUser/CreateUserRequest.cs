namespace RecruitmentModule.Features.StaffManagementFeatures.CreateUser;

/// <summary>The body of <c>POST /api/v1/recruitment/staff-users</c>: a new staff account.</summary>
public sealed record CreateUserRequest
{
    /// <summary>The name shown in Entra and in HealthyHires.</summary>
    /// <example>Jana Nováková</example>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>The sign-in name before the <c>@</c>; the domain is the company tenant's.</summary>
    /// <example>jana.novakova</example>
    public string Username { get; init; } = string.Empty;
}
