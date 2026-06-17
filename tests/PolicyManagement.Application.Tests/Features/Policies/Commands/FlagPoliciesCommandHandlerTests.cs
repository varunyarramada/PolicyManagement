using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PolicyManagement.Application.Features.Policies.Commands.FlagPolicies;
using PolicyManagement.Application.Interfaces;
using PolicyManagement.Domain.Events;
using PolicyManagement.Domain.Exceptions;
using PolicyManagement.Domain.Interfaces;
using PolicyManagement.TestHelpers;
using Xunit;

namespace PolicyManagement.Application.Tests.Features.Policies.Commands;

/// <summary>
/// Unit tests for <see cref="FlagPoliciesCommandHandler"/>.
/// Repository, event publisher, cache, and current-user service are mocked.
/// </summary>
public sealed class FlagPoliciesCommandHandlerTests
{
    private readonly Mock<IPolicyRepository> _repositoryMock;
    private readonly Mock<IEventPublisher> _eventPublisherMock;
    private readonly Mock<ICacheService> _cacheMock;
    private readonly Mock<ICurrentUserService> _currentUserMock;
    private readonly FlagPoliciesCommandHandler _handler;

    public FlagPoliciesCommandHandlerTests()
    {
        _repositoryMock    = new Mock<IPolicyRepository>();
        _eventPublisherMock = new Mock<IEventPublisher>();
        _cacheMock         = new Mock<ICacheService>();
        _currentUserMock   = new Mock<ICurrentUserService>();

        _currentUserMock.Setup(u => u.UserId).Returns("user-123");

        _cacheMock
            .Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _eventPublisherMock
            .Setup(e => e.PublishAsync(It.IsAny<PolicyFlaggedEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _repositoryMock
            .Setup(r => r.UpdateRangeAsync(It.IsAny<IEnumerable<Domain.Entities.Policy>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _handler = new FlagPoliciesCommandHandler(
            _repositoryMock.Object,
            _eventPublisherMock.Object,
            _cacheMock.Object,
            _currentUserMock.Object,
            NullLogger<FlagPoliciesCommandHandler>.Instance);
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Sets up <c>GetByIdsAsync</c> to return exactly the supplied policies (single batch query).
    /// </summary>
    private void SetupGetByIds(params Domain.Entities.Policy[] policies) =>
        _repositoryMock
            .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyList<Domain.Entities.Policy>)policies);

    // -----------------------------------------------------------------------
    // Happy path — all IDs exist, none already flagged
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenAllIdsExistAndNotFlagged_ShouldCallUpdateRangeOnce()
    {
        // Arrange
        var policies = new[]
        {
            new PolicyBuilder().WithId(Guid.NewGuid()).Build(),
            new PolicyBuilder().WithId(Guid.NewGuid()).Build(),
        };
        SetupGetByIds(policies);

        var command = new FlagPoliciesCommand(policies.Select(p => p.Id).ToList().AsReadOnly());

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        _repositoryMock.Verify(
            r => r.UpdateRangeAsync(It.IsAny<IEnumerable<Domain.Entities.Policy>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenAllIdsExistAndNotFlagged_ShouldIssueOneRepositoryBatchQuery()
    {
        // Arrange
        var policies = new[]
        {
            new PolicyBuilder().WithId(Guid.NewGuid()).Build(),
            new PolicyBuilder().WithId(Guid.NewGuid()).Build(),
            new PolicyBuilder().WithId(Guid.NewGuid()).Build(),
        };
        SetupGetByIds(policies);

        var command = new FlagPoliciesCommand(policies.Select(p => p.Id).ToList().AsReadOnly());

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — exactly one batch call regardless of how many IDs are in the command (no N+1)
        _repositoryMock.Verify(
            r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenAllIdsExistAndNotFlagged_ShouldFlagAllPolicies()
    {
        // Arrange
        var policies = new[]
        {
            new PolicyBuilder().WithId(Guid.NewGuid()).Build(),
            new PolicyBuilder().WithId(Guid.NewGuid()).Build(),
            new PolicyBuilder().WithId(Guid.NewGuid()).Build(),
        };
        SetupGetByIds(policies);

        IEnumerable<Domain.Entities.Policy>? savedPolicies = null;
        _repositoryMock
            .Setup(r => r.UpdateRangeAsync(It.IsAny<IEnumerable<Domain.Entities.Policy>>(), It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<Domain.Entities.Policy>, CancellationToken>((p, _) => savedPolicies = p)
            .Returns(Task.CompletedTask);

        var command = new FlagPoliciesCommand(policies.Select(p => p.Id).ToList().AsReadOnly());

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — every policy passed to UpdateRangeAsync must have FlaggedForReview = true
        savedPolicies.Should().NotBeNull();
        savedPolicies!.Should().AllSatisfy(p => p.FlaggedForReview.Should().BeTrue());
    }

    [Fact]
    public async Task Handle_WhenAllIdsExistAndNotFlagged_ShouldPublishOneEventPerPolicy()
    {
        // Arrange
        var policies = new[]
        {
            new PolicyBuilder().WithId(Guid.NewGuid()).Build(),
            new PolicyBuilder().WithId(Guid.NewGuid()).Build(),
        };
        SetupGetByIds(policies);

        var command = new FlagPoliciesCommand(policies.Select(p => p.Id).ToList().AsReadOnly());

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — one PolicyFlaggedEvent per policy, published after commit
        _eventPublisherMock.Verify(
            e => e.PublishAsync(It.IsAny<PolicyFlaggedEvent>(), It.IsAny<CancellationToken>()),
            Times.Exactly(policies.Length));
    }

    [Fact]
    public async Task Handle_WhenAllIdsExistAndNotFlagged_ShouldPublishEventsWithCorrectPolicyIds()
    {
        // Arrange
        var policy = new PolicyBuilder().WithId(Guid.NewGuid()).Build();
        SetupGetByIds(policy);

        PolicyFlaggedEvent? capturedEvent = null;
        _eventPublisherMock
            .Setup(e => e.PublishAsync(It.IsAny<PolicyFlaggedEvent>(), It.IsAny<CancellationToken>()))
            .Callback<PolicyFlaggedEvent, CancellationToken>((evt, _) => capturedEvent = evt)
            .Returns(Task.CompletedTask);

        var command = new FlagPoliciesCommand(new[] { policy.Id }.ToList().AsReadOnly());

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        capturedEvent.Should().NotBeNull();
        capturedEvent!.PolicyId.Should().Be(policy.Id);
        capturedEvent.FlaggedByUserId.Should().Be("user-123");
    }

    [Fact]
    public async Task Handle_WhenAllIdsExistAndNotFlagged_ShouldInvalidateSummaryCache()
    {
        // Arrange
        var policy = new PolicyBuilder().WithId(Guid.NewGuid()).Build();
        SetupGetByIds(policy);

        var command = new FlagPoliciesCommand(new[] { policy.Id }.ToList().AsReadOnly());

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — summary cache must be invalidated after commit
        _cacheMock.Verify(
            c => c.RemoveAsync("policy:v1:summary", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenAllIdsExistAndNotFlagged_ShouldInvalidatePerPolicyCacheEntries()
    {
        // Arrange
        var policies = new[]
        {
            new PolicyBuilder().WithId(Guid.NewGuid()).Build(),
            new PolicyBuilder().WithId(Guid.NewGuid()).Build(),
        };
        SetupGetByIds(policies);

        var command = new FlagPoliciesCommand(policies.Select(p => p.Id).ToList().AsReadOnly());

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — one RemoveAsync per policy with key policy:v1:{id}
        foreach (var p in policies)
        {
            _cacheMock.Verify(
                c => c.RemoveAsync($"policy:v1:{p.Id}", It.IsAny<CancellationToken>()),
                Times.Once);
        }
    }

    // -----------------------------------------------------------------------
    // Not found — throws PolicyNotFoundException
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenPolicyIdNotFound_ShouldThrowPolicyNotFoundException()
    {
        // Arrange
        var missingId = Guid.NewGuid();

        // Repository returns an empty list — the requested ID is absent
        _repositoryMock
            .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Domain.Entities.Policy>().AsReadOnly());

        var command = new FlagPoliciesCommand(new[] { missingId }.ToList().AsReadOnly());

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        var exception = await act.Should().ThrowAsync<PolicyNotFoundException>();
        exception.Which.PolicyId.Should().Be(missingId);
    }

    [Fact]
    public async Task Handle_WhenPolicyIdNotFound_ShouldNotCallUpdateRange()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Domain.Entities.Policy>().AsReadOnly());

        var command = new FlagPoliciesCommand(new[] { Guid.NewGuid() }.ToList().AsReadOnly());

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<PolicyNotFoundException>();
        _repositoryMock.Verify(
            r => r.UpdateRangeAsync(It.IsAny<IEnumerable<Domain.Entities.Policy>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPolicyIdNotFound_ShouldNotPublishEvents()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<Guid>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Domain.Entities.Policy>().AsReadOnly());

        var command = new FlagPoliciesCommand(new[] { Guid.NewGuid() }.ToList().AsReadOnly());

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<PolicyNotFoundException>();
        _eventPublisherMock.Verify(
            e => e.PublishAsync(It.IsAny<PolicyFlaggedEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // -----------------------------------------------------------------------
    // Already flagged — throws InvalidPolicyStateException
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenPolicyAlreadyFlagged_ShouldThrowInvalidPolicyStateException()
    {
        // Arrange
        var policy = new PolicyBuilder().WithId(Guid.NewGuid()).Build();
        policy.Flag(DateTimeOffset.UtcNow); // already flagged
        SetupGetByIds(policy);

        var command = new FlagPoliciesCommand(new[] { policy.Id }.ToList().AsReadOnly());

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        var exception = await act.Should().ThrowAsync<InvalidPolicyStateException>();
        exception.Which.PolicyId.Should().Be(policy.Id);
    }

    [Fact]
    public async Task Handle_WhenPolicyAlreadyFlagged_ShouldNotCallUpdateRange()
    {
        // Arrange
        var policy = new PolicyBuilder().WithId(Guid.NewGuid()).Build();
        policy.Flag(DateTimeOffset.UtcNow);
        SetupGetByIds(policy);

        var command = new FlagPoliciesCommand(new[] { policy.Id }.ToList().AsReadOnly());

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidPolicyStateException>();
        _repositoryMock.Verify(
            r => r.UpdateRangeAsync(It.IsAny<IEnumerable<Domain.Entities.Policy>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPolicyAlreadyFlagged_ShouldNotInvalidateCache()
    {
        // Arrange
        var policy = new PolicyBuilder().WithId(Guid.NewGuid()).Build();
        policy.Flag(DateTimeOffset.UtcNow);
        SetupGetByIds(policy);

        var command = new FlagPoliciesCommand(new[] { policy.Id }.ToList().AsReadOnly());

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert — cache must NOT be touched when the command fails validation
        await act.Should().ThrowAsync<InvalidPolicyStateException>();
        _cacheMock.Verify(
            c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
