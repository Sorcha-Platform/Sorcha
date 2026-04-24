// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <inheritdoc />
public sealed partial class EmailBrandingResolver : IEmailBrandingResolver
{
    // Hex colour literal like "#2563eb" or "#F06" — nothing else belongs in a CSS
    // attribute value generated from org-controlled input. Validated at resolve-time
    // so an attacker-controlled value can't smuggle a CSS expression or extra property.
    [GeneratedRegex(@"^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$")]
    private static partial Regex HexColorPattern();

    private readonly EmailSettings _settings;
    private readonly ILogger<EmailBrandingResolver> _logger;

    /// <summary>
    /// Initializes a new instance of <see cref="EmailBrandingResolver"/>.
    /// </summary>
    public EmailBrandingResolver(
        IOptions<EmailSettings> settings,
        ILogger<EmailBrandingResolver> logger)
    {
        _settings = settings.Value;
        _logger = logger;
    }

    /// <inheritdoc />
    public EmailBranding GetDefault() => new(
        SenderName: _settings.FromName,
        LogoUrl: _settings.LogoUrl,
        PrimaryColor: _settings.PrimaryColor,
        Tagline: _settings.Tagline,
        ReplyTo: _settings.ReplyTo);

    /// <inheritdoc />
    public EmailBranding GetForOrganization(Organization organization)
    {
        ArgumentNullException.ThrowIfNull(organization);

        // Validate org-controlled fields before they flow into a template. Malicious
        // or malformed values fall back per-field to Sorcha defaults — we'd rather
        // render a plain Sorcha frame than leak an attacker's URL/colour into the
        // HTML or the CSS of every invitation the org sends.
        return new EmailBranding(
            SenderName: organization.Name,
            LogoUrl: SafeLogoUrl(organization) ?? _settings.LogoUrl,
            PrimaryColor: SafePrimaryColor(organization) ?? _settings.PrimaryColor,
            // Org tagline has no Sorcha fallback — if the org has nothing to say, say nothing.
            Tagline: organization.Branding?.CompanyTagline,
            // Reply-to is platform-level, not per-org.
            ReplyTo: _settings.ReplyTo);
    }

    /// <summary>
    /// Returns the organisation's logo URL only if it's an absolute https URL.
    /// Anything else (non-https, malformed, or null) returns null so the caller
    /// falls back to the Sorcha default.
    /// </summary>
    private string? SafeLogoUrl(Organization organization)
    {
        var candidate = organization.Branding?.LogoUrl;
        if (string.IsNullOrWhiteSpace(candidate)) return null;

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeHttps)
        {
            _logger.LogWarning(
                "Rejecting non-https or malformed logo URL for org {OrgId}; falling back to Sorcha default",
                organization.Id);
            return null;
        }
        return parsed.AbsoluteUri;
    }

    /// <summary>
    /// Returns the organisation's primary colour only if it's a valid 3- or 6-digit
    /// hex literal (e.g. <c>#FF5722</c>). Anything else returns null.
    /// </summary>
    private string? SafePrimaryColor(Organization organization)
    {
        var candidate = organization.Branding?.PrimaryColor;
        if (string.IsNullOrWhiteSpace(candidate)) return null;

        if (!HexColorPattern().IsMatch(candidate))
        {
            _logger.LogWarning(
                "Rejecting non-hex primary colour '{Colour}' for org {OrgId}; falling back to Sorcha default",
                candidate, organization.Id);
            return null;
        }
        return candidate;
    }
}
