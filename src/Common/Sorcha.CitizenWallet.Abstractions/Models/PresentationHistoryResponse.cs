// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.ObjectModel;

namespace Sorcha.CitizenWallet.Abstractions.Models;

/// <summary>
/// Response body for <c>GET /api/v1/wallet/presentations</c> (Feature 114, US5 PR3).
/// The citizen's cross-device presentation history, newest-first. Reuses the wire
/// <see cref="PresentationLogEntry"/> shape (carrying disclosed claim names only —
/// never values); the vestigial <c>RegisterId</c>/<c>ActionTxId</c> fields are
/// always null for these citizen-owned records.
/// </summary>
public sealed record PresentationHistoryResponse
{
    /// <summary>Presentation history entries, newest-first.</summary>
    public IReadOnlyList<PresentationLogEntry> Entries { get; init; } =
        new ReadOnlyCollection<PresentationLogEntry>([]);
}
