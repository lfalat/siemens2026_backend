namespace RecruitmentModule.Domain.Entity;

/// <summary>A position the company is hiring for (<c>recruitment.job_post</c>).</summary>
public sealed class JobPost
{
    public Guid Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Location { get; set; } = string.Empty;

    public string Department { get; set; } = string.Empty;

    /// <summary><c>false</c> while the post is a draft (<i>koncept</i>), seen by staff only.</summary>
    public bool IsPublished { get; set; }

    public int OpenPositions { get; set; }

    /// <summary>One of <see cref="PaymentTypes"/>.</summary>
    public string PaymentType { get; set; } = string.Empty;

    /// <summary>One of <see cref="EmploymentTypes"/>.</summary>
    public string EmploymentType { get; set; } = string.Empty;

    /// <summary>
    /// The Entra object id of the recruiter responsible for the hiring (<i>zodpovedný za realizáciu náboru</i>);
    /// <c>null</c> until a hiring manager assigns one. A logical reference: staff accounts live in Entra.
    /// </summary>
    public Guid? RecruiterId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public Guid CreatedBy { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    public Guid? UpdatedBy { get; set; }

    public short Version { get; set; }
}
