using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using PolicyManagement.Application.Interfaces;
using PolicyManagement.Domain.Interfaces;
using PolicyManagement.Infrastructure.Caching;
using PolicyManagement.Infrastructure.Events;
using PolicyManagement.Infrastructure.Options;
using PolicyManagement.Infrastructure.Persistence;
using PolicyManagement.Infrastructure.Persistence.Repositories;

namespace PolicyManagement.Infrastructure.Extensions;

/// <summary>
/// Extension methods for registering Infrastructure layer services into the DI container.
/// Called from <c>PolicyManagement.API/Program.cs</c> as part of the DI composition root.
/// </summary>
public static class InfrastructureServiceExtensions
{
    /// <summary>
    /// Registers all Infrastructure layer services:
    /// <list type="bullet">
    ///   <item>
    ///     <description>
    ///       <c>PolicyDbContext</c> — EF Core SQL Server context. Connection string read
    ///       from <c>ConnectionStrings:DefaultConnection</c> (environment variable:
    ///       <c>ConnectionStrings__DefaultConnection</c>).
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="IPolicyRepository"/> → <c>PolicyRepository</c> (Scoped).
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="ICacheService"/> → <see cref="InMemoryCacheService"/> (Singleton).
    ///       TTL values bound from <c>Cache</c> configuration section via
    ///       <see cref="CacheOptions"/>.
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <see cref="IEventPublisher"/> → <see cref="InMemoryEventPublisher"/> (Singleton).
    ///     </description>
    ///   </item>
    ///   <item>
    ///     <description>
    ///       <c>IMemoryCache</c> — required by <see cref="InMemoryCacheService"/>.
    ///     </description>
    ///   </item>
    /// </list>
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="configuration">The application configuration used to read connection strings and options.</param>
    /// <returns>The same <see cref="IServiceCollection"/> for chaining.</returns>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // ---- EF Core ----
        services.AddDbContext<PolicyDbContext>(opts =>
            opts.UseSqlServer(
                configuration.GetConnectionString("DefaultConnection"),
                sql => sql.MigrationsAssembly(typeof(PolicyDbContext).Assembly.FullName)));

        // ---- Repository ----
        services.AddScoped<IPolicyRepository, PolicyRepository>();

        // ---- Cache ----
        services.AddMemoryCache();
        services.Configure<CacheOptions>(opts =>
        {
            configuration.GetSection(CacheOptions.SectionName).Bind(opts);
        });
        services.AddSingleton<ICacheService, InMemoryCacheService>();

        // ---- Event publisher ----
        services.AddSingleton<IEventPublisher, InMemoryEventPublisher>();

        return services;
    }

    /// <summary>
    /// Applies any pending EF Core migrations and seeds the database if the
    /// <c>Policies</c> table is empty. Safe to call on every startup.
    /// </summary>
    /// <param name="serviceProvider">The root service provider.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task ApplyMigrationsAndSeedAsync(
        IServiceProvider serviceProvider,
        CancellationToken cancellationToken = default)
    {
        using var scope = serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PolicyDbContext>();

        await dbContext.Database.MigrateAsync(cancellationToken);
        await dbContext.SeedAsync(cancellationToken);
    }
}
