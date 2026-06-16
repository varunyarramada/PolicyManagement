# Skill: Testing Standards — PolicyManagement BFF

**Audience:** Backend Developer agents, QA agents
**Project:** PolicyManagement BFF — Chubb APAC
**Runtime:** .NET 10 / C# · xUnit · Moq · FluentAssertions · WebApplicationFactory

---

## Guiding Principles

- **xUnit** is the only test framework. NUnit, MSTest, and other frameworks are not used.
- Every feature branch must include tests before a PR is raised. Untested code is not mergeable.
- Tests verify **behaviour**, not implementation details. Testing that a method was called is secondary to testing that the outcome is correct.
- No shared mutable state between tests — each test is fully isolated.
- Arrange-Act-Assert is the only permitted test body structure.

---

## Test Project Structure

Test projects mirror the source project structure. Each source project has a dedicated test project.

```
tests/
├── PolicyManagement.Domain.Tests/
│   └── Entities/
│       └── PolicyTests.cs
│   └── ValueObjects/
│       └── PolicyNumberTests.cs
│       └── MoneyTests.cs
│   └── Exceptions/
│       └── PolicyNotFoundExceptionTests.cs
│
├── PolicyManagement.Application.Tests/
│   └── Features/
│       └── Policies/
│           ├── Commands/
│           │   └── FlagPolicies/
│           │       ├── FlagPoliciesCommandHandlerTests.cs
│           │       └── FlagPoliciesCommandValidatorTests.cs
│           └── Queries/
│               ├── GetPolicyById/
│               │   └── GetPolicyByIdQueryHandlerTests.cs
│               ├── GetPolicies/
│               │   ├── GetPoliciesQueryHandlerTests.cs
│               │   └── GetPoliciesQueryValidatorTests.cs
│               └── GetPolicySummary/
│                   └── GetPolicySummaryQueryHandlerTests.cs
│   └── Builders/
│       └── PolicyBuilder.cs
│
├── PolicyManagement.Infrastructure.Tests/
│   └── Persistence/
│       └── Repositories/
│           └── PolicyRepositoryTests.cs
│
└── PolicyManagement.API.IntegrationTests/
    └── Controllers/
        └── PoliciesControllerTests.cs
    └── Fixtures/
        └── ApiWebApplicationFactory.cs
```

---

## Naming Conventions

| Element | Convention | Example |
|---|---|---|
| Test class | `{ClassUnderTest}Tests` | `GetPolicyByIdQueryHandlerTests` |
| Test method | `{Method}_When{Condition}_Should{Expected}` | `Handle_WhenPolicyExists_ShouldReturnPolicyDto` |
| Test project | `{SourceProject}.Tests` or `.IntegrationTests` | `PolicyManagement.Application.Tests` |
| Builder class | `{Entity}Builder` | `PolicyBuilder` |
| Fixture class | `{Context}Fixture` or `{Context}WebApplicationFactory` | `ApiWebApplicationFactory` |

Test method names must be readable as plain English sentences:
- `Handle_WhenPolicyNotFound_ShouldThrowPolicyNotFoundException`
- `Handle_WhenCacheHit_ShouldReturnCachedDtoWithoutCallingRepository`
- `Validate_WhenPolicyIdsIsEmpty_ShouldHaveValidationError`
- `GetById_WhenPolicyExists_ShouldReturn200WithPolicyDto`

---

## Arrange-Act-Assert Structure

Every test body follows Arrange-Act-Assert with blank-line separation. No exceptions.

```csharp
[Fact]
public async Task Handle_WhenPolicyExists_ShouldReturnPolicyDto()
{
    // Arrange
    var policy = new PolicyBuilder().WithStatus(PolicyStatus.Active).Build();
    _repositoryMock
        .Setup(r => r.GetByIdAsync(policy.Id, It.IsAny<CancellationToken>()))
        .ReturnsAsync(policy);

    // Act
    var result = await _handler.Handle(
        new GetPolicyByIdQuery(policy.Id), CancellationToken.None);

    // Assert
    result.Should().NotBeNull();
    result.Id.Should().Be(policy.Id);
    result.PolicyNumber.Should().Be(policy.PolicyNumber.Value);
    result.Status.Should().Be(policy.Status.ToString());
}
```

