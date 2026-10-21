using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RecruitmentModule.Domain.Entity;

namespace RecruitmentModule.Domain.EntityTypeConfiguration;

/// <summary>Maps <see cref="JobPost"/> onto <c>recruitment.job_post</c>, which the DDL script owns (ADR-D002).</summary>
internal sealed class JobPostConfiguration : IEntityTypeConfiguration<JobPost>
{
    public void Configure(EntityTypeBuilder<JobPost> b)
    {
        b.ToTable("job_post", t => t.ExcludeFromMigrations());
        b.HasKey(p => p.Id);

        b.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
        b.Property(p => p.Title).HasColumnName("title");
        b.Property(p => p.Location).HasColumnName("location");
        b.Property(p => p.Department).HasColumnName("department");
        b.Property(p => p.IsPublished).HasColumnName("is_published");
        b.Property(p => p.OpenPositions).HasColumnName("open_positions");
        b.Property(p => p.PaymentType).HasColumnName("payment_type");
        b.Property(p => p.EmploymentType).HasColumnName("employment_type");
        b.Property(p => p.RecruiterId).HasColumnName("recruiter_id");

        // The column default and the audit trigger own these (conventions.md § Audit columns).
        b.Property(p => p.CreatedAt).HasColumnName("created_at").ValueGeneratedOnAdd();
        b.Property(p => p.CreatedBy).HasColumnName("created_by");
        b.Property(p => p.UpdatedAt).HasColumnName("updated_at").ValueGeneratedOnUpdate();
        b.Property(p => p.UpdatedBy).HasColumnName("updated_by");
        b.Property(p => p.Version).HasColumnName("version").IsConcurrencyToken().ValueGeneratedOnAddOrUpdate();
    }
}
