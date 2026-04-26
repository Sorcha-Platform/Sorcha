// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Core.Domain.Entities;

/// <summary>
/// Tenant-org-scoped citizen wallet device status list (Feature 114, data-model §A2).
/// Maintains a packed bitstring (active=0, revoked=1) — one bit per allocated
/// <c>PlatformUserDevice.StatusListIndex</c> belonging to citizens of the org.
/// </summary>
/// <remarks>
/// Each org has one or more lists; lists fill from index 0 to <see cref="Capacity"/>
/// (default 32 768) and a new list is allocated when the watermark reaches capacity.
/// Indexes are NEVER reused — revocation only flips the bit, allocation only moves
/// the watermark. The signed JWT is the IETF Token Status List 2024 wire format
/// served verbatim from <c>GET /api/v1/wallet/status/{orgId}/citizen-devices/{listId}.statuslist+jwt</c>.
/// </remarks>
public class CitizenDeviceStatusList
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning tenant organisation.</summary>
    public Guid OrganizationId { get; set; }

    /// <summary>Sequential list id within the org, contiguous from 0.</summary>
    public int ListId { get; set; }

    /// <summary>Number of bits this list can hold; default 32 768.</summary>
    public int Capacity { get; set; } = 32_768;

    /// <summary>Packed bitstring; length = <see cref="Capacity"/> / 8.</summary>
    public byte[] Bitstring { get; set; } = Array.Empty<byte>();

    /// <summary>Number of bits currently set (revoked devices) for fast capacity-tracking.</summary>
    public int RevokedCount { get; set; }

    /// <summary>Highest allocated bit index; monotonically increasing watermark.</summary>
    public int LastAllocatedIndex { get; set; } = -1;

    /// <summary>UTC time the current <see cref="SignedJwt"/> was generated.</summary>
    public DateTimeOffset GeneratedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>UTC expiry of the current signed JWT (default <see cref="GeneratedAt"/> + 24h).</summary>
    public DateTimeOffset ExpiresAt { get; set; } = DateTimeOffset.UtcNow.AddHours(24);

    /// <summary>Compact-serialised Token Status List 2024 JWT, signed with the org's <c>sorcha:citizen-status-signing</c> key.</summary>
    public string SignedJwt { get; set; } = string.Empty;
}
