// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using FluentAssertions;
using Sorcha.Tenant.Service.Trust;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Trust;

/// <summary>
/// Feature 181 US3 (T036 / FR-016) — trusted-list freshness computation. Fresh strictly before the
/// effective next-update; the boundary instant is Stale; a null next-update falls back to the
/// list-issue + default-window. Boundary-deterministic (explicit instants, no wall clock).
/// </summary>
public sealed class TrustListFreshnessTests
{
    private static readonly DateTimeOffset Issue = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

    [Fact]
    public void Compute_BeforeNextUpdate_IsFresh()
    {
        var nextUpdate = DateTimeOffset.Parse("2026-06-01T00:00:00Z");
        var now = nextUpdate.AddSeconds(-1);

        TrustListFreshness.Compute(nextUpdate, Issue, now).Should().Be(TrustListFreshnessState.Fresh);
    }

    [Fact]
    public void Compute_AtNextUpdateBoundary_IsStale()
    {
        var nextUpdate = DateTimeOffset.Parse("2026-06-01T00:00:00Z");

        // now == nextUpdate → Stale (Fresh is strictly-before).
        TrustListFreshness.Compute(nextUpdate, Issue, nextUpdate).Should().Be(TrustListFreshnessState.Stale);
    }

    [Fact]
    public void Compute_PastNextUpdate_IsStale()
    {
        var nextUpdate = DateTimeOffset.Parse("2026-06-01T00:00:00Z");

        TrustListFreshness.Compute(nextUpdate, Issue, nextUpdate.AddDays(1)).Should().Be(TrustListFreshnessState.Stale);
    }

    [Fact]
    public void Compute_NullNextUpdate_WithinDefaultWindow_IsFresh()
    {
        // No next-update → effective window is Issue + 90d default.
        var now = Issue.AddDays(89);

        TrustListFreshness.Compute(nextUpdate: null, Issue, now).Should().Be(TrustListFreshnessState.Fresh);
    }

    [Fact]
    public void Compute_NullNextUpdate_PastDefaultWindow_IsStale()
    {
        var now = Issue.Add(TrustListFreshness.DefaultStaleAfter).AddSeconds(1);

        TrustListFreshness.Compute(nextUpdate: null, Issue, now).Should().Be(TrustListFreshnessState.Stale);
    }

    [Fact]
    public void Compute_NullNextUpdate_CustomWindow_HonoursOverride()
    {
        var now = Issue.AddDays(31);

        TrustListFreshness.Compute(nextUpdate: null, Issue, now, TimeSpan.FromDays(30))
            .Should().Be(TrustListFreshnessState.Stale);
    }
}
