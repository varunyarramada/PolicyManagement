namespace PolicyManagement.Application.Constants;

/// <summary>
/// Well-known role name constants used for authorisation checks via
/// <see cref="Interfaces.ICurrentUserService.IsInRole"/>.
/// Defined here so all Application layer code references the constant —
/// never a magic string literal.
/// </summary>
public static class Roles
{
    /// <summary>
    /// Grants write access to policy mutation operations.
    /// Required to call <c>PATCH /api/v1/policies/flag</c>.
    /// Enforced via <c>[Authorize(Policy = "PolicyWrite")]</c> in the API layer.
    /// </summary>
    public const string PolicyWrite = "Policy.Write";

    /// <summary>
    /// Implicit read access granted to any authenticated user with a valid JWT.
    /// No explicit role check is required; a valid token is sufficient.
    /// </summary>
    public const string PolicyRead = "Policy.Read";
}
