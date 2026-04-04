// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Core.Domain.Enums;

/// <summary>
/// Determines how a wallet's signing keys are managed.
/// </summary>
public enum SigningMode
{
    /// <summary>Private key stored encrypted locally using envelope encryption.</summary>
    Local = 0,

    /// <summary>Private key created and held within cloud KMS. Never extracted.</summary>
    KmsResident = 1
}
