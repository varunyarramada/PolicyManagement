**CHUBB APAC**

**Take-Home Assessment**

Backend Developer

*Policy Management BFF Service*

**Time guidance:** 2--3 hours target \| Hard cap: 5 hours

**Submission:** TBD --- supervised session or self-timed (details to follow)

**Version:** 1.0 \| May 2026

########## Background

Chubb\'s APAC operations team manages insurance policies across multiple regions. The front-end team is building a Policy Overview Dashboard and needs a Backend-for-Frontend (BFF) service to aggregate, transform, and serve policy data. Your task is to design and implement this BFF service.

The BFF will sit between the front-end application and downstream systems (database, cache, message broker). It must be production-quality --- the kind of service you would confidently put through a pull request review with senior engineers on your team.

########## Technology Choice

You may implement the service using **either**:

- **C# / .NET** --- the current stack for the OneHub platform

- **Java / Spring Boot** --- the stack for the Commercial Insurance platform

Choose the stack you are strongest in. Both are equally valid and both are actively used across Chubb APAC engineering.

########## Requirements

#################### Contract-First API Design

The service must expose a RESTful API. You are expected to take a **contract-first approach**:

- **OpenAPI 3.x** (Swagger) specification defined before implementing endpoints

- Generate server stubs or use the contract to drive your implementation

- The contract should be the single source of truth for the API shape

**Core API Endpoints:**

| **Endpoint** | **Description** |
|----|----|
| GET /api/v1/policies | List policies with pagination, sorting, filtering (by status, line of business, date range, region) and free-text search |
| GET /api/v1/policies/{id} | Get a single policy by ID |
| PATCH /api/v1/policies/flag | Bulk flag policies for review (accepts array of policy IDs) |
| GET /api/v1/policies/summary | Aggregated statistics --- counts by status, total premium by line of business, expiring-soon count |

############################## Pagination and Filtering Contract

The **GET /api/v1/policies** endpoint should support:

- **page** and **size** query parameters (with sensible defaults)

- **sort** parameter (field and direction, e.g. sort=premiumAmount,desc)

- **status** filter (enum: Active, Expired, Pending, Cancelled)

- **lineOfBusiness** filter (enum: Property, Casualty, A&H, Marine)

- **region** filter

- **effectiveDateFrom / effectiveDateTo** range filter

- **search** free-text parameter (searches across policyNumber, policyholderName, underwriter)

#################### Database Integration (Required)

Use a relational database with proper schema management via migrations. SQL Server is preferred (it matches the OneHub production stack on Azure SQL); PostgreSQL or SQLite are acceptable for local development. Seed the database with 200+ realistic policy records covering all status values, lines of business, APAC regions, and a realistic spread of dates and premium amounts.

**Policy Data Schema:**

| **Field** | **Type** | **Notes** |
|----|----|----|
| id | UUID | Primary key |
| policyNumber | String | Unique, format: POL-XXXXXX |
| policyholderName | String | Realistic APAC names |
| lineOfBusiness | Enum | Property, Casualty, A&H, Marine |
| status | Enum | Active, Expired, Pending, Cancelled |
| premiumAmount | Decimal | Range: 1,000 -- 5,000,000 |
| currency | String | USD, SGD, HKD, AUD, JPY, THB |
| effectiveDate | Date |  |
| expiryDate | Date |  |
| region | String | Singapore, Hong Kong, Australia, Japan, Thailand, Indonesia, Malaysia, Philippines |
| underwriter | String |  |
| flaggedForReview | Boolean | Default: false |
| createdAt | Timestamp |  |
| updatedAt | Timestamp |  |

#################### Clean Architecture (Required)

Demonstrate clear architectural layering with dependencies pointing inward. API, service, domain, and infrastructure concerns should be properly separated. Infrastructure details should not leak into domain or service layers.

#################### Caching (Bonus)

A caching layer for frequently accessed data such as summary statistics or policy listings, with a clear invalidation strategy.

#################### Kafka Integration (Bonus)

Implement a Kafka producer that publishes events when policies are flagged for review, and a consumer that listens for policy status change events. Idempotent consumer handling and a well-defined event schema are expected.

#################### Principles We Value

We expect the service to reflect the principles a senior engineer would naturally apply: DRY, SOLID, 12-factor configuration, and contract-first thinking --- not as a checklist, but evident in how the code is structured.

#################### Test Automation (Required)

We expect production-quality engineering standards for testing. Think about what a senior engineer would expect to see in a pull request.

#################### Cross-Cutting Concerns

A production service needs more than just endpoints. Consider logging, error handling, health checks, externalised configuration, API documentation, and a runnable local setup.

########## Deliverables

1.  **Git repository** with meaningful commit history showing your development process

2.  **Working service** that can be started locally (ideally via docker-compose up)

3.  **OpenAPI specification** file (YAML or JSON) as the API contract

4.  **AI working journal** --- a prompt log or equivalent showing what you accepted, what you challenged, and what you overrode, with brief reasoning. This does not need to be polished --- a running notes file committed alongside the code is fine.

5.  **Any other supporting documentation** you feel is appropriate --- architecture decisions, design rationale, trade-off analysis, diagrams

6.  **30--60 minute walkthrough** with the hiring panel

#################### Walkthrough Format

The walkthrough is 30--60 minutes and is as important as the code itself. This is where we explore your architecture thinking, design decisions, and how you approached the problem under time pressure.

| **Segment** | **Duration** | **Description** |
|----|----|----|
| Your presentation | 15--20 min | Walk through your architecture, key decisions, and demonstrate the running service. Cover what you built, what you prioritised, and why. |
| Panel Q&A | 10--15 min | Technical deep-dive, \"why not X?\" questions, trade-off discussions |
| What would you do with more time? | 10 min | Walk us through what you would tackle next, in priority order, and how you would approach it |
| Your questions | 5 min | Anything you want to ask us |

The panel will probe architectural decisions and AI collaboration process. Come prepared to explain every decision --- including what you chose not to build, what shortcuts you took, and what you would do differently with more time.

#################### Notes

This is a **sprint-format assessment** --- the goal is to show what is possible with AI in **2--3 hours**. If you feel you need more time to do the work justice, you may extend to a maximum of **5 hours** --- but we encourage you to treat the 2--3 hour mark as the real target. We are not expecting a finished product --- we are evaluating how much you can build, and how well, when you work with AI effectively.

- **Prioritise ruthlessly.** You may not complete everything --- that is by design. Decide what to build and what to leave out, and be prepared to explain that prioritisation in the walkthrough.

- **Output is a key measure, but quality matters.** A well-engineered, well-prioritised submission tells us more than a sloppy complete one.

- **AI is your primary working interface.** We expect AI tooling to drive the bulk of code generation. What we are evaluating is how you direct, challenge, and override it --- not whether you used it. Document what you accepted, what you challenged, and what you overrode as you go. You sign off every line you submit; the panel will probe anything you cannot defend.

- **The walkthrough is where your thinking is explored.** Come prepared to articulate the approaches you took and why, the shortcuts you made under time pressure, and what you would do differently or tackle next with more time. Verbal explanation counts --- you don\'t need a polished ADR for every decision.

If you have questions about the assessment, contact the hiring panel at \[hiring panel contact\].

**Good luck.**
