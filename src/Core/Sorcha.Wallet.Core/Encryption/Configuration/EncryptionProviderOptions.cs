// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Core.Encryption.Configuration;

/// <summary>
/// Root configuration for encryption provider selection
/// </summary>
public sealed class EncryptionProviderOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json
    /// </summary>
    public const string SectionName = "EncryptionProvider";

    /// <summary>
    /// Type of encryption provider to use
    /// </summary>
    /// <remarks>
    /// Supported values:
    /// - "Local" - In-memory provider (development only, keys lost on restart)
    /// - "WindowsDpapi" - Windows Data Protection API (production, Windows only)
    /// - "LinuxSecretService" - Linux Secret Service with fallback (production, Linux only)
    /// - "MacOsKeychain" - macOS Keychain Services (development, macOS only)
    /// - "AzureKeyVault" - Azure Key Vault (production, cloud)
    /// </remarks>
    public string Type { get; set; } = "Local";

    /// <summary>
    /// Default key ID to use for new encryptions
    /// </summary>
    /// <remarks>
    /// This should be updated when rotating encryption keys.
    /// Old keys remain accessible for decryption via their keyId.
    /// </remarks>
    public string DefaultKeyId { get; set; } = "default-key";

    /// <summary>
    /// Windows DPAPI provider configuration (when Type = "WindowsDpapi")
    /// </summary>
    public WindowsDpapiOptions? WindowsDpapi { get; set; }

    /// <summary>
    /// Linux Secret Service provider configuration (when Type = "LinuxSecretService")
    /// </summary>
    public LinuxSecretServiceOptions? LinuxSecretService { get; set; }

    /// <summary>
    /// macOS Keychain provider configuration (when Type = "MacOsKeychain")
    /// </summary>
    public MacOsKeychainOptions? MacOsKeychain { get; set; }

    /// <summary>
    /// Azure Key Vault provider configuration (when Type = "AzureKeyVault")
    /// </summary>
    public AzureKeyVaultOptions? AzureKeyVault { get; set; }
}

/// <summary>
/// Configuration for Windows DPAPI encryption provider
/// </summary>
public sealed class WindowsDpapiOptions
{
    /// <summary>
    /// Directory path for encrypted DEK storage (must be on persistent volume)
    /// </summary>
    /// <remarks>
    /// Docker: Mount persistent volume to this path
    /// Example (docker-compose.yml):
    ///   volumes:
    ///     - wallet-encryption-keys:/app/keys
    ///
    /// Windows: C:\ProgramData\Sorcha\WalletKeys
    /// Docker (Windows): C:\app\keys
    /// Docker (Linux): /app/keys
    /// </remarks>
    public string KeyStorePath { get; set; } = "/app/keys";

    /// <summary>
    /// DPAPI protection scope
    /// </summary>
    /// <remarks>
    /// - "LocalMachine" - Recommended for services (machine-specific, survives user logout)
    /// - "CurrentUser" - For desktop apps (user-specific, requires user login)
    /// </remarks>
    public string Scope { get; set; } = "LocalMachine";
}

/// <summary>
/// Configuration for Linux Secret Service encryption provider
/// </summary>
public sealed class LinuxSecretServiceOptions
{
    /// <summary>
    /// Service name identifier for Secret Service storage
    /// </summary>
    /// <remarks>
    /// Used as the "service" attribute when storing keys in GNOME Keyring or KWallet.
    /// Should be unique to your application.
    /// </remarks>
    public string ServiceName { get; set; } = "sorcha-wallet-service";

    /// <summary>
    /// Fallback key storage path (used when Secret Service is unavailable)
    /// </summary>
    /// <remarks>
    /// Fallback mode uses machine-derived encryption with PBKDF2.
    /// This is typically needed in Docker containers where Secret Service is not available.
    ///
    /// Docker: Mount persistent volume to this path
    /// Example (docker-compose.yml):
    ///   volumes:
    ///     - wallet-encryption-keys:/var/lib/sorcha/wallet-keys
    ///
    /// Linux: /var/lib/sorcha/wallet-keys
    /// Docker: /var/lib/sorcha/wallet-keys or /app/keys
    /// </remarks>
    public string FallbackKeyStorePath { get; set; } = "/var/lib/sorcha/wallet-keys";

