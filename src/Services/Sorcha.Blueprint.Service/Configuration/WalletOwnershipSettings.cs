// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Service.Configuration;

/// <summary>
/// Configures how <c>ActionExecutionService.ValidateWalletOwnershipAsync</c> reacts
/// when the participant-service-backed ownership check cannot complete (network
/// failure, missing participant profile, wallet not yet linked).
/// </summary>
/// <remarks>
/// Multi-node audit CRITICAL #3 fix. Default is <see cref="WalletOwnershipEnforcementMode.FailClosed"/>;
/// dev environments can opt out by setting <c>Security:WalletOwnership:EnforcementMode</c>
/// to <c>FailOpen</c> in <c>appsettings.Development.json</c>. The per-branch flags
/// (<see cref="AllowMissingParticipant"/>, <see cref="AllowUnlinkedWallet"/>)
/// let operators ratchet enforcement incrementally — e.g. fail-open on missing
/// participant during a participant-system rollout, fail-closed everywhere else.
/// </remarks>
public sealed class WalletOwnershipSettings
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Security:WalletOwnership";

    /// <summary>
    /// Overall enforcement mode. Applied to the two failure branches that don't
    /// have dedicated per-branch flags (Participant Service throws,
    /// <c>GetLinkedWallets</c> throws) — cross-node network blips MUST NOT
    /// silently degrade wallet ownership to "any authenticated user".
    /// </summary>
    public WalletOwnershipEnforcementMode EnforcementMode { get; set; }
        = WalletOwnershipEnforcementMode.FailClosed;

    /// <summary>
    /// When true, requests proceed even when no participant profile is found for
    /// the authenticated user + org. Useful during participant-system rollouts
    /// where most users are linked but a backfill is still in progress. Defaults
    /// to <c>false</c> in production (fail-closed); set to <c>true</c> in dev
    /// configs to keep historical flows working.
    /// </summary>
    public bool AllowMissingParticipant { get; set; }

    /// <summary>
    /// When true, requests proceed even when the sender wallet is not in the
    /// participant's linked-wallet list. Useful for environments where wallet
    /// linkage isn't fully configured yet. Defaults to <c>false</c>.
    /// </summary>
    public bool AllowUnlinkedWallet { get; set; }
}

/// <summary>
/// Enforcement mode for participant-service-backed wallet ownership validation.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum WalletOwnershipEnforcementMode
{
    /// <summary>
    /// Fail closed — Participant Service unavailability or unexpected exceptions
    /// reject the request with <see cref="UnauthorizedAccessException"/>. This is
    /// the safe default for multi-node deployments where cross-node network blips
    /// must not silently downgrade authorization.
    /// </summary>
    FailClosed = 0,

    /// <summary>
    /// Fail open — log a warning and allow the authenticated user through.
    /// Pre-Feature-117 behaviour. Acceptable in single-node dev environments
    /// where the trade-off favours availability over strict authorisation.
    /// </summary>
    FailOpen = 1,
}
