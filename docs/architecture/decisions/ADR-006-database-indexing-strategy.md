# ADR-006: Database Indexing Strategy for Policy List Queries

- **Date:** 2026-06-16
- **Status:** Accepted

## Context

The `GET /api/v1/policies` endpoint is the most query-intensive endpoint in the service. It supports:

- **Filtering** by `status`, `lineOfBusiness`, `region`, `effectiveDateFrom`, `effectiveDateTo`, and free-text `search` (across `policyNumber`, `policyholderName`, `underwriter`).
- **Sorting** by `policyNumber`, `status`, `premiumAmount`, `effectiveDate`, `expiryDate`, `createdAt`, `policyholderName`.
- **Pagination** with a default sort of `createdAt DESC`.
- **AND-combined filters** — multiple filter parameters narrow the result set progressively.

Additionally, `GET /api/v1/policies/summary` performs GROUP BY aggregations on `status`, `region`, `lineOfBusiness`, `currency`, and `flaggedForReview`, and a COUNT on Active policies with `expiryDate` within the next 30 days.

All queries must exclude soft-deleted records (`is_deleted = 0`). Without a deliberate indexing strategy, many of these queries would result in full table scans — even at the 200-record seed scale this is not an architectural discipline the production code should rely on.

Three strategic questions are answered in this ADR:

1. What indexes should be created for the filter and sort access patterns?
2. Should composite or single-column indexes be preferred?
3. How should the soft-delete filter interact with indexes?

## Decision

The indexing strategy uses a combination of **single-column filtered indexes** for high-cardinality filter and sort columns, **composite filtered indexes** for the most common multi-column filter combinations, and **filtered index predicates** (`WHERE is_deleted = 0`) on every non-unique index to avoid indexing deleted rows.

### Index catalogue

| Index Name | Columns (order) | Direction | Filter predicate | Purpose |
|---|---|---|---|---|
| `PK_Policies` | `id` | — | — | Clustered primary key; used for `GET /policies/{id}` and join lookups |
| `UQ_Policies_PolicyNumber` | `policy_number` | ASC | — | Unique constraint enforcement; used for LIKE search prefix scans |
| `IX_Policies_Status` | `status` | ASC | `WHERE is_deleted = 0` | Status filter; `countByStatus` aggregation |
| `IX_Policies_LineOfBusiness` | `line_of_business` | ASC | `WHERE is_deleted = 0` | LOB filter; `countByLineOfBusiness` aggregation |
| `IX_Policies_Region` | `region` | ASC | `WHERE is_deleted = 0` | Region filter; `countByRegion` aggregation |
| `IX_Policies_EffectiveDate` | `effective_date` | ASC | `WHERE is_deleted = 0` | Date range filter (`effectiveDateFrom`, `effectiveDateTo`) |
| `IX_Policies_ExpiryDate` | `expiry_date` | ASC | `WHERE is_deleted = 0` | Expiry date filter; `expiringSoonCount` computation |
| `IX_Policies_CreatedAt` | `created_at` | DESC | `WHERE is_deleted = 0` | Default sort (`createdAt DESC`); pagination keyset support |
| `IX_Policies_PolicyholderName` | `policyholder_name` | ASC | `WHERE is_deleted = 0` | `policyholderName` sort; partial support for LIKE search |
| `IX_Policies_FlaggedForReview` | `flagged_for_review` | ASC | `WHERE is_deleted = 0` | `flaggedCount` aggregation |
| `IX_Policies_Status_LineOfBusiness` | `status`, `line_of_business` | ASC, ASC | `WHERE is_deleted = 0` | Most common combined filter: status + LOB |
| `IX_Policies_Status_Region` | `status`, `region` | ASC, ASC | `WHERE is_deleted = 0` | Common dashboard filter: status + region |
| `IX_Policies_ExpiryDate_Status` | `expiry_date`, `status` | ASC, ASC | `WHERE is_deleted = 0` | `expiringSoonCount`: Active policies expiring within 30 days |

### Index implementation approach

Indexes are declared in the EF Core entity configuration (`PolicyConfiguration : IEntityTypeConfiguration<Policy>`) using the Fluent API. They are created and maintained via EF Core migrations — never manually applied to the database.

```
// Infrastructure/Persistence/Configurations/PolicyConfiguration.cs
// (illustrative — not implementation code)
builder.HasIndex(p => p.Status)
    .HasFilter("is_deleted = 0")
    .HasDatabaseName("IX_Policies_Status");

builder.HasIndex(p => new { p.Status, p.LineOfBusiness })
    .HasFilter("is_deleted = 0")
    .HasDatabaseName("IX_Policies_Status_LineOfBusiness");
```

### Soft-delete filter predicate rationale

All non-unique indexes include a `WHERE is_deleted = 0` filter clause. This:

