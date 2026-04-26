// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.CitizenWallet.Abstractions.Models;

/// <summary>
/// Response body for <c>POST /api/v1/wallet/devices/enrol</c>. Carries everything
/// the wallet needs to make offline presentations: server-assigned device id, the
/// signed device delegation credential, the holder's public key (verifier
/// convenience), the status-list URI + index for revocation lookup, and the
/// delegation expiry.
/// </summary>
public sealed record DeviceEnrolmentResponse
{
    /// <summary>Server-assigned device identifier (Tenant Service's <c>PlatformUserDevice.Id</c>).</summary>
    public Guid DeviceId { get; init; }

    /// <summary>
    /// Compact-serialised SD-JWT VC. Issuer = the citizen's holder key; subject = this device.
    /// See <c>contracts/device-delegation-credential.schema.json</c>.
    /// </summary>
    public string DelegationCredential { get; init; } = string.Empty;

    /// <summary>Public half of the citizen's holder key, for verifier-side cnf-chain validation.</summary>
    public EcP256PublicJwk HolderPublicJwk { get; init; } = new();

    /// <summary>Token Status List 2024 URI for revocation lookup.</summary>
    public string StatusListUri { get; init; } = string.Empty;

    /// <summary>Bit position within the status list.</summary>
    public int StatusListIndex { get; init; }

    /// <summary>UTC time at which the delegation credential expires.</summary>
    public DateTimeOffset DelegationExpiresAt { get; init; }
}
