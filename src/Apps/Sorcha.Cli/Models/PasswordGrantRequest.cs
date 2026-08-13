// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
namespace Sorcha.Cli.Models;

/// <summary>
/// Input to the OAuth2 <b>password grant</b> — sent FORM-ENCODED to the token endpoint as
/// <c>grant_type=password</c> with <c>username</c> / <c>password</c> / <c>client_id</c> / <c>scope</c>.
///
/// <para>Issue #1160 — this was called <c>LoginRequest</c>, which collided by name with the Tenant
/// Service's <c>LoginRequest</c> (the JSON body of <c>POST /api/auth/login</c>, carrying
/// <c>email</c> / <c>password</c> / <c>tier</c> / <c>returnTo</c>). The two never travel the same
/// request and share no field but <c>password</c>, so the collision was pure noise — it forced a
/// standing justification in <c>CliWireContractTests.NotAWireContract</c>, whose own text named
/// renaming this type as the real fix. The name now says which grant it is.</para>
///
/// <para>Issue #1402 — the CLI's <c>auth login</c> user flow no longer uses this type. It targeted
/// the wrong endpoint for user login (the service-principal token endpoint,
/// <c>/api/service-auth/token</c>, not the intended user-facing route) and now goes through
/// <see cref="UserLoginRequest"/> against <c>POST /api/auth/login</c> instead, which also handles
/// multi-org account selection that this form-encoded grant cannot express. This type is left in
/// place — unused by any CLI command today — because the OAuth2 password grant it models is still a
/// live, documented server capability (<c>ServiceAuthEndpoints.HandlePasswordGrant</c>); it is not
/// dead in the sense of describing something the server no longer does, only in the sense of having
/// no current CLI call site.</para>
/// </summary>
public class PasswordGrantRequest
{
    /// <summary>
    /// Username or email.
    /// </summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>
    /// Password.
    /// </summary>
    public string Password { get; set; } = string.Empty;

    /// <summary>
    /// OAuth2 client ID (optional, uses profile default if not specified).
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// OAuth2 scope (optional).
    /// </summary>
    public string? Scope { get; set; }
}

/// <summary>
/// Service principal (client credentials) login request.
/// </summary>
public class ServicePrincipalLoginRequest
{
    /// <summary>
    /// Client ID (service principal ID).
    /// </summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>
    /// Client secret.
    /// </summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>
    /// OAuth2 scope (optional).
    /// </summary>
    public string? Scope { get; set; }
}
