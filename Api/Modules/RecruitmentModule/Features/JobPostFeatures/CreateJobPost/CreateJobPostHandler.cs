using System.ComponentModel.DataAnnotations;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Validation;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Entra;
using ValidationException = Kernel.Exceptions.ValidationException;

namespace RecruitmentModule.Features.JobPostFeatures.CreateJobPost;

/// <summary>A hiring manager creates a job post and names the recruiter responsible for its hiring.</summary>
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

    [Required, StringLength(10000)]
    public string Description { get; init; } = string.Empty;

    public bool IsPublished { get; init; }

    [Range(1, 1000)]
    public int OpenPositions { get; init; }

    [Range(typeof(decimal), "0", "99999999.99", MinimumIsExclusive = true,
        ParseLimitsInInvariantCulture = true, ConvertValueInInvariantCulture = true)]
    [DecimalPrecision(10, 2)]
    public decimal Salary { get; init; }

    [Required, AllowedValues(PaymentTypes.Hourly, PaymentTypes.Monthly, PaymentTypes.Yearly)]
    public string PaymentType { get; init; } = string.Empty;

    [Required, AllowedValues(
        EmploymentTypes.FullTime, EmploymentTypes.PartTime, EmploymentTypes.FixedTerm,
        EmploymentTypes.WorkPerformanceAgreement, EmploymentTypes.WorkActivityAgreement,
        EmploymentTypes.StudentWorkAgreement, EmploymentTypes.SelfEmployed)]
    public string EmploymentType { get; init; } = string.Empty;

    /// <summary>The Entra object id of a staff member with the <see cref="StaffRoles.Recruiter"/> role.</summary>
    [Required, NotEmpty]
    public Guid RecruiterId { get; init; }

    /// <summary>The acting hiring manager's object id.</summary>
    public Guid CreatedBy { get; init; }
}

/// <summary>The created job post.</summary>
/// <param name="Version">The optimistic-locking version a later change must carry.</param>
public sealed record CreateJobPostResponse(
    Guid Id,
    string Title,
    string Location,
    string Department,
    string Description,
    bool IsPublished,
    int OpenPositions,
    decimal Salary,
    string PaymentType,
    string EmploymentType,
    Guid RecruiterId,
    DateTimeOffset CreatedAt,
    short Version);

internal sealed class CreateJobPostHandler(RecruitmentDbContext db, IStaffDirectory directory)
    : ICommandHandler<CreateJobPostCommand, CreateJobPostResponse>
{
    public async Task<CreateJobPostResponse> HandleAsync(CreateJobPostCommand command, CancellationToken ct = default)
    {
        // Staff accounts live in Entra, so the database cannot enforce the role (ADR-D004); the directory is asked.
        IReadOnlyList<StaffMember> staff = await directory.ListStaffAsync(ct);
        if (!staff.Any(s => s.Id == command.RecruiterId && s.Roles.Contains(StaffRoles.Recruiter)))
            throw ValidationException.ForField(nameof(command.RecruiterId),
                "Zodpovedný za realizáciu náboru musí byť používateľ s rolou náborový pracovník.");

        JobPost post = new()
        {
            Id = Guid.CreateVersion7(),
            Title = command.Title,
            Location = command.Location,
            Department = command.Department,
            Description = command.Description,
            IsPublished = command.IsPublished,
            OpenPositions = command.OpenPositions,
            Salary = command.Salary,
            PaymentType = command.PaymentType,
            EmploymentType = command.EmploymentType,
            RecruiterId = command.RecruiterId,
            CreatedBy = command.CreatedBy,
        };
        db.JobPosts.Add(post);
        await db.SaveChangesAsync(ct);

        return new CreateJobPostResponse(
            post.Id, post.Title, post.Location, post.Department, post.Description, post.IsPublished,
            post.OpenPositions, post.Salary, post.PaymentType, post.EmploymentType, post.RecruiterId,
            post.CreatedAt, post.Version);
    }
}
