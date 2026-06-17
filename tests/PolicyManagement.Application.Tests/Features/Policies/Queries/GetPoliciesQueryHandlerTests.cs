using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using PolicyManagement.Application.Features.Policies.Queries.GetPolicies;
using PolicyManagement.TestHelpers;
using PolicyManagement.Domain.Enums;
using PolicyManagement.Domain.Filters;
using PolicyManagement.Domain.Interfaces;
using Xunit;

namespace PolicyManagement.Application.Tests.Features.Policies.Queries;

/// <summary>
/// Unit tests for <see cref="GetPoliciesQueryHandler"/>.
/// The repository is mocked — no database or EF Core dependency.
/// </summary>
public sealed class GetPoliciesQueryHandlerTests
{
    private readonly Mock<IPolicyRepository> _repositoryMock;
    private readonly GetPoliciesQueryHandler _handler;

    public GetPoliciesQueryHandlerTests()
    {
        _repositoryMock = new Mock<IPolicyRepository>();
        _handler = new GetPoliciesQueryHandler(
            _repositoryMock.Object,
            NullLogger<GetPoliciesQueryHandler>.Instance);
    }

    // -----------------------------------------------------------------------
    // Happy path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenPoliciesExist_ShouldReturnMappedDtos()
    {
        // Arrange
        var policies = PolicyBuilder.BuildMany(3);
        _repositoryMock
            .Setup(r => r.GetPagedAsync(It.IsAny<PolicyFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((policies, 3));

        var query   = new GetPoliciesQuery(Page: 1, Size: 20);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert
        result.Data.Should().HaveCount(3);
        result.Pagination.Page.Should().Be(1);
        result.Pagination.Size.Should().Be(20);
        result.Pagination.TotalCount.Should().Be(3);
        result.Pagination.TotalPages.Should().Be(1);

        // Verify fields map correctly for first item
        var first = result.Data[0];
        first.Id.Should().Be(policies[0].Id);
        first.PolicyNumber.Should().Be(policies[0].PolicyNumber);
        first.PolicyholderName.Should().Be(policies[0].PolicyholderName);
    }

    [Fact]
    public async Task Handle_WhenLineOfBusinessIsAH_ShouldMapToAmpersandHString()
    {
        // Arrange
        var policy = new PolicyBuilder()
            .WithLineOfBusiness(LineOfBusiness.AH)
            .Build();

        _repositoryMock
            .Setup(r => r.GetPagedAsync(It.IsAny<PolicyFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((new[] { policy }, 1));


        // Act
        var result = await _handler.Handle(new GetPoliciesQuery(), CancellationToken.None);

        // Assert
        result.Data[0].LineOfBusiness.Should().Be("A&H");
    }

    // -----------------------------------------------------------------------
    // Empty result
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenNoMatchingPolicies_ShouldReturnEmptyDataArray()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetPagedAsync(It.IsAny<PolicyFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<Domain.Entities.Policy>(), 0));


        // Act
        var result = await _handler.Handle(new GetPoliciesQuery(), CancellationToken.None);

        // Assert
        result.Data.Should().BeEmpty();
        result.Pagination.TotalCount.Should().Be(0);
        result.Pagination.TotalPages.Should().Be(1); // PaginationMeta.Create returns 1 on zero count
    }

    // -----------------------------------------------------------------------
    // Page beyond total
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenPageBeyondTotalCount_ShouldReturn200WithEmptyData()
    {
        // Arrange — 3 total records, requesting page 5 (page 5 × size 20 > 3)
        _repositoryMock
            .Setup(r => r.GetPagedAsync(It.IsAny<PolicyFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<Domain.Entities.Policy>(), 3));

        var query   = new GetPoliciesQuery(Page: 5, Size: 20);

        // Act
        var result = await _handler.Handle(query, CancellationToken.None);

        // Assert — HTTP 200 is implied by successful return; empty data + full pagination meta
        result.Data.Should().BeEmpty();
        result.Pagination.TotalCount.Should().Be(3);
        result.Pagination.Page.Should().Be(5);
        result.Pagination.TotalPages.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Sort parsing
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("premiumAmount,desc", "premiumAmount", SortDirection.Desc)]
    [InlineData("policyNumber,asc",   "policyNumber",  SortDirection.Asc)]
    [InlineData("createdAt,desc",     "createdAt",     SortDirection.Desc)]
    [InlineData("status",             "status",        SortDirection.Desc)] // direction defaults to Desc
    public async Task Handle_WhenSortExpressionProvided_ShouldPassCorrectFilterToRepository(
        string sort, string expectedField, SortDirection expectedDirection)
    {
        // Arrange
        PolicyFilter? capturedFilter = null;
        _repositoryMock
            .Setup(r => r.GetPagedAsync(It.IsAny<PolicyFilter>(), It.IsAny<CancellationToken>()))
            .Callback<PolicyFilter, CancellationToken>((f, _) => capturedFilter = f)
            .ReturnsAsync((Array.Empty<Domain.Entities.Policy>(), 0));


        // Act
        await _handler.Handle(new GetPoliciesQuery(Sort: sort), CancellationToken.None);

        // Assert
        capturedFilter.Should().NotBeNull();
        capturedFilter!.SortField.Should().Be(expectedField);
        capturedFilter.SortDirection.Should().Be(expectedDirection);
    }

    // -----------------------------------------------------------------------
    // Filter pass-through
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_WhenStatusFilterProvided_ShouldPassParsedEnumToRepository()
    {
        // Arrange
        PolicyFilter? capturedFilter = null;
        _repositoryMock
            .Setup(r => r.GetPagedAsync(It.IsAny<PolicyFilter>(), It.IsAny<CancellationToken>()))
            .Callback<PolicyFilter, CancellationToken>((f, _) => capturedFilter = f)
            .ReturnsAsync((Array.Empty<Domain.Entities.Policy>(), 0));


        // Act
        await _handler.Handle(new GetPoliciesQuery(Status: "Expired"), CancellationToken.None);

        // Assert
        capturedFilter!.Status.Should().Be(PolicyStatus.Expired);
    }

    [Fact]
    public async Task Handle_WhenLineOfBusinessIsAH_ShouldPassAHEnumToRepository()
    {
        // Arrange
        PolicyFilter? capturedFilter = null;
        _repositoryMock
            .Setup(r => r.GetPagedAsync(It.IsAny<PolicyFilter>(), It.IsAny<CancellationToken>()))
            .Callback<PolicyFilter, CancellationToken>((f, _) => capturedFilter = f)
            .ReturnsAsync((Array.Empty<Domain.Entities.Policy>(), 0));


        // Act
        await _handler.Handle(new GetPoliciesQuery(LineOfBusiness: "A&H"), CancellationToken.None);

        // Assert
        capturedFilter!.LineOfBusiness.Should().Be(Domain.Enums.LineOfBusiness.AH);
    }

    [Fact]
    public async Task Handle_WhenRegionFilterProvided_ShouldPassRegionStringToRepository()
    {
        // Arrange
        PolicyFilter? capturedFilter = null;
        _repositoryMock
            .Setup(r => r.GetPagedAsync(It.IsAny<PolicyFilter>(), It.IsAny<CancellationToken>()))
            .Callback<PolicyFilter, CancellationToken>((f, _) => capturedFilter = f)
            .ReturnsAsync((Array.Empty<Domain.Entities.Policy>(), 0));


        // Act
        await _handler.Handle(new GetPoliciesQuery(Region: "Hong Kong"), CancellationToken.None);

        // Assert
        capturedFilter!.Region.Should().Be("Hong Kong");
    }

    // -----------------------------------------------------------------------
    // Pagination meta
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(1, 20, 45, 3)]  // 45 items / 20 per page = 3 pages
    [InlineData(1, 10, 100, 10)] // exactly 100 items / 10 per page = 10 pages
    [InlineData(1, 20, 0, 1)]   // 0 items → TotalPages = 1 (not 0)
    public async Task Handle_WhenPaginationRequested_ShouldComputeTotalPagesCorrectly(
        int page, int size, int totalCount, int expectedTotalPages)
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetPagedAsync(It.IsAny<PolicyFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<Domain.Entities.Policy>(), totalCount));


        // Act
        var result = await _handler.Handle(
            new GetPoliciesQuery(Page: page, Size: size), CancellationToken.None);

        // Assert
        result.Pagination.TotalPages.Should().Be(expectedTotalPages);
    }

    // -----------------------------------------------------------------------
    // Repository called exactly once
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_Always_ShouldCallRepositoryExactlyOnce()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetPagedAsync(It.IsAny<PolicyFilter>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Array.Empty<Domain.Entities.Policy>(), 0));


        // Act
        await _handler.Handle(new GetPoliciesQuery(), CancellationToken.None);

        // Assert
        _repositoryMock.Verify(
            r => r.GetPagedAsync(It.IsAny<PolicyFilter>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
