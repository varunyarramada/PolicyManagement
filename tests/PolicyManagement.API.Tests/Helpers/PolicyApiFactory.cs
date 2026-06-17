using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.IdentityModel.Tokens;
using PolicyManagement.Domain.Entities;
using PolicyManagement.Infrastructure.Persistence;

namespace PolicyManagement.API.Tests.Helpers;

/// <summary>
/// <see cref="WebApplicationFactory{TEntryPoint}"/> for integration tests.
/// Overrides:
/// <list type="bullet">
///   <item><description>SQL Server DbContext → EF Core InMemory (unique per factory instance)</description></item>
///   <item><description>JWT Bearer middleware → validates tokens signed by <see cref="JwtTokenFactory.SigningKey"/></description></item>
///   <item><description>Environment = "Test" → skips EF Core migrations (no SQL Server required)</description></item>
/// </list>
/// </summary>
public sealed class PolicyApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"PolicyTestDb_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Test" environment causes Program.cs to skip MigrateAsync / SeedAsync
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // Provide required configuration values so ValidateOnStart() passes
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Authority"]             = "https://test-authority",
                ["Jwt:Audience"]              = "test-audience",
                ["Jwt:RequireHttpsMetadata"]  = "false",
                // Non-empty connection string prevents ArgumentNullException in AddSqlServer health check
                ["ConnectionStrings:DefaultConnection"] =
                    "Server=.;Database=TestDb;Integrated Security=true;",
            });
        });

        builder.ConfigureServices(services =>
        {
            // ---- Replace SQL Server DbContext with InMemory ----
            // Use a dedicated internal service provider so that EF Core's InMemory provider
            // services do not conflict with the pre-registered SQL Server provider services.
            services.RemoveAll<DbContextOptions<PolicyDbContext>>();
            var inMemoryServiceProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();
            services.AddDbContext<PolicyDbContext>(opts =>
                opts.UseInMemoryDatabase(_databaseName)
                    .UseInternalServiceProvider(inMemoryServiceProvider));

            // ---- Remove the SQL Server health check (no SQL Server in test environment) ----
            // Removing the registration means the readiness probe has no degraded dependency
            // and will return 200 OK in the test environment.
            services.Configure<HealthCheckServiceOptions>(opts =>
            {
                var sqlCheck = opts.Registrations.FirstOrDefault(r => r.Name == "sql-server");
                if (sqlCheck is not null)
                    opts.Registrations.Remove(sqlCheck);
            });

            // ---- Override JWT Bearer to accept test-signed tokens ----
            services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                opts =>
                {
                    opts.Authority = null; // Disable OIDC discovery (no running Keycloak)
                    opts.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer           = false,
                        ValidateAudience         = false,
                        ValidateLifetime         = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey         = JwtTokenFactory.SigningKey,
                    };
                });
        });
    }

    /// <summary>
    /// Ensures the InMemory database schema is created and optionally seeds policies.
    /// Call once before any request in a test class.
    /// </summary>
    public async Task InitialiseDatabaseAsync(params Policy[] policies)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PolicyDbContext>();
        await db.Database.EnsureCreatedAsync();

        if (policies.Length > 0)
        {
            db.Policies.AddRange(policies);
            await db.SaveChangesAsync();
        }
    }

    /// <summary>
    /// Creates an <see cref="HttpClient"/> with the provided Bearer token pre-configured.
    /// </summary>
    public HttpClient CreateAuthenticatedClient(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <summary>
    /// Creates an <see cref="HttpClient"/> with a valid read-only Bearer token
    /// (no <c>Policy.Write</c> role).
    /// </summary>
    public HttpClient CreateReadOnlyClient() =>
        CreateAuthenticatedClient(JwtTokenFactory.CreateToken());

    /// <summary>
    /// Creates an <see cref="HttpClient"/> with a Bearer token that includes
    /// the <c>Policy.Write</c> role.
    /// </summary>
    public HttpClient CreateWriteClient() =>
        CreateAuthenticatedClient(JwtTokenFactory.CreateToken(["Policy.Write"]));
}
