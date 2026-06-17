using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Moq;
using PolicyManagement.Domain.Entities;
using PolicyManagement.Domain.Filters;
using PolicyManagement.Domain.Interfaces;
using PolicyManagement.Domain.Models;
using PolicyManagement.Infrastructure.Persistence;

namespace PolicyManagement.API.Tests.Helpers;

/// <summary>
/// A <see cref="WebApplicationFactory{TEntryPoint}"/> variant that replaces
/// <see cref="IPolicyRepository"/> with a mock that throws <see cref="InvalidOperationException"/>
/// on every method call, producing a 500 Internal Server Error response from
/// <c>GlobalExceptionMiddleware</c>.
/// Used exclusively for 500 status code integration tests.
/// </summary>
public sealed class BrokenRepositoryApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = $"BrokenDb_{Guid.NewGuid():N}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Test");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Jwt:Authority"]             = "https://test-authority",
                ["Jwt:Audience"]              = "test-audience",
                ["Jwt:RequireHttpsMetadata"]  = "false",
                ["ConnectionStrings:DefaultConnection"] =
                    "Server=.;Database=TestDb;Integrated Security=true;",
            });
        });

        builder.ConfigureServices(services =>
        {
            // ---- Replace SQL Server DbContext with InMemory ----
            services.RemoveAll<DbContextOptions<PolicyDbContext>>();
            var inMemoryServiceProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();
            services.AddDbContext<PolicyDbContext>(opts =>
                opts.UseInMemoryDatabase(_databaseName)
                    .UseInternalServiceProvider(inMemoryServiceProvider));

            // ---- Replace IPolicyRepository with a broken mock ----
            services.RemoveAll<IPolicyRepository>();
            var brokenRepo = new Mock<IPolicyRepository>();

            brokenRepo
                .Setup(r => r.GetPagedAsync(It.IsAny<PolicyFilter>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Simulated repository failure for 500 test."));

            brokenRepo
                .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Simulated repository failure for 500 test."));

            brokenRepo
                .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Simulated repository failure for 500 test."));

            brokenRepo
                .Setup(r => r.GetSummaryAsync(It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("Simulated repository failure for 500 test."));

            services.AddScoped<IPolicyRepository>(_ => brokenRepo.Object);

            // ---- Override JWT Bearer to accept test-signed tokens ----
            services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme,
                opts =>
                {
                    opts.Authority = null;
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

    /// <inheritdoc cref="PolicyApiFactory.CreateAuthenticatedClient"/>
    public HttpClient CreateAuthenticatedClient(string token)
    {
        var client = CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    /// <inheritdoc cref="PolicyApiFactory.CreateReadOnlyClient"/>
    public HttpClient CreateReadOnlyClient() =>
        CreateAuthenticatedClient(JwtTokenFactory.CreateToken());

    /// <inheritdoc cref="PolicyApiFactory.CreateWriteClient"/>
    public HttpClient CreateWriteClient() =>
        CreateAuthenticatedClient(JwtTokenFactory.CreateToken(["Policy.Write"]));
}
