namespace RecruitmentModule.Domain;

/// <summary>Names that identify the module.</summary>
public static class ModuleTypes
{
    /// <summary>The module's schema and route-group name.</summary>
    public const string ModuleName = "recruitment";
}

/// <summary>The feature event names the module's commands carry (<c>ICommand.EventName</c>).</summary>
public static class ModuleEvents
{
    /// <summary>An administrator created a staff account in Entra.</summary>
    public const string StaffUserCreated = "staff_user_created";

    /// <summary>A staff member created a job post.</summary>
    public const string JobPostCreated = "job_post_created";

    /// <summary>A hiring manager assigned the recruiter responsible for a job post.</summary>
    public const string JobPostRecruiterAssigned = "job_post_recruiter_assigned";
}

/// <summary>How a job post's wage is stated (<c>job_post.payment_type</c>).</summary>
public static class PaymentTypes
{
    /// <summary>Hourly wage (<i>hodinová mzda</i>).</summary>
    public const string Hourly = "hourly";

    /// <summary>Monthly wage (<i>mesačná mzda</i>).</summary>
    public const string Monthly = "monthly";
}

/// <summary>The form of employment a job post offers (<c>job_post.employment_type</c>).</summary>
public static class EmploymentTypes
{
    /// <summary>Permanent full-time employment (<i>trvalý pracovný pomer, TPP</i>).</summary>
    public const string FullTime = "full_time";

    /// <summary>Part-time employment (<i>skrátený úväzok</i>).</summary>
    public const string PartTime = "part_time";

    /// <summary>Fixed-term employment (<i>pracovný pomer na dobu určitú</i>).</summary>
    public const string FixedTerm = "fixed_term";

    /// <summary>Agreement on the performance of work (<i>dohoda o vykonaní práce</i>).</summary>
    public const string WorkPerformanceAgreement = "work_performance_agreement";

    /// <summary>Agreement on work activity (<i>dohoda o pracovnej činnosti</i>).</summary>
    public const string WorkActivityAgreement = "work_activity_agreement";

    /// <summary>Student work agreement (<i>dohoda o brigádnickej práci študentov, brigádnická dohoda</i>).</summary>
    public const string StudentWorkAgreement = "student_work_agreement";

    /// <summary>Self-employed contractor (<i>živnosť</i>).</summary>
    public const string SelfEmployed = "self_employed";
}
