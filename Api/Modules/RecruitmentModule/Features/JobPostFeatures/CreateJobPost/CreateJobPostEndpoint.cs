using System.Security.Claims;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RecruitmentModule.Features.JobPostFeatures.CreateJobPost;

/// <summary><c>POST /api/v1/recruitment/job-posts</c>: a staff member creates a job post.</summary>
internal sealed class CreateJobPostEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapGroup("/job-posts")
            .RequireStaff()
            .MapPost("", static async (
                CreateJobPostRequest request,
                ClaimsPrincipal user,
                ICommandHandler<CreateJobPostCommand, CreateJobPostResponse> handler,
                CancellationToken ct) =>
            {
                CreateJobPostCommand command = new() // manual map (ADR-A012)
                {
                    Title = request.Title,
                    Location = request.Location,
                    Department = request.Department,
                    IsPublished = request.IsPublished,
                    OpenPositions = request.OpenPositions,
                    PaymentType = request.PaymentType,
                    EmploymentType = request.EmploymentType,
                    CreatedBy = user.GetStaffId(),
                };
                CreateJobPostResponse result = await handler.HandleAsync(command, ct);

                // No Location: there is no endpoint reading a single job post yet.
                return Results.Created((string?)null, result);
            })
            .WithName("CreateJobPost")
            .Produces<CreateJobPostResponse>(StatusCodes.Status201Created)
            .ProducesLink(StatusCodes.Status201Created, "AssignJobPostRecruiter",
                ("jobPostId", "$response.body#/id"));
}
