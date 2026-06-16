# Skill: Contract-First API Design — PolicyManagement BFF

**Audience:** Architect agent, Backend Developer agents, API Designer agents
**Project:** PolicyManagement BFF — Chubb APAC
**Runtime:** .NET 10 / C# · ASP.NET Core Web API · OpenAPI 3.x

---

## What Contract-First API Design Means

Contract-first means the **OpenAPI 3.x specification is written before any implementation code**. The spec is the source of truth. Controllers, request models, response models, and validators are all written to match the spec — the spec is never reverse-engineered from code.

The opposite approach — code-first, where the spec is generated from attributes or annotations — means the contract evolves accidentally as the code changes. For a BFF serving a frontend dashboard, this creates uncontrolled breaking changes and misalignment between what the frontend expects and what the backend delivers.

### Why contract-first is mandatory here

| Risk avoided | How contract-first helps |
|---|---|
| Frontend/backend misalignment | Frontend team reads the spec, not the code |
| Accidental breaking changes | Any change to the spec is explicit and reviewed |
| Undocumented behaviour | Every endpoint, parameter, and status code is declared |
| Schema drift | Request and response shapes are defined once and enforced |
| Inconsistent error formats | `ProblemDetails` shape is defined in the spec, not per-controller |

---

## How OpenAPI 3.x Drives Implementation

The workflow is strictly linear:

```
1. Write or update the OpenAPI spec
       ↓
2. Review and approve the spec (API contract review)
       ↓
3. Implement controllers, request models, response models to match
       ↓
4. Implement handlers, validators, repositories
       ↓
5. Validate implementation matches the spec (manual or automated)
```

**No step may be skipped.** Implementation never precedes the spec. If requirements change, the spec is updated first, then the code follows.

The spec lives at:
```
docs/openapi/policy-management-api.yaml
```

This file is the single source of truth for all API contracts in the PolicyManagement BFF.

---

## OpenAPI Spec File Structure

The spec is organised as a single YAML file with clearly separated sections. Schemas are defined under `components/schemas` and referenced with `$ref` — never inlined at the endpoint level.

```
docs/openapi/
└── policy-management-api.yaml
    ├── openapi: 3.1.0
    ├── info
    │   ├── title
    │   ├── version
    │   └── description
    ├── servers
    │   └── url: /api/v1
    ├── paths
    │   ├── /policies
    │   ├── /policies/{id}
    │   ├── /policies/flag
    │   └── /policies/summary
    └── components
        ├── schemas
        │   ├── PolicyDto
        │   ├── PolicySummaryResponse
        │   ├── PagedResponse
        │   ├── FlagPoliciesRequest
        │   └── ProblemDetails
        └── parameters
            ├── PageParam
            ├── SizeParam
            └── SortParam
```

Reusable parameters (pagination, sorting) are declared once under `components/parameters` and referenced on every endpoint that uses them — never duplicated.

---

## API Versioning Strategy

All endpoints are versioned under a URL path prefix from day one:

```
/api/v{version}/{resource}
```

For PolicyManagement:
```
/api/v1/policies
/api/v1/policies/{id}
/api/v1/policies/flag
/api/v1/policies/summary
```

Rules:
- The version segment is always present — there is no versionless URL.
- Breaking changes (removed fields, changed response shapes, new required parameters) require a new version (`v2`).
- Non-breaking additions (new optional fields, new optional query parameters) may be made to the existing version.
- The `info.version` field in the spec reflects the API version, not the deployment version.

In ASP.NET Core, the route prefix is declared at the controller level:
```csharp
[ApiController]
[Route("api/v1/[controller]")]
public sealed class PoliciesController : ControllerBase { ... }
```

---

## Endpoint Overview — PolicyManagement

| Method | Path | Purpose | Success code |
|---|---|---|---|
| `GET` | `/api/v1/policies` | List with pagination, filtering, sorting, search | `200 OK` |
| `GET` | `/api/v1/policies/{id}` | Single policy by UUID | `200 OK` |
| `PATCH` | `/api/v1/policies/flag` | Bulk flag policies for review | `204 No Content` |
| `GET` | `/api/v1/policies/summary` | Aggregated statistics | `200 OK` |

---

## Query Parameter Conventions

Query parameters follow consistent naming and typing across all list endpoints.

### Pagination parameters

| Parameter | Type | Default | Constraints | Description |
|---|---|---|---|---|
| `page` | integer | `1` | `>= 1` | 1-based page number |
| `size` | integer | `20` | `1–100` | Records per page |

### Sorting parameters

| Parameter | Type | Example | Description |
|---|---|---|---|
| `sort` | string | `premium` | Field to sort by |
| `order` | string | `desc` | `asc` or `desc` |

