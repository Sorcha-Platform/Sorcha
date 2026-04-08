// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Internal S2S HTTP client used by <see cref="PersonaService"/> to call the
/// Wallet Service's persona encrypt / decrypt endpoints. Lives inside the
/// Tenant Service project because it is the only caller in v1 — no need to
/// pollute the shared service-clients project.
/// </summary>
public interface IPersonaCryptoClient
{
    /// <summary>
    /// Encrypts plaintext bytes for the owner of the given wallet address.
    /// </summary>
    Task<PersonaCryptoRemoteResult> EncryptAsync(
        string walletAddress,
        byte[] plaintext,
        CancellationToken ct = default);

    /// <summary>
    /// Decrypts a ciphertext blob back to plaintext bytes.
    /// </summary>
    /// <exception cref="PersonaWalletNotFoundException">No wallet found for the given address.</exception>
    /// <exception cref="PersonaCryptoException">Decryption failed (corrupt ciphertext, authentication tag mismatch, transport error).</exception>
    Task<byte[]> DecryptAsync(
        string walletAddress,
        byte[] ciphertext,
        byte[] nonce,
        string wrappedKeyRef,
        CancellationToken ct = default);
}

/// <summary>
/// Result of a remote persona encrypt call.
/// </summary>
public sealed record PersonaCryptoRemoteResult(
    byte[] Ciphertext,
    byte[] Nonce,
    string WrappedKeyRef);

/// <summary>
/// Thrown when the Wallet Service reports 404 for the given wallet address.
/// </summary>
public sealed class PersonaWalletNotFoundException : Exception
{
    public PersonaWalletNotFoundException(string walletAddress)
        : base($"Wallet not found: {walletAddress}") { }
}

/// <summary>
/// Thrown when persona encryption / decryption fails at the Wallet Service
/// boundary for any reason other than missing wallet.
/// </summary>
public sealed class PersonaCryptoException : Exception
{
    public PersonaCryptoException(string message) : base(message) { }
    public PersonaCryptoException(string message, Exception inner) : base(message, inner) { }
}
