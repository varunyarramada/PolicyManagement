using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PolicyManagement.Application.DTOs;
using PolicyManagement.Application.Features.Policies.Queries.GetPolicyById;
using PolicyManagement.Application.Interfaces;
using PolicyManagement.Application.Options;
using PolicyManagement.Domain.Exceptions;
using PolicyManagement.Domain.Interfaces;
using PolicyManagement.TestHelpers;
using Xunit;
using IOptions = Microsoft.Extensions.Options.Options;
using Microsoft.Extensions.Options;

namespace PolicyManagement.Application.Tests.Features.Policies.Queries;

/// <summary>
/// Unit tests for <see cref="GetPolicyByIdQueryHandler"/>.
/// Repository and cache are mocked — no database or infrastructure dependency.
/// </summary>
public sealed class GetPolicyByIdQueryHandlerTests
{
    private readonly Mock<IPolicyRepository> _repositoryMock;
    private readonly Mock<ICacheService> _cacheMock;
    private readonly GetPolicyByIdQueryHandler _handler;

    public GetPolicyByIdQueryHandlerTests()
    {
        _repositoryMock = new Mock<IPolicyRepository>();
        _cacheMock      = new Mock<ICacheService>();

        var options = IOptions.Create(new CacheOptions
        {
            PolicyTtlSeconds  = 300,
            SummaryTtlSeconds = 60,
        });

        _handler = new GetPolicyByIdQueryHandler(
            _repositoryMock.Object,
            _cacheMock.Object,
            options,
            NullLogger<GetPolicyByIdQueryHandler>.Instance);
    }