If the test requires more than ~15 lines in Arrange, extract a builder or factory method. Long arrangement code obscures what is being tested.

---

## Mocking Strategy

**Library:** Moq

### What to mock

| Type | Reason |
|---|---|
| `IPolicyRepository` | Prevents database access in unit tests |
| `ICacheService` | Controls cache hit/miss scenarios explicitly |
| `IEventPublisher` | Verifies events are published without a real bus |
| `ILogger<T>` | Suppresses log output; can verify log calls if needed |

### What NOT to mock

| Type | Reason |
|---|---|
| Domain entities (`Policy`, `Policyholder`) | Use real instances — they are POCOs |
| Value objects (`PolicyNumber`, `Money`) | Use real instances — they are immutable records |
| DTOs (`PolicyDto`, `PagedResponse<T>`) | Use real instances — they are plain data |
| `CancellationToken` | Use `CancellationToken.None` in unit tests |

```csharp
// Standard mock setup in a handler test class
public class GetPolicyByIdQueryHandlerTests
{
    private readonly Mock<IPolicyRepository> _repositoryMock = new();
    private readonly Mock<ICacheService> _cacheMock         = new();
    private readonly Mock<ILogger<GetPolicyByIdQueryHandler>> _loggerMock = new();
    private readonly GetPolicyByIdQueryHandler _handler;

    public GetPolicyByIdQueryHandlerTests()
    {
        _handler = new GetPolicyByIdQueryHandler(
            _repositoryMock.Object,
            _cacheMock.Object,
            _loggerMock.Object);
    }
}
```

---

## Test Data Builder — Policy

A `PolicyBuilder` provides a fluent interface for creating `Policy` instances with sensible defaults. All tests that need a `Policy` use the builder — no ad-hoc object construction scattered across test files.

```csharp
// tests/PolicyManagement.Application.Tests/Builders/PolicyBuilder.cs
public sealed class PolicyBuilder
{
    private Guid             _id              = Guid.NewGuid();
    private string           _policyNumber    = "POL-000001";
    private string           _holderName      = "Test Policyholder";
    private LineOfBusiness   _lineOfBusiness  = LineOfBusiness.Marine;
    private PolicyStatus     _status          = PolicyStatus.Active;
    private decimal          _premiumAmount   = 10_000.00m;
    private string           _currency        = "SGD";
    private DateOnly         _effectiveDate   = new(2025, 1, 1);
    private DateOnly         _expiryDate      = new(2025, 12, 31);
    private string           _region          = "Singapore";
    private string           _underwriter     = "James Wong";
    private bool             _flaggedForReview = false;

    public PolicyBuilder WithId(Guid id)                           { _id = id;                       return this; }
    public PolicyBuilder WithPolicyNumber(string number)           { _policyNumber = number;         return this; }
    public PolicyBuilder WithStatus(PolicyStatus status)           { _status = status;               return this; }
    public PolicyBuilder WithRegion(string region)                 { _region = region;               return this; }
    public PolicyBuilder WithLineOfBusiness(LineOfBusiness lob)    { _lineOfBusiness = lob;          return this; }
    public PolicyBuilder WithPremium(decimal amount, string currency = "SGD")
    {
        _premiumAmount = amount;
        _currency      = currency;
        return this;
    }
    public PolicyBuilder WithEffectiveDate(DateOnly date)     { _effectiveDate = date;          return this; }
    public PolicyBuilder WithExpiryDate(DateOnly date)        { _expiryDate = date;             return this; }
    public PolicyBuilder WithFlaggedForReview(bool flagged)   { _flaggedForReview = flagged;    return this; }

    public Policy Build() => new()
    {
        Id               = _id,
        PolicyNumber     = _policyNumber,
        PolicyholderName = _holderName,
        LineOfBusiness   = _lineOfBusiness,
        Status           = _status,
        PremiumAmount    = _premiumAmount,
        Currency         = _currency,
        EffectiveDate    = _effectiveDate,
        ExpiryDate       = _expiryDate,
        Region           = _region,
        Underwriter      = _underwriter,
        FlaggedForReview = _flaggedForReview,
        IsDeleted        = false,
        CreatedAt        = DateTimeOffset.UtcNow,
        UpdatedAt        = DateTimeOffset.UtcNow
    };

    public static PolicyBuilder Active()   => new PolicyBuilder().WithStatus(PolicyStatus.Active);
    public static PolicyBuilder Expired()  => new PolicyBuilder().WithStatus(PolicyStatus.Expired);
    public static PolicyBuilder Flagged()  => new PolicyBuilder().WithFlaggedForReview(true);
}
```

