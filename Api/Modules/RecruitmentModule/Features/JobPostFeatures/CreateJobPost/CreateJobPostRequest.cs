namespace RecruitmentModule.Features.JobPostFeatures.CreateJobPost;

/// <summary>The body of <c>POST /api/v1/recruitment/job-posts</c>: a new job post.</summary>
public sealed record CreateJobPostRequest
{
    /// <summary>The position's title.</summary>
    /// <example>Elektrotechnik údržby</example>
    public string Title { get; init; } = string.Empty;

    /// <summary>Where the work is done.</summary>
    /// <example>Bratislava</example>
    public string Location { get; init; } = string.Empty;

    /// <summary>The department the position belongs to.</summary>
    /// <example>Údržba</example>
    public string Department { get; init; } = string.Empty;

    /// <summary>Whether applicants can see the post; <c>false</c> keeps it a draft (koncept).</summary>
    /// <example>false</example>
    public bool IsPublished { get; init; }

    /// <summary>How many people are hired for the position.</summary>
    /// <example>2</example>
    public int OpenPositions { get; init; }

    /// <summary>How the wage is stated: <c>hourly</c> (hodinová mzda) or <c>monthly</c> (mesačná mzda).</summary>
    /// <example>monthly</example>
    public string PaymentType { get; init; } = string.Empty;

    /// <summary>
    /// The form of employment: <c>full_time</c> (TPP), <c>part_time</c> (skrátený úväzok), <c>fixed_term</c> (na dobu
    /// určitú), <c>work_performance_agreement</c> (dohoda o vykonaní práce), <c>work_activity_agreement</c> (dohoda o
    /// pracovnej činnosti), <c>student_work_agreement</c> (brigádnická dohoda) or <c>self_employed</c> (živnosť).
    /// </summary>
    /// <example>full_time</example>
    public string EmploymentType { get; init; } = string.Empty;
}
