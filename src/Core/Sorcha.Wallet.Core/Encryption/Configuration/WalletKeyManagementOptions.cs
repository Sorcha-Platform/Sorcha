// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Wallet.Core.Domain.Enums;

namespace Sorcha.Wallet.Core.Encryption.Configuration;

/// <summary>
/// Configuration options for wallet key management including DEK caching,
/// signing mode policy, and key protection provider selection.
/// </summary>
public sealed class WalletKeyManagementOptions
{
    /// <summary>
    /// Configuration section name in appsettings.json.
    /// </summary>
    public const string SectionName = "WalletKeyManagement";

    /// <summary>
    /// Default key ID used when no specific key is requested for new encryptions.
    /// </summary>
    public string DefaultKeyId { get; set; } = "default-key";

    /// <summary>
    /// TTL in minutes for cached Data Encryption Keys (DEKs).
    /// After this period, DEKs are re-fetched from the key protection provider.
    /// </summary>
    /// <remarks>Default: 30 minutes.</remarks>
    public int DekCacheTtlMinutes { get; set; } = 30;

    /// <summary>
    /// Grace period in minutes after TTL expiry during which stale DEKs
    /// may still be used for decryption if the provider is unreachable.
    /// New encryptions always require a fresh DEK.
    /// </summary>
    /// <remarks>Default: 5 minutes. Set to 0 to disable grace period.</remarks>
    public int DekCacheGracePeriodMinutes { get; set; } = 5;

    /// <summary>
    /// Maximum number of DEKs to keep in the in-memory cache.
    /// Oldest entries are evicted when the limit is reached.
    /// </summary>
    /// <remarks>Default: 1000.</remarks>
    public int MaxCachedDeks { get; set; } = 1000;

    /// <summary>
    /// Signing mode policy configuration.
    /// </summary>
    public SigningModePolicy SigningPolicy { get; set; } = new();

    /// <summary>
    /// Default signing mode for newly created wallets when no override is provided.
    /// </summary>
    public SigningMode DefaultSigningMode { get; set; } = SigningMode.Local;

    /// <summary>
    /// BIP44 derivation paths that should always use KMS-resident signing.
    /// Paths are matched as prefixes (e.g., "m/44'/0'/0'/0/100" matches exact path).
    /// </summary>
    public List<string> KmsResidentPaths { get; set; } = new();

    /// <summary>
    /// Whether API callers may override the signing mode on wallet creation requests.
    /// When false, the API-provided signing mode is ignored and policy defaults apply.
    /// </summary>
    public bool AllowSigningModeOverride { get; set; } = true;
}
