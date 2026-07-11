// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Core.Domain.ValueObjects;

/// <summary>
/// The signed, broadcast-ready product of the wallet's Ethereum transaction signer (Feature 182, Phase 4).
/// Carries the raw transaction and its hash — <b>never</b> any private key material.
/// </summary>
/// <param name="RawTxHex">The <c>0x02…</c> signed raw transaction for <c>eth_sendRawTransaction</c>.</param>
/// <param name="TxHash">The <c>0x…</c> keccak-256 hash of the signed raw transaction.</param>
/// <param name="From">The EIP-55 sender address (the wallet's Ethereum identity).</param>
public sealed record SignedEthereumTransaction(string RawTxHex, string TxHash, string From);
