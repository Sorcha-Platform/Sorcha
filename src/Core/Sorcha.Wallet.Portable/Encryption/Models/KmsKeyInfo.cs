// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Core.Encryption.Models;

/// <summary>
/// Information about a key created in a cloud KMS.
/// </summary>
public sealed record KmsKeyInfo(
    string KeyId,
    byte[] PublicKey,
    string Algorithm,
    DateTimeOffset CreatedAt);
