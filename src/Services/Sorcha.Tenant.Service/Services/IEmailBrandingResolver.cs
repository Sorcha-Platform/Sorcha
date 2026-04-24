// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Resolves the <see cref="EmailBranding"/> surface applied to a single render.
/// Sorcha-default branding comes from <c>EmailSettings</c>. Per-organisation
/// overrides apply only to invitation and invited-welcome emails — other flows
/// always use the Sorcha default.
/// </summary>
public interface IEmailBrandingResolver
{
    /// <summary>Returns the Sorcha platform default branding.</summary>
    EmailBranding GetDefault();

    /// <summary>
    /// Returns branding for a message whose inviting or joining organisation is
    /// <paramref name="organization"/>. Per-field fallback to Sorcha defaults applies:
    /// the org's name always wins; logo and primary colour fall back per-field.
    /// </summary>
    EmailBranding GetForOrganization(Organization organization);
}
