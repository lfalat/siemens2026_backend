using Kernel.CQRS;
using Kernel.Exceptions;
using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Infrastructure;

namespace RecruitmentModule.Features.JobPostFeatures.GetPublishedJobPost;

/// <summary>One published job post, as an applicant reads it.</summary>
public sealed record GetPublishedJobPostQuery(Guid JobPostId);

/// <summary>A published job post. Leaves out what applicants have no business seeing: the recruiter and the audit fields.</summary>
/// <param name="Salary">The wage in EUR, per hour, month or year as <paramref name="PaymentType"/> says.</param>
public sealed record GetPublishedJobPostResponse(
    Guid Id,
    string Title,
    string Location,
    string Department,
    string Description,
    int OpenPositions,
    decimal Salary,
    string PaymentType,
    string EmploymentType,
    DateTimeOffset CreatedAt);

internal sealed class GetPublishedJobPostHandler(RecruitmentDbContext db)
    : IQueryHandler<GetPublishedJobPostQuery, GetPublishedJobPostResponse>
{
    public async Task<GetPublishedJobPostResponse> HandleAsync(GetPublishedJobPostQuery query, CancellationToken ct = default) =>
        // A draft answers 404 like a missing post, so its id does not reveal that it exists.
        await db.JobPosts.AsNoTracking()
            .Where(p => p.Id == query.JobPostId && p.IsPublished)
            .Select(p => new GetPublishedJobPostResponse(
                p.Id, p.Title, p.Location, p.Department, p.Description, p.OpenPositions, p.Salary, p.PaymentType,
                p.EmploymentType, p.CreatedAt))
            .SingleOrDefaultAsync(ct)
        ?? throw new NotFoundException("Pracovná ponuka neexistuje.");
}
