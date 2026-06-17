using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PolicyManagement.Application.DTOs;
using PolicyManagement.Application.Features.Policies.Queries.GetPolicySummary;
using PolicyManagement.Application.Interfaces;
using PolicyManagement.Application.Options;
using PolicyManagement.Domain.Enums;
using PolicyManagement.Domain.Interfaces;
using PolicyManagement.Domain.Models;
using Xunit;
using IOptions = Microsoft.Extensions.Options.Options;

namespace PolicyManagement.Application.Tests.Features.Policies.Queries;

/// <summary>
/// Unit tests for <see cref="GetPolicySummaryQueryHandler"/>.
/// Repository and cache are mocked — no database or infrastructure dependency.
/// </summary>
public sealed class GetPolicySummaryQueryHandlerTests
{
    private readonly Mock<IPolicyRepository> _repositoryMock;
    private readonly Mock<ICacheService> _cacheMock;
    private readonly GetPolicySummaryQueryHandler _handler;

    public GetPolicySummaryQueryHandlerTests()
    {
        _repositoryMock = new Mock<IPolicyRepository>();
        _cacheMock      = new Mock<ICacheService>();

        var options = IOptions.Create(new CacheOptions
        {
            PolicyTtlSeconds  = 300,
            SummaryTtlSeconds = 60,
        });

        _handler = new GetPolicySummaryQueryHandler(
            _repositoryMock.Object,
            _cacheMock.Object,
            options,
            NullLogger<GetPolicySummaryQueryHandler>.Instance);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Builds a <see cref="PolicySummaryData"/> with deterministic values for assertion.
    /// </summary>
    private static PolicySummaryData BuildSummaryData(int expiringSoonCount = 5) =>
        new(
            TotalPolicies:          100,
            TotalPremium:           500_000m,
            FlaggedCount:           12,
            ExpiringSoonCount:      expiringSoonCount,
            CountByStatus: new Dictionary<PolicyStatus, int>
            {
                [PolicyStatus.Active]    = 60,
                [PolicyStatus.Expired]   = 25,
                [PolicyStatus.Pending]   = 10,
                [PolicyStatus.Cancelled] =  5,
            },
            CountByLineOfBusiness: new Dictionary<LineOfBusiness, int>
            {
                [LineOfBusiness.Property] = 40,
                [LineOfBusiness.Casualty] = 30,
                [LineOfBusiness.AH]       = 20,
                [LineOfBusiness.Marine]   = 10,
            },
            CountByRegion: new Dictionary<string, int>
            {
                ["Singapore"] = 30,
                ["Hong Kong"] = 25,
                ["Australia"] = 20,
                ["Japan"]     = 15,
                ["Thailand"]  = 10,
            },
            PremiumByCurrency: new Dictionary<string, decimal>
            {
                ["USD"] = 200_000m,
                ["SGD"] = 150_000m,
                ["HKD"] = 100_000m,
                ["AUD"] =  50_000m,
            });

    // -----------------------------------------------------------------------
    // Cache miss — correctly mapped response
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenCacheMiss_ShouldReturnCorrectlyMappedResponse()
    {
        // Arrange
        var data = BuildSummaryData();

        _cacheMock
            .Setup(c => c.GetAsync<PolicySummaryResponse>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PolicySummaryResponse?)null);

        _repositoryMock
            .Setup(r => r.GetSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(data);

        // Act
        var result = await _handler.Handle(new GetPolicySummaryQuery(), CancellationToken.None);

        // Assert — values asserted directly against the raw BuildSummaryData() constants
        // so that a bug in ToPolicySummaryResponse() is caught here, not hidden by
        // re-calling the same mapping method to produce the expected value.
        result.TotalCount.Should().Be(100);
        result.FlaggedCount.Should().Be(12);
        result.ExpiringSoonCount.Should().Be(5);
        result.CountByStatus["Active"].Should().Be(60);
        result.CountByStatus["Expired"].Should().Be(25);
        result.CountByStatus["Pending"].Should().Be(10);
        result.CountByStatus["Cancelled"].Should().Be(5);
        result.CountByRegion["Singapore"].Should().Be(30);
        result.CountByRegion["Hong Kong"].Should().Be(25);
        result.CountByLineOfBusiness["Property"].Should().Be(40);
        result.CountByLineOfBusiness["Casualty"].Should().Be(30);
        result.CountByLineOfBusiness["A&H"].Should().Be(20);   // LineOfBusiness.AH → "A&H"
        result.CountByLineOfBusiness["Marine"].Should().Be(10);
        result.PremiumTotalByCurrency["USD"].Should().Be(200_000m);
        result.PremiumTotalByCurrency["SGD"].Should().Be(150_000m);
        result.PremiumTotalByCurrency["HKD"].Should().Be(100_000m);
        result.PremiumTotalByCurrency["AUD"].Should().Be(50_000m);
    }

    [Fact]
    public async Task Handle_WhenCacheMiss_ShouldMapAHLineOfBusinessToDisplayString()
    {
        // Arrange
        var data = BuildSummaryData();

        _cacheMock
            .Setup(c => c.GetAsync<PolicySummaryResponse>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PolicySummaryResponse?)null);

        _repositoryMock
            .Setup(r => r.GetSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(data);

        // Act
        var result = await _handler.Handle(new GetPolicySummaryQuery(), CancellationToken.None);

        // Assert — LineOfBusiness.AH must be keyed as "A&H" in the response dictionary
        result.CountByLineOfBusiness.Should().ContainKey("A&H");
        result.CountByLineOfBusiness["A&H"].Should().Be(20);
    }

    [Fact]
    public async Task Handle_WhenCacheMiss_ShouldMapAllStatusKeys()
    {
        // Arrange
        var data = BuildSummaryData();

        _cacheMock
            .Setup(c => c.GetAsync<PolicySummaryResponse>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PolicySummaryResponse?)null);

        _repositoryMock
            .Setup(r => r.GetSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(data);

        // Act
        var result = await _handler.Handle(new GetPolicySummaryQuery(), CancellationToken.None);

        // Assert — all four status keys must be present with correct string keys
        result.CountByStatus.Should().ContainKey("Active").WhoseValue.Should().Be(60);
        result.CountByStatus.Should().ContainKey("Expired").WhoseValue.Should().Be(25);
        result.CountByStatus.Should().ContainKey("Pending").WhoseValue.Should().Be(10);
        result.CountByStatus.Should().ContainKey("Cancelled").WhoseValue.Should().Be(5);
    }

    // -----------------------------------------------------------------------
    // ExpiringSoonCount — 30-day window
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenCacheMiss_ShouldReflectExpiringSoonCountFrom30DayWindow()
    {
        // Arrange — ExpiringSoonCount is computed by the repository using a 30-day window;
        // the handler must pass it through unchanged to the response DTO.
        const int expiringSoon = 7;
        var data = BuildSummaryData(expiringSoonCount: expiringSoon);

        _cacheMock
            .Setup(c => c.GetAsync<PolicySummaryResponse>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PolicySummaryResponse?)null);

        _repositoryMock
            .Setup(r => r.GetSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(data);

        // Act
        var result = await _handler.Handle(new GetPolicySummaryQuery(), CancellationToken.None);

        // Assert — handler must not alter the expiring-soon count computed by the repository
        result.ExpiringSoonCount.Should().Be(expiringSoon);
    }

    [Fact]
    public async Task Handle_WhenNoPoliciesExpireSoon_ShouldReturnZeroExpiringSoonCount()
    {
        // Arrange
        var data = BuildSummaryData(expiringSoonCount: 0);

        _cacheMock
            .Setup(c => c.GetAsync<PolicySummaryResponse>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PolicySummaryResponse?)null);

        _repositoryMock
            .Setup(r => r.GetSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(data);

        // Act
        var result = await _handler.Handle(new GetPolicySummaryQuery(), CancellationToken.None);

        // Assert
        result.ExpiringSoonCount.Should().Be(0);
    }

    // -----------------------------------------------------------------------
    // Cache hit — repository not called
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenCacheHit_ShouldReturnCachedResponseWithoutCallingRepository()
    {
        // Arrange
        var cachedResponse = new PolicySummaryResponse(
            TotalCount:             50,
            FlaggedCount:           3,
            ExpiringSoonCount:      2,
            CountByStatus:          new Dictionary<string, int> { ["Active"] = 50 },
            CountByRegion:          new Dictionary<string, int> { ["Singapore"] = 50 },
            CountByLineOfBusiness:  new Dictionary<string, int> { ["Property"] = 50 },
            PremiumTotalByCurrency: new Dictionary<string, decimal> { ["USD"] = 100_000m });

        _cacheMock
            .Setup(c => c.GetAsync<PolicySummaryResponse>("policy:v1:summary", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedResponse);

        // Act
        var result = await _handler.Handle(new GetPolicySummaryQuery(), CancellationToken.None);

        // Assert
        result.Should().BeSameAs(cachedResponse);
        _repositoryMock.Verify(
            r => r.GetSummaryAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCacheHit_ShouldNotCallCacheSet()
    {
        // Arrange
        var cachedResponse = new PolicySummaryResponse(
            TotalCount:             10,
            FlaggedCount:           1,
            ExpiringSoonCount:      0,
            CountByStatus:          new Dictionary<string, int>(),
            CountByRegion:          new Dictionary<string, int>(),
            CountByLineOfBusiness:  new Dictionary<string, int>(),
            PremiumTotalByCurrency: new Dictionary<string, decimal>());

        _cacheMock
            .Setup(c => c.GetAsync<PolicySummaryResponse>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedResponse);

        // Act
        await _handler.Handle(new GetPolicySummaryQuery(), CancellationToken.None);

        // Assert
        _cacheMock.Verify(
            c => c.SetAsync(It.IsAny<string>(), It.IsAny<PolicySummaryResponse>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // Cache miss — repository called, cache populated
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenCacheMiss_ShouldCallRepositoryExactlyOnce()
    {
        // Arrange
        _cacheMock
            .Setup(c => c.GetAsync<PolicySummaryResponse>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PolicySummaryResponse?)null);

        _repositoryMock
            .Setup(r => r.GetSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSummaryData());

        // Act
        await _handler.Handle(new GetPolicySummaryQuery(), CancellationToken.None);

        // Assert
        _repositoryMock.Verify(
            r => r.GetSummaryAsync(It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCacheMiss_ShouldSetCacheWithCorrectKeyAndTtl()
    {
        // Arrange
        _cacheMock
            .Setup(c => c.GetAsync<PolicySummaryResponse>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PolicySummaryResponse?)null);

        _repositoryMock
            .Setup(r => r.GetSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildSummaryData());

        // Act
        await _handler.Handle(new GetPolicySummaryQuery(), CancellationToken.None);

        // Assert — exact cache key and TTL matching ADR-004 (SummaryTtlSeconds = 60)
        _cacheMock.Verify(
            c => c.SetAsync(
                "policy:v1:summary",
                It.IsAny<PolicySummaryResponse>(),
                TimeSpan.FromSeconds(60),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCacheMiss_ShouldSetCacheWithMappedResponse()
    {
        // Arrange
        var data = BuildSummaryData();
        PolicySummaryResponse? storedValue = null;

        _cacheMock
            .Setup(c => c.GetAsync<PolicySummaryResponse>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PolicySummaryResponse?)null);

        _repositoryMock
            .Setup(r => r.GetSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(data);

        _cacheMock
            .Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<PolicySummaryResponse>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<string, PolicySummaryResponse, TimeSpan, CancellationToken>((_, v, _, _) => storedValue = v)
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(new GetPolicySummaryQuery(), CancellationToken.None);

        // Assert — the value written to cache is the same object returned to the caller
        storedValue.Should().NotBeNull();
        storedValue.Should().BeSameAs(result);
    }

    // -----------------------------------------------------------------------
    // Cache key format
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_Always_ShouldUseCorrectCacheKeyFormat()
    {
        // Arrange
        var data = BuildSummaryData();
        const string expectedKey = "policy:v1:summary";

        _cacheMock
            .Setup(c => c.GetAsync<PolicySummaryResponse>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PolicySummaryResponse?)null);

        _repositoryMock
            .Setup(r => r.GetSummaryAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(data);

        // Act
        await _handler.Handle(new GetPolicySummaryQuery(), CancellationToken.None);

        // Assert — both GetAsync and SetAsync must use the same key
        _cacheMock.Verify(
            c => c.GetAsync<PolicySummaryResponse>(expectedKey, It.IsAny<CancellationToken>()),
            Times.Once);
        _cacheMock.Verify(
            c => c.SetAsync(expectedKey, It.IsAny<PolicySummaryResponse>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