---

## Unit Testing — Application Layer (Handlers)

### Complete handler test class example

```csharp
// tests/PolicyManagement.Application.Tests/Features/Policies/Queries/GetPolicyById/
// GetPolicyByIdQueryHandlerTests.cs
public class GetPolicyByIdQueryHandlerTests
{
    private readonly Mock<IPolicyRepository> _repositoryMock = new();
    private readonly Mock<ICacheService>     _cacheMock      = new();
    private readonly Mock<ILogger<GetPolicyByIdQueryHandler>> _loggerMock = new();
    private readonly GetPolicyByIdQueryHandler _handler;

    public GetPolicyByIdQueryHandlerTests()
    {
        _handler = new GetPolicyByIdQueryHandler(
            _repositoryMock.Object,
            _cacheMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenPolicyExistsAndCacheMiss_ShouldReturnPolicyDtoAndPopulateCache()
    {
        // Arrange
        var policy = PolicyBuilder.Active().Build();
        var cacheKey = $"policy:v1:{policy.Id}";

        _cacheMock
            .Setup(c => c.GetAsync<PolicyDto>(cacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PolicyDto?)null);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(policy.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        // Act
        var result = await _handler.Handle(
            new GetPolicyByIdQuery(policy.Id), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Id.Should().Be(policy.Id);
        result.Status.Should().Be("Active");

        _cacheMock.Verify(c => c.SetAsync(
            cacheKey,
            It.IsAny<PolicyDto>(),
            It.IsAny<TimeSpan>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenCacheHit_ShouldReturnCachedDtoWithoutCallingRepository()
    {
        // Arrange
        var policy   = PolicyBuilder.Active().Build();
        var cached   = new PolicyDto(policy.Id, policy.PolicyNumber, "Active", 10_000m, "SGD");
        var cacheKey = $"policy:v1:{policy.Id}";

        _cacheMock
            .Setup(c => c.GetAsync<PolicyDto>(cacheKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(cached);

        // Act
        var result = await _handler.Handle(
            new GetPolicyByIdQuery(policy.Id), CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(cached);
        _repositoryMock.Verify(
            r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPolicyNotFound_ShouldThrowPolicyNotFoundException()
    {
        // Arrange
        var id = Guid.NewGuid();

        _cacheMock
            .Setup(c => c.GetAsync<PolicyDto>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PolicyDto?)null);

        _repositoryMock
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Policy?)null);

        // Act
        var act = async () => await _handler.Handle(
            new GetPolicyByIdQuery(id), CancellationToken.None);

        // Assert
        await act.Should()
            .ThrowAsync<PolicyNotFoundException>()
            .Where(ex => ex.PolicyId == id);
    }
}
```

### FlagPoliciesCommandHandler test scenarios

