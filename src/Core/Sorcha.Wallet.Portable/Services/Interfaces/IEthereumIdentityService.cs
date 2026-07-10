// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Cryptography.Secp256k1.Siwe;

namespace Sorcha.Wallet.Core.Services.Interfaces;

/// <summary>
/// The result of signing a SIWE prove-control message (Feature 180). Carries the formatted message, the
/// 0x-hex 65-byte signature, and the signer's Ethereum address — <b>never</b> any private key material.
/// </summary>
public sealed record SiweSignResult(string Message, string Signature, string Address);

/// <summary>
/// A wallet's <b>auxiliary Ethereum identity</b> (Feature 180) — a secp256k1 key derived on demand from
/// the wallet's existing encrypted seed at <c>m/44'/60'/0'/0/{index}</c>, used only to <b>prove control</b>
/// of an Ethereum address (EIP-191 / SIWE). The private key is never returned; the wallet's primary
/// signing algorithm is unchanged. Signing a payload that decodes as a blockchain transaction is refused.
/// </summary>
public interface IEthereumIdentityService
{
    /// <summary>Return the wallet's EIP-55 Ethereum address (deterministic from its seed and <paramref name="index"/>).</summary>
    Task<string> GetAddressAsync(string walletAddress, int index = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sign an EIP-191 <c>personal_sign</c> prove-control message, returning the 65-byte <c>r‖s‖v</c>
    /// signature. Refuses a payload that decodes as a blockchain transaction.
    /// </summary>
    Task<byte[]> SignPersonalMessageAsync(string walletAddress, byte[] message, int index = 0, CancellationToken cancellationToken = default);

    /// <summary>
    /// Sign a SIWE (EIP-4361) prove-control message, returning the formatted message, 0x-hex signature,
    /// and address.
    /// </summary>
    Task<SiweSignResult> SignSiweAsync(string walletAddress, SiweMessage message, int index = 0, CancellationToken cancellationToken = default);
}
