// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Numerics;

namespace Sorcha.Wallet.Core.Domain.ValueObjects;

/// <summary>
/// A fully-specified, deterministic native ETH transfer ready to sign (Feature 182, Phase 4). The Wallet
/// Service gathers the live chain parameters (nonce, gas, fees) and hands the signer a complete request —
/// the signer never guesses a field. Native-transfer scope: the call data is always empty.
/// </summary>
/// <param name="ChainId">Target chain id (must be positive).</param>
/// <param name="To">Recipient address (<c>0x</c>-prefixed 20-byte hex).</param>
/// <param name="ValueWei">Transfer amount in wei (non-negative).</param>
/// <param name="Nonce">Sender nonce (non-negative).</param>
/// <param name="GasLimit">Gas limit (positive).</param>
/// <param name="MaxFeePerGasWei">EIP-1559 max fee per gas, in wei (positive).</param>
/// <param name="MaxPriorityFeePerGasWei">EIP-1559 max priority fee per gas, in wei (0 ≤ tip ≤ max fee).</param>
public sealed record EthereumTransactionRequest(
    long ChainId,
    string To,
    BigInteger ValueWei,
    long Nonce,
    long GasLimit,
    BigInteger MaxFeePerGasWei,
    BigInteger MaxPriorityFeePerGasWei);
