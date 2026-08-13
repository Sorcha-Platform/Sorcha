// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Data;

/// <summary>
/// Where the seeded admin user's password came from. Never used to decide WHETHER to seed —
/// only to log what happened. Unlike <see cref="ServicePrincipalSecretSource"/>, the password
/// value itself is deliberately human-known in the dev-default case (an operator has to be able
/// to log in with it), so it is safe to log alongside the source (issue #1409).
/// </summary>
public enum AdminPasswordSource
{
    /// <summary>Read from <c>Seed:AdminPassword</c> — a per-deploy password set by the operator.
    /// Valid in every environment.</summary>
    Configured,

    /// <summary>No configured password — the committed <see cref="DatabaseInitializer.DefaultAdminPassword"/>
    /// convenience literal was used. Only reachable outside Production/Staging.
    /// Production/Staging refuse instead (see <see cref="AdminPasswordResolver.Resolve"/>).</summary>
    DevDefault
}

/// <summary>
/// Resolves the password to seed for the initial admin <c>PlatformUser</c>, given the per-deploy
/// configured value (if any), the committed dev-literal convenience default, and the running
/// environment. Extracted from <see cref="DatabaseInitializer"/> so the selection logic is
/// unit-testable without a database, mirroring <see cref="ServicePrincipalSecretResolver"/>
/// (issue #1409).
/// </summary>
public static class AdminPasswordResolver
{
    /// <summary>
    /// Resolution order:
    /// <list type="number">
    /// <item>A configured password (<c>Seed:AdminPassword</c>) always wins. Valid in every
    /// environment.</item>
    /// <item>Production or Staging with no configured password FAILS CLOSED. Seeding the
    /// well-known <c>Dev_Pass_2025!</c> literal on an internet-reachable node is the exact
    /// known-credential bug this resolver exists to close — refusing loudly at startup is
    /// strictly better than a node that starts with a publicly known admin password.</item>
    /// <item>Any other environment (Development, Testing, or unset) with no configured password
    /// uses the committed <see cref="DatabaseInitializer.DefaultAdminPassword"/> literal — the
    /// unchanged local dev/test convenience.</item>
    /// </list>
    /// </summary>
    /// <param name="configuredValue">The value read from <c>Seed:AdminPassword</c>, or
    /// null/empty/whitespace if absent.</param>
    /// <param name="environment">The running <c>ASPNETCORE_ENVIRONMENT</c> value (may be null).</param>
    /// <returns>The resolved password plus where it came from.</returns>
    /// <exception cref="InvalidOperationException">Thrown when running as Production or Staging
    /// with no configured password.</exception>
    public static (string Password, AdminPasswordSource Source) Resolve(
        string? configuredValue,
        string? environment)
    {
        if (!string.IsNullOrWhiteSpace(configuredValue))
        {
            return (configuredValue, AdminPasswordSource.Configured);
        }

        if (IsEnvironment(environment, "Production") || IsEnvironment(environment, "Staging"))
        {
            throw new InvalidOperationException(
                "No admin password configured. Set Seed:AdminPassword (env Seed__AdminPassword). " +
                "Refusing to seed the well-known default admin password outside Development.");
        }

        return (DatabaseInitializer.DefaultAdminPassword, AdminPasswordSource.DevDefault);
    }

    private static bool IsEnvironment(string? environment, string expected) =>
        string.Equals(environment, expected, StringComparison.OrdinalIgnoreCase);
}
