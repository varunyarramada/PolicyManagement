using Asp.Versioning;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PolicyManagement.API.Models;
using PolicyManagement.Application.DTOs;
using PolicyManagement.Application.Features.Policies.Commands.FlagPolicies;
using PolicyManagement.Application.Features.Policies.Queries.GetPolicies;
using PolicyManagement.Application.Features.Policies.Queries.GetPolicyById;
using PolicyManagement.Application.Features.Policies.Queries.GetPolicySummary;

namespace PolicyManagement.API.Controllers;

/// <summary>
/// Handles all Policy management endpoints.
/// All actions require a valid JWT Bearer token — enforced at the class level via <c>[Authorize]</c>.
/// The <c>PATCH /flag</c> action additionally requires the <c>Policy.Write</c> role.
/// </summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/policies")]
[Authorize]
[Produces("application/json")]
public sealed class PoliciesController(IMediator mediator) : ControllerBase
{
    // -----------------------------------------------------------------------
    // GET /api/v1/policies
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns a paginated, filtered, and sorted list of policies.
    /// </summary>
    /// <param name="page">1-based page number. Default: 1.</param>
    /// <param name="size">Page size (1–100). Default: 20.</param>
    /// <param name="sort">Sort expression, e.g. <c>premiumAmount,desc</c>. Default: <c>createdAt,desc</c>.</param>
    /// <param name="status">Optional filter by status string (Active, Expired, Pending, Cancelled).</param>
    /// <param name="lineOfBusiness">Optional filter by line of business (Property, Casualty, A&amp;H, Marine).</param>
    /// <param name="region">Optional filter by APAC region.</param>
    /// <param name="effectiveDateFrom">Optional inclusive start of effective date range (YYYY-MM-DD).</param>
    /// <param name="effectiveDateTo">Optional inclusive end of effective date range (YYYY-MM-DD).</param>
    /// <param name="search">Optional free-text search across policy number, policyholder name, and underwriter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PagedPolicyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPoliciesAsync(
        [FromQuery] int page = 1,
        [FromQuery] int size = 20,
        [FromQuery] string sort = "createdAt,desc",
        [FromQuery] string? status = null,
        [FromQuery] string? lineOfBusiness = null,
        [FromQuery] string? region = null,
        [FromQuery] DateOnly? effectiveDateFrom = null,
        [FromQuery] DateOnly? effectiveDateTo = null,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new GetPoliciesQuery(
                Page:              page,
                Size:              size,
                Sort:              sort,
                Status:            status,
                LineOfBusiness:    lineOfBusiness,
                Region:            region,
                EffectiveDateFrom: effectiveDateFrom,
                EffectiveDateTo:   effectiveDateTo,
                Search:            search),
            cancellationToken);

        return Ok(result);
    }

    // -----------------------------------------------------------------------
    // GET /api/v1/policies/{id}
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns a single policy by its unique identifier.
    /// Response is cached under <c>policy:v1:{id}</c> with a 5-minute TTL.
    /// </summary>
    /// <param name="id">The unique identifier of the policy (UUID).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(PolicyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPolicyByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new GetPolicyByIdQuery(id),
            cancellationToken);

        return Ok(result);
    }

    // -----------------------------------------------------------------------
    // GET /api/v1/policies/summary
    // -----------------------------------------------------------------------

    /// <summary>
    /// Returns aggregated statistics across all non-deleted policies.
    /// Response is cached under <c>policy:v1:summary</c> with a 1-minute TTL.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet("summary")]
    [ProducesResponseType(typeof(PolicySummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> GetPolicySummaryAsync(
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new GetPolicySummaryQuery(),
            cancellationToken);

        return Ok(result);
    }

    // -----------------------------------------------------------------------
    // PATCH /api/v1/policies/flag
    // -----------------------------------------------------------------------

    /// <summary>
    /// Flags a batch of policies for review in a single atomic operation.
    /// Requires the <c>Policy.Write</c> role.
    /// </summary>
    /// <remarks>
    /// Returns <c>204 No Content</c> on success.
    /// Returns <c>404 Not Found</c> if any policy ID does not exist.
    /// Returns <c>409 Conflict</c> if any policy is already flagged.
    /// </remarks>
    /// <param name="request">The request body containing the list of policy IDs to flag.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpPatch("flag")]
    [Authorize(Policy = "PolicyWrite")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> FlagPoliciesAsync(
        [FromBody] FlagPoliciesRequest request,
        CancellationToken cancellationToken = default)
    {
        await mediator.Send(
            new FlagPoliciesCommand(request.PolicyIds),
            cancellationToken);

        return NoContent();
    }
}
