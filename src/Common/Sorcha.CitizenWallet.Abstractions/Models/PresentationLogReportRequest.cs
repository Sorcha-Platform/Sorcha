// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.ObjectModel;

namespace Sorcha.CitizenWallet.Abstractions.Models;

/// <summary>
/// Request body for <c>POST /api/v1/wallet/presentations/log</c>. Wallets may batch
/// any number of pending entries; the server dedupes on each entry's <c>Id</c>.
/// </summary>
public sealed record PresentationLogReportRequest
{
    /// <summary>Pending log entries to report.</summary>
    public IReadOnlyList<PresentationLogEntry> Entries { get; init; } =
        new ReadOnlyCollection<PresentationLogEntry>([]);
}
