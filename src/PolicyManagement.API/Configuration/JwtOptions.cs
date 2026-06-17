using System.ComponentModel.DataAnnotations;

namespace PolicyManagement.API.Configuration;

/// <summary>
/// Strongly-typed configuration for JWT Bearer authentication.
/// Bound from the <c>"Jwt"</c> section of application configuration.
/// Registered with <c>ValidateOnStart()</c> so a missing or invalid Keycloak
/// configuration fails at application startup rather than on the first request.
/// </summary>
/// <remarks>
/// Supply values via environment variables only — never commit JWT configuration to
/// source control:
/// <list type="bullet">
///   <item><description><c>Jwt__Authority</c> — Keycloak realm URL, e.g. <c>http://keycloak:8080/realms/policy-management</c></description></item>
///   <item><description><c>Jwt__Audience</c> — Keycloak client ID, e.g. <c>policy-management-bff</c></description></item>
///   <item><description><c>Jwt__RequireHttpsMetadata</c> — <c>false</c> for local Docker; <c>true</c> in production</description></item>
/// </list>
/// See ADR-007 for the full JWT Bearer authentication decision.
/// </remarks>
public sealed class JwtOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Jwt";

    /// <summary>
    /// Gets or initialises the Keycloak realm URL used as the JWT token issuer
    /// (the <c>iss</c> claim). Example: <c>http://keycloak:8080/realms/policy-management</c>.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Jwt:Authority is required.")]
    public string Authority { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initialises the expected JWT audience — matches the Keycloak client ID
    /// (the <c>aud</c> claim). Example: <c>policy-management-bff</c>.
    /// </summary>
    [Required(AllowEmptyStrings = false, ErrorMessage = "Jwt:Audience is required.")]
    public string Audience { get; init; } = string.Empty;

    /// <summary>
    /// Gets or initialises whether HTTPS metadata is required when fetching
    /// the Keycloak OIDC discovery document.
    /// Set to <c>false</c> for local development/Docker; <c>true</c> in production.
    /// </summary>
    public bool RequireHttpsMetadata { get; init; } = true;
}