```csharp
public class FlagPoliciesCommandHandlerTests
{
    private readonly Mock<IPolicyRepository> _repositoryMock   = new();
    private readonly Mock<IEventPublisher>   _publisherMock    = new();
    private readonly Mock<ILogger<FlagPoliciesCommandHandler>> _loggerMock = new();
    private readonly FlagPoliciesCommandHandler _handler;

    public FlagPoliciesCommandHandlerTests()
    {
        _handler = new FlagPoliciesCommandHandler(
            _repositoryMock.Object,
            _publisherMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WhenPoliciesExist_ShouldFlagAndPublishEvent()
    {
        // Arrange
        var policy  = PolicyBuilder.Active().Build();
        var command = new FlagPoliciesCommand(new[] { policy.Id });

        _repositoryMock
            .Setup(r => r.GetByIdAsync(policy.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert
        policy.FlaggedForReview.Should().BeTrue();
        _repositoryMock.Verify(r => r.UpdateAsync(policy, It.IsAny<CancellationToken>()), Times.Once);
        _publisherMock.Verify(p => p.PublishAsync(
            It.Is<PolicyFlaggedEvent>(e => e.PolicyId == policy.Id),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenPolicyNotFound_ShouldThrowPolicyNotFoundException()
    {
        // Arrange
        var missingId = Guid.NewGuid();
        _repositoryMock
            .Setup(r => r.GetByIdAsync(missingId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Policy?)null);

        // Act
        var act = async () => await _handler.Handle(
            new FlagPoliciesCommand(new[] { missingId }), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<PolicyNotFoundException>();
        _repositoryMock.Verify(r => r.UpdateAsync(It.IsAny<Policy>(), It.IsAny<CancellationToken>()), Times.Never);
        _publisherMock.Verify(p => p.PublishAsync(It.IsAny<object>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenPolicyAlreadyFlagged_ShouldThrowInvalidPolicyStateException()
    {
        // Arrange
        var policy = PolicyBuilder.Flagged().Build();
        _repositoryMock
            .Setup(r => r.GetByIdAsync(policy.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(policy);

        // Act
        var act = async () => await _handler.Handle(
            new FlagPoliciesCommand(new[] { policy.Id }), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidPolicyStateException>();
    }
}
```

---

## Unit Testing — Validators

Validator tests verify every rule defined in the validator class — both valid and invalid inputs.

```csharp
// tests/PolicyManagement.Application.Tests/Features/Policies/Commands/FlagPolicies/
// FlagPoliciesCommandValidatorTests.cs
public class FlagPoliciesCommandValidatorTests
{
    private readonly FlagPoliciesCommandValidator _validator = new();

    [Fact]
    public void Validate_WhenCommandIsValid_ShouldPassValidation()
    {
        // Arrange
        var command = new FlagPoliciesCommand(new[] { Guid.NewGuid(), Guid.NewGuid() });

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_WhenPolicyIdsIsEmpty_ShouldHaveValidationError()
    {
        // Arrange
        var command = new FlagPoliciesCommand(Array.Empty<Guid>());

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.PropertyName == "PolicyIds" &&
            e.ErrorMessage.Contains("At least one policy ID"));
    }

    [Fact]
    public void Validate_WhenPolicyIdsExceeds100_ShouldHaveValidationError()
    {
        // Arrange
        var ids     = Enumerable.Range(0, 101).Select(_ => Guid.NewGuid()).ToList();
        var command = new FlagPoliciesCommand(ids);

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e =>
            e.PropertyName == "PolicyIds" &&
            e.ErrorMessage.Contains("100"));
    }

    [Fact]
    public void Validate_WhenPolicyIdsContainsEmptyGuid_ShouldHaveValidationError()
    {
        // Arrange
        var command = new FlagPoliciesCommand(new[] { Guid.NewGuid(), Guid.Empty });

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("PolicyIds"));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(50)]
    [InlineData(100)]
    public void Validate_WhenPolicyIdsCountIsWithinBounds_ShouldPassValidation(int count)
    {
        // Arrange
        var ids     = Enumerable.Range(0, count).Select(_ => Guid.NewGuid()).ToList();
        var command = new FlagPoliciesCommand(ids);

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
    }
}
```

---

## Unit Testing — Domain Layer

Domain tests verify entity invariants, value object construction, and domain exception construction. No mocking is needed — domain types are pure C# with no dependencies.

