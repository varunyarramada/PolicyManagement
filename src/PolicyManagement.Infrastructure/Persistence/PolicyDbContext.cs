using Microsoft.EntityFrameworkCore;
using PolicyManagement.Domain.Entities;
using PolicyManagement.Domain.Interfaces;
using PolicyManagement.Infrastructure.Persistence.Configurations;
using PolicyManagement.Infrastructure.Persistence.Seed;

namespace PolicyManagement.Infrastructure.Persistence;

/// <summary>
/// EF Core database context for the PolicyManagement BFF.
/// Defines the <c>Policies</c> table and applies all entity configurations.
/// <para>
/// Global query filter <c>p => !p.IsDeleted</c> ensures soft-deleted policies are
/// invisible to all queries by default. Admin operations that need deleted records
/// must call <c>.IgnoreQueryFilters()</c> explicitly.
/// </para>
/// </summary>
public sealed class PolicyDbContext(DbContextOptions<PolicyDbContext> options)
    : DbContext(options)
{
    /// <summary>Gets the <c>Policies</c> table.</summary>
    public DbSet<Policy> Policies => Set<Policy>();

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new PolicyConfiguration());
    }

    /// <summary>
    /// Intercepts saves to automatically set <c>CreatedAt</c> on insert and
    /// <c>UpdatedAt</c> on every write for all <see cref="IAuditableEntity"/> entities.
    /// Uses EF Core's property API so domain entity private setters are not exposed.
    /// </summary>
    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<IAuditableEntity>())
        {
            // Use EF Core's property API (entry.Property(...).CurrentValue) rather than
            // direct assignment (entry.Entity.CreatedAt = now) because the domain entity
            // uses private setters. Direct assignment would not compile; the property API
            // bypasses access modifiers and is the correct approach for this entity design.
            if (entry.State == EntityState.Added)
                entry.Property(nameof(IAuditableEntity.CreatedAt)).CurrentValue = now;

            if (entry.State is EntityState.Added or EntityState.Modified)
                entry.Property(nameof(IAuditableEntity.UpdatedAt)).CurrentValue = now;
        }

        return await base.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Seeds the database with development data when the environment is Development
    /// and the <c>Policies</c> table is empty.
    /// Called from <c>InfrastructureServiceExtensions</c> at startup.
    /// </summary>
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await Policies.AnyAsync(cancellationToken))
            return;

        var policies = PolicySeeder.Generate();
        await Policies.AddRangeAsync(policies, cancellationToken);

        // INTENTIONAL: calls base.SaveChangesAsync to bypass the audit-timestamp override
        // in the overridden SaveChangesAsync. Seed entities already have CreatedAt and
        // UpdatedAt set to deterministic historical values inside PolicySeeder.BuildPolicy().
        // Calling the override would overwrite those values with DateTimeOffset.UtcNow.
        // If you add additional interceptors to SaveChangesAsync, ensure they are also
        // safe to skip for seed data, or refactor to a skipAudit flag pattern.
        await base.SaveChangesAsync(cancellationToken);
    }
}
