using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using PolicyManagement.API.Tests.Helpers;
using PolicyManagement.TestHelpers;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PolicyManagement.API.Tests.Controllers;

/// <summary>
/// Integration tests for <c>GET /api/v1/policies/summary</c>.
/// Covers: 200 OK, 401 Unauthorized, 500 Internal Server Error.
/// </summary>
[Collection("ApiIntegration")]
public sealed class GetPolicySummaryTests : IAsyncLifetime
{
    private readonly PolicyApiFactory _factory = new();

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    public async Task InitializeAsync()
    {
        var policies = new[]
        {
            new PolicyBuilder().WithPolicyNumber("POL-002001").Build(),
            new PolicyBuilder().WithPolicyNumber("POL-002002").Build(),
        };

        await _factory.InitialiseDatabaseAsync(policies);
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // -----------------------------------------------------------------------
    // 200 OK
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetPolicySummary_WhenAuthenticated_ShouldReturn200WithSummaryResponse()
    {
        // Arrange
        var client = _factory.CreateReadOnlyClient();

        // Act
        var response = await client.GetAsync("/api/v1/policies/summary");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("totalCount", out _).Should().BeTrue("response should contain 'totalCount'");
    }

    // -----------------------------------------------------------------------
    // 401 Unauthorized
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetPolicySummary_WhenNoTokenProvided_ShouldReturn401ProblemDetails()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/policies/summary");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem!.Status.Should().Be(401);
        problem.Extensions.Should().ContainKey("correlationId");
    }

    [Fact]
    public async Task GetPolicySummary_WhenExpiredTokenProvided_ShouldReturn401ProblemDetails()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient(JwtTokenFactory.CreateExpiredToken());

        // Act
        var response = await client.GetAsync("/api/v1/policies/summary");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    // -----------------------------------------------------------------------
    // 500 Internal Server Error
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetPolicySummary_WhenRepositoryThrows_ShouldReturn500ProblemDetails()
    {
        // Arrange
        await using var factory = new BrokenRepositoryApiFactory();
        var client = factory.CreateReadOnlyClient();

        // Act
        var response = await client.GetAsync("/api/v1/policies/summary");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem!.Status.Should().Be(500);
        problem.Detail.Should().NotContain("stack");
        problem.Extensions.Should().ContainKey("correlationId");
    }
}