Valid `sort` values for `/policies`: `policyNumber`, `status`, `premium`, `effectiveDate`, `expiryDate`, `createdAt`.

### Filter parameters — `/api/v1/policies`

| Parameter | Type | Example | Description |
|---|---|---|---|
| `status` | string (enum) | `Active` | Filter by policy status |
| `lineOfBusiness` | string (enum) | `Marine` | Filter by line of business |
| `region` | string (enum) | `Singapore` | Filter by APAC region |
| `effectiveDateFrom` | string (date) | `2025-01-01` | Effective date range start (ISO 8601) |
| `effectiveDateTo` | string (date) | `2025-12-31` | Effective date range end (ISO 8601) |
| `search` | string | `POL-001234` | Free-text search — policy number, holder name |

All filter parameters are optional. When multiple filters are supplied they are combined with AND logic.

---

## Request and Response Schema Conventions

### Naming

- Schema names are PascalCase: `PolicyDto`, `FlagPoliciesRequest`, `PagedResponse`.
- Property names in JSON are camelCase: `policyNumber`, `effectiveDate`, `lineOfBusiness`.
- Enum values are PascalCase strings: `"Active"`, `"Expired"`, `"Pending"`, `"Cancelled"`.
- Dates are ISO 8601 strings (`format: date`): `"2025-06-15"`.
- Timestamps are ISO 8601 strings (`format: date-time`): `"2025-06-15T09:30:00Z"`.
- Monetary amounts are `number` with a separate `currency` string field — never a single formatted string.
- UUIDs are `string` with `format: uuid`.

### Required vs optional fields

Every field in a response schema is either explicitly `required` or documented as nullable with `nullable: true`. There are no undocumented optional fields. Frontend consumers must be able to trust the spec completely.

---

## Pagination Response Envelope

All list endpoints return a consistent pagination envelope. The envelope is defined once in `components/schemas` as `PagedResponse` and referenced by every list endpoint's response schema.

```yaml
# components/schemas/PagedResponse
PagedResponse:
  type: object
  required:
    - data
    - pagination
  properties:
    data:
      type: array
      items:
        $ref: '#/components/schemas/PolicyDto'
    pagination:
      $ref: '#/components/schemas/PaginationMeta'

PaginationMeta:
  type: object
  required:
    - page
    - size
    - totalCount
    - totalPages
  properties:
    page:
      type: integer
      minimum: 1
      example: 1
    size:
      type: integer
      minimum: 1
      maximum: 100
      example: 20
    totalCount:
      type: integer
      minimum: 0
      example: 247
    totalPages:
      type: integer
      minimum: 0
      example: 13
```

Example response body:
```json
{
  "data": [
    {
      "id": "3fa85f64-5717-4562-b3fc-2c963f66afa6",
      "policyNumber": "POL-001234",
      "status": "Active",
      "lineOfBusiness": "Marine",
      "region": "Singapore",
      "premium": { "amount": 15000.00, "currency": "SGD" },
      "effectiveDate": "2025-01-01",
      "expiryDate": "2025-12-31"
    }
  ],
  "pagination": {
    "page": 1,
    "size": 20,
    "totalCount": 247,
    "totalPages": 13
  }
}
```

The `PagedResponse` wrapper type in C# mirrors this shape:
```csharp
// Application/DTOs/PagedResponse.cs
public record PagedResponse<T>(
    IReadOnlyList<T> Data,
    PaginationMeta Pagination);

public record PaginationMeta(
    int Page,
    int Size,
    int TotalCount,
    int TotalPages);
```

---

## Error Response Format — RFC 7807 ProblemDetails

All error responses use RFC 7807 `ProblemDetails`. This is the **only** error format returned by the API — no custom error envelopes, no plain string messages.

```yaml
# components/schemas/ProblemDetails
ProblemDetails:
  type: object
  required:
    - type
    - title
    - status
  properties:
    type:
      type: string
      format: uri
      example: "https://tools.ietf.org/html/rfc7231#section-6.5.4"
    title:
      type: string
      example: "Not Found"
    status:
      type: integer
      example: 404
    detail:
      type: string
      example: "Policy with ID 3fa85f64 was not found."
    instance:
      type: string
      format: uri
      example: "/api/v1/policies/3fa85f64-5717-4562-b3fc-2c963f66afa6"
    errors:
      type: object
      additionalProperties:
        type: array
        items:
          type: string
      description: >
        Field-level validation errors. Present only on 400 responses.
      example:
        policyIds: ["At least one policy ID must be provided."]
        page: ["Page number must be at least 1."]
```

### HTTP status code usage

