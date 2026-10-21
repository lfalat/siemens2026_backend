namespace RecruitmentModule.Features.JobPostFeatures.AssignJobPostRecruiter;

/// <summary>The body of <c>PUT /api/v1/recruitment/job-posts/{jobPostId}/recruiter</c>.</summary>
public sealed record AssignJobPostRecruiterRequest
{
    /// <summary>
    /// The Entra object id of a staff member with the <c>Recruiter</c> role, who becomes responsible for the hiring
    /// (zodpovedný za realizáciu náboru). Listed by <c>GET /staff-users?role=Recruiter</c>.
    /// </summary>
    /// <example>6f1c1f0e-2d0a-4a43-9a3b-1b8f8b7a2f10</example>
    public Guid RecruiterId { get; init; }

    /// <summary>The job post's version as last read; a stale one is answered <c>409</c>.</summary>
    /// <example>1</example>
    public short Version { get; init; }
}
