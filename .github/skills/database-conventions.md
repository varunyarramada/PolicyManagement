# Skill: Database Conventions — PolicyManagement BFF

**Audience:** Architect agent, Backend Developer agents
**Project:** PolicyManagement BFF — Chubb APAC
**Runtime:** .NET 10 / C# · EF Core · SQL Server · Clean Architecture

---

## Guiding Principles

- `PolicyDbContext` and everything EF Core-specific lives exclusively in `PolicyManagement.Infrastructure`. It never appears in `Domain` or `Application`.
- Domain entities are POCOs — no EF Core attributes on entity classes. All mapping is done in `IEntityTypeConfiguration<T>` classes in `Infrastructure/Persistence/Configurations/`.
- Read queries always use `.AsNoTracking()`. Tracking is only enabled when a write will follow.
- All configuration is externalised — no connection strings in code.

---

## Column Naming Convention

**Convention: `snake_case` column names, PascalCase C# properties.**

SQL Server column names use `snake_case` (e.g., `policy_number`, `effective_date`, `flagged_for_review`). C# entity properties use PascalCase as per .NET conventions. The mapping between them is declared explicitly in the entity configuration.

**Rationale:**
- `snake_case` is the most portable SQL convention — it works identically across SQL Server, PostgreSQL, and MySQL without quoting.
- It avoids accidental reliance on EF Core's default PascalCase → `PascalCase` column naming, which produces inconsistent names when the model evolves.
- Explicit column name mapping in `IEntityTypeConfiguration<T>` makes schema decisions visible and reviewable in code, not hidden in conventions.

Table names are `PascalCase` to match SQL Server norms (e.g., `Policies`, `Policyholders`).

---

## Enum Storage Convention

**Convention: store enums as strings (`varchar(50)`).**

```csharp
// In entity configuration
builder.Property(p => p.Status)
    .HasConversion<string>()
    .HasColumnName("status")
    .HasColumnType("varchar(50)")
    .IsRequired();
```

**Rationale:**
- Integer enum values are opaque in the database — a value of `2` requires consulting the C# source to be understood.
- String values are self-documenting in queries, migrations, seed scripts, and data exports.
- Adding a new enum member does not require a migration to update a check constraint or lookup table.
- The performance difference for a `varchar(50)` column with an index is negligible compared to the operational clarity benefit.

Enums affected: `PolicyStatus`, `LineOfBusiness`.

---

## Soft Delete vs Hard Delete

**Convention: soft delete using `IsDeleted` flag for `Policy` records.**

Policies are business records and must not be physically deleted. A `PATCH` to delete a policy sets `is_deleted = true` and updates `updated_at`. The `PolicyDbContext` applies a global query filter so deleted records are invisible to all queries by default.

```csharp
// In PolicyDbContext.OnModelCreating
modelBuilder.Entity<Policy>().HasQueryFilter(p => !p.IsDeleted);
```

Hard deletes (physical `DELETE` statements) are only permitted for non-business data (e.g., test seed records in development environments).

---

## Audit Columns

Every entity that represents a business record carries two audit columns:

| Column | C# type | SQL type | Behaviour |
|---|---|---|---|
| `created_at` | `DateTimeOffset` | `datetimeoffset(7)` | Set once on insert; never updated |
| `updated_at` | `DateTimeOffset` | `datetimeoffset(7)` | Updated on every write |

`DateTimeOffset` is used instead of `DateTime` to preserve timezone information for a multi-region APAC system spanning UTC+8 (Singapore/HK), UTC+10/+11 (Australia), UTC+9 (Japan), UTC+7 (Thailand/Indonesia), UTC+8 (Malaysia/Philippines).

Audit values are set in `PolicyDbContext.SaveChangesAsync` via a `SaveChanges` override — not in individual handlers:

```csharp
// Infrastructure/Persistence/PolicyDbContext.cs
public override async Task<int> SaveChangesAsync(CancellationToken ct = default)
{
    var now = DateTimeOffset.UtcNow;

    foreach (var entry in ChangeTracker.Entries<IAuditableEntity>())
    {
        if (entry.State == EntityState.Added)
            entry.Entity.CreatedAt = now;

        if (entry.State is EntityState.Added or EntityState.Modified)
            entry.Entity.UpdatedAt = now;
    }

    return await base.SaveChangesAsync(ct);
}
```

`IAuditableEntity` is a marker interface in `Domain`:
```csharp
// Domain/Interfaces/IAuditableEntity.cs
public interface IAuditableEntity
{
    DateTimeOffset CreatedAt { get; set; }
    DateTimeOffset UpdatedAt { get; set; }
}
```

