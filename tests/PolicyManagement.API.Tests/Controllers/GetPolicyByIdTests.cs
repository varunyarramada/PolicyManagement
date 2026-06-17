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
/// Integration tests for <c>GET /api/v1/policies/{id}</c>.
/// Covers: 200 OK, 401 Unauthorized, 404 Not Found, 500 Internal Server Error.
/// </summary>
[Collection("ApiIntegration")]
public sealed class GetPolicyByIdTests : IAsyncLifetime
{
    private readonly PolicyApiFactory _factory = new();
    private Guid _existingPolicyId;

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    public async Task InitializeAsync()
    {
        _existingPolicyId = Guid.NewGuid();
        var policy = new PolicyBuilder()
            .WithId(_existingPolicyId)
            .WithPolicyNumber("POL-001001")
            .Build();

        await _factory.InitialiseDatabaseAsync(policy);
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
    public async Task GetPolicyById_WhenPolicyExists_ShouldReturn200WithPolicyDto()
    {
        // Arrange
        var client = _factory.CreateReadOnlyClient();

        // Act
        var response = await client.GetAsync($"/api/v1/policies/{_existingPolicyId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/json");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        doc.RootElement.GetProperty("id").GetGuid().Should().Be(_existingPolicyId);
    }

    // -----------------------------------------------------------------------
    // 401 Unauthorized
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetPolicyById_WhenNoTokenProvided_ShouldReturn401ProblemDetails()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync($"/api/v1/policies/{_existingPolicyId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem!.Status.Should().Be(401);
        problem.Extensions.Should().ContainKey("correlationId");
    }

    [Fact]
    public async Task GetPolicyById_WhenExpiredTokenProvided_ShouldReturn401ProblemDetails()
    {
        // Arrange
        var client = _factory.CreateAuthenticatedClient(JwtTokenFactory.CreateExpiredToken());

        // Act
        var response = await client.GetAsync($"/api/v1/policies/{_existingPolicyId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    // -----------------------------------------------------------------------
    // 404 Not Found
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetPolicyById_WhenPolicyDoesNotExist_ShouldReturn404ProblemDetails()
    {
        // Arrange
        var client   = _factory.CreateReadOnlyClient();
        var missingId = Guid.NewGuid();

        // Act
        var response = await client.GetAsync($"/api/v1/policies/{missingId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem!.Status.Should().Be(404);
        problem.Extensions.Should().ContainKey("correlationId");
    }

    // -----------------------------------------------------------------------
    // 500 Internal Server Error
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetPolicyById_WhenRepositoryThrows_ShouldReturn500ProblemDetails()
    {
        // Arrange
        await using var factory = new BrokenRepositoryApiFactory();
        var client = factory.CreateReadOnlyClient();
        var id = Guid.NewGuid();

        // Act
        var response = await client.GetAsync($"/api/v1/policies/{id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem!.Status.Should().Be(500);
        problem.Detail.Should().NotContain("stack");
        problem.Extensions.Should().ContainKey("correlationId");
    }
}
