// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Services.Interfaces;

/// <summary>
/// Feature 142 (T026 / D1) — lazily provisions and reuses ONE devMode sandbox register per
/// organisation for full rehearsals. The sandbox register persists across rehearsals (a "reset"
/// discards the rehearsal instance + ephemeral identities, never the register). Sandbox registers
/// carry <c>Metadata["sandbox"] = "true"</c> so they are excluded from the Go-live picker and
/// normal listings (see <c>Sorcha.Register.Models.Register.Sandbox</c>, T009).
/// </summary>
public interface ISandboxRegisterProvider
{
    /// <summary>
    /// Returns the organisation's sandbox register id, creating it on first use and caching it for
    /// reuse. Idempotent per organisation.
    /// </summary>
    /// <param name="organizationId">The owning organisation / tenant id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sandbox register id for the organisation.</returns>
    Task<string> GetOrCreateSandboxRegisterAsync(
        string organizationId,
        CancellationToken cancellationToken = default);
}
