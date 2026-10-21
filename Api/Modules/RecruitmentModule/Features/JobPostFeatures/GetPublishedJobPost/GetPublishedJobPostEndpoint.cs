using Kernel.CQRS;
using Kernel.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace RecruitmentModule.Features.JobPostFeatures.GetPublishedJobPost;

/// <summary><c>GET /api/v1/recruitment/job-posts/{jobPostId}</c>: one published job post. Public, like the list.</summary>
internal sealed class GetPublishedJobPostEndpoint : IEndpoint
{
    public void MapEndpoint(IEndpointRouteBuilder group) =>
        group.MapGet("/job-posts/{jobPostId:guid}", static async (
                Guid jobPostId,
                IQueryHandler<GetPublishedJobPostQuery, GetPublishedJobPostResponse> handler,
                CancellationToken ct) =>
                Results.Ok(await handler.HandleAsync(new GetPublishedJobPostQuery(jobPostId), ct)))
            .WithName("GetPublishedJobPost")
            .Produces<GetPublishedJobPostResponse>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status404NotFound);
}