```csharp
// tests/PolicyManagement.Domain.Tests/Entities/PolicyTests.cs
public class PolicyTests
{
    [Fact]
    public void Flag_WhenPolicyIsActive_ShouldSetFlaggedForReviewToTrue()
    {
        // Arrange
        var policy = PolicyBuilder.Active().Build();

        // Act
        policy.Flag();

        // Assert
        policy.FlaggedForReview.Should().BeTrue();
    }

    [Fact]
    public void Flag_WhenAlreadyFlagged_ShouldThrowInvalidPolicyStateException()
    {
        // Arrange
        var policy = PolicyBuilder.Flagged().Build();

        // Act
        var act = () => policy.Flag();

        // Assert
        act.Should().Throw<InvalidPolicyStateException>()
            .WithMessage("*already flagged*");
    }

    [Fact]
    public void Flag_WhenPolicyCancelled_ShouldThrowInvalidPolicyStateException()
    {
        // Arrange
        var policy = new PolicyBuilder().WithStatus(PolicyStatus.Cancelled).Build();

        // Act
        var act = () => policy.Flag();

        // Assert
        act.Should().Throw<InvalidPolicyStateException>();
    }
}
```

```csharp
// tests/PolicyManagement.Domain.Tests/ValueObjects/PolicyNumberTests.cs
public class PolicyNumberTests
{
    [Theory]
    [InlineData("POL-000001")]
    [InlineData("POL-999999")]
    [InlineData("POL-123456")]
    public void Create_WhenFormatIsValid_ShouldSucceed(string value)
    {
        // Act
        var act = () => new PolicyNumber(value);

        // Assert
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("")]
    [InlineData("POL-12345")]    // too short
    [InlineData("POL-1234567")]  // too long
    [InlineData("pol-123456")]   // lowercase
    [InlineData("123456")]       // missing prefix
    [InlineData("POL123456")]    // missing hyphen
    public void Create_WhenFormatIsInvalid_ShouldThrow(string value)
    {
        // Act
        var act = () => new PolicyNumber(value);

        // Assert
        act.Should().Throw<ArgumentException>();
    }
}
```

---

## Integration Testing — API Endpoints

Integration tests use `WebApplicationFactory<Program>` to spin up the full ASP.NET Core pipeline with a real DI container and an EF Core InMemory database. They test the complete request path: routing → controller → MediatR pipeline → handler → repository → response.

### ApiWebApplicationFactory

```csharp
// tests/PolicyManagement.API.IntegrationTests/Fixtures/ApiWebApplicationFactory.cs
public sealed class ApiWebApplicationFactory : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // Replace SQL Server DbContext with InMemory for tests
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<PolicyDbContext>));
            if (descriptor is not null)
                services.Remove(descriptor);

            services.AddDbContext<PolicyDbContext>(options =>
                options.UseInMemoryDatabase(
                    $"PolicyDb_Test_{Guid.NewGuid()}")); // unique per factory instance
        });

        builder.UseEnvironment("Test");
    }
}
```

A unique in-memory database name per factory instance ensures tests are fully isolated — no state leaks between test classes.

### Complete integration test class

