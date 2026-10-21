using System.ComponentModel.DataAnnotations;
using Kernel.CQRS;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;

namespace RecruitmentModule.Features.JobPostFeatures.CreateJobPost;

/// <summary>Creates a job post, without a recruiter: only a hiring manager assigns one.</summary>
public sealed record CreateJobPostCommand : ICommand
{
    /// <inheritdoc />
    public static string EventName => ModuleEvents.JobPostCreated;

    [Required, StringLength(200)]
    public string Title { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string Location { get; init; } = string.Empty;

    [Required, StringLength(200)]
    public string Department { get; init; } = string.Empty;

    public bool IsPublished { get; init; }

    [Range(1, 1000)]
    public int OpenPositions { get; init; }

    [Required, AllowedValues(PaymentTypes.Hourly, PaymentTypes.Monthly)]
    public string PaymentType { get; init; } = string.Empty;

    [Required, AllowedValues(
        EmploymentTypes.FullTime, EmploymentTypes.PartTime, EmploymentTypes.FixedTerm,
        EmploymentTypes.WorkPerformanceAgreement, EmploymentTypes.WorkActivityAgreement,
        EmploymentTypes.StudentWorkAgreement, EmploymentTypes.SelfEmployed)]
    public string EmploymentType { get; init; } = string.Empty;

    /// <summary>The acting staff member's object id.</summary>
    public Guid CreatedBy { get; init; }
}

/// <summary>The created job post.</summary>
/// <param name="Version">The optimistic-locking version a later change must carry.</param>
public sealed record CreateJobPostResponse(
    Guid Id,
    string Title,
    string Location,
    string Department,
    bool IsPublished,
    int OpenPositions,
    string PaymentType,
    string EmploymentType,
    Guid? RecruiterId,
    DateTimeOffset CreatedAt,
    short Version);

internal sealed class CreateJobPostHandler(RecruitmentDbContext db)
    : ICommandHandler<CreateJobPostCommand, CreateJobPostResponse>
{
    public async Task<CreateJobPostResponse> HandleAsync(CreateJobPostCommand command, CancellationToken ct = default)
    {
        JobPost post = new()
        {
            Id = Guid.CreateVersion7(),
            Title = command.Title,
            Location = command.Location,
            Department = command.Department,
            IsPublished = command.IsPublished,
            OpenPositions = command.OpenPositions,
            PaymentType = command.PaymentType,
            EmploymentType = command.EmploymentType,
            CreatedBy = command.CreatedBy,
        };
        db.JobPosts.Add(post);
        await db.SaveChangesAsync(ct);

        return new CreateJobPostResponse(
            post.Id, post.Title, post.Location, post.Department, post.IsPublished, post.OpenPositions,
            post.PaymentType, post.EmploymentType, post.RecruiterId, post.CreatedAt, post.Version);
    }
}
