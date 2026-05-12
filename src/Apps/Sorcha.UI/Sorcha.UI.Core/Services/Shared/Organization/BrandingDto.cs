// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Branding configuration DTO. Paired with <see cref="OrganizationDto"/>;
/// extracted together as part of Feature 123 because the two types form
/// a natural unit (an organization carries its branding).
/// </summary>
public record BrandingDto
{
    public string? LogoUrl { get; init; }
    public string? PrimaryColor { get; init; }
    public string? SecondaryColor { get; init; }
    public string? CompanyTagline { get; init; }
}
