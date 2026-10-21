using System.Security.Claims;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RecruitmentModule.Features.JobPostFeatures.CreateJobPost;

/// <summary><c>POST /api/v1/recruitment/job-posts</c>: a hiring manager creates a job post.</summary>
internal sealed class CreateJobPostEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapGroup("/job-posts")
            .RequireStaff(StaffPolicies.HiringManager)
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
                    Description = request.Description,
                    IsPublished = request.IsPublished,
                    OpenPositions = request.OpenPositions,
                    Salary = request.Salary,
                    PaymentType = request.PaymentType,
                    EmploymentType = request.EmploymentType,
                    RecruiterId = request.RecruiterId,
                    CreatedBy = user.GetStaffId(),
                };
                CreateJobPostResponse result = await handler.HandleAsync(command, ct);

                // No Location: there is no endpoint reading a single job post yet.
                return Results.Created((string?)null, result);
            })
            .WithName("CreateJobPost")
            .Produces<CreateJobPostResponse>(StatusCodes.Status201Created)
            .ProducesProblem(StatusCodes.Status502BadGateway) // Entra refused the recruiter lookup
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
}