| Status | When used |
|---|---|
| `200 OK` | Successful GET returning data |
| `204 No Content` | Successful PATCH/POST/DELETE with no response body |
| `400 Bad Request` | Validation failure — `errors` map present in `ProblemDetails` |
| `404 Not Found` | Resource not found (policy ID does not exist) |
| `409 Conflict` | State conflict (e.g., policy already flagged) |
| `422 Unprocessable Entity` | Semantically invalid request (valid structure, invalid business state) |
| `500 Internal Server Error` | Unhandled server error — no internal details in response |

Stack traces and internal exception messages are **never** included in error responses. `GlobalExceptionMiddleware` ensures this.

---

## Illustrative OpenAPI YAML — GET /policies

```yaml
paths:
  /policies:
    get:
      operationId: listPolicies
      summary: List policies with pagination, filtering, and sorting
      tags:
        - Policies
      parameters:
        - $ref: '#/components/parameters/PageParam'
        - $ref: '#/components/parameters/SizeParam'
        - name: sort
          in: query
          schema:
            type: string
            enum: [policyNumber, status, premium, effectiveDate, expiryDate, createdAt]
          example: premium
        - name: order
          in: query
          schema:
            type: string
            enum: [asc, desc]
            default: asc
        - name: status
          in: query
          schema:
            type: string
            enum: [Active, Expired, Pending, Cancelled]
        - name: lineOfBusiness
          in: query
          schema:
            type: string
            enum: [Property, Casualty, A&H, Marine]
        - name: region
          in: query
          schema:
            type: string
            enum: [Singapore, HongKong, Australia, Japan, Thailand, Indonesia, Malaysia, Philippines]
        - name: effectiveDateFrom
          in: query
          schema:
            type: string
            format: date
          example: "2025-01-01"
        - name: effectiveDateTo
          in: query
          schema:
            type: string
            format: date
          example: "2025-12-31"
        - name: search
          in: query
          schema:
            type: string
            maxLength: 100
          example: "POL-001234"
      responses:
        '200':
          description: Paged list of policies
          content:
            application/json:
              schema:
                $ref: '#/components/schemas/PagedPolicyResponse'
        '400':
          description: Invalid query parameters
          content:
            application/problem+json:
              schema:
                $ref: '#/components/schemas/ProblemDetails'
```

---

## Illustrative OpenAPI YAML — PATCH /policies/flag

```yaml
  /policies/flag:
    patch:
      operationId: flagPolicies
      summary: Bulk flag policies for review
      tags:
        - Policies
      requestBody:
        required: true
        content:
          application/json:
            schema:
              $ref: '#/components/schemas/FlagPoliciesRequest'
      responses:
        '204':
          description: Policies flagged successfully — no response body
        '400':
          description: Validation failure — empty list or invalid IDs
          content:
            application/problem+json:
              schema:
                $ref: '#/components/schemas/ProblemDetails'
        '404':
          description: One or more policy IDs not found
          content:
            application/problem+json:
              schema:
                $ref: '#/components/schemas/ProblemDetails'

components:
  schemas:
    FlagPoliciesRequest:
      type: object
      required:
        - policyIds
      properties:
        policyIds:
          type: array
          items:
            type: string
            format: uuid
          minItems: 1
          maxItems: 100
          example:
            - "3fa85f64-5717-4562-b3fc-2c963f66afa6"
            - "6ba7b810-9dad-11d1-80b4-00c04fd430c8"
```

---

## How Controllers Must Match the Contract

Every controller action must implement the contract exactly as declared in the spec:

- The HTTP method, route, and path parameters match the spec.
- Every query parameter declared in the spec has a corresponding action parameter.
- Every `200` response returns the schema declared in the spec.
- Every non-`2xx` response declared in the spec is annotated with `[ProducesResponseType]`.
- No undeclared status codes are returned by the action.
- Content type is `application/json` for success responses, `application/problem+json` for error responses.

If the spec declares a query parameter as optional (no `required: true`), the corresponding C# parameter is nullable or has a default value. If the spec declares a path parameter as `format: uuid`, the C# type is `Guid`.

### Illustrative C# — matching the contract

