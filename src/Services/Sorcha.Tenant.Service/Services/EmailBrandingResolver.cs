// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Options;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <inheritdoc />
public sealed class EmailBrandingResolver : IEmailBrandingResolver
{
    private readonly EmailSettings _settings;

    /// <summary>
    /// Initializes a new instance of <see cref="EmailBrandingResolver"/>.
    /// </summary>
    public EmailBrandingResolver(IOptions<EmailSettings> settings)
    {
        _settings = settings.Value;
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

        return new EmailBranding(
            SenderName: organization.Name,
            LogoUrl: organization.Branding?.LogoUrl ?? _settings.LogoUrl,
            PrimaryColor: organization.Branding?.PrimaryColor ?? _settings.PrimaryColor,
            // Org tagline has no Sorcha fallback — if the org has nothing to say, say nothing.
            Tagline: organization.Branding?.CompanyTagline,
            // Reply-to is platform-level, not per-org.
            ReplyTo: _settings.ReplyTo);
    }
}
