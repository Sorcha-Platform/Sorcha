// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Verification status for a custom domain CNAME mapping.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CustomDomainStatus
{
    /// <summary>
    /// No custom domain configured.
    /// </summary>
    None,

    /// <summary>
    /// Custom domain configured, awaiting CNAME verification.
    /// </summary>
    Pending,

    /// <summary>
    /// CNAME record verified — custom domain is active.
    /// </summary>
    Verified,

    /// <summary>
    /// CNAME verification failed or previously verified record removed.
    /// </summary>
    Failed
}
