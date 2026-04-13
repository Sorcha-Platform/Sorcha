// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Models;

/// <summary>
/// Platform-curated sector definition for schema classification.
/// </summary>
public sealed record SchemaSector
{
    /// <summary>
    /// Lowercase identifier (e.g., "finance", "healthcare").
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Human-readable display name.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Description of what this sector covers.
    /// </summary>
    public required string Description { get; init; }

    /// <summary>
    /// MudBlazor icon identifier.
    /// </summary>
    public required string Icon { get; init; }

    /// <summary>
    /// All platform-curated sectors.
    /// </summary>
    public static IReadOnlyList<SchemaSector> All { get; } =
    [
        new SchemaSector
        {
            Id = "finance",
            DisplayName = "Finance & Banking",
            Description = "Financial messaging, payment processing, and banking standards",
            Icon = "Icons.Material.Filled.AccountBalance"
        },
        new SchemaSector
        {
            Id = "healthcare",
            DisplayName = "Healthcare",
            Description = "Clinical data, patient records, and health information exchange",
            Icon = "Icons.Material.Filled.LocalHospital"
        },
        new SchemaSector
        {
            Id = "construction",
            DisplayName = "Construction & Planning",
            Description = "Building information models, permits, and infrastructure",
            Icon = "Icons.Material.Filled.Construction"
        },
        new SchemaSector
        {
            Id = "government",
            DisplayName = "Government & Public Sector",
            Description = "Public records, regulatory compliance, and civic data",
            Icon = "Icons.Material.Filled.AccountBalanceWallet"
        },
        new SchemaSector
        {
            Id = "identity",
            DisplayName = "Identity & Credentials",
            Description = "Digital identity, verifiable credentials, and authentication",
            Icon = "Icons.Material.Filled.Badge"
        },
        new SchemaSector
        {
            Id = "commerce",
            DisplayName = "Commerce & Trade",
            Description = "Orders, invoices, shipping, and supply chain",
            Icon = "Icons.Material.Filled.ShoppingCart"
        },
        new SchemaSector
        {
            Id = "technology",
            DisplayName = "Technology & DevOps",
            Description = "Configuration files, CI/CD, and developer tools",
            Icon = "Icons.Material.Filled.Code"
        },
        new SchemaSector
        {
            Id = "sustainability",
            DisplayName = "Sustainability & DPP",
            Description = "Digital product passports, circular economy, carbon footprint, and environmental compliance",
            Icon = "Icons.Material.Filled.Recycling"
        },
        new SchemaSector
        {
            Id = "general",
            DisplayName = "General Purpose",
            Description = "Cross-domain schemas for common data structures",
            Icon = "Icons.Material.Filled.Category"
        },
        new SchemaSector
        {
            Id = "core",
            DisplayName = "Sorcha Core Primitives",
            Description = "Platform-managed reusable identity primitives (name, DOB, email, postal address) referenced by blueprints via JSON Schema $ref",
            Icon = "Icons.Material.Filled.Layers"
        }
    ];

    /// <summary>
    /// All valid sector IDs.
    /// </summary>
    public static IReadOnlySet<string> ValidIds { get; } =
        All.Select(s => s.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Checks whether a sector ID is valid.
    /// </summary>
    public static bool IsValid(string sectorId) =>
        ValidIds.Contains(sectorId);
}
