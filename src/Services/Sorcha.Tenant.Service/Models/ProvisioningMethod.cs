// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// How a user account was provisioned in the organization.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ProvisioningMethod
{
    /// <summary>
    /// Local email/password registration.
    /// </summary>
    Local,

    /// <summary>
    /// Auto-provisioned via OIDC identity provider login.
    /// </summary>
    Oidc,

    /// <summary>
    /// Provisioned via organization invitation acceptance.
    /// </summary>
    Invitation,

    /// <summary>
    /// Provisioned via social login (Google, Microsoft, Apple).
    /// </summary>
    SocialLogin
}
