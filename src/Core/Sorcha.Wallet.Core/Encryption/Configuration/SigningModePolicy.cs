// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Core.Domain.Enums;

namespace Sorcha.Wallet.Core.Encryption.Configuration;

/// <summary>
/// Policy controlling which signing mode is used for new wallets
/// and whether mode transitions are allowed.
/// </summary>
public sealed class SigningModePolicy
{
    /// <summary>
    /// Default signing mode for newly created wallets.
    /// </summary>
    /// <remarks>
    /// <see cref="SigningMode.Local"/>: private keys are stored encrypted locally (envelope encryption).
    /// <see cref="SigningMode.KmsResident"/>: private keys are created and held within cloud KMS.
    /// </remarks>
    public SigningMode DefaultMode { get; set; } = SigningMode.Local;

    /// <summary>
    /// Whether wallets may be migrated from <see cref="SigningMode.Local"/>
    /// to <see cref="SigningMode.KmsResident"/>. The reverse is never allowed.
    /// </summary>
    public bool AllowLocalToKmsMigration { get; set; }

    /// <summary>
    /// Algorithms that are permitted for KMS-resident signing keys.
    /// Empty means all algorithms are allowed.
    /// </summary>
    /// <remarks>
    /// Example: ["ED25519", "NISTP256"]
    /// </remarks>
    public List<string> AllowedKmsAlgorithms { get; set; } = [];
}