```csharp
// tests/PolicyManagement.API.IntegrationTests/Controllers/PoliciesControllerTests.cs
public class PoliciesControllerTests : IClassFixture<ApiWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly ApiWebApplicationFactory _factory;

    public PoliciesControllerTests(ApiWebApplicationFactory factory)
    {
        _factory = factory;
        _client  = factory.CreateClient();
    }

    // --- GET /api/v1/policies/{id} ---

    [Fact]
    public async Task GetById_WhenPolicyExists_ShouldReturn200WithPolicyDto()
    {
        // Arrange
        var policy = await SeedPolicyAsync(PolicyBuilder.Active().Build());

        // Act
        var response = await _client.GetAsync($"/api/v1/policies/{policy.Id}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PolicyDto>();
        body.Should().NotBeNull();
        body!.Id.Should().Be(policy.Id);
        body.PolicyNumber.Should().Be(policy.PolicyNumber);
    }

    [Fact]
    public async Task GetById_WhenPolicyNotFound_ShouldReturn404ProblemDetails()
    {
        // Arrange
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/v1/policies/{nonExistentId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType?.MediaType
            .Should().Be("application/problem+json");

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Status.Should().Be(404);
    }

    // --- GET /api/v1/policies ---

    [Fact]
    public async Task List_WithNoPoliciesSeeded_ShouldReturn200WithEmptyData()
    {
        // Arrange — empty database (unique factory instance per test class)

        // Act
        var response = await _client.GetAsync("/api/v1/policies");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<PolicyDto>>();
        body!.Data.Should().BeEmpty();
        body.Pagination.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task List_WithStatusFilter_ShouldReturnOnlyMatchingPolicies()
    {
        // Arrange
        await SeedPolicyAsync(PolicyBuilder.Active().Build());
        await SeedPolicyAsync(PolicyBuilder.Active().Build());
        await SeedPolicyAsync(PolicyBuilder.Expired().Build());

        // Act
        var response = await _client.GetAsync("/api/v1/policies?status=Active");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PagedResponse<PolicyDto>>();
        body!.Data.Should().HaveCount(2);
        body.Data.Should().AllSatisfy(p => p.Status.Should().Be("Active"));
    }

    [Fact]
    public async Task List_WithInvalidPageNumber_ShouldReturn400ProblemDetails()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/policies?page=0");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Status.Should().Be(400);
        problem.Extensions.Should().ContainKey("errors");
    }

    // --- PATCH /api/v1/policies/flag ---

    [Fact]
    public async Task Flag_WhenPoliciesExist_ShouldReturn204()
    {
        // Arrange
        var policy  = await SeedPolicyAsync(PolicyBuilder.Active().Build());
        var request = new FlagPoliciesRequest { PolicyIds = new[] { policy.Id } };

        // Act
        var response = await _client.PatchAsJsonAsync("/api/v1/policies/flag", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task Flag_WhenPolicyNotFound_ShouldReturn404ProblemDetails()
    {
        // Arrange
        var request = new FlagPoliciesRequest { PolicyIds = new[] { Guid.NewGuid() } };

        // Act
        var response = await _client.PatchAsJsonAsync("/api/v1/policies/flag", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Status.Should().Be(404);
    }

    [Fact]
    public async Task Flag_WhenPolicyIdsIsEmpty_ShouldReturn400ProblemDetails()
    {
        // Arrange
        var request = new FlagPoliciesRequest { PolicyIds = Array.Empty<Guid>() };

        // Act
        var response = await _client.PatchAsJsonAsync("/api/v1/policies/flag", request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Extensions.Should().ContainKey("errors");
    }

    // --- GET /api/v1/policies/summary ---

    [Fact]
    public async Task Summary_WithSeedData_ShouldReturn200WithAggregatedStats()
    {
        // Arrange
        await SeedPolicyAsync(PolicyBuilder.Active().Build());
        await SeedPolicyAsync(PolicyBuilder.Active().Build());
        await SeedPolicyAsync(PolicyBuilder.Expired().Build());

        // Act
        var response = await _client.GetAsync("/api/v1/policies/summary");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PolicySummaryResponse>();
        body!.TotalCount.Should().BeGreaterThanOrEqualTo(3);
    }

    // --- Helpers ---

    private async Task<Policy> SeedPolicyAsync(Policy policy)
    {
        using var scope  = _factory.Services.CreateScope();
        var context      = scope.ServiceProvider.GetRequiredService<PolicyDbContext>();
        context.Policies.Add(policy);
        await context.SaveChangesAsync();
        return policy;
    }
}
```

---

## Required Test Scenarios per Handler

### `GetPolicyByIdQueryHandler`

| Scenario | Expected outcome |
|---|---|
| Policy exists, cache miss | Returns `PolicyDto`; populates cache |
| Policy exists, cache hit | Returns cached `PolicyDto`; repository not called |
| Policy does not exist | Throws `PolicyNotFoundException` |

### `GetPoliciesQueryHandler`

