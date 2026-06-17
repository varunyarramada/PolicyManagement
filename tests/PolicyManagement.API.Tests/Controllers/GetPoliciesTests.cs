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
/// Integration tests for <c>GET /api/v1/policies</c>.
/// Covers: 200 OK, 400 Bad Request, 401 Unauthorized, 500 Internal Server Error.
/// </summary>
[Collection("ApiIntegration")]
public sealed class GetPoliciesTests : IAsyncLifetime
{
    private readonly PolicyApiFactory _factory = new();
    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    public async Task InitializeAsync()
    {
        var policies = new[]
        {
            new PolicyBuilder().WithPolicyNumber("POL-000001").Build(),
            new PolicyBuilder().WithPolicyNumber("POL-000002").Build(),
            new PolicyBuilder().WithPolicyNumber("POL-000003").Build(),
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
    public async Task GetPolicies_WhenAuthenticatedAndPoliciesExist_ShouldReturn200WithPagedResponse()
    {
        // Arrange
        var client = _factory.CreateReadOnlyClient();

        // Act
        var response = await client.GetAsync("/api/v1/policies");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.TryGetProperty("data", out _).Should().BeTrue("response should have a 'data' array");
        doc.RootElement.TryGetProperty("pagination", out _).Should().BeTrue("response should have a 'pagination' object");
    }

    [Fact]
    public async Task GetPolicies_WhenAuthenticatedWithFilters_ShouldReturn200()
    {
        // Arrange
        var client = _factory.CreateReadOnlyClient();

        // Act
        var response = await client.GetAsync("/api/v1/policies?page=1&size=10&status=Active");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // -----------------------------------------------------------------------
    // 400 Bad Request (invalid query parameters caught by validator)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetPolicies_WhenPageIsZero_ShouldReturn400ProblemDetails()
    {
        // Arrange
        var client = _factory.CreateReadOnlyClient();

        // Act
        var response = await client.GetAsync("/api/v1/policies?page=0");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem!.Status.Should().Be(400);
        problem.Extensions.Should().ContainKey("correlationId");
    }

    [Fact]
    public async Task GetPolicies_WhenSizeExceedsMaximum_ShouldReturn400ProblemDetails()
    {
        // Arrange
        var client = _factory.CreateReadOnlyClient();

        // Act
        var response = await client.GetAsync("/api/v1/policies?size=101");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    // -----------------------------------------------------------------------
    // 401 Unauthorized
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetPolicies_WhenNoTokenProvided_ShouldReturn401ProblemDetails()
    {
        // Arrange
        var client = _factory.CreateClient(); // no Bearer token

        // Act
        var response = await client.GetAsync("/api/v1/policies");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem!.Status.Should().Be(401);
        problem.Extensions.Should().ContainKey("correlationId");
    }

    [Fact]
    public async Task GetPolicies_WhenExpiredTokenProvided_ShouldReturn401ProblemDetails()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient(JwtTokenFactory.CreateExpiredToken());

        // Act
        var response = await client.GetAsync("/api/v1/policies");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    // -----------------------------------------------------------------------
    // 500 Internal Server Error
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetPolicies_WhenRepositoryThrows_ShouldReturn500ProblemDetails()
    {
        // Arrange — factory with a broken repository that throws unexpectedly
        await using var factory = new BrokenRepositoryApiFactory();
        var client = factory.CreateReadOnlyClient();

        // Act
        var response = await client.GetAsync("/api/v1/policies");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem!.Status.Should().Be(500);
        problem.Detail.Should().NotContain("stack"); // no stack trace in response
        problem.Extensions.Should().ContainKey("correlationId");
    }
}
