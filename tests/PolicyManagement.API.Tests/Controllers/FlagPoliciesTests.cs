using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using PolicyManagement.API.Tests.Helpers;
using PolicyManagement.TestHelpers;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace PolicyManagement.API.Tests.Controllers;

/// <summary>
/// Integration tests for <c>PATCH /api/v1/policies/flag</c>.
/// Covers: 204 No Content, 400 Bad Request, 401 Unauthorized, 403 Forbidden,
///         404 Not Found, 409 Conflict, 500 Internal Server Error.
/// </summary>
[Collection("ApiIntegration")]
public sealed class FlagPoliciesTests : IAsyncLifetime
{
    private readonly PolicyApiFactory _factory = new();
    private Guid _unflaggedPolicyId;
    private Guid _alreadyFlaggedPolicyId;

    private static readonly JsonSerializerOptions JsonOptions =
        new() { PropertyNameCaseInsensitive = true };

    private const string FlagEndpoint = "/api/v1/policies/flag";

    public async Task InitializeAsync()
    {
        _unflaggedPolicyId      = Guid.NewGuid();
        _alreadyFlaggedPolicyId = Guid.NewGuid();

        var unflagged = new PolicyBuilder()
            .WithId(_unflaggedPolicyId)
            .WithPolicyNumber("POL-003001")
            .Build();

        var alreadyFlagged = new PolicyBuilder()
            .WithId(_alreadyFlaggedPolicyId)
            .WithPolicyNumber("POL-003002")
            .Build();

        // Flag the second policy before seeding (domain method to avoid bypassing invariants)
        alreadyFlagged.Flag(DateTimeOffset.UtcNow);

        await _factory.InitialiseDatabaseAsync(unflagged, alreadyFlagged);
    }

    public Task DisposeAsync()
    {
        _factory.Dispose();
        return Task.CompletedTask;
    }

    // -----------------------------------------------------------------------
    // 204 No Content
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FlagPolicies_WhenValidRequestWithWriteRole_ShouldReturn204()
    {
        // Arrange
        var client  = _factory.CreateWriteClient();
        var payload = new { policyIds = new[] { _unflaggedPolicyId } };

        // Act
        var response = await client.PatchAsJsonAsync(FlagEndpoint, payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    // -----------------------------------------------------------------------
    // 400 Bad Request
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FlagPolicies_WhenPolicyIdsIsEmpty_ShouldReturn400ProblemDetails()
    {
        // Arrange
        var client  = _factory.CreateWriteClient();
        var payload = new { policyIds = Array.Empty<Guid>() };

        // Act
        var response = await client.PatchAsJsonAsync(FlagEndpoint, payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem!.Status.Should().Be(400);
        problem.Extensions.Should().ContainKey("correlationId");
    }

    [Fact]
    public async Task FlagPolicies_WhenRequestBodyIsMalformed_ShouldReturn400ProblemDetails()
    {
        // Arrange
        var client  = _factory.CreateWriteClient();
        var content = new StringContent("not-valid-json",
            System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await client.PatchAsync(FlagEndpoint, content);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // -----------------------------------------------------------------------
    // 401 Unauthorized
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FlagPolicies_WhenNoTokenProvided_ShouldReturn401ProblemDetails()
    {
        // Arrange
        var client  = _factory.CreateClient();
        var payload = new { policyIds = new[] { _unflaggedPolicyId } };

        // Act
        var response = await client.PatchAsJsonAsync(FlagEndpoint, payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem!.Status.Should().Be(401);
        problem.Extensions.Should().ContainKey("correlationId");
    }

    [Fact]
    public async Task FlagPolicies_WhenExpiredTokenProvided_ShouldReturn401ProblemDetails()
    {
        // Arrange
        var client  = _factory.CreateAuthenticatedClient(JwtTokenFactory.CreateExpiredToken());
        var payload = new { policyIds = new[] { _unflaggedPolicyId } };

        // Act
        var response = await client.PatchAsJsonAsync(FlagEndpoint, payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    // -----------------------------------------------------------------------
    // 403 Forbidden
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FlagPolicies_WhenAuthenticatedWithoutWriteRole_ShouldReturn403ProblemDetails()
    {
        // Arrange — valid token, but no Policy.Write role
        var client  = _factory.CreateReadOnlyClient();
        var payload = new { policyIds = new[] { _unflaggedPolicyId } };

        // Act
        var response = await client.PatchAsJsonAsync(FlagEndpoint, payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem!.Status.Should().Be(403);
        problem.Extensions.Should().ContainKey("correlationId");
    }

    // -----------------------------------------------------------------------
    // 404 Not Found
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FlagPolicies_WhenPolicyDoesNotExist_ShouldReturn404ProblemDetails()
    {
        // Arrange
        var client    = _factory.CreateWriteClient();
        var missingId = Guid.NewGuid();
        var payload   = new { policyIds = new[] { missingId } };

        // Act
        var response = await client.PatchAsJsonAsync(FlagEndpoint, payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem!.Status.Should().Be(404);
        problem.Extensions.Should().ContainKey("correlationId");
    }

    // -----------------------------------------------------------------------
    // 409 Conflict
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FlagPolicies_WhenPolicyAlreadyFlagged_ShouldReturn409ProblemDetails()
    {
        // Arrange
        var client  = _factory.CreateWriteClient();
        var payload = new { policyIds = new[] { _alreadyFlaggedPolicyId } };

        // Act
        var response = await client.PatchAsJsonAsync(FlagEndpoint, payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem!.Status.Should().Be(409);
        problem.Extensions.Should().ContainKey("correlationId");
    }

    // -----------------------------------------------------------------------
    // 500 Internal Server Error
    // -----------------------------------------------------------------------

    [Fact]
    public async Task FlagPolicies_WhenRepositoryThrows_ShouldReturn500ProblemDetails()
    {
        // Arrange
        await using var factory = new BrokenRepositoryApiFactory();
        var client  = factory.CreateWriteClient();
        var payload = new { policyIds = new[] { Guid.NewGuid() } };

        // Act
        var response = await client.PatchAsJsonAsync(FlagEndpoint, payload);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(JsonOptions);
        problem!.Status.Should().Be(500);
        problem.Detail.Should().NotContain("stack");
        problem.Extensions.Should().ContainKey("correlationId");
    }
}