| Scenario | Expected outcome |
|---|---|
| No filters, default pagination | Returns page 1 of 20, correct `totalCount` |
| Filter by `status = Active` | Returns only Active policies |
| Filter by `region = Singapore` | Returns only Singapore policies |
| Filter by `lineOfBusiness = Marine` | Returns only Marine policies |
| Combined filters (`status + region`) | Returns intersection |
| Sort by `premium` descending | Results ordered correctly |
| Search by policy number fragment | Returns matching policies |
| Empty result set | Returns `200` with `data: []`, `totalCount: 0` |
| `page` beyond available pages | Returns `200` with `data: []` |

### `FlagPoliciesCommandHandler`

| Scenario | Expected outcome |
|---|---|
| All policies exist and are unflagged | All flagged; events published per policy; repository updated per policy |
| One policy not found | Throws `PolicyNotFoundException`; no updates or events for that ID |
| Policy already flagged | Throws `InvalidPolicyStateException` |
| Multiple IDs, first fails | Subsequent policies not processed (fail-fast) |

### `GetPolicySummaryQueryHandler`

| Scenario | Expected outcome |
|---|---|
| Policies exist with varied statuses | Returns correct counts per status |
| No policies in database | Returns all counts as zero |
| Premium totals | Returns correct sum grouped by currency |

---

## Code Coverage Expectations

| Scope | Requirement |
|---|---|
| Application handlers — all public methods | 100% |
| Domain entities — all invariants and state transitions | 100% |
| Validators — every rule (valid and invalid inputs) | 100% |
| API endpoints — every declared HTTP status code path | 100% |
| Infrastructure repositories | Integration tests for key query paths |

Coverage is measured per-layer. The goal is meaningful behavioural coverage — not line coverage achieved by trivial assertions.

---

## Common Testing Mistakes to Avoid

### Asserting on mock calls instead of outcomes

```csharp
// WRONG — tests implementation detail, not behaviour
_repositoryMock.Verify(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()), Times.Once);

// CORRECT — test the outcome; verify mock calls only as secondary assertions
result.Should().NotBeNull();
result.Status.Should().Be("Active");
// Then optionally verify cache was populated
_cacheMock.Verify(c => c.SetAsync(...), Times.Once);
```

---

### Sharing state between tests via static fields

```csharp
// WRONG — static list shared across all test instances; tests interfere
public class GetPoliciesQueryHandlerTests
{
    private static readonly List<Policy> _policies = new(); // shared mutable state

// CORRECT — fresh state per test instance
public class GetPoliciesQueryHandlerTests
{
    private readonly Mock<IPolicyRepository> _repositoryMock = new(); // new mock per test
```

---

### Using the same in-memory database across integration tests

```csharp
// WRONG — same database name reused; tests pollute each other
services.AddDbContext<PolicyDbContext>(options =>
    options.UseInMemoryDatabase("PolicyDb_Test")); // fixed name

// CORRECT — unique name per factory instance
services.AddDbContext<PolicyDbContext>(options =>
    options.UseInMemoryDatabase($"PolicyDb_Test_{Guid.NewGuid()}"));
```

---

### Not testing the unhappy path

Every handler test class must include at least one test for each exception the handler can throw. A test suite that only tests the happy path gives false confidence.

---

### Testing `[Theory]` cases with magic values

```csharp
// WRONG — no indication why these specific values matter
[Theory]
[InlineData("a")]
[InlineData("ab")]
public void Validate_ShouldFail(string value) { ... }

// CORRECT — names or comments explain the boundary condition
[Theory]
[InlineData("a",  "single character — below minimum length of 3")]
[InlineData("ab", "two characters — still below minimum")]
public void Validate_WhenValueBelowMinimumLength_ShouldHaveError(
    string value, string reason) { ... }
```

---

### Asserting with `Assert.True` instead of FluentAssertions

```csharp
// WRONG — failure message is unhelpful: "Expected True but was False"
Assert.True(result.Status == "Active");

// CORRECT — failure message shows actual vs expected values
result.Status.Should().Be("Active");
```
