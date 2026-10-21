using RecruitmentModule.Domain;
using RecruitmentModule.Domain.Entity;
using RecruitmentModule.Infrastructure;

namespace Tests.RecruitmentModule.Features.JobPostFeatures;

/// <summary>Job posts that satisfy the real schema, for the read features' tests.</summary>
internal static class JobPostSeed
{
    public static JobPost Post(
        string title, bool isPublished = true, string location = "Bratislava",
        string employmentType = EmploymentTypes.FullTime) => new()
    {
        Id = Guid.CreateVersion7(),
        Title = title,
        Location = location,
        Department = "Údržba",
        Description = $"Opis pozície {title}.",
        IsPublished = isPublished,
        OpenPositions = 1,
        Salary = 1500m,
        PaymentType = PaymentTypes.Monthly,
        EmploymentType = employmentType,
        RecruiterId = Guid.NewGuid(),
        CreatedBy = Guid.NewGuid(),
    };

    /// <summary>Stores <paramref name="posts"/> one save each, so each gets its own <c>created_at</c>, in order.</summary>
    public static async Task AddAsync(RecruitmentDbContext db, CancellationToken ct, params JobPost[] posts)
    {
        foreach (JobPost post in posts)
        {
            db.JobPosts.Add(post);
            await db.SaveChangesAsync(ct);
        }
        db.ChangeTracker.Clear();
    }
}
