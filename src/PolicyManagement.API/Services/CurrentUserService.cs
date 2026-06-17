using System.Text.Json;
using PolicyManagement.Application.Interfaces;

namespace PolicyManagement.API.Services;

/// <summary>
/// ASP.NET Core implementation of <see cref="ICurrentUserService"/>.
/// Reads the authenticated user's identity from the current HTTP request's
/// <see cref="System.Security.Claims.ClaimsPrincipal"/> via <see cref="IHttpContextAccessor"/>.
/// Registered as <c>Scoped</c> — one instance per HTTP request.
/// </summary>
/// <remarks>
/// <para>
/// This class lives in the <c>API</c> layer because it depends on ASP.NET Core's
/// <see cref="IHttpContextAccessor"/>. Handlers that need user identity inject
/// <see cref="ICurrentUserService"/> (defined in <c>Application</c>) — they never
/// reference this class or ASP.NET Core types directly.
/// </para>
/// <para>
/// Keycloak JWT claim mappings:
/// <list type="bullet">
///   <item><description><c>sub</c> → <see cref="UserId"/></description></item>
///   <item><description><c>email</c> → <see cref="Email"/></description></item>
///   <item><description><c>realm_access.roles[]</c> → <see cref="Roles"/></description></item>
/// </list>
/// </para>
/// </remarks>
public sealed class CurrentUserService(IHttpContextAccessor httpContextAccessor) : ICurrentUserService
{
    private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

    /// <summary>
    /// Gets the current authenticated <see cref="System.Security.Claims.ClaimsPrincipal"/>
    /// from the live <see cref="IHttpContextAccessor"/> on each access, so that any
    /// mutations to <c>HttpContext.User</c> after construction are always visible.
    /// </summary>
    private System.Security.Claims.ClaimsPrincipal? User =>
        _httpContextAccessor.HttpContext?.User;

    /// <inheritdoc/>
    public string? UserId =>
        // With MapInboundClaims = true (default), JWT 'sub' is remapped to ClaimTypes.NameIdentifier.
        // Fall back to the short-form 'sub' for configurations where mapping is disabled.
        User?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
        ?? User?.FindFirst("sub")?.Value;

    /// <inheritdoc/>
    public string? Email =>
        // With MapInboundClaims = true (default), JWT 'email' is remapped to ClaimTypes.Email.
        User?.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
        ?? User?.FindFirst("email")?.Value;

    /// <inheritdoc/>
    public IReadOnlyList<string> Roles => ExtractRoles();

    /// <inheritdoc/>
    public bool IsInRole(string role) =>
        Roles.Contains(role, StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Extracts roles from the Keycloak <c>realm_access.roles</c> JSON claim.
    /// Returns an empty list when the claim is absent or cannot be parsed.
    /// </summary>
    private IReadOnlyList<string> ExtractRoles()
    {
        var user = User;
        if (user is null)
            return [];

        // Keycloak emits roles as a nested JSON object:
        // { "realm_access": { "roles": ["Policy.Write", "offline_access", ...] } }
        var realmAccessClaim = user.FindFirst("realm_access")?.Value;
        if (string.IsNullOrWhiteSpace(realmAccessClaim))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(realmAccessClaim);
            if (doc.RootElement.TryGetProperty("roles", out var rolesElement)
                && rolesElement.ValueKind == JsonValueKind.Array)
            {
                return rolesElement
                    .EnumerateArray()
                    .Select(r => r.GetString())
                    .Where(r => !string.IsNullOrWhiteSpace(r))
                    .Select(r => r!)
                    .ToList()
                    .AsReadOnly();
            }
        }
        catch (JsonException)
        {
            // Malformed claim — treat as no roles.
        }

        return [];
    }
}
