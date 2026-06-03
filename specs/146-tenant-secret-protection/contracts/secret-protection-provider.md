# Contract: `ISecretProtectionProvider` (internal seam)

This feature adds **no new HTTP/gRPC endpoint** — the contract is an internal Tenant-Service
abstraction. It is deliberately shaped to mirror `Sorcha.Wallet.Core.Services.Interfaces.IOrgKeyProtectionProvider`
so the two converge later (research D6).

## Interface

```csharp
namespace Sorcha.Tenant.Service.Services.Interfaces;

/// <summary>
/// Protects/unprotects sensitive Tenant secrets at rest with authenticated encryption.
/// CONVERGENCE NOTE: intentional mirror of Sorcha.Wallet's IOrgKeyProtectionProvider /
/// SoftwareKeyProtectionProvider. During the Hardware Key Storage initiative, lift this and the
/// Wallet provider onto a shared Sorcha.* provider in Common and add the KMS/HSM impl behind this
/// same seam. Keep the AES-256-GCM body identical until then.
/// </summary>
public interface ISecretProtectionProvider
{
    /// <summary>Encrypts plaintext; returns the ciphertext envelope and the KeyId that protected it.</summary>
    Task<(byte[] Ciphertext, string KeyId)> EncryptAsync(byte[] plaintext, CancellationToken ct = default);

    /// <summary>Decrypts a ciphertext envelope previously produced under <paramref name="keyId"/>.</summary>
    Task<byte[]> DecryptAsync(byte[] ciphertext, string keyId, CancellationToken ct = default);

    /// <summary>Provider name for storage metadata, e.g. "Software" (future: "AzureKeyVault").</summary>
    string ProviderName { get; }
}
```

## Semantics

| Aspect | Contract |
|--------|----------|
| Algorithm | AES-256-GCM (BCL `AesGcm`), 12-byte nonce, 16-byte tag. |
| Envelope | `nonce(12) ∥ ciphertext ∥ tag(16)` (matches Wallet `SoftwareKeyProtectionProvider`). |
| Nonce | Fresh `RandomNumberGenerator`-filled 12 bytes per `EncryptAsync`. |
| `KeyId` | Returned by `EncryptAsync`, persisted by the caller, passed back to `DecryptAsync`. `"jwt-derived-v1"` (default) or `"config-v1"` (override). |
| Round-trip | `Decrypt(Encrypt(p).Ciphertext, Encrypt(p).KeyId) == p` for the current key. |
| Tamper | A modified envelope (any byte) → `AuthenticationTagMismatchException`. Callers MUST catch and surface safely (FR-010). |
| Too-short input | `< 28` bytes → `ArgumentException`. |
| Threading | Implementation is thread-safe (stateless except the immutable key); registered **singleton**. |
| Logging | MUST NOT log plaintext, ciphertext, or key bytes. May log `ProviderName`/`KeyId`. |

## Key resolution contract (`TenantSecretKeyResolver`)

Resolves `(byte[] Key /*32*/, string KeyId)` once at startup, in order:

1. `Tenant:SecretProtection:Key` set → base64-decode; MUST be 32 bytes (else throw) → `KeyId = "config-v1"`.
2. Else derive: `HKDF-SHA256(ikm = utf8(JwtConfiguration.SigningKey), salt = null, info = utf8("sorcha:tenant:secret-protection:v1"), L = 32)` → `KeyId = "jwt-derived-v1"`.
3. Else (no override, no JWT signing key):
   - Production / Staging → **throw at startup** (fail-closed, FR-006).
   - Other environments → derive from the dev JWT signing key (same as step 2).

## Login-token signing key (not via this provider)

Separately derived once at startup: `HKDF-SHA256(ikm = utf8(JwtConfiguration.SigningKey), info = utf8("sorcha:tenant:login-token-hmac:v1"), L = 32)`. Replaces the per-process random key in `TotpService`. Stable across replicas/restarts (FR-004). Used for HMAC signing of the 2FA intermediate token — **not** AEAD, so it does not flow through `ISecretProtectionProvider`.

## Consumers (call sites)

| Consumer | Use |
|----------|-----|
| `TotpService` | `EncryptAsync(utf8(base32Secret))` on setup; `DecryptAsync` on verify/validate. |
| `IdpConfigurationService` | `EncryptAsync(utf8(clientSecret))` on save; `DecryptAsync` on read. |
| `OidcExchangeService` (~:127) | Receives the real decrypted client secret for the IdP token exchange. |
| `DatabaseInitializer` (~:479) | IdP seed routed through the provider (or seeded secret dropped). |
