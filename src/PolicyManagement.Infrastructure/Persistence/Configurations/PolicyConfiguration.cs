using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using PolicyManagement.Domain.Entities;
using PolicyManagement.Domain.Enums;

namespace PolicyManagement.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core Fluent API configuration for the <see cref="Policy"/> entity.
/// Defines the <c>Policies</c> table: column names, types, constraints,
/// enum converters, and all 13 indexes from ADR-006.
/// <para>
/// No data annotations are used on the entity class — all schema decisions are here.
/// </para>
/// </summary>
public sealed class PolicyConfiguration : IEntityTypeConfiguration<Policy>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Policy> builder)
    {
        // -------------------------------------------------------------------------
        // Table
        // -------------------------------------------------------------------------
        builder.ToTable("Policies");

        // -------------------------------------------------------------------------
        // Primary key
        // -------------------------------------------------------------------------
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .ValueGeneratedNever(); // client-generated GUIDs

        // -------------------------------------------------------------------------
        // String columns
        // -------------------------------------------------------------------------
        builder.Property(p => p.PolicyNumber)
            .HasColumnName("policy_number")
            .HasColumnType("varchar(20)")
            .IsRequired();

        builder.Property(p => p.PolicyholderName)
            .HasColumnName("policyholder_name")
            .HasColumnType("nvarchar(200)")
            .IsRequired();

        builder.Property(p => p.Region)
            .HasColumnName("region")
            .HasColumnType("varchar(100)")
            .IsRequired();

        builder.Property(p => p.Underwriter)
            .HasColumnName("underwriter")
            .HasColumnType("nvarchar(200)")
            .IsRequired();

        builder.Property(p => p.Currency)
            .HasColumnName("currency")
            .HasColumnType("varchar(10)")
            .IsRequired();

        // -------------------------------------------------------------------------
        // Enum columns — stored as varchar strings
        // PolicyStatus: default .HasConversion<string>() is sufficient (no special chars)
        // LineOfBusiness: AH must serialise as "A&H" — requires a custom converter
        // -------------------------------------------------------------------------
        builder.Property(p => p.Status)
            .HasColumnName("status")
            .HasColumnType("varchar(50)")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(p => p.LineOfBusiness)
            .HasColumnName("line_of_business")
            .HasColumnType("varchar(50)")
            .HasConversion(
                new ValueConverter<LineOfBusiness, string>(
                    v => v == LineOfBusiness.AH ? "A&H" : v.ToString(),
                    v => v == "A&H" ? LineOfBusiness.AH : Enum.Parse<LineOfBusiness>(v)))
            .IsRequired();

        // -------------------------------------------------------------------------
        // Numeric columns
        // -------------------------------------------------------------------------
        builder.Property(p => p.PremiumAmount)
            .HasColumnName("premium_amount")
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        // -------------------------------------------------------------------------
        // Date columns
        // -------------------------------------------------------------------------
        builder.Property(p => p.EffectiveDate)
            .HasColumnName("effective_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(p => p.ExpiryDate)
            .HasColumnName("expiry_date")
            .HasColumnType("date")
            .IsRequired();

        // -------------------------------------------------------------------------
        // Boolean columns
        // -------------------------------------------------------------------------
        builder.Property(p => p.FlaggedForReview)
            .HasColumnName("flagged_for_review")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(p => p.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false)
            .IsRequired();

        // -------------------------------------------------------------------------
        // Audit timestamp columns
        // -------------------------------------------------------------------------
        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("datetimeoffset(7)")
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("datetimeoffset(7)")
            .IsRequired();

        // -------------------------------------------------------------------------
        // Global query filter — excludes soft-deleted rows from all queries
        // -------------------------------------------------------------------------
        builder.HasQueryFilter(p => !p.IsDeleted);

        // -------------------------------------------------------------------------
        // Unique constraint
        // -------------------------------------------------------------------------
        builder.HasIndex(p => p.PolicyNumber)
            .IsUnique()
            .HasDatabaseName("UQ_Policies_PolicyNumber");

        // -------------------------------------------------------------------------
        // ADR-006 — Single-column filtered indexes (WHERE is_deleted = 0)
        // -------------------------------------------------------------------------
        builder.HasIndex(p => p.Status)
            .HasFilter("is_deleted = 0")
            .HasDatabaseName("IX_Policies_Status");

        builder.HasIndex(p => p.LineOfBusiness)
            .HasFilter("is_deleted = 0")
            .HasDatabaseName("IX_Policies_LineOfBusiness");

        builder.HasIndex(p => p.Region)
            .HasFilter("is_deleted = 0")
            .HasDatabaseName("IX_Policies_Region");

        builder.HasIndex(p => p.EffectiveDate)
            .HasFilter("is_deleted = 0")
            .HasDatabaseName("IX_Policies_EffectiveDate");

        builder.HasIndex(p => p.ExpiryDate)
            .HasFilter("is_deleted = 0")
            .HasDatabaseName("IX_Policies_ExpiryDate");

        builder.HasIndex(p => p.CreatedAt)
            .IsDescending()
            .HasFilter("is_deleted = 0")
            .HasDatabaseName("IX_Policies_CreatedAt");

        builder.HasIndex(p => p.PolicyholderName)
            .HasFilter("is_deleted = 0")
            .HasDatabaseName("IX_Policies_PolicyholderName");

        builder.HasIndex(p => p.FlaggedForReview)
            .HasFilter("is_deleted = 0")
            .HasDatabaseName("IX_Policies_FlaggedForReview");

        // -------------------------------------------------------------------------
        // ADR-006 — Composite filtered indexes
        // -------------------------------------------------------------------------
        builder.HasIndex(p => new { p.Status, p.LineOfBusiness })
            .HasFilter("is_deleted = 0")
            .HasDatabaseName("IX_Policies_Status_LineOfBusiness");

        builder.HasIndex(p => new { p.Status, p.Region })
            .HasFilter("is_deleted = 0")
            .HasDatabaseName("IX_Policies_Status_Region");

        builder.HasIndex(p => new { p.ExpiryDate, p.Status })
            .HasFilter("is_deleted = 0")
            .HasDatabaseName("IX_Policies_ExpiryDate_Status");
    }
}