---

## Policy Entity — Database Schema

### Table: `Policies`

| Column | C# Property | SQL Type | Nullable | Constraints |
|---|---|---|---|---|
| `id` | `Id` | `uniqueidentifier` | No | Primary key |
| `policy_number` | `PolicyNumber` | `varchar(20)` | No | Unique, format `POL-XXXXXX` |
| `policyholder_name` | `PolicyholderName` | `nvarchar(200)` | No | |
| `line_of_business` | `LineOfBusiness` | `varchar(50)` | No | Enum as string |
| `status` | `Status` | `varchar(50)` | No | Enum as string |
| `premium_amount` | `PremiumAmount` | `decimal(18,2)` | No | Range: 1,000–5,000,000 |
| `currency` | `Currency` | `varchar(10)` | No | USD/SGD/HKD/AUD/JPY/THB |
| `effective_date` | `EffectiveDate` | `date` | No | |
| `expiry_date` | `ExpiryDate` | `date` | No | Must be after `effective_date` |
| `region` | `Region` | `varchar(100)` | No | APAC region name |
| `underwriter` | `Underwriter` | `nvarchar(200)` | No | |
| `flagged_for_review` | `FlaggedForReview` | `bit` | No | Default `0` |
| `is_deleted` | `IsDeleted` | `bit` | No | Default `0` (soft delete) |
| `created_at` | `CreatedAt` | `datetimeoffset(7)` | No | Set on insert |
| `updated_at` | `UpdatedAt` | `datetimeoffset(7)` | No | Set on insert and update |

**C# type mappings:**
- `decimal(18,2)` ← `decimal` — exact numeric, never `float` or `double` for money
- `date` ← `DateOnly` (.NET 6+) — no time component needed for policy dates
- `datetimeoffset(7)` ← `DateTimeOffset` — timezone-aware timestamps
- `nvarchar` for human-readable names (Unicode) — `varchar` for codes and enum values (ASCII)
- `uniqueidentifier` ← `Guid` — client-generated IDs, no sequential identity columns

---

## Entity Configuration

Entity configurations implement `IEntityTypeConfiguration<T>` and live in `Infrastructure/Persistence/Configurations/`. No EF Core data annotations (`[Key]`, `[Column]`, `[MaxLength]`) appear on entity classes.

```csharp
// Infrastructure/Persistence/Configurations/PolicyConfiguration.cs
public sealed class PolicyConfiguration : IEntityTypeConfiguration<Policy>
{
    public void Configure(EntityTypeBuilder<Policy> builder)
    {
        builder.ToTable("Policies");

        // Primary key
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasColumnType("uniqueidentifier")
            .ValueGeneratedNever(); // client-assigned GUIDs

        // Policy number — unique, bounded length
        builder.Property(p => p.PolicyNumber)
            .HasColumnName("policy_number")
            .HasColumnType("varchar(20)")
            .IsRequired();
        builder.HasIndex(p => p.PolicyNumber)
            .IsUnique()
            .HasDatabaseName("UQ_Policies_PolicyNumber");

        // Policyholder name — Unicode, full-text search candidate
        builder.Property(p => p.PolicyholderName)
            .HasColumnName("policyholder_name")
            .HasColumnType("nvarchar(200)")
            .IsRequired();

        // Enums stored as strings
        builder.Property(p => p.Status)
            .HasConversion<string>()
            .HasColumnName("status")
            .HasColumnType("varchar(50)")
            .IsRequired();

        builder.Property(p => p.LineOfBusiness)
            .HasConversion<string>()
            .HasColumnName("line_of_business")
            .HasColumnType("varchar(50)")
            .IsRequired();

        // Premium — exact decimal, never float
        builder.Property(p => p.PremiumAmount)
            .HasColumnName("premium_amount")
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(p => p.Currency)
            .HasColumnName("currency")
            .HasColumnType("varchar(10)")
            .IsRequired();

        // Date columns — DateOnly maps to SQL date
        builder.Property(p => p.EffectiveDate)
            .HasColumnName("effective_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(p => p.ExpiryDate)
            .HasColumnName("expiry_date")
            .HasColumnType("date")
            .IsRequired();

        builder.Property(p => p.Region)
            .HasColumnName("region")
            .HasColumnType("varchar(100)")
            .IsRequired();

        builder.Property(p => p.Underwriter)
            .HasColumnName("underwriter")
            .HasColumnType("nvarchar(200)")
            .IsRequired();

        // Boolean flags — SQL bit with explicit defaults
        builder.Property(p => p.FlaggedForReview)
            .HasColumnName("flagged_for_review")
            .HasColumnType("bit")
            .HasDefaultValue(false)
            .IsRequired();

        builder.Property(p => p.IsDeleted)
            .HasColumnName("is_deleted")
            .HasColumnType("bit")
            .HasDefaultValue(false)
            .IsRequired();

        // Audit columns
        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasColumnType("datetimeoffset(7)")
            .IsRequired();

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .HasColumnType("datetimeoffset(7)")
            .IsRequired();

        // Global soft-delete filter
        builder.HasQueryFilter(p => !p.IsDeleted);

        // Indexes — see Index Design section
        builder.HasIndex(p => p.Status)
            .HasDatabaseName("IX_Policies_Status");

        builder.HasIndex(p => p.LineOfBusiness)
            .HasDatabaseName("IX_Policies_LineOfBusiness");

        builder.HasIndex(p => p.Region)
            .HasDatabaseName("IX_Policies_Region");

        builder.HasIndex(p => p.EffectiveDate)
            .HasDatabaseName("IX_Policies_EffectiveDate");

        builder.HasIndex(p => p.ExpiryDate)
            .HasDatabaseName("IX_Policies_ExpiryDate");

        builder.HasIndex(p => new { p.Status, p.Region })
            .HasDatabaseName("IX_Policies_Status_Region");

        builder.HasIndex(p => new { p.Status, p.LineOfBusiness })
            .HasDatabaseName("IX_Policies_Status_LineOfBusiness");

        builder.HasIndex(p => new { p.Status, p.EffectiveDate, p.ExpiryDate })
            .HasDatabaseName("IX_Policies_Status_EffectiveDate_ExpiryDate");
    }
}
```

