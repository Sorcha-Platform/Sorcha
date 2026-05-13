// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Wallet;

/// <summary>
/// Response model for wallet creation
/// </summary>
public class CreateWalletResponse
{
    /// <summary>
    /// The created wallet details
    /// </summary>
    public required WalletDto Wallet { get; set; }

    /// <summary>
    /// BIP39 mnemonic phrase (CRITICAL: User must save this securely!)
    /// </summary>
    public required string[] MnemonicWords { get; set; }

    /// <summary>
    /// Warning message about mnemonic security
    /// </summary>
    public string Warning { get; set; } = "IMPORTANT: Save your mnemonic phrase securely. It cannot be recovered if lost!";
}
