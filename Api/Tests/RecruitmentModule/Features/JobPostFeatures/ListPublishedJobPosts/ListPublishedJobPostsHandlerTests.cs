using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Features.JobPostFeatures.ListPublishedJobPosts;
using RecruitmentModule.Infrastructure;
using Tests.Shared;

namespace Tests.RecruitmentModule.Features.JobPostFeatures.ListPublishedJobPosts;

/// <summary>Listing published job posts: never drafts, newest first, a page at a time, filtered.</summary>
public class ListPublishedJobPostsHandlerTests
{
    private static async Task<ListPublishedJobPostsResponse> ListAsync(
        RecruitmentDbContext db, ListPublishedJobPostsQuery query) =>
        await new ListPublishedJobPostsHandler(db).HandleAsync(query, TestContext.Current.CancellationToken);

    [Fact]
    public async Task Lists_only_published_posts_newest_first()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost older = JobPostSeed.Post("Elektrikár");
        JobPost draft = JobPostSeed.Post("Koncept", isPublished: false);
        JobPost newer = JobPostSeed.Post("Zvárač");
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, older, draft, newer);

        ListPublishedJobPostsResponse response = await ListAsync(db, new ListPublishedJobPostsQuery());

        Assert.Equal([newer.Id, older.Id], response.Items.Select(i => i.Id));
        Assert.Equal(2, response.Total);
        Assert.Equal((1500m, PaymentTypes.Monthly), (response.Items[0].Salary, response.Items[0].PaymentType));
    }

    [Fact]
    public async Task Pages_by_offset_and_limit_with_the_total_of_all_pages()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost[] posts = Enumerable.Range(1, 5).Select(i => JobPostSeed.Post($"Pozícia {i}")).ToArray();
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, posts);

        ListPublishedJobPostsResponse response = await ListAsync(db, new ListPublishedJobPostsQuery { Offset = 2, Limit = 2 });

        Assert.Equal(["Pozícia 3", "Pozícia 2"], response.Items.Select(i => i.Title));
        Assert.Equal((5, 2, 2), (response.Total, response.Offset, response.Limit));
    }

    [Fact]
    public async Task An_offset_past_the_end_is_an_empty_page()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, JobPostSeed.Post("Elektrikár"));

        ListPublishedJobPostsResponse response = await ListAsync(db, new ListPublishedJobPostsQuery { Offset = 10 });

        Assert.Empty(response.Items);
        Assert.Equal(1, response.Total);
    }

    [Fact]
    public async Task Searches_the_title_and_the_location_ignoring_case()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost byTitle = JobPostSeed.Post("Elektrikár údržby");
        JobPost byLocation = JobPostSeed.Post("Zvárač", location: "Nové Zámky");
        JobPost neither = JobPostSeed.Post("Skladník", location: "Košice");
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, byTitle, byLocation, neither);

        Assert.Equal([byTitle.Id], (await ListAsync(db, new ListPublishedJobPostsQuery { Search = "ELEKTRI" })).Items.Select(i => i.Id));
        Assert.Equal([byLocation.Id], (await ListAsync(db, new ListPublishedJobPostsQuery { Search = " zámky " })).Items.Select(i => i.Id));
    }

    [Fact]
    public async Task Wildcards_in_the_search_match_literally()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, JobPostSeed.Post("Elektrikár"));

        Assert.Empty((await ListAsync(db, new ListPublishedJobPostsQuery { Search = "%" })).Items);
        Assert.Empty((await ListAsync(db, new ListPublishedJobPostsQuery { Search = "Elektri_ár" })).Items);
    }

    [Fact]
    public async Task Filters_by_employment_type()
    {
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost student = JobPostSeed.Post("Brigáda", employmentType: EmploymentTypes.StudentWorkAgreement);
        await JobPostSeed.AddAsync(db, TestContext.Current.CancellationToken, JobPostSeed.Post("Elektrikár"), student);

        ListPublishedJobPostsResponse response = await ListAsync(
            db, new ListPublishedJobPostsQuery { EmploymentType = EmploymentTypes.StudentWorkAgreement });

        Assert.Equal([student.Id], response.Items.Select(i => i.Id));
        Assert.Equal(1, response.Total);
    }
}