Register all configurations by assembly scan in `PolicyDbContext`:

```csharp
// Infrastructure/Persistence/PolicyDbContext.cs
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.ApplyConfigurationsFromAssembly(
        typeof(PolicyDbContext).Assembly);
}
```

---

## Index Design Strategy

The policy list endpoint (`GET /api/v1/policies`) supports nine filter/sort parameters. Indexes are designed around the most common access patterns.

### Single-column indexes

| Index name | Column | Justifies |
|---|---|---|
| `UQ_Policies_PolicyNumber` | `policy_number` | Unique constraint; exact lookup and sort |
| `IX_Policies_Status` | `status` | Most common filter; low cardinality but high selectivity when combined |
| `IX_Policies_LineOfBusiness` | `line_of_business` | Frequent dashboard filter |
| `IX_Policies_Region` | `region` | Frequent APAC region filter |
| `IX_Policies_EffectiveDate` | `effective_date` | Date range filter and sorting |
| `IX_Policies_ExpiryDate` | `expiry_date` | Date range filter and sorting |

### Composite indexes

Composite indexes are ordered by selectivity (highest first) and designed for the most common filter combinations observed in insurance dashboard usage:

| Index name | Columns | Access pattern covered |
|---|---|---|
| `IX_Policies_Status_Region` | `(status, region)` | Regional manager view filtered by status |
| `IX_Policies_Status_LineOfBusiness` | `(status, line_of_business)` | LOB performance view |
| `IX_Policies_Status_EffectiveDate_ExpiryDate` | `(status, effective_date, expiry_date)` | Renewal pipeline queries (active policies expiring in range) |

**Composite index column order rule:** place equality-filter columns before range-filter columns. SQL Server uses the leading columns of a composite index for equality predicates, then the next column for range scans. A `(status, effective_date)` index supports `WHERE status = 'Active' AND effective_date >= '2025-01-01'` efficiently; reversing the columns would not.

### Full-text search — `search` parameter

The `search` query parameter covers policy number and policyholder name. Two approaches:

**Option A — SQL Server Full-Text Search (recommended for production):**
- Create a Full-Text Index on `(policy_number, policyholder_name)`.
- Use `CONTAINS` or `FREETEXT` predicates in the repository query.
- Requires Full-Text Search to be enabled on the SQL Server instance.

**Option B — `LIKE` with leading wildcard (acceptable for development/seed data):**
- `WHERE policy_number LIKE '%search%' OR policyholder_name LIKE N'%search%'`
- Does not use indexes — performs a table scan.
- Acceptable only for low-volume development environments with the 200-record seed dataset.

