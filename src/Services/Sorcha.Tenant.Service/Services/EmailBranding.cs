// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Sender-identity surface applied to a single email render. Built by
/// <see cref="IEmailBrandingResolver"/> from either Sorcha platform defaults
/// or an organisation's branding record (per-field fallback applies).
/// </summary>
/// <param name="SenderName">Display name of the sender — "Sorcha" or an organisation name.</param>
/// <param name="LogoUrl">Absolute https URL of a logo image; null renders the sender name as text.</param>
/// <param name="PrimaryColor">Hex colour applied to the action button and footer links.</param>
/// <param name="Tagline">Optional short tagline rendered above the reply-to footer line.</param>
/// <param name="ReplyTo">Reply-to email address shown in the footer.</param>
public sealed record EmailBranding(
    string SenderName,
    string? LogoUrl,
    string PrimaryColor,
    string? Tagline,
    string ReplyTo);
