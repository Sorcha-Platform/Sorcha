// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Models;

/// <summary>
/// Health status of a register as observed during recovery and periodic refresh.
/// </summary>
public enum RegisterHealthStatus
{
    /// <summary>Register has not been checked yet.</summary>
    Unknown,

    /// <summary>Register is reachable and responding to queries.</summary>
    Online,

    /// <summary>Register is unreachable or queries are failing.</summary>
    Offline,

    /// <summary>Register is reachable but responding slowly or partially failing.</summary>
    Degraded
}
