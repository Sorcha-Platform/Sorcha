// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.CitizenWallet.Abstractions.Models;

/// <summary>
/// Wire shape of the citizen's pending-application notice as returned by
/// <c>GET /api/v1/wallet/pending-applications</c> (Feature 124). The envelope
/// is always present; a null <see cref="Notice"/> means no application is in
/// flight. Carries only a human-readable label — no credential content.
/// </summary>
public sealed record PendingApplicationResponse
{
    /// <summary>The active notice, or null when no application is pending.</summary>
    public PendingApplicationNoticeDto? Notice { get; init; }
}

/// <summary>
/// The citizen's in-flight application notice — the metadata that drives the
/// wallet's "watch your wallet" waiting state until the credential lands.
/// </summary>
public sealed record PendingApplicationNoticeDto
{
    /// <summary>Human-readable application label, e.g. "Assured Identity".</summary>
    public string Label { get; init; } = string.Empty;

    /// <summary>UTC time the notice was set.</summary>
    public DateTimeOffset SetAt { get; init; }
}