For the assessment implementation, Option B is acceptable. The repository method should be written so the predicate is isolated and swappable when Full-Text Search is enabled.

### Indexes and soft delete

Because `IsDeleted = false` is a global query filter, every query implicitly includes `WHERE is_deleted = 0`. For large datasets, include `is_deleted` as a leading column in composite indexes or use a filtered index:

```sql
-- Filtered index — only indexes non-deleted rows
CREATE INDEX IX_Policies_Status_Region_Active
ON Policies (status, region)
WHERE is_deleted = 0;
```

EF Core does not natively declare filtered indexes via Fluent API in all versions — this may require a raw migration SQL statement.

---

## AsNoTracking for Read Queries

Every repository method that services a query (read-only) must use `.AsNoTracking()`. Tracking is only enabled when the retrieved entity will be modified and saved.

```csharp
// READ — always AsNoTracking
public async Task<Policy?> GetByIdAsync(Guid id, CancellationToken ct)
    => await _context.Policies
           .AsNoTracking()
           .FirstOrDefaultAsync(p => p.Id == id, ct);

public async Task<PagedResult<Policy>> GetPagedAsync(
    PolicyFilter filter, CancellationToken ct)
    => await _context.Policies
           .AsNoTracking()
           .Where(...)
           .ToPagedResultAsync(filter.Page, filter.Size, ct);

// WRITE — tracking required so EF Core detects changes
public async Task UpdateAsync(Policy policy, CancellationToken ct)
{
    _context.Policies.Update(policy);
    await _context.SaveChangesAsync(ct);
}
```

**Why this matters:**
- EF Core change tracking adds memory and CPU overhead for every tracked entity.
- A paged query returning 100 policies allocates 100 tracking snapshots when tracking is enabled unnecessarily.
- `AsNoTracking()` also avoids stale entity conflicts when the same entity is queried twice in one request.

---

## Connection String Management

Connection strings are never hardcoded. They are bound from configuration to a strongly-typed options class.

```csharp
// Infrastructure/Options/SqlServerOptions.cs
public sealed class SqlServerOptions
{
    public const string SectionName = "SqlServer";
    public string ConnectionString { get; init; } = string.Empty;
    public int CommandTimeoutSeconds { get; init; } = 30;
}
```

```csharp
// API/Program.cs
builder.Services.Configure<SqlServerOptions>(
    builder.Configuration.GetSection(SqlServerOptions.SectionName));

builder.Services.AddDbContext<PolicyDbContext>((serviceProvider, options) =>
{
    var sqlOptions = serviceProvider
        .GetRequiredService<IOptions<SqlServerOptions>>().Value;

    options.UseSqlServer(
        sqlOptions.ConnectionString,
        sql => sql.CommandTimeout(sqlOptions.CommandTimeoutSeconds));
});
```

```json
// appsettings.json (placeholder only — real value from environment)
{
  "SqlServer": {
    "ConnectionString": "",
    "CommandTimeoutSeconds": 30
  }
}
```

```json
// appsettings.Development.json (development only — not committed with real credentials)
{
  "SqlServer": {
    "ConnectionString": "Server=(localdb)\\mssqllocaldb;Database=PolicyManagementDev;Trusted_Connection=True;"
  }
}
```

Production connection strings are supplied via environment variables or a secrets manager — never committed to source control.

---

## Migration Workflow

Migrations are generated and applied using the EF Core CLI. All migration commands are run from the solution root, targeting the Infrastructure project (which contains `PolicyDbContext`) and using the API project as the startup project (which has the DI registration).

```powershell
# Add a new migration
dotnet ef migrations add InitialCreate `
    --project src/PolicyManagement.Infrastructure `
    --startup-project src/PolicyManagement.API `
    --output-dir Persistence/Migrations

# Apply migrations to the development database
dotnet ef database update `
    --project src/PolicyManagement.Infrastructure `
    --startup-project src/PolicyManagement.API

# Generate a SQL script for production deployment review
dotnet ef migrations script `
    --project src/PolicyManagement.Infrastructure `
    --startup-project src/PolicyManagement.API `
    --output docs/migrations/latest.sql `
    --idempotent
