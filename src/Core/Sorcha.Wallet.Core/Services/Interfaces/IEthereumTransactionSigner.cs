// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Core.Domain.ValueObjects;

namespace Sorcha.Wallet.Core.Services.Interfaces;

/// <summary>
/// The <b>sanctioned, server-side</b> native ETH transaction-signing path for a wallet's auxiliary
/// Ethereum identity (Feature 182, Phase 4). Deliberately separate from the WASM-safe
/// <see cref="IEthereumIdentityService"/> prove-control surface — value-moving signing lives only here, in
/// the server-side core, and the prove-control message signer keeps refusing transaction-shaped payloads.
/// Builds and signs an EIP-1559 (type-2) transaction from a fully-specified request; the private key is
/// derived on demand, used, and cleared, and is never returned. Does <b>not</b> broadcast — the Wallet
/// Service gathers chain parameters and broadcasts.
/// </summary>
public interface IEthereumTransactionSigner
{
    /// <summary>
    /// Sign a fully-specified native ETH transfer with the wallet's Ethereum key at
    /// <c>m/44'/60'/0'/0/{index}</c>, returning the signed raw transaction, its hash, and the sender address.
    /// </summary>
    /// <exception cref="ArgumentException">The request is malformed (e.g. a bad recipient or a tip above the max fee).</exception>
    Task<SignedEthereumTransaction> SignTransactionAsync(
        string walletAddress, EthereumTransactionRequest request, int index = 0, CancellationToken cancellationToken = default);
}
