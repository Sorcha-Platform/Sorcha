// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Platform-wide user account status.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlatformUserStatus
{
    /// <summary>
    /// User account is active and can authenticate.
    /// </summary>
    Active = 0,

    /// <summary>
    /// User account is suspended (cannot authenticate).
    /// </summary>
    Suspended = 1,

    /// <summary>
    /// User account is soft-deleted.
    /// </summary>
    Deleted = 2
}
