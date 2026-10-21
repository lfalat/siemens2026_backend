using Kernel.Exceptions;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Features.JobPostFeatures.GetPublishedJobPost;
using RecruitmentModule.Infrastructure;
using Tests.Shared;

namespace Tests.RecruitmentModule.Features.JobPostFeatures.GetPublishedJobPost;

/// <summary>Reading one published job post; a draft is as invisible as a missing one.</summary>
public class GetPublishedJobPostHandlerTests
{
    [Fact]
    public async Task Returns_a_published_post()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = JobPostSeed.Post("Elektrikár");
        await JobPostSeed.AddAsync(db, ct, post);

        GetPublishedJobPostResponse response = await new GetPublishedJobPostHandler(db).HandleAsync(new(post.Id), ct);

        Assert.Equal((post.Id, "Elektrikár", "Opis pozície Elektrikár.", 1500m), (response.Id, response.Title, response.Description, response.Salary));
    }

    [Fact]
    public async Task A_draft_is_404()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost draft = JobPostSeed.Post("Koncept", isPublished: false);
        await JobPostSeed.AddAsync(db, ct, draft);

        await Assert.ThrowsAsync<NotFoundException>(() => new GetPublishedJobPostHandler(db).HandleAsync(new(draft.Id), ct));
    }

    [Fact]
    public async Task A_missing_post_is_404()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();

        await Assert.ThrowsAsync<NotFoundException>(() => new GetPublishedJobPostHandler(db).HandleAsync(new(Guid.NewGuid()), ct));
    }
}
