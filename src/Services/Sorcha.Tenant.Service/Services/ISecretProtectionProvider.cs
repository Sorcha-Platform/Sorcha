// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Protects and unprotects sensitive Tenant secrets at rest with authenticated encryption
/// (AES-256-GCM). Used for TOTP secrets and OIDC client secrets — values that must be stored
/// confidentially yet recovered at runtime.
/// </summary>
/// <remarks>
/// <para>
/// <b>CONVERGENCE NOTE.</b> This abstraction is an intentional mirror of
/// <c>Sorcha.Wallet.Core.Services.Interfaces.IOrgKeyProtectionProvider</c> /
/// <c>Sorcha.Wallet.Service.Services.Implementation.SoftwareKeyProtectionProvider</c>
/// (same AES-256-GCM body: 12-byte nonce, 16-byte tag, envelope <c>nonce ∥ ciphertext ∥ tag</c>,
/// and a <c>KeyId</c> persisted alongside the ciphertext). During the future Hardware Key Storage
/// initiative, lift this and the Wallet provider onto a single shared <c>Sorcha.*</c> provider in a
/// Common project and add the KMS/HSM implementation behind this same seam. Keep the crypto body
/// identical until then. See <c>docs/superpowers/specs/2026-06-03-tenant-secret-protection-design.md</c>.
/// </para>
/// <para>
/// Implementations MUST NOT log plaintext, ciphertext, or key material. They MAY log
/// <see cref="ProviderName"/> / KeyId.
/// </para>
/// </remarks>
public interface ISecretProtectionProvider
{
    /// <summary>
    /// Encrypts <paramref name="plaintext"/> and returns the ciphertext envelope together with the
    /// identifier of the key that protected it.
    /// </summary>
    /// <param name="plaintext">The raw secret bytes to protect.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The ciphertext envelope (<c>nonce ∥ ciphertext ∥ tag</c>) and the protecting key's id.</returns>
    Task<(byte[] Ciphertext, string KeyId)> EncryptAsync(byte[] plaintext, CancellationToken ct = default);

    /// <summary>
    /// Decrypts a ciphertext envelope previously produced by <see cref="EncryptAsync"/>.
    /// </summary>
    /// <param name="ciphertext">The ciphertext envelope (<c>nonce ∥ ciphertext ∥ tag</c>).</param>
    /// <param name="keyId">The key identifier persisted alongside the ciphertext.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The recovered plaintext bytes.</returns>
    Task<byte[]> DecryptAsync(byte[] ciphertext, string keyId, CancellationToken ct = default);

    /// <summary>Provider name for storage/diagnostics metadata, e.g. "Software" (future: "AzureKeyVault").</summary>
    string ProviderName { get; }
}
