namespace PolicyManagement.Application.Interfaces;

/// <summary>
/// Abstraction over the current authenticated user's identity. Handlers that need user context
/// inject this interface — they never access <c>HttpContext.User</c>, <c>ClaimsPrincipal</c>,
/// or <c>IHttpContextAccessor</c> directly.
/// <para>
/// The implementation (<c>CurrentUserService</c>) lives in <c>PolicyManagement.API/Services/</c>
/// and is registered as <c>Scoped</c> — one instance per HTTP request.
/// </para>
/// </summary>
/// <remarks>
/// See ADR-007 for the JWT Bearer authentication decision and claim-extraction conventions.
/// Claims are extracted from the Keycloak-issued JWT:
/// <list type="bullet">
///   <item><description><c>sub</c> → <see cref="UserId"/></description></item>
///   <item><description><c>email</c> → <see cref="Email"/></description></item>
///   <item><description><c>realm_access.roles[]</c> → <see cref="Roles"/></description></item>
/// </list>
/// </remarks>
public interface ICurrentUserService
{
    /// <summary>
    /// Gets the unique identifier of the authenticated user, extracted from the JWT <c>sub</c> claim.
    /// Returns <see langword="null"/> if no user is authenticated.
    /// </summary>
    string? UserId { get; }

    /// <summary>
    /// Gets the email address of the authenticated user, extracted from the JWT <c>email</c> claim.
    /// Returns <see langword="null"/> if the claim is absent.
    /// </summary>
    string? Email { get; }

    /// <summary>
    /// Gets the set of roles assigned to the authenticated user, extracted from the
    /// Keycloak <c>realm_access.roles</c> claim array.
    /// Returns an empty collection if no roles are present.
    /// </summary>
    IReadOnlyList<string> Roles { get; }

    /// <summary>
    /// Returns <see langword="true"/> if the authenticated user has the specified role.
    /// Comparison is case-sensitive, matching Keycloak role naming conventions.
    /// </summary>
    /// <param name="role">The role name to check (e.g. <c>"Policy.Write"</c>).</param>
    bool IsInRole(string role);
}
