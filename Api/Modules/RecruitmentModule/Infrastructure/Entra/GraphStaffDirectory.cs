using System.Globalization;
using Azure.Identity;
using Kernel.Exceptions;
using Microsoft.Extensions.Options;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using Microsoft.Graph.Models.ODataErrors;

namespace RecruitmentModule.Infrastructure.Entra;

/// <summary>
/// <see cref="IStaffDirectory"/> over Microsoft Graph, signed in as the VM's managed identity. That identity holds the
/// Graph application permissions <c>User.ReadWrite.All</c>, <c>Application.Read.All</c> and
/// <c>AppRoleAssignment.ReadWrite.All</c> (iam.md §4.2, <c>scripts/azure/graph-grant-identity.sh</c>).
/// </summary>
internal sealed class GraphStaffDirectory(GraphServiceClient graph, IOptions<StaffDirectoryOptions> options)
    : IStaffDirectory
{
    /// <summary>Attempts at an app-role assignment; see <see cref="AssignAppRoleAsync"/>.</summary>
    private const int AssignmentAttempts = 4;

    /// <inheritdoc />
    public Task<bool> UserExistsAsync(string username, CancellationToken ct) =>
        CallAsync("look up the account", async () =>
        {
            try
            {
                await graph.Users[UserPrincipalName(username)].GetAsync(r => r.QueryParameters.Select = ["id"], ct);
                return true;
            }
            catch (ODataError e) when (e.ResponseStatusCode == 404)
            {
                return false;
            }
        }, ct);

    /// <inheritdoc />
    public Task<StaffAccount> CreateUserAsync(string displayName, string username, string password, CancellationToken ct) =>
        CallAsync("create the account", async () =>
        {
            User user = await graph.Users.PostAsync(new User
            {
                AccountEnabled = true,
                DisplayName = displayName,
                MailNickname = username,
                UserPrincipalName = UserPrincipalName(username),
                PasswordProfile = new PasswordProfile { Password = password, ForceChangePasswordNextSignIn = true },
            }, cancellationToken: ct) ?? throw new InvalidOperationException("Graph answered the user creation with no body.");

            return new StaffAccount(Guid.Parse(user.Id!), user.UserPrincipalName!);
        }, ct);

    /// <inheritdoc />
    public async Task AssignAppRoleAsync(Guid userId, string role, CancellationToken ct)
    {
        string apiClientId = Configured().ApiClientId!;
        ServicePrincipal api = await CallAsync("read the API's enterprise application", () =>
                graph.ServicePrincipalsWithAppId(apiClientId).GetAsync(r => r.QueryParameters.Select = ["id", "appRoles"], ct), ct)
            ?? throw new InvalidOperationException($"The tenant has no enterprise application for the API registration {apiClientId}.");

        AppRole appRole = api.AppRoles?.FirstOrDefault(r => r.Value == role && r.IsEnabled == true)
            ?? throw new InvalidOperationException($"The API registration defines no enabled app role '{role}'.");

        AppRoleAssignment assignment = new() { PrincipalId = userId, ResourceId = Guid.Parse(api.Id!), AppRoleId = appRole.Id };

        await CallAsync("assign the app role", async () =>
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    return await graph.ServicePrincipals[api.Id].AppRoleAssignedTo.PostAsync(assignment, cancellationToken: ct);
                }
                // An account created a moment ago may not have reached the directory replica that serves the
                // assignment yet, which then answers 404. It catches up within seconds.
                catch (ODataError e) when (e.ResponseStatusCode == 404 && attempt < AssignmentAttempts)
                {
                    await Task.Delay(TimeSpan.FromSeconds(attempt), ct);
                }
            }
        }, ct);
    }

    /// <inheritdoc />
    public Task DeleteUserAsync(Guid userId, CancellationToken ct) =>
        CallAsync("delete the account", async () =>
        {
            await graph.Users[userId.ToString()].DeleteAsync(cancellationToken: ct);
            return true;
        }, ct);

    private string UserPrincipalName(string username) => $"{username}@{Configured().UserDomain}";

    private StaffDirectoryOptions Configured() =>
        options.Value is { IsConfigured: true } value
            ? value
            : throw new InvalidOperationException(
                $"{StaffDirectoryOptions.SectionName}:ApiClientId and {StaffDirectoryOptions.SectionName}:UserDomain must be set to manage staff accounts.");

    /// <summary>Runs a Graph call, turning its failures into the exceptions the host answers with 502 and 503.</summary>
    private static async Task<T> CallAsync<T>(string action, Func<Task<T>> call, CancellationToken ct)
    {
        try
        {
            return await call();
        }
        catch (ODataError e) when (e.ResponseStatusCode is 429 or >= 500)
        {
            throw new ServiceUnavailableException($"Microsoft Graph could not {action} right now.", e);
        }
        catch (ODataError e)
        {
            // Graph's own code (e.g. Authorization_RequestDenied when a permission is missing) tells the admin why.
            throw new ExternalServiceException($"Microsoft Graph refused to {action}: {e.Error?.Code ?? e.ResponseStatusCode.ToString(CultureInfo.InvariantCulture)}.", e);
        }
        catch (AuthenticationFailedException e)
        {
            throw new ExternalServiceException("The API could not sign in to Microsoft Graph.", e);
        }
        catch (HttpRequestException e)
        {
            throw new ServiceUnavailableException("Microsoft Graph could not be reached.", e);
        }
        catch (TaskCanceledException e) when (!ct.IsCancellationRequested)
        {
            throw new ServiceUnavailableException("Microsoft Graph did not answer in time.", e);
        }
    }
}
