// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Wallet.Providers.Azure;

/// <summary>
/// Configuration options for Azure Key Vault integration.
/// </summary>
public class AzureKmsOptions
{
    public const string SectionName = "EncryptionProvider:AzureKeyVault";

    /// <summary>Azure Key Vault URI (e.g., https://your-vault.vault.azure.net/).</summary>
    public string VaultUri { get; set; } = string.Empty;

    /// <summary>Use Managed Identity for authentication (recommended for production).</summary>
    public bool UseManagedIdentity { get; set; } = true;

    /// <summary>User-Assigned Managed Identity client ID. Null for System-Assigned.</summary>
    public string? ManagedIdentityClientId { get; set; }

    /// <summary>Default key name for DEK wrapping.</summary>
    public string DefaultKeyName { get; set; } = "wallet-encryption-key";
}
