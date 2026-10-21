using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Features.JobPostFeatures.CreateJobPost;
using RecruitmentModule.Infrastructure;
using Tests.Shared;

namespace Tests.RecruitmentModule.Features.JobPostFeatures.CreateJobPost;

/// <summary>Creating a job post: stored as given, without a recruiter, under the real schema's constraints.</summary>
public class CreateJobPostHandlerTests
{
    private static readonly Guid StaffId = Guid.NewGuid();

    private static readonly CreateJobPostCommand Command = new()
    {
        Title = "Elektrotechnik údržby",
        Location = "Bratislava",
        Department = "Údržba",
        OpenPositions = 2,
        PaymentType = PaymentTypes.Monthly,
        EmploymentType = EmploymentTypes.FullTime,
        CreatedBy = StaffId,
    };

    [Fact]
    public async Task Stores_a_draft_without_a_recruiter()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();

        CreateJobPostResponse response = await new CreateJobPostHandler(db).HandleAsync(Command, ct);

        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        JobPost stored = await read.JobPosts.SingleAsync(ct);
        Assert.Equal(response.Id, stored.Id);
        Assert.Equal(("Elektrotechnik údržby", "Bratislava", "Údržba", 2), (stored.Title, stored.Location, stored.Department, stored.OpenPositions));
        Assert.Equal((PaymentTypes.Monthly, EmploymentTypes.FullTime), (stored.PaymentType, stored.EmploymentType));
        Assert.False(stored.IsPublished);
        Assert.Null(stored.RecruiterId);
        Assert.Equal(StaffId, stored.CreatedBy);
        Assert.Null(stored.UpdatedAt);
        Assert.Equal((short)1, stored.Version);
        Assert.Equal((short)1, response.Version);
        Assert.Equal(stored.CreatedAt, response.CreatedAt);
    }

    [Fact]
    public async Task Stores_a_published_post()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();

        CreateJobPostResponse response = await new CreateJobPostHandler(db).HandleAsync(Command with { IsPublished = true }, ct);

        Assert.True(response.IsPublished);
    }

    [Theory]
    [InlineData(EmploymentTypes.FullTime)]
    [InlineData(EmploymentTypes.PartTime)]
    [InlineData(EmploymentTypes.FixedTerm)]
    [InlineData(EmploymentTypes.WorkPerformanceAgreement)]
    [InlineData(EmploymentTypes.WorkActivityAgreement)]
    [InlineData(EmploymentTypes.StudentWorkAgreement)]
    [InlineData(EmploymentTypes.SelfEmployed)]
    public async Task Every_employment_type_satisfies_the_schema(string employmentType)
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();

        await new CreateJobPostHandler(db).HandleAsync(Command with { EmploymentType = employmentType, PaymentType = PaymentTypes.Hourly }, ct);

        Assert.Equal(1, await db.JobPosts.CountAsync(ct));
    }

    [Theory]
    [InlineData("TPP")]
    [InlineData("")]
    public void An_unknown_employment_type_is_rejected(string employmentType)
    {
        Assert.False(CommandValidation.IsValid(Command with { EmploymentType = employmentType }));
    }

    [Theory]
    [InlineData("daily")]
    [InlineData("")]
    public void An_unknown_payment_type_is_rejected(string paymentType)
    {
        Assert.False(CommandValidation.IsValid(Command with { PaymentType = paymentType }));
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(1000, true)]
    [InlineData(1001, false)]
    public void Open_positions_are_between_1_and_1000(int openPositions, bool valid)
    {
        Assert.Equal(valid, CommandValidation.IsValid(Command with { OpenPositions = openPositions }));
    }

    [Fact]
    public void Title_location_and_department_are_required()
    {
        Assert.False(CommandValidation.IsValid(Command with { Title = " " }));
        Assert.False(CommandValidation.IsValid(Command with { Location = "" }));
        Assert.False(CommandValidation.IsValid(Command with { Department = new string('a', 201) }));
    }
}
