using System.ComponentModel.DataAnnotations;
using Kernel.Auth;
using Kernel.CQRS;
using Kernel.Exceptions;
using Kernel.Validation;
using Microsoft.EntityFrameworkCore;
using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;
using RecruitmentModule.Infrastructure.Entra;
using ValidationException = Kernel.Exceptions.ValidationException;

namespace RecruitmentModule.Features.JobPostFeatures.AssignJobPostRecruiter;

/// <summary>Makes a recruiter responsible for a job post's hiring, replacing any earlier one.</summary>
public sealed record AssignJobPostRecruiterCommand : ICommand
{
    /// <inheritdoc />
    public static string EventName => ModuleEvents.JobPostRecruiterAssigned;

    /// <summary>The job post, from the route.</summary>
    public Guid JobPostId { get; init; }

    /// <summary>The Entra object id of a staff member with the <see cref="StaffRoles.Recruiter"/> role.</summary>
    [Required, NotEmpty]
    public Guid RecruiterId { get; init; }

    /// <summary>The version the caller last read.</summary>
    [Range(1, short.MaxValue)]
    public short Version { get; init; }

    /// <summary>The acting hiring manager's object id.</summary>
    public Guid UpdatedBy { get; init; }
}

/// <summary>The job post's new recruiter.</summary>
/// <param name="Version">The version a later change must carry.</param>
public sealed record AssignJobPostRecruiterResponse(Guid Id, Guid RecruiterId, short Version);

internal sealed class AssignJobPostRecruiterHandler(RecruitmentDbContext db, IStaffDirectory directory)
    : ICommandHandler<AssignJobPostRecruiterCommand, AssignJobPostRecruiterResponse>
{
    public async Task<AssignJobPostRecruiterResponse> HandleAsync(AssignJobPostRecruiterCommand command, CancellationToken ct = default)
    {
        JobPost post = await db.JobPosts.SingleOrDefaultAsync(p => p.Id == command.JobPostId, ct)
            ?? throw new NotFoundException("Pracovná ponuka neexistuje.");

        // Staff accounts live in Entra, so the database cannot enforce the role (ADR-D004); the directory is asked.
        IReadOnlyList<StaffMember> staff = await directory.ListStaffAsync(ct);
        if (!staff.Any(s => s.Id == command.RecruiterId && s.Roles.Contains(StaffRoles.Recruiter)))
            throw ValidationException.ForField(nameof(command.RecruiterId),
                "Zodpovedný za realizáciu náboru musí byť používateľ s rolou náborový pracovník.");

        // The caller's version, so a post changed since it was read fails with 409. Assigning the recruiter the post
        // already has changes nothing, so a repeated PUT is not checked and succeeds.
        db.Entry(post).Property(p => p.Version).OriginalValue = command.Version;
        post.RecruiterId = command.RecruiterId;
        post.UpdatedBy = command.UpdatedBy;
        await db.SaveChangesAsync(ct);

        return new AssignJobPostRecruiterResponse(post.Id, command.RecruiterId, post.Version);
    }
}
