using System.Security.Claims;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RecruitmentModule.Features.JobPostFeatures.AssignJobPostRecruiter;

/// <summary>
/// <c>PUT /api/v1/recruitment/job-posts/{jobPostId}/recruiter</c>: a hiring manager assigns the recruiter responsible
/// for the hiring.
/// </summary>
internal sealed class AssignJobPostRecruiterEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapGroup("/job-posts")
            .RequireStaff(StaffPolicies.HiringManager)
            .MapPut("/{jobPostId:guid}/recruiter", static async (
                Guid jobPostId,
                AssignJobPostRecruiterRequest request,
                ClaimsPrincipal user,
                ICommandHandler<AssignJobPostRecruiterCommand, AssignJobPostRecruiterResponse> handler,
                CancellationToken ct) =>
            {
                AssignJobPostRecruiterCommand command = new() // manual map (ADR-A012)
                {
                    JobPostId = jobPostId,
                    RecruiterId = request.RecruiterId,
                    Version = request.Version,
                    UpdatedBy = user.GetStaffId(),
                };
                return Results.Ok(await handler.HandleAsync(command, ct));
            })
            .WithName("AssignJobPostRecruiter")
            .Produces<AssignJobPostRecruiterResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status502BadGateway) // Entra refused the staff lookup
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
}
