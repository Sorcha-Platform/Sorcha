// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Organization type that determines self-registration and public access behavior.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum OrgType
{
    /// <summary>
    /// Private organization — users join via invitation or admin provisioning only.
    /// </summary>
    Standard,

    /// <summary>
    /// Public organization — supports self-registration and social login.
    /// </summary>
    Public
}
