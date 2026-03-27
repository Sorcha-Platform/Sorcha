// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Models;

/// <summary>
/// Configuration for the blueprint recovery and register refresh service.
/// Bind from appsettings section "Recovery".
/// </summary>
public class RecoveryOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "Recovery";

    /// <summary>Interval in seconds between periodic register refresh checks. Default: 60.</summary>
    public int RefreshIntervalSeconds { get; set; } = 60;

    /// <summary>Maximum time in seconds to wait for initial recovery to complete. Default: 30.</summary>
    public int StartupTimeoutSeconds { get; set; } = 30;

    /// <summary>Maximum retry attempts for unreachable registers before giving up until next refresh. Default: 3.</summary>
    public int MaxRetryAttempts { get; set; } = 3;
}
