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
    /// reuse. Idempotent per organisation. A new register is returned only once its genesis has
    /// SEALED: until then it has no roster, and the validator refuses the rehearsal's blueprint
    /// publication into it.
    /// </summary>
    /// <param name="organizationId">The owning organisation / tenant id.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The sandbox register id for the organisation.</returns>
    /// <exception cref="SandboxNotReadyException">
    /// The register was created but its genesis did not seal in time. A retry reuses the same
    /// register rather than creating another.
    /// </exception>
    Task<string> GetOrCreateSandboxRegisterAsync(
        string organizationId,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The organisation's sandbox register exists but its genesis has not sealed yet, so nothing can be
/// published into it. Transient: the caller should retry shortly.
/// </summary>
public sealed class SandboxNotReadyException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="registerId">The sandbox register that is not ready.</param>
    /// <param name="waited">How long the provider waited for its genesis to seal.</param>
    public SandboxNotReadyException(string registerId, TimeSpan waited)
        : base($"The rehearsal sandbox register {registerId} was created, but its genesis did not seal "
               + $"within {waited.TotalSeconds:0}s, so a blueprint cannot be published into it yet. This "
               + "is a sealing delay, not a problem with the blueprint: start the rehearsal again shortly, "
               + "and the same sandbox register will be reused.")
    {
        RegisterId = registerId;
    }

    /// <summary>The sandbox register that is not ready.</summary>
    public string RegisterId { get; }
}