    // -----------------------------------------------------------------------
    // Happy path — existing policy, cache miss
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenPolicyExistsAndCacheMiss_ShouldReturnMappedDto()
    {
        // Arrange
        var policy = new PolicyBuilder()
            .WithId(Guid.NewGuid())
            .WithPolicyNumber("POL-000001")
            .WithPolicyholderName("Alice Smith")
            .Build();

        _cacheMock
            .Setup(c => c.GetAsync<PolicyDto>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PolicyDto?)null);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(policy.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        // Act
        var result = await _handler.Handle(new GetPolicyByIdQuery(policy.Id), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(policy.Id);
        result.PolicyNumber.Should().Be("POL-000001");
        result.PolicyholderName.Should().Be("Alice Smith");
        result.Status.Should().Be("Active");
        result.Currency.Should().Be("USD");
    }

    [Fact]
    public async Task Handle_WhenLineOfBusinessIsAH_ShouldMapToAmpersandHString()
    {
        // Arrange
        var policy = new PolicyBuilder()
            .WithLineOfBusiness(Domain.Enums.LineOfBusiness.AH)
            .Build();

        _cacheMock
            .Setup(c => c.GetAsync<PolicyDto>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PolicyDto?)null);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(policy.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        // Act
        var result = await _handler.Handle(new GetPolicyByIdQuery(policy.Id), CancellationToken.None);

        // Assert
        result.LineOfBusiness.Should().Be("A&H");
    }

    // -----------------------------------------------------------------------
    // Unknown ID — PolicyNotFoundException
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenPolicyNotFound_ShouldThrowPolicyNotFoundException()
    {
        // Arrange
        var missingId = Guid.NewGuid();

        _cacheMock
            .Setup(c => c.GetAsync<PolicyDto>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PolicyDto?)null);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Entities.Policy?)null);

        // Act
        var act = () => _handler.Handle(new GetPolicyByIdQuery(missingId), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<PolicyNotFoundException>()
            .Where(ex => ex.PolicyId == missingId);
    }

    [Fact]
    public async Task Handle_WhenPolicyNotFound_ShouldNotCallCacheSet()
    {
        // Arrange
        _cacheMock
            .Setup(c => c.GetAsync<PolicyDto>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PolicyDto?)null);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Entities.Policy?)null);

        // Act
        var act = () => _handler.Handle(new GetPolicyByIdQuery(Guid.NewGuid()), CancellationToken.None);
        await act.Should().ThrowAsync<PolicyNotFoundException>();

        // Assert — cache.SetAsync must never be called when the policy is not found
        _cacheMock.Verify(
            c => c.SetAsync(It.IsAny<string>(), It.IsAny<PolicyDto>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // Cache hit — repository not called
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenCacheHit_ShouldReturnCachedDtoWithoutCallingRepository()
    {
        // Arrange
        var id = Guid.NewGuid();
        var cachedDto = new PolicyDto(
            Id:               id,
            PolicyNumber:     "POL-CACHED",
            PolicyholderName: "Cached Holder",
            LineOfBusiness:   "Marine",
            Status:           "Active",
            PremiumAmount:    50_000m,
            Currency:         "SGD",
            EffectiveDate:    new DateOnly(2024, 1, 1),
            ExpiryDate:       new DateOnly(2025, 1, 1),
            Region:           "Singapore",
            Underwriter:      "Cached Underwriter",
            FlaggedForReview: false,
            CreatedAt:        DateTimeOffset.UtcNow,
            UpdatedAt:        DateTimeOffset.UtcNow);

        _cacheMock
            .Setup(c => c.GetAsync<PolicyDto>($"policy:v1:{id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedDto);

        // Act
        var result = await _handler.Handle(new GetPolicyByIdQuery(id), CancellationToken.None);

        // Assert
        result.Should().BeSameAs(cachedDto);

        _repositoryMock.Verify(
            r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCacheHit_ShouldNotCallCacheSet()
    {
        // Arrange
        var id = Guid.NewGuid();
        var cachedDto = new PolicyDto(
            Id:               id,
            PolicyNumber:     "POL-CACHED",
            PolicyholderName: "Cached Holder",
            LineOfBusiness:   "Property",
            Status:           "Active",
            PremiumAmount:    10_000m,
            Currency:         "USD",
            EffectiveDate:    new DateOnly(2024, 1, 1),
            ExpiryDate:       new DateOnly(2025, 1, 1),
            Region:           "Japan",
            Underwriter:      "U1",
            FlaggedForReview: false,
            CreatedAt:        DateTimeOffset.UtcNow,
            UpdatedAt:        DateTimeOffset.UtcNow);

        _cacheMock
            .Setup(c => c.GetAsync<PolicyDto>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedDto);

        // Act
        await _handler.Handle(new GetPolicyByIdQuery(id), CancellationToken.None);

        // Assert — cache.SetAsync must NOT be called on a cache hit
        _cacheMock.Verify(
            c => c.SetAsync(It.IsAny<string>(), It.IsAny<PolicyDto>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // Cache miss — repository called, cache populated
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenCacheMiss_ShouldCallRepositoryExactlyOnce()
    {
        // Arrange
        var policy = new PolicyBuilder().Build();

        _cacheMock
            .Setup(c => c.GetAsync<PolicyDto>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PolicyDto?)null);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(policy.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        // Act
        await _handler.Handle(new GetPolicyByIdQuery(policy.Id), CancellationToken.None);

        // Assert
        _repositoryMock.Verify(
            r => r.GetByIdAsync(policy.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCacheMiss_ShouldSetCacheWithCorrectKeyAndTtl()
    {
        // Arrange
        var policy = new PolicyBuilder().Build();
        var expectedKey = $"policy:v1:{policy.Id}";
        var expectedTtl = TimeSpan.FromSeconds(300); // PolicyTtlSeconds default

        _cacheMock
            .Setup(c => c.GetAsync<PolicyDto>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PolicyDto?)null);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(policy.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        // Act
        await _handler.Handle(new GetPolicyByIdQuery(policy.Id), CancellationToken.None);

        // Assert — cache.SetAsync called with the correct key and TTL
        _cacheMock.Verify(
            c => c.SetAsync(
                expectedKey,
                It.IsAny<PolicyDto>(),
                expectedTtl,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCacheMiss_ShouldSetCacheWithMappedDto()
    {
        // Arrange
        var policy = new PolicyBuilder()
            .WithPolicyNumber("POL-999999")
            .Build();

        _cacheMock
            .Setup(c => c.GetAsync<PolicyDto>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PolicyDto?)null);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(policy.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        PolicyDto? storedDto = null;
        _cacheMock
            .Setup(c => c.SetAsync(It.IsAny<string>(), It.IsAny<PolicyDto>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()))
            .Callback<string, PolicyDto, TimeSpan, CancellationToken>((_, dto, _, _) => storedDto = dto)
            .Returns(Task.CompletedTask);

        // Act
        var result = await _handler.Handle(new GetPolicyByIdQuery(policy.Id), CancellationToken.None);

        // Assert — the DTO stored in the cache is the same as the one returned
        storedDto.Should().NotBeNull();
        storedDto!.PolicyNumber.Should().Be("POL-999999");
        storedDto.Should().BeEquivalentTo(result);
    }

    // -----------------------------------------------------------------------
    // Cache key format
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_Always_ShouldUseCorrectCacheKeyFormat()
    {
        // Arrange
        var id = Guid.NewGuid();
        var expectedKey = $"policy:v1:{id}";

        _cacheMock
            .Setup(c => c.GetAsync<PolicyDto>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PolicyDto?)null);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Domain.Entities.Policy?)null);

        // Act — will throw PolicyNotFoundException, but we only care about the cache key used
        var act = () => _handler.Handle(new GetPolicyByIdQuery(id), CancellationToken.None);
        await act.Should().ThrowAsync<PolicyNotFoundException>();

        // Assert — cache was queried with the correct key
        _cacheMock.Verify(
            c => c.GetAsync<PolicyDto>(expectedKey, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