1. Reduces the size of every non-unique index by excluding deleted rows — improving both storage efficiency and scan performance.
2. Ensures the query optimiser selects the filtered index for queries that already include `AND is_deleted = 0` (which EF Core's global query filter adds automatically).
3. Means that a query without the `is_deleted = 0` predicate (e.g., an admin tool that queries deleted records) cannot use these filtered indexes — this is an acceptable trade-off since no such endpoint exists in the current API.

### LIKE search limitation and upgrade path

The `search` query parameter uses `LIKE '%term%'`. A leading wildcard prevents SQL Server from using a B-tree index for the search — it results in an index scan regardless of whether an index exists on `policy_number`, `policyholder_name`, or `underwriter`.

The search predicate is isolated in `IPolicyRepository.GetPagedAsync`. To upgrade to SQL Server Full-Text Search:

1. Add Full-Text Catalog and Full-Text Index on `Policies(policy_number, policyholder_name, underwriter)` via a migration.
2. Replace the LIKE predicates in the repository with `CONTAINS(policy_number, @term) OR CONTAINS(policyholder_name, @term) OR CONTAINS(underwriter, @term)`.
3. No changes to the handler, validator, controller, or DTO.

## Alternatives Considered

| Option | Description | Why Rejected |
|--------|-------------|-------------|
| **No indexes beyond the primary key** | Rely on SQL Server's query optimiser and table scans. Simple to implement — no migration changes needed. | Acceptable only for the development seed dataset (200 records). At production scale, a list query with `status = 'Active' AND region = 'Singapore'` on a million-row table without indexes would take seconds per request under concurrent load. This is not a production-quality decision. |
| **Single composite index covering all filter columns** | A single composite index on `(status, line_of_business, region, effective_date, expiry_date, created_at)` in a fixed left-to-right order. | SQL Server's B-tree index is only usable when the query predicates match the leading columns of the composite index in order. A query filtering by `region` only (not `status` or `line_of_business`) cannot use this index because `region` is not a leading column. Multi-column indexes work only when filter patterns are predictable and uniform — they do not serve ad-hoc combinations. Single-column indexes on each filterable column are more flexible. |
| **Columnstore index for summary aggregations** | A non-clustered columnstore index on `(status, region, line_of_business, currency, flagged_for_review, is_deleted)` to accelerate GROUP BY aggregations for the summary endpoint. | Columnstore indexes are designed for analytic workloads with large datasets (millions of rows). For the assessment's 200-record seed and a table expected to grow to tens of thousands of rows, the overhead of maintaining a columnstore index is not justified. Row-mode GROUP BY aggregations with filtered B-tree indexes are sufficient. The columnstore upgrade path is documented as a production readiness item if the table grows past ~500,000 rows. |
| **No filtered indexes — include `is_deleted` as leading index column** | Instead of a filter predicate, include `is_deleted` as the first column of every index: `(is_deleted, status)`, `(is_deleted, region)`, etc. | Including `is_deleted` as the leading column is a workaround for databases that do not support filtered indexes (e.g., MySQL 5.x). SQL Server supports filtered indexes natively and the filtered approach is preferred: it produces a smaller, more efficient index that covers only the non-deleted rows, rather than a full index with a leading `is_deleted` column that the optimiser must match on every query. |

## Consequences

### Positive

- **Index per access pattern.** Every filterable column has a single-column index that the optimiser can select independently or in combination via index intersection. Common filter pairs (status + LOB, status + region, expiry + status) have dedicated composite indexes that avoid the overhead of index intersection.
- **Aggregation queries use existing indexes.** The summary aggregations (`countByStatus`, `countByRegion`, `countByLineOfBusiness`, `flaggedCount`, `expiringSoonCount`) all read columns that are indexed — the database can satisfy these GROUP BY queries from index scans rather than table scans.
- **Default sort is indexed.** `createdAt DESC` (the default sort) has a dedicated descending index, so pagination without a filter returns results in order from the index without a sort operation.
- **Soft-delete filter is free.** The `WHERE is_deleted = 0` predicate added by EF Core's global query filter matches the filtered index predicate — the optimiser will select these indexes automatically.
- **Upgrade path documented.** The LIKE search limitation and the GUID clustering fragmentation issue are documented with explicit mitigation steps that require only repository or infrastructure changes.

### Negative / Trade-offs

- **Write overhead.** Thirteen indexes on the `Policies` table means every `INSERT` and `UPDATE` (including the bulk-flag command) must maintain all thirteen index structures. For the assessment's low write volume (only the seed insert and flag operations), this is negligible. At production scale with high flag operation throughput, index maintenance overhead should be profiled.
- **Index fragmentation from non-sequential GUIDs.** Client-generated `uniqueidentifier` primary keys produce non-sequential values that cause page splits in the clustered index. This affects all non-clustered indexes via their clustered key lookups. Mitigation: use `NEWSEQUENTIALID()` or a separate sequential clustered key for production. Addressed in the architecture document's risk register.
- **Filtered indexes and the EF Core global query filter.** The filtered indexes assume `is_deleted = 0` is always included in queries. EF Core's global query filter ensures this for all standard operations. If a developer bypasses the filter using `IgnoreQueryFilters()` (e.g., for an admin query or a seed validation check), the filtered indexes will not be used — SQL Server will fall back to a table scan. This is a known limitation of filtered indexes and is acceptable for the current API surface (no admin endpoints).

## Compliance with Clean Architecture

Index definitions are declared in `Infrastructure/Persistence/Configurations/PolicyConfiguration.cs` — the correct Infrastructure layer location. The `Domain` `Policy` entity has no awareness of indexes. The `Application` handlers reference `IPolicyRepository` — they have no knowledge of which indexes back the queries. Index strategy is a pure infrastructure concern, correctly encapsulated in the `Infrastructure` layer. The inward-dependency rule is fully observed.
