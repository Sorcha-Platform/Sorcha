// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Service.Services.Interfaces;

/// <summary>
/// Encrypts and decrypts per-user persona plaintext blobs using a symmetric
/// key derived from the user's wallet under the <c>sorcha:persona-vault</c>
/// purpose.
/// </summary>
/// <remarks>
/// <para>
/// The Wallet Service is the sole owner of the encryption key. The ciphertext
/// is stored by the Tenant Service alongside the <c>PlatformUserPersona</c>
/// row. Separating the two services prevents ciphertext and key from sharing
/// a single compromise surface.
/// </para>
/// <para>
/// Derivation flow (v1):
/// <list type="number">
///   <item>Load the wallet identified by <paramref name="walletAddress"/>.</item>
///   <item>Decrypt its private key via the key management service.</item>
///   <item>Derive a 32-byte symmetric content key from the private key using
///     HKDF-SHA256 with info = <c>sorcha:persona-vault</c>.</item>
///   <item>Encrypt / decrypt the blob with XChaCha20-Poly1305.</item>
///   <item>Zeroise the derived key and the decrypted private key before
///     returning.</item>
/// </list>
/// </para>
/// <para>
/// <c>WrappedKeyRef</c> is returned so the Tenant Service can store it
/// opaquely alongside the ciphertext. In v1 it is simply the wallet address.
/// The column exists for forward compatibility with per-recipient key
/// wrapping when wallet delegation (Power of Attorney) lands.
/// </para>
/// </remarks>
public interface IPersonaCryptoService
{
    /// <summary>
    /// Encrypts a persona plaintext blob for the owner of the given wallet.
    /// </summary>
    /// <param name="walletAddress">The owning user's wallet address.</param>
    /// <param name="plaintext">UTF-8 JSON bytes of the persona.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A tuple of ciphertext bytes, 24-byte nonce, and an opaque
    /// <c>WrappedKeyRef</c> for the Tenant Service to store.</returns>
    Task<PersonaCryptoResult> EncryptAsync(
        string walletAddress,
        byte[] plaintext,
        CancellationToken ct = default);

    /// <summary>
    /// Decrypts a previously stored persona ciphertext.
    /// </summary>
    /// <param name="walletAddress">The owning user's wallet address.</param>
    /// <param name="ciphertext">Stored ciphertext bytes.</param>
    /// <param name="nonce">Stored 24-byte nonce.</param>
    /// <param name="wrappedKeyRef">Opaque handle stored alongside the
    /// ciphertext. In v1 this must equal the wallet address.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>Decrypted plaintext bytes.</returns>
    /// <exception cref="KeyNotFoundException">No wallet found for the given
    /// address.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// Decryption failed — ciphertext corrupt, nonce tampered, or the
    /// authentication tag did not verify.</exception>
    Task<byte[]> DecryptAsync(
        string walletAddress,
        byte[] ciphertext,
        byte[] nonce,
        string wrappedKeyRef,
        CancellationToken ct = default);
}

/// <summary>
/// Result of a persona encrypt operation.
/// </summary>
/// <param name="Ciphertext">Encrypted bytes (includes the Poly1305 auth tag).</param>
/// <param name="Nonce">24-byte nonce used for this encryption.</param>
/// <param name="WrappedKeyRef">Opaque handle for the Tenant Service to store.</param>
public sealed record PersonaCryptoResult(
    byte[] Ciphertext,
    byte[] Nonce,
    string WrappedKeyRef);

/// <summary>
/// Thrown by <see cref="IPersonaCryptoService.DecryptAsync"/> when the
/// supplied <c>wrappedKeyRef</c> does not match the wallet address — a
/// caller-side invariant violation. The endpoint layer maps this to
/// HTTP 400 instead of the generic 500 that other
/// <see cref="System.Security.Cryptography.CryptographicException"/>s
/// produce.
/// </summary>
public sealed class PersonaKeyRefMismatchException : System.Security.Cryptography.CryptographicException
{
    /// <summary>
    /// Initialises a new instance of the <see cref="PersonaKeyRefMismatchException"/> class.
    /// </summary>
    public PersonaKeyRefMismatchException(string message) : base(message) { }
}
