using FluentAssertions;
using PolicyManagement.API.Tests.Helpers;
using System.Net;
using Xunit;

namespace PolicyManagement.API.Tests.Health;

/// <summary>
/// Integration tests for health check endpoints.
/// Both <c>/health/live</c> and <c>/health/ready</c> must be reachable without
/// a JWT Bearer token -- they are infrastructure endpoints for container orchestration.
/// </summary>
[Collection("ApiIntegration")]
public sealed class HealthCheckIntegrationTests : IAsyncLifetime
{
    private readonly PolicyApiFactory _factory = new();

    public Task InitializeAsync() => _factory.InitialiseDatabaseAsync();

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // -----------------------------------------------------------------------
    // GET /health/live -- 200 (no token required)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HealthLive_WhenNoTokenProvided_ShouldReturn200()
    {
        // Arrange -- unauthenticated client (no Bearer header)
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/live");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HealthLive_WhenAuthenticatedClient_ShouldReturn200()
    {
        // Arrange
        var client = _factory.CreateReadOnlyClient();

        // Act
        var response = await client.GetAsync("/health/live");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // -----------------------------------------------------------------------
    // GET /health/ready -- 200 (no token required)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HealthReady_WhenNoTokenProvided_ShouldBeReachableWithoutAuthError()
    {
        // Arrange -- unauthenticated client (no Bearer header)
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/health/ready");

        // Assert -- SQL Server health check is removed in the test environment,
        // so the readiness probe has no degraded dependency and always returns 200.
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "the readiness probe should be reachable without a token and return 200 in the test environment");
    }

    [Fact]
    public async Task HealthReady_WhenAuthenticatedClient_ShouldReturn200()
    {
        // Arrange
        var client = _factory.CreateReadOnlyClient();

        // Act
        var response = await client.GetAsync("/health/ready");

        // Assert -- SQL Server health check is removed in the test environment,
        // so the readiness probe always returns 200.
        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "health check endpoints must not require authentication and must return 200 in the test environment");
    }
}