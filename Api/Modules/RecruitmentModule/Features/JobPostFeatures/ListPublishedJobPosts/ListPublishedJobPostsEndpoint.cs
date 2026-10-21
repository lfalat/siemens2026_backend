using System.ComponentModel.DataAnnotations;
using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using RecruitmentModule.Domain;

namespace RecruitmentModule.Features.JobPostFeatures.ListPublishedJobPosts;

/// <summary>
/// <c>GET /api/v1/recruitment/job-posts</c>: the published job posts, a page at a time. Public: applicants browse them
/// anonymously, so drafts never appear.
/// </summary>
internal sealed class ListPublishedJobPostsEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapGet("/job-posts", static async (
                [Range(0, int.MaxValue)] int? offset,
                [Range(1, ListPublishedJobPostsQuery.MaxLimit)] int? limit,
                [StringLength(200)] string? search,
                [AllowedValues(
                    EmploymentTypes.FullTime, EmploymentTypes.PartTime, EmploymentTypes.FixedTerm,
                    EmploymentTypes.WorkPerformanceAgreement, EmploymentTypes.WorkActivityAgreement,
                    EmploymentTypes.StudentWorkAgreement, EmploymentTypes.SelfEmployed, null)]
                string? employmentType,
                IQueryHandler<ListPublishedJobPostsQuery, ListPublishedJobPostsResponse> handler,
                CancellationToken ct) =>
            {
                ListPublishedJobPostsQuery query = new()
                {
                    Offset = offset ?? 0,
                    Limit = limit ?? ListPublishedJobPostsQuery.DefaultLimit,
                    Search = search,
                    EmploymentType = employmentType,
                };
                return Results.Ok(await handler.HandleAsync(query, ct));
            })
            .WithName("ListPublishedJobPosts")
            .Produces<ListPublishedJobPostsResponse>(StatusCodes.Status200OK)
            .ProducesLink(StatusCodes.Status200OK, "GetPublishedJobPost",
                ("jobPostId", "$response.body#/items/0/id"));
}
