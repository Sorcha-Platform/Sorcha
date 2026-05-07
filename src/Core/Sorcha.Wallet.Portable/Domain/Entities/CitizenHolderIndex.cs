// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;

namespace Sorcha.Wallet.Core.Domain.Entities;

/// <summary>
/// Reverse map from a citizen's holder wallet address to the owning PlatformUser
/// (Feature 114, US4).
/// </summary>
/// <remarks>
/// Populated at first holder-key derivation in <c>HolderKeyService.GetOrCreateAsync</c>
/// and consumed by <c>IHolderAddressLookup</c> so the citizen-inbox projector can
/// resolve <c>WalletAddress → PlatformUserId</c> when an inbound credential is
/// detected. Without this index, <c>InboundCredentialDetector</c> has no way to
/// tell whether a recipient address belongs to a citizen-PWA holder versus an
/// org-credential wallet.
/// </remarks>
public class CitizenHolderIndex
{
    /// <summary>
    /// Citizen holder wallet address (slot-108 derivation). Primary key — one
    /// holder address maps to exactly one PlatformUser.
    /// </summary>
    /// <remarks>
    /// <see cref="KeyAttribute"/> makes the PK discoverable by convention so
    /// test DbContexts that subclass <c>WalletDbContext</c> with their own
    /// <c>OnModelCreating</c> override (e.g. <c>TestRecoveryDbContext</c>) pick
    /// up the key without needing per-test configuration. The fluent API in
    /// <c>WalletDbContext.ConfigureCitizenHolderIndex</c> still applies the
    /// production <c>text</c> column type and indexes.
    /// </remarks>
    [Key]
    public required string WalletAddress { get; set; }

    /// <summary>Owning citizen account (Tenant Service's PlatformUser).</summary>
    public Guid PlatformUserId { get; set; }

    /// <summary>UTC time the index entry was first written.</summary>
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
