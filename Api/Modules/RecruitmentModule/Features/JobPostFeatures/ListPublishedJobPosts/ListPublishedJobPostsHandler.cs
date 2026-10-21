using Kernel.CQRS;
using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;

namespace RecruitmentModule.Features.JobPostFeatures.ListPublishedJobPosts;

/// <summary>One page of the published job posts, newest first, optionally filtered.</summary>
public sealed record ListPublishedJobPostsQuery
{
    /// <summary>The page size when the caller names none.</summary>
    public const int DefaultLimit = 20;

    /// <summary>The largest page size; the endpoint's rule uses the same number.</summary>
    public const int MaxLimit = 100;

    /// <summary>How many matching posts to skip.</summary>
    public int Offset { get; init; }

    /// <summary>How many posts to return, 1 to <see cref="MaxLimit"/>.</summary>
    public int Limit { get; init; } = DefaultLimit;

    /// <summary>Text the title or the location contains, ignoring case; every post when <c>null</c>.</summary>
    public string? Search { get; init; }

    /// <summary>Only posts with this employment type; every type when <c>null</c>.</summary>
    public string? EmploymentType { get; init; }
}

/// <summary>A page of published job posts and how many match in total.</summary>
/// <param name="Total">How many posts match the filter across all pages.</param>
public sealed record ListPublishedJobPostsResponse(
    IReadOnlyList<PublishedJobPostItem> Items, int Total, int Offset, int Limit);

/// <summary>A published job post as the list shows it.</summary>
/// <param name="Salary">The wage in EUR, per hour, month or year as <paramref name="PaymentType"/> says.</param>
public sealed record PublishedJobPostItem(
    Guid Id,
    string Title,
    string Location,
    string Department,
    string EmploymentType,
    decimal Salary,
    string PaymentType,
    DateTimeOffset CreatedAt);

internal sealed class ListPublishedJobPostsHandler(RecruitmentDbContext db)
    : IQueryHandler<ListPublishedJobPostsQuery, ListPublishedJobPostsResponse>
{
    public async Task<ListPublishedJobPostsResponse> HandleAsync(ListPublishedJobPostsQuery query, CancellationToken ct = default)
    {
        // Queries are not validated (implementation-guide.md §6): the endpoint's rules keep these in range, and the
        // handler does not rely on it.
        int offset = Math.Max(query.Offset, 0);
        int limit = Math.Clamp(query.Limit, 1, ListPublishedJobPostsQuery.MaxLimit);

        IQueryable<JobPost> posts = db.JobPosts.AsNoTracking().Where(p => p.IsPublished);

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            string pattern = $"%{EscapeLike(query.Search.Trim())}%";
            posts = posts.Where(p => EF.Functions.ILike(p.Title, pattern) || EF.Functions.ILike(p.Location, pattern));
        }

        if (query.EmploymentType is not null)
            posts = posts.Where(p => p.EmploymentType == query.EmploymentType);

        int total = await posts.CountAsync(ct);

        // Newest first. A v7 id is only ordered to the millisecond, so it breaks ties rather than leading; being unique,
        // it keeps the order total, which offset pages need to neither repeat nor skip a post.
        List<PublishedJobPostItem> items = await posts
            .OrderByDescending(p => p.CreatedAt)
            .ThenByDescending(p => p.Id)
            .Skip(offset)
            .Take(limit)
            .Select(p => new PublishedJobPostItem(
                p.Id, p.Title, p.Location, p.Department, p.EmploymentType, p.Salary, p.PaymentType, p.CreatedAt))
            .ToListAsync(ct);

        return new ListPublishedJobPostsResponse(items, total, offset, limit);
    }

    /// <summary>Makes the caller's text match literally: <c>%</c> and <c>_</c> are LIKE wildcards, <c>\</c> its escape.</summary>
    private static string EscapeLike(string text) =>
        text.Replace(@"\", @"\\", StringComparison.Ordinal)
            .Replace("%", @"\%", StringComparison.Ordinal)
            .Replace("_", @"\_", StringComparison.Ordinal);
}