```csharp
// API/Controllers/PoliciesController.cs

[ApiController]
[Route("api/v1/[controller]")]
public sealed class PoliciesController : ControllerBase
{
    private readonly IMediator _mediator;

    public PoliciesController(IMediator mediator) => _mediator = mediator;

    /// <summary>List policies with pagination, filtering, and sorting.</summary>
    [HttpGet]
    [Produces("application/json")]
    [ProducesResponseType(typeof(PagedResponse<PolicyDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> List(
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        [FromQuery] string? sort = null,
        [FromQuery] string? order = "asc",
        [FromQuery] string? status = null,
        [FromQuery] string? lineOfBusiness = null,
        [FromQuery] string? region = null,
        [FromQuery] DateOnly? effectiveDateFrom = null,
        [FromQuery] DateOnly? effectiveDateTo = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(new GetPoliciesQuery(
            page, size, sort, order, status,
            lineOfBusiness, region,
            effectiveDateFrom, effectiveDateTo, search),
            cancellationToken);

        return Ok(result);
    }

    /// <summary>Get a single policy by ID.</summary>
    [HttpGet("{id:guid}")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(PolicyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetById(
        Guid id,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetPolicyByIdQuery(id), cancellationToken);
        return Ok(result);
    }

    /// <summary>Bulk flag policies for review.</summary>
    [HttpPatch("flag")]
    [Consumes("application/json")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Flag(
        [FromBody] FlagPoliciesRequest request,
        CancellationToken cancellationToken)
    {
        await _mediator.Send(
            new FlagPoliciesCommand(request.PolicyIds), cancellationToken);
        return NoContent();
    }

    /// <summary>Get aggregated policy statistics.</summary>
    [HttpGet("summary")]
    [Produces("application/json")]
    [ProducesResponseType(typeof(PolicySummaryResponse), StatusCodes.Status200OK)]
    public async Task<IActionResult> Summary(CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetPolicySummaryQuery(), cancellationToken);
        return Ok(result);
    }
}
```

---

## ProducesResponseType Rules

`[ProducesResponseType]` must be applied to **every** controller action. There are no exceptions.

| Rule | Rationale |
|---|---|
| Declare every possible status code the action can return | Keeps the Swagger UI accurate and the contract complete |
| Use the typed overload `typeof(T)` for 2xx responses with a body | Enables correct schema generation |
| Use `typeof(ProblemDetails)` for all 4xx and 5xx responses | Enforces consistent error schema in Swagger |
| Use `StatusCodes.Status{NNN}{Name}` constants, not raw integers | Readability and refactoring safety |
| Annotate `204 No Content` actions with the untyped overload | No body means no type parameter |

```csharp
// All of these are required — not optional documentation niceties
[ProducesResponseType(typeof(PolicyDto), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
```

The Swagger UI and any generated client SDKs are derived from these annotations combined with the OpenAPI spec. Incomplete annotations result in inaccurate client code and misleading documentation.

---

## Spec-to-Implementation Validation Checklist

Before raising a PR for any new or modified endpoint, verify:

- [ ] OpenAPI spec updated with the new or changed endpoint
- [ ] All query parameters in the spec have a matching `[FromQuery]` parameter in the action
- [ ] All path parameters in the spec match `[HttpGet("{param}")]` route templates
- [ ] All request body schemas in the spec have a matching `[FromBody]` model
- [ ] All response schemas in the spec have a matching `[ProducesResponseType]` annotation
- [ ] All `4xx` responses in the spec use `ProblemDetails` schema
- [ ] No status codes are returned from the action that are not declared in the spec
- [ ] Enum values in query parameters are validated (via `GetPoliciesQueryValidator`) against the spec-declared enum list
- [ ] Date and UUID format parameters use `DateOnly` and `Guid` C# types respectively
- [ ] Response property names in JSON match the camelCase names declared in the spec

---

## Common Mistakes to Avoid

### Adding undocumented response status codes

```csharp
// WRONG — 409 returned but not declared in [ProducesResponseType] or the spec
public async Task<IActionResult> Flag([FromBody] FlagPoliciesRequest request, ...)
{
    ...
    if (alreadyFlagged)
        return Conflict(); // undeclared status code
}

// CORRECT — declare 409 in both the spec and the annotation
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
```

---

### Returning anonymous objects instead of declared response types

```csharp
// WRONG — shape not declared in spec; client cannot rely on this structure
return Ok(new { count = 5, ids = flaggedIds });

// CORRECT — return a declared DTO that matches the spec schema
return Ok(result); // result is PolicySummaryResponse, declared in spec
```

---

### Changing the response shape without updating the spec

If a property is added to or removed from a response DTO, the spec `components/schemas` entry must be updated in the same commit. The spec and the implementation must always be in sync.

---

### Using route parameters for non-identifier lookups

```csharp
// WRONG — status is a filter, not an identifier; it belongs in query params
[HttpGet("{status}")]
public async Task<IActionResult> GetByStatus(string status, ...) { ... }

// CORRECT — filtering belongs in query parameters
[HttpGet]
public async Task<IActionResult> List([FromQuery] string? status, ...) { ... }
```

---

### Omitting `[Consumes]` on endpoints with a request body

```csharp
// WRONG — content type not declared; Swagger UI shows no request body schema
[HttpPatch("flag")]
public async Task<IActionResult> Flag([FromBody] FlagPoliciesRequest request, ...)

// CORRECT
[HttpPatch("flag")]
[Consumes("application/json")]
public async Task<IActionResult> Flag([FromBody] FlagPoliciesRequest request, ...)
```
