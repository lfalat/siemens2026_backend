using System.ComponentModel.DataAnnotations;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Exceptions;
using Kernel.Telemetry;
using Microsoft.Extensions.Logging;
using RecruitmentModule.Domain;
using RecruitmentModule.Infrastructure.Entra;

namespace RecruitmentModule.Features.StaffManagementFeatures.CreateUser;

/// <summary>Creates a staff account in Entra with the <see cref="StaffRoles.Recruiter"/> role.</summary>
public sealed record CreateUserCommand : ICommand
{
    /// <inheritdoc />
    public static string EventName => ModuleEvents.StaffUserCreated;

    /// <summary>The name shown in Entra and in HealthyHires.</summary>
    [Required, StringLength(256)]
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// The sign-in name before the <c>@</c>. Letters, digits, <c>.</c>, <c>_</c> and <c>-</c>, starting and ending with
    /// a letter or digit: a subset of what Entra accepts in both the user principal name and the mail nickname.
    /// </summary>
    [Required, StringLength(64)]
    [RegularExpression("^[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?$",
        ErrorMessage = "Use letters, digits, '.', '_' and '-', starting and ending with a letter or digit.")]
    public string Username { get; init; } = string.Empty;
}

/// <summary>The created account and its first password.</summary>
/// <param name="Id">The Entra object id (<c>oid</c>).</param>
/// <param name="UserPrincipalName">What the user signs in with.</param>
/// <param name="DisplayName">The name shown in Entra and in HealthyHires.</param>
/// <param name="Role">The assigned app role.</param>
/// <param name="TemporaryPassword">
/// The password to hand to the user, who must change it at the first sign-in. Returned only here and stored nowhere.
/// </param>
public sealed record CreateUserResponse(
    Guid Id, string UserPrincipalName, string DisplayName, string Role, string TemporaryPassword);

internal sealed partial class CreateUserHandler(IStaffDirectory directory, ILogger<CreateUserHandler> logger)
    : ICommandHandler<CreateUserCommand, CreateUserResponse>
{
    /// <summary>Every account this feature creates is a recruiter; other roles are assigned in Entra.</summary>
    private const string Role = StaffRoles.Recruiter;

    public async Task<CreateUserResponse> HandleAsync(CreateUserCommand command, CancellationToken ct = default)
    {
        if (await directory.UserExistsAsync(command.Username, ct))
            throw new ConflictException($"An account named '{command.Username}' already exists.")
            {
                Code = RecruitmentErrors.UsernameTaken,
            };

        string password = TemporaryPassword.Generate();
        StaffAccount account = await directory.CreateUserAsync(command.DisplayName, command.Username, password, ct);

        try
        {
            await directory.AssignAppRoleAsync(account.Id, Role, ct);
        }
        catch
        {
            // Without a role the account cannot sign in to HealthyHires (assignment is required), yet it holds the
            // username, so a retry would only answer 409. Remove it, so the administrator can simply try again.
            await DeleteAsync(account.Id);
            throw;
        }

        LogCreated(logger, account.Id, Role);
        return new CreateUserResponse(account.Id, account.UserPrincipalName, command.DisplayName, Role, password);
    }

    private async Task DeleteAsync(Guid userId)
    {
        try
        {
            // Not the request's token: the account has to go even when the caller has given up.
            await directory.DeleteUserAsync(userId, CancellationToken.None);
        }
        catch (Exception e)
        {
            LogDeleteFailed(logger, e, userId);
        }
    }

    [LoggerMessage(EventId = 4100, EventName = "recruitment.staff_user.created", Level = LogLevel.Information,
        Message = "Created staff account {UserId} with role {Role}.")]
    private static partial void LogCreated(ILogger logger, [PersonalData] Guid userId, string role);

    [LoggerMessage(EventId = 4101, EventName = "recruitment.staff_user.delete_failed", Level = LogLevel.Error,
        Message = "Staff account {UserId} was created without its role and could not be deleted; delete it in Entra.")]
    private static partial void LogDeleteFailed(ILogger logger, Exception exception, [PersonalData] Guid userId);
}