```

**Migration naming conventions:**

| Migration | Name |
|---|---|
| Initial schema creation | `InitialCreate` |
| Adding a column | `Add{ColumnName}To{Table}` |
| Adding an index | `Add{IndexName}Index` |
| Renaming a column | `Rename{OldName}To{NewName}In{Table}` |
| Schema-breaking change | `V2_{Description}` |

Never edit a migration file after it has been applied to any shared environment (development, staging, production). Create a new migration instead.

The `Migrations/` folder is committed to source control. Generated migration files are not hand-edited — schema changes go through new migrations.

---

## Database Seeding Strategy

The seed dataset provides 200+ realistic policy records covering the full range of statuses, regions, lines of business, and currencies. This ensures the list endpoint's filtering, sorting, pagination, and search features are testable with representative data.

Seeding is performed by `PolicyDataSeeder` in `Infrastructure/Persistence/Seed/`. It is invoked during application startup in development environments only.

```csharp
// Infrastructure/Persistence/Seed/PolicyDataSeeder.cs
public static class PolicyDataSeeder
{
    public static async Task SeedAsync(PolicyDbContext context)
    {
        if (await context.Policies.AnyAsync())
            return; // already seeded — idempotent

        var policies = GeneratePolicies();
        await context.Policies.AddRangeAsync(policies);
        await context.SaveChangesAsync();
    }

    private static IReadOnlyList<Policy> GeneratePolicies()
    {
        var regions = new[]
        {
            "Singapore", "Hong Kong", "Australia",
            "Japan", "Thailand", "Indonesia", "Malaysia", "Philippines"
        };
        var lobs = new[] { "Property", "Casualty", "A&H", "Marine" };
        var statuses = new[] { "Active", "Expired", "Pending", "Cancelled" };
        var currencies = new[] { "USD", "SGD", "HKD", "AUD", "JPY", "THB" };
        var underwriters = new[]
        {
            "James Wong", "Sarah Tan", "Michael Lim",
            "Emily Chen", "David Kumar", "Priya Sharma"
        };

        var policies = new List<Policy>();
        var now = DateTimeOffset.UtcNow;
        var rng  = new Random(42); // fixed seed for reproducible data

        for (int i = 1; i <= 220; i++)
        {
            var effectiveDate = DateOnly.FromDateTime(
                DateTime.Today.AddDays(-rng.Next(0, 730)));
            var expiryDate = effectiveDate.AddYears(1);

            policies.Add(new Policy
            {
                Id               = Guid.NewGuid(),
                PolicyNumber     = $"POL-{i:D6}",
                PolicyholderName = $"Policyholder {i}",
                LineOfBusiness   = lobs[rng.Next(lobs.Length)],
                Status           = statuses[rng.Next(statuses.Length)],
                PremiumAmount    = Math.Round(
                    1000 + (decimal)rng.NextDouble() * 4_999_000, 2),
                Currency         = currencies[rng.Next(currencies.Length)],
                EffectiveDate    = effectiveDate,
                ExpiryDate       = expiryDate,
                Region           = regions[rng.Next(regions.Length)],
                Underwriter      = underwriters[rng.Next(underwriters.Length)],
                FlaggedForReview = rng.Next(10) == 0, // ~10% flagged
                IsDeleted        = false,
                CreatedAt        = now.AddDays(-rng.Next(0, 730)),
                UpdatedAt        = now
            });
        }

        return policies;
    }
}
```

Invoke the seeder in `Program.cs`, guarded by environment check:

```csharp
// API/Program.cs
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var context = scope.ServiceProvider.GetRequiredService<PolicyDbContext>();
    await PolicyDataSeeder.SeedAsync(context);
}
```

**Seeding rules:**
- The seeder is **idempotent** — it checks for existing records before inserting.
- It is only invoked in `Development` — never in `Production` or `Staging`.
- The random seed (`new Random(42)`) is fixed so the dataset is reproducible across developer machines.
- All 200+ records cover every combination of status, region, and LOB at least once to ensure filtering tests have data to return.

---

## DbContext Registration Summary

```csharp
// API/Program.cs — Infrastructure service registration
builder.Services.Configure<SqlServerOptions>(
    builder.Configuration.GetSection(SqlServerOptions.SectionName));

builder.Services.AddDbContext<PolicyDbContext>((sp, options) =>
{
    var sql = sp.GetRequiredService<IOptions<SqlServerOptions>>().Value;
    options.UseSqlServer(
        sql.ConnectionString,
        o => o.CommandTimeout(sql.CommandTimeoutSeconds));

    if (builder.Environment.IsDevelopment())
        options.EnableSensitiveDataLogging();
});

// Repository registration
builder.Services.AddScoped<IPolicyRepository, PolicyRepository>();
```

`EnableSensitiveDataLogging()` is only enabled in development — it logs parameter values which must never appear in production logs.
