using System.ComponentModel.DataAnnotations;
using Kernel.Auth;
using Kernel.CQRS;
using RecruitmentModule.Infrastructure.Entra;

namespace RecruitmentModule.Features.StaffManagementFeatures.ListStaffUsers;

/// <summary>The staff, optionally only those holding one role.</summary>
public sealed record ListStaffUsersQuery
{
    /// <summary>Only staff with this role; everyone when <c>null</c>.</summary>
    [AllowedValues(StaffRoles.Recruiter, StaffRoles.HiringManager, StaffRoles.Admin, null)]
    public string? Role { get; init; }
}

/// <summary>Every matching staff member, by name. Unpaged: one company has few staff.</summary>
public sealed record ListStaffUsersResponse(IReadOnlyList<StaffUserItem> Users);

/// <summary>One staff member.</summary>
/// <param name="Id">The Entra object id (<c>oid</c>), e.g. a job post's <c>recruiterId</c>.</param>
/// <param name="DisplayName">The name shown in Entra.</param>
/// <param name="Roles">The assigned app roles; one, unless Entra was given more.</param>
public sealed record StaffUserItem(Guid Id, string DisplayName, IReadOnlyList<string> Roles);

internal sealed class ListStaffUsersHandler(IStaffDirectory directory)
    : IQueryHandler<ListStaffUsersQuery, ListStaffUsersResponse>
{
    public async Task<ListStaffUsersResponse> HandleAsync(ListStaffUsersQuery query, CancellationToken ct = default)
    {
        IReadOnlyList<StaffMember> staff = await directory.ListStaffAsync(ct);

        // An unknown role matches nobody, so the endpoint's AllowedValues check needs no second one here.
        return new ListStaffUsersResponse(staff
            .Where(s => query.Role is null || s.Roles.Contains(query.Role))
            .OrderBy(s => s.DisplayName, StringComparer.InvariantCultureIgnoreCase)
            .Select(s => new StaffUserItem(s.Id, s.DisplayName, s.Roles))
            .ToList());
    }
}
