// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Trust;

/// <summary>Feature 181 US3 — computed freshness of a trusted-list snapshot (never stored — FR-016).</summary>
public enum TrustListFreshnessState
{
    /// <summary>The snapshot is within its next-update window.</summary>
    Fresh = 0,

    /// <summary>The snapshot is past its next-update (or the default staleness window when the list carried no next-update).</summary>
    Stale = 1,
}

/// <summary>Computes trusted-list snapshot freshness from the list's next-update date (data-model §2 / FR-016).</summary>
public static class TrustListFreshness
{
    /// <summary>Default staleness window applied when a list declares no <c>NextUpdate</c> (90 days).</summary>
    public static readonly TimeSpan DefaultStaleAfter = TimeSpan.FromDays(90);

    /// <summary>
    /// Fresh while <paramref name="now"/> precedes the effective next-update: the list's own
    /// <paramref name="nextUpdate"/>, or <paramref name="listIssue"/> + <paramref name="defaultStaleAfter"/>
    /// when the list carried none.
    /// </summary>
    public static TrustListFreshnessState Compute(
        DateTimeOffset? nextUpdate,
        DateTimeOffset listIssue,
        DateTimeOffset now,
        TimeSpan? defaultStaleAfter = null)
    {
        var effectiveNextUpdate = nextUpdate ?? listIssue.Add(defaultStaleAfter ?? DefaultStaleAfter);
        return now < effectiveNextUpdate ? TrustListFreshnessState.Fresh : TrustListFreshnessState.Stale;
    }
}
