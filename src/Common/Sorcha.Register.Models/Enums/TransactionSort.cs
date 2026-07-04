// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Models.Enums;

/// <summary>
/// Sort orders supported by the pushed-down register transaction read methods.
/// Each maps to a store-side sort that rides an existing index (TimeStamp / DocketNumber),
/// so paging is served from the index rather than an in-memory sort.
/// </summary>
public enum TransactionSort
{
    /// <summary>Newest first by <c>TimeStamp</c> (the default list ordering).</summary>
    TimeStampDescending = 0,

    /// <summary>Oldest first by <c>TimeStamp</c> (genesis-first scans, e.g. policy history).</summary>
    TimeStampAscending = 1,

    /// <summary>Highest docket first by <c>DocketNumber</c> (governance history/proposals).</summary>
    DocketNumberDescending = 2,

    /// <summary>Lowest docket first by <c>DocketNumber</c> (roster reconstruction — apply in order).</summary>
    DocketNumberAscending = 3,
}
