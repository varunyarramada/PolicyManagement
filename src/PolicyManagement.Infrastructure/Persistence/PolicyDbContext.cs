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
        await base.SaveChangesAsync(cancellationToken); // bypass audit interceptor for seed data
    }
}
