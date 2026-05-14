// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Service.Models;

/// <summary>
/// Request body for setting (or replacing) the citizen's pending-application
/// notice — the small piece of metadata that drives the wallet's waiting state
/// when an application is in flight (Feature 124). Carries only a human-readable
/// label; no credential content.
/// </summary>
public sealed class SetPendingApplicationRequest
{
    /// <summary>Human-readable application label, e.g. "Assured Identity".</summary>
    public string Label { get; init; } = string.Empty;
}

/// <summary>
/// Envelope wrapping the optional pending-application notice. The wrapping
/// shape lets the wallet deserialise consistently whether or not a notice is
/// currently set — the field is always present, the value distinguishes
/// presence.
/// </summary>
public sealed class PendingApplicationEnvelope
{
    /// <summary>Active notice, or null when no application is pending.</summary>
    public PendingApplicationNotice? Notice { get; init; }
}

/// <summary>
/// Server-side state representing a citizen's in-flight application whose
/// credential will eventually land in the wallet. Lives in distributed cache
/// only — never persisted to the relational store.
/// </summary>
/// <param name="Label">Human-readable application label (1..80 chars, plain text).</param>
/// <param name="SetAt">UTC time the notice was set. Informational only.</param>
public sealed record PendingApplicationNotice(
    string Label,
    DateTimeOffset SetAt);
