using System.ComponentModel.DataAnnotations;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RecruitmentModule.Features.StaffManagementFeatures.ListStaffUsers;

/// <summary><c>GET /api/v1/recruitment/staff-users</c>: the staff, e.g. the recruiters to choose from for a job post.</summary>
internal sealed class ListStaffUsersEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapGroup("/staff-users")
            .RequireStaff()
            .MapGet("", static async (
                [AllowedValues(StaffRoles.Recruiter, StaffRoles.HiringManager, StaffRoles.Admin, null)] string? role,
                IQueryHandler<ListStaffUsersQuery, ListStaffUsersResponse> handler,
                CancellationToken ct) =>
                Results.Ok(await handler.HandleAsync(new ListStaffUsersQuery { Role = role }, ct)))
            .WithName("ListStaffUsers")
            .Produces<ListStaffUsersResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status502BadGateway)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
}
