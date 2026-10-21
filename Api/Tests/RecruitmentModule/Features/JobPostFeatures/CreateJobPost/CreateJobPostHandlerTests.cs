using Kernel.Auth;
using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Features.JobPostFeatures.CreateJobPost;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Entra;
using Tests.RecruitmentModule.Infrastructure.Entra;
using Tests.Shared;
using ValidationException = Kernel.Exceptions.ValidationException;

namespace Tests.RecruitmentModule.Features.JobPostFeatures.CreateJobPost;

/// <summary>
/// Creating a job post: stored as given under the real schema's constraints, with a recruiter who holds the
/// Recruiter role.
/// </summary>
public class CreateJobPostHandlerTests
{
    private static readonly StaffMember Recruiter = new(Guid.NewGuid(), "Jana Nováková", [StaffRoles.Recruiter]);
    private static readonly StaffMember Manager = new(Guid.NewGuid(), "Peter Horváth", [StaffRoles.HiringManager]);

    private static readonly CreateJobPostCommand Command = new()
    {
        Title = "Elektrotechnik údržby",
        Location = "Bratislava",
        Department = "Údržba",
        Description = "Údržba a opravy elektrických zariadení vo výrobnej hale.",
        OpenPositions = 2,
        Salary = 1800.50m,
        PaymentType = PaymentTypes.Monthly,
        EmploymentType = EmploymentTypes.FullTime,
        RecruiterId = Recruiter.Id,
        CreatedBy = Manager.Id,
    };

    private static CreateJobPostHandler Handler(RecruitmentDbContext db) =>
        new(db, new FakeStaffDirectory { Staff = { Recruiter, Manager } });

    [Fact]
    public async Task Stores_a_draft_with_its_recruiter()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();

        CreateJobPostResponse response = await Handler(db).HandleAsync(Command, ct);

        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        JobPost stored = await read.JobPosts.SingleAsync(ct);
        Assert.Equal(response.Id, stored.Id);
        Assert.Equal(("Elektrotechnik údržby", "Bratislava", "Údržba", 2), (stored.Title, stored.Location, stored.Department, stored.OpenPositions));
        Assert.Equal(Command.Description, stored.Description);
        Assert.Equal((1800.50m, PaymentTypes.Monthly, EmploymentTypes.FullTime), (stored.Salary, stored.PaymentType, stored.EmploymentType));
        Assert.False(stored.IsPublished);
        Assert.Equal(Recruiter.Id, stored.RecruiterId);
        Assert.Equal(Manager.Id, stored.CreatedBy);
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

        CreateJobPostResponse response = await Handler(db).HandleAsync(Command with { IsPublished = true }, ct);

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

        await Handler(db).HandleAsync(Command with { EmploymentType = employmentType, PaymentType = PaymentTypes.Hourly }, ct);

        Assert.Equal(1, await db.JobPosts.CountAsync(ct));
    }

    [Fact]
    public async Task The_largest_salary_fits_the_column()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();

        CreateJobPostResponse response = await Handler(db).HandleAsync(Command with { Salary = 99999999.99m }, ct);

        Assert.Equal(99999999.99m, response.Salary);
    }

    [Fact]
    public async Task A_staff_member_without_the_recruiter_role_is_400_and_stores_nothing()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();

        ValidationException e = await Assert.ThrowsAsync<ValidationException>(
            () => Handler(db).HandleAsync(Command with { RecruiterId = Manager.Id }, ct));

        Assert.Contains(nameof(CreateJobPostCommand.RecruiterId), e.Errors.Keys);
        Assert.Equal(0, await db.JobPosts.CountAsync(ct));
    }

    [Fact]
    public async Task Someone_who_is_not_staff_is_400()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();

        await Assert.ThrowsAsync<ValidationException>(
            () => Handler(db).HandleAsync(Command with { RecruiterId = Guid.NewGuid() }, ct));
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

    [Theory]
    [InlineData("0", false)]
    [InlineData("-5", false)]
    [InlineData("0.01", true)]
    [InlineData("12.345", false)]
    [InlineData("99999999.99", true)]
    [InlineData("100000000", false)]
    public void A_salary_is_positive_with_at_most_2_decimal_places(string salary, bool valid)
    {
        decimal value = decimal.Parse(salary, System.Globalization.CultureInfo.InvariantCulture);
        Assert.Equal(valid, CommandValidation.IsValid(Command with { Salary = value }));
    }

    [Fact]
    public void Text_fields_and_the_recruiter_are_required()
    {
        Assert.False(CommandValidation.IsValid(Command with { Title = " " }));
        Assert.False(CommandValidation.IsValid(Command with { Location = "" }));
        Assert.False(CommandValidation.IsValid(Command with { Department = new string('a', 201) }));
        Assert.False(CommandValidation.IsValid(Command with { Description = " " }));
        Assert.False(CommandValidation.IsValid(Command with { RecruiterId = Guid.Empty }));
        Assert.True(CommandValidation.IsValid(Command));
    }
}
