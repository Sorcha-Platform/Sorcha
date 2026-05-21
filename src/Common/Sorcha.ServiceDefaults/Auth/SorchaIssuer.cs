// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Hosting;

namespace Sorcha.ServiceDefaults.Auth;

/// <summary>
/// Resolves the JWT issuer (<c>iss</c>) for an installation (spec 136). There is NO shared
/// default — a misconfigured installation fails closed rather than sharing an identity (e.g. a
/// domain the platform may not own). Both token issuance (Tenant Service) and validation (every
/// service) MUST resolve through here so the minted and validated issuer always agree.
/// <para>Resolution order: explicit issuer → <c>urn:sorcha:{installationName}</c> →
/// (Development) <c>urn:sorcha:dev-local</c> → otherwise throw.</para>
/// </summary>
public static class SorchaIssuer
{
    /// <summary>The Development fallback issuer (clearly local, never a real/owned domain).</summary>
    public const string DevLocalIssuer = "urn:sorcha:dev-local";

    /// <summary>
    /// Resolves the issuer string. When neither an explicit issuer nor an installation name is
    /// configured, returns <see cref="DevLocalIssuer"/> if <paramref name="allowDevLocalFallback"/>
    /// is true (non-production environments), otherwise throws <see cref="InvalidOperationException"/>
    /// (fail-closed in Production/Staging — no shared default).
    /// </summary>
    /// <param name="explicitIssuer">An explicitly configured issuer, if any.</param>
    /// <param name="installationName">The installation name, if any (issuer derives as <c>urn:sorcha:{name}</c>).</param>
    /// <param name="allowDevLocalFallback">
    /// True for non-production environments (Development, Testing, etc.) where a clearly-local issuer
    /// is acceptable; false for Production/Staging, which fail closed.
    /// </param>
    public static string Resolve(string? explicitIssuer, string? installationName, bool allowDevLocalFallback)
    {
        if (!string.IsNullOrWhiteSpace(explicitIssuer))
        {
            return explicitIssuer.Trim();
        }

        if (!string.IsNullOrWhiteSpace(installationName))
        {
            return $"urn:sorcha:{SorchaAudiences.Normalize(installationName)}";
        }

        if (allowDevLocalFallback)
        {
            return DevLocalIssuer;
        }

        throw new InvalidOperationException(
            "JWT Issuer is required. Set JwtSettings:Issuer explicitly, or set JwtSettings:InstallationName " +
            "(the issuer derives as urn:sorcha:{InstallationName}). Refusing to start with a shared default " +
            "issuer in Production/Staging — see spec 136 (issuer hardening).");
    }

    /// <summary>
    /// True when an installation may fall back to <see cref="DevLocalIssuer"/> — any environment that
    /// is not Production or Staging. Production/Staging fail closed.
    /// </summary>
    public static bool AllowsDevLocalFallback(Microsoft.Extensions.Hosting.IHostEnvironment environment) =>
        environment is not null
        && !environment.IsProduction()
        && !environment.IsEnvironment("Staging");
}
