using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text.Json;

namespace PolicyManagement.API.Tests.Helpers;

/// <summary>
/// Creates signed JWT Bearer tokens for use in integration tests.
/// Tokens are signed with a symmetric HMAC-SHA256 key — no running Keycloak required.
/// The <see cref="PolicyApiFactory"/> configures the JWT Bearer middleware to accept
/// tokens signed with the same key.
/// </summary>
public static class JwtTokenFactory
{
    // 256-bit (32-byte) key — test use only, never committed to production config.
    private const string RawKey = "integration-test-signing-key-32b";

    /// <summary>
    /// The <see cref="SymmetricSecurityKey"/> shared between this factory and
    /// <see cref="PolicyApiFactory"/>'s JWT Bearer <c>PostConfigure</c>.
    /// </summary>
    public static readonly SymmetricSecurityKey SigningKey =
        new(System.Text.Encoding.UTF8.GetBytes(RawKey));

    /// <summary>
    /// Creates a valid, non-expired JWT Bearer token with the specified roles.
    /// </summary>
    /// <param name="roles">
    /// Roles to embed in the <c>realm_access.roles</c> claim (Keycloak format).
    /// Pass <see langword="null"/> or empty for an authenticated user without roles.
    /// </param>
    /// <param name="userId">The <c>sub</c> claim value. Defaults to a random GUID.</param>
    /// <returns>A compact-serialised JWT string ready to use as a Bearer token.</returns>
    public static string CreateToken(string[]? roles = null, string? userId = null)
    {
        var claims = new List<Claim>
        {
            new("sub",   userId ?? Guid.NewGuid().ToString()),
            new("email", "testuser@example.com"),
        };

        if (roles?.Length > 0)
        {
            // Add individual 'role' claims — JWT Bearer's MapInboundClaims maps
            // the short form 'role' to ClaimTypes.Role so that ClaimsPrincipal.IsInRole works.
            foreach (var role in roles)
                claims.Add(new Claim("role", role));

            // Also include Keycloak-format realm_access.roles for CurrentUserService.ExtractRoles()
            var realmAccess = JsonSerializer.Serialize(new { roles });
            claims.Add(new Claim("realm_access", realmAccess, JsonClaimValueTypes.Json));
        }

        var creds = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer:             "test-issuer",
            audience:           "test-audience",
            claims:             claims,
            expires:            DateTime.UtcNow.AddHours(1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Creates an expired JWT Bearer token (for testing 401 responses).
    /// </summary>
    public static string CreateExpiredToken()
    {
        var claims = new List<Claim> { new("sub", Guid.NewGuid().ToString()) };

        var creds = new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer:             "test-issuer",
            audience:           "test-audience",
            claims:             claims,
            notBefore:          DateTime.UtcNow.AddHours(-2),
            expires:            DateTime.UtcNow.AddHours(-1),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
