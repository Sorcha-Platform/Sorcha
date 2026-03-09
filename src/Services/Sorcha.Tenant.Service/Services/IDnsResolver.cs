// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Abstraction for DNS resolution operations.
/// Enables testing without live DNS lookups.
/// </summary>
public interface IDnsResolver
{
    /// <summary>
    /// Verifies that a CNAME record for the given domain points to the expected target.
    /// </summary>
    /// <param name="domain">The domain to verify.</param>
    /// <param name="expectedCnameTarget">The expected CNAME target (e.g., "acme.sorcha.io").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the CNAME record matches; false otherwise.</returns>
    Task<bool> VerifyCnameAsync(string domain, string expectedCnameTarget, CancellationToken cancellationToken = default);
}