    /// <summary>
    /// Stable key material for machine key derivation (overrides /etc/machine-id)
    /// </summary>
    /// <remarks>
    /// In Docker, /etc/machine-id is regenerated on every container rebuild, which
    /// causes the KEK (Key Encryption Key) to change and makes stored DEKs
    /// undecryptable (AuthenticationTagMismatchException).
    ///
    /// Set this to a stable value in Docker environments to ensure the KEK remains
    /// consistent across container rebuilds.
    ///
    /// Example (docker-compose.yml):
    ///   environment:
    ///     EncryptionProvider__LinuxSecretService__MachineKeyMaterial: "my-stable-key-material"
    ///
    /// When null/empty, falls back to /etc/machine-id (original behavior).
    /// </remarks>
    public string? MachineKeyMaterial { get; set; }
}

/// <summary>
/// Configuration for macOS Keychain encryption provider
/// </summary>
public sealed class MacOsKeychainOptions
{
    /// <summary>
    /// Service name identifier for Keychain storage
    /// </summary>
    /// <remarks>
    /// Used as the "service" name when storing keys in macOS Keychain.
    /// Should be unique to your application.
    /// </remarks>
    public string ServiceName { get; set; } = "sorcha-wallet-service";

    /// <summary>
    /// Keychain access group (optional, for shared keychain access)
    /// </summary>
    /// <remarks>
    /// Leave null for default keychain access.
    /// Set to a team identifier to share keys between apps.
    /// </remarks>
    public string? AccessGroup { get; set; }
}

/// <summary>
/// Configuration for Azure Key Vault encryption provider
/// </summary>
public sealed class AzureKeyVaultOptions
{
    /// <summary>
    /// Azure Key Vault URI
    /// </summary>
    /// <remarks>
    /// Example: https://your-vault.vault.azure.net/
    /// </remarks>
    public string VaultUri { get; set; } = string.Empty;

    /// <summary>
    /// Default key name in Azure Key Vault
    /// </summary>
    /// <remarks>
    /// This is the name of the key in Azure Key Vault, not the keyId.
    /// The keyId is derived from the key version.
    /// </remarks>
    public string DefaultKeyName { get; set; } = "wallet-encryption-key";

    /// <summary>
    /// Use Managed Identity for authentication (recommended for production)
    /// </summary>
    /// <remarks>
    /// When true, uses Azure Managed Identity (no credentials in configuration).
    /// When false, uses DefaultAzureCredential (interactive login for development).
    /// </remarks>
    public bool UseManagedIdentity { get; set; } = true;

    /// <summary>
    /// Client ID for User-Assigned Managed Identity (optional)
    /// </summary>
    /// <remarks>
    /// Leave null to use System-Assigned Managed Identity.
    /// Set to User-Assigned Managed Identity client ID if using UAMI.
    /// </remarks>
    public string? ManagedIdentityClientId { get; set; }

    /// <summary>
    /// Cache DEKs in memory for performance (recommended)
    /// </summary>
    /// <remarks>
    /// When true, decrypted DEKs are cached in memory with TTL.
    /// Reduces Key Vault API calls (~10-50ms network latency per operation).
    /// </remarks>
    public bool EnableDekCache { get; set; } = true;

    /// <summary>
    /// DEK cache TTL in minutes (default: 60 minutes)
    /// </summary>
    /// <remarks>
    /// Cached DEKs are automatically refreshed when expired.
    /// Lower values = more Key Vault calls, higher security.
    /// Higher values = fewer Key Vault calls, better performance.
    /// </remarks>
    public int DekCacheTtlMinutes { get; set; } = 60;

    /// <summary>
    /// Allow stale DEKs during Key Vault outages (read-through cache)
    /// </summary>
    /// <remarks>
    /// When true, allows use of cached DEKs for decryption even if cache is expired
    /// and Key Vault is unavailable. New wallet creation still fails.
    /// When false, expires cached DEKs strictly (fails if Key Vault unavailable).
    /// </remarks>
    public bool AllowStaleDeksOnOutage { get; set; } = true;
}
