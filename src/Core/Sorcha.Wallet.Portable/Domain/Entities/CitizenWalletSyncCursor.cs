// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Core.Domain.Entities;

/// <summary>
/// Per-(PlatformUser, device) sync cursor (Feature 114, data-model §A3).
/// Materialises the wallet's last-applied watermark on the user's credential-events
/// stream so the sync endpoint can ship deltas without re-scanning history.
/// </summary>
public class CitizenWalletSyncCursor
{
    /// <summary>Surrogate primary key.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Owning citizen account (Tenant Service's PlatformUser).</summary>
    public Guid PlatformUserId { get; set; }

    /// <summary>Specific device whose sync state this row tracks.</summary>
    public Guid PlatformUserDeviceId { get; set; }

    /// <summary>Highest credential-event sequence number this device has applied.</summary>
    public long LastEventSeq { get; set; }

    /// <summary>UTC time of the most recent successful sync from this device.</summary>
    public DateTimeOffset LastSyncAt { get; set; } = DateTimeOffset.UtcNow;
}
