// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Contracts.Models;

/// <summary>
/// Immutable wire contract for the result of wallet creation.
/// </summary>
public sealed record CreateWalletResponse
{
    /// <summary>The created wallet details.</summary>
    public required WalletDto Wallet { get; init; }

    /// <summary>BIP39 mnemonic phrase (CRITICAL: User must save this securely!).</summary>
    public required string[] MnemonicWords { get; init; }

    /// <summary>PQC wallet address (ws2 prefix) — present only for hybrid or PQC-only wallets.</summary>
    public string? PqcWalletAddress { get; init; }

    /// <summary>PQC algorithm used — present only for hybrid or PQC-only wallets.</summary>
    public string? PqcAlgorithm { get; init; }

    /// <summary>Warning message about mnemonic security.</summary>
    public string Warning { get; init; } =
        "IMPORTANT: Save your mnemonic phrase securely. It cannot be recovered if lost!";
}
