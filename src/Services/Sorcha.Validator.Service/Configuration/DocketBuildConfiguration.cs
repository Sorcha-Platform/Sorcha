// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Validator.Service.Configuration;

/// <summary>
/// Configuration for docket building triggers and constraints
/// </summary>
public class DocketBuildConfiguration
{
    /// <summary>
    /// Time threshold for building a docket (hybrid trigger)
    /// </summary>
    public TimeSpan TimeThreshold { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// Size threshold (transaction count) for building a docket (hybrid trigger)
    /// </summary>
    public int SizeThreshold { get; set; } = 50;

    /// <summary>
    /// Maximum transactions per docket
    /// </summary>
    public int MaxTransactionsPerDocket { get; set; } = 100;

    /// <summary>
    /// Whether to allow dockets with zero transactions
    /// </summary>
    public bool AllowEmptyDockets { get; set; } = false;

    /// <summary>
    /// Lease duration when claiming transactions from the verified queue.
    /// If the build crashes or this process dies before ConfirmAsync, the
    /// lease auto-releases on the next ClaimAsync after this many seconds.
    /// Sized to docket-build worst-case plus margin; tune up if validator
    /// builds take longer than 60s in your deployment. Range 1–3600.
    /// </summary>
    [System.ComponentModel.DataAnnotations.Range(1, 3600,
        ErrorMessage = "LeaseDurationSeconds must be between 1 and 3600 seconds.")]
    public int LeaseDurationSeconds { get; set; } = 60;
}
