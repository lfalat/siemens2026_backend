using Kernel.Auth;
using Kernel.Exceptions;
using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Features.JobPostFeatures.AssignJobPostRecruiter;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Entra;
using Tests.RecruitmentModule.Infrastructure.Entra;
using Tests.Shared;

namespace Tests.RecruitmentModule.Features.JobPostFeatures.AssignJobPostRecruiter;

/// <summary>Assigning a job post's recruiter: only a staff member with the Recruiter role, only on the current version.</summary>
public class AssignJobPostRecruiterHandlerTests
{
    private static readonly StaffMember Recruiter = new(Guid.NewGuid(), "Jana Nováková", [StaffRoles.Recruiter]);
    private static readonly StaffMember Manager = new(Guid.NewGuid(), "Peter Horváth", [StaffRoles.HiringManager]);
    private static readonly Guid ManagerId = Manager.Id;

    private static async Task<JobPost> SeedAsync(RecruitmentDbContext db, CancellationToken ct)
    {
        JobPost post = new()
        {
            Id = Guid.CreateVersion7(),
            Title = "Elektrotechnik údržby",
            Location = "Bratislava",
            Department = "Údržba",
            OpenPositions = 1,
            PaymentType = PaymentTypes.Hourly,
            EmploymentType = EmploymentTypes.PartTime,
            CreatedBy = Guid.NewGuid(),
        };
        db.JobPosts.Add(post);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return post;
    }

    private static AssignJobPostRecruiterHandler Handler(RecruitmentDbContext db) =>
        new(db, new FakeStaffDirectory { Staff = { Recruiter, Manager } });

    private static AssignJobPostRecruiterCommand Command(JobPost post) => new()
    {
        JobPostId = post.Id,
        RecruiterId = Recruiter.Id,
        Version = post.Version,
        UpdatedBy = ManagerId,
    };

    [Fact]
    public async Task Assigns_the_recruiter_and_bumps_the_version()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, ct);

        AssignJobPostRecruiterResponse response = await Handler(db).HandleAsync(Command(post), ct);

        await using RecruitmentDbContext read = TestDb.NewContextOnSameDb<RecruitmentDbContext>(db);
        JobPost stored = await read.JobPosts.SingleAsync(ct);
        Assert.Equal(Recruiter.Id, stored.RecruiterId);
        Assert.Equal(ManagerId, stored.UpdatedBy);
        Assert.NotNull(stored.UpdatedAt);
        Assert.Equal((short)2, stored.Version);
        Assert.Equal(new AssignJobPostRecruiterResponse(post.Id, Recruiter.Id, 2), response);
    }

    [Fact]
    public async Task Replaces_an_earlier_recruiter()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, ct);
        StaffMember other = new(Guid.NewGuid(), "Eva Kováčová", [StaffRoles.Recruiter]);
        AssignJobPostRecruiterHandler handler = new(db, new FakeStaffDirectory { Staff = { Recruiter, other } });

        await handler.HandleAsync(Command(post), ct);
        db.ChangeTracker.Clear();
        AssignJobPostRecruiterResponse response = await handler.HandleAsync(Command(post) with { RecruiterId = other.Id, Version = 2 }, ct);

        Assert.Equal(new AssignJobPostRecruiterResponse(post.Id, other.Id, 3), response);
    }

    [Fact]
    public async Task An_unknown_job_post_is_404()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, ct);

        await Assert.ThrowsAsync<NotFoundException>(
            () => Handler(db).HandleAsync(Command(post) with { JobPostId = Guid.NewGuid() }, ct));
    }

    [Fact]
    public async Task A_staff_member_without_the_recruiter_role_is_400()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, ct);

        ValidationException e = await Assert.ThrowsAsync<ValidationException>(
            () => Handler(db).HandleAsync(Command(post) with { RecruiterId = Manager.Id }, ct));

        Assert.Contains(nameof(AssignJobPostRecruiterCommand.RecruiterId), e.Errors.Keys);
        Assert.Null((await db.JobPosts.AsNoTracking().SingleAsync(ct)).RecruiterId);
    }

    [Fact]
    public async Task Someone_who_is_not_staff_is_400()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, ct);

        await Assert.ThrowsAsync<ValidationException>(
            () => Handler(db).HandleAsync(Command(post) with { RecruiterId = Guid.NewGuid() }, ct));
    }

    [Fact]
    public async Task A_stale_version_is_a_concurrency_conflict()
    {
        CancellationToken ct = TestContext.Current.CancellationToken;
        await using RecruitmentDbContext db = TestDb.NewRecruitmentContext();
        JobPost post = await SeedAsync(db, ct);
        await Handler(db).HandleAsync(Command(post), ct);
        db.ChangeTracker.Clear();

        StaffMember other = new(Guid.NewGuid(), "Eva Kováčová", [StaffRoles.Recruiter]);
        AssignJobPostRecruiterHandler handler = new(db, new FakeStaffDirectory { Staff = { other } });

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(
            () => handler.HandleAsync(Command(post) with { RecruiterId = other.Id, Version = 1 }, ct));
    }

    [Fact]
    public void An_empty_recruiter_id_is_rejected()
    {
        JobPost post = new() { Id = Guid.NewGuid(), Version = 1 };

        Assert.False(CommandValidation.IsValid(Command(post) with { RecruiterId = Guid.Empty }));
        Assert.False(CommandValidation.IsValid(Command(post) with { Version = 0 }));
        Assert.True(CommandValidation.IsValid(Command(post)));
    }
}
