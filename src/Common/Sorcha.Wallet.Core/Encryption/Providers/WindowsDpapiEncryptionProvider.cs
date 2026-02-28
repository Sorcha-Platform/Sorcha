// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Sorcha.Wallet.Core.Encryption.Providers;

/// <summary>
/// Windows Data Protection API (DPAPI) encryption provider for production use
///
/// Implementation Strategy (from 2026-01-11 clarifications):
/// - API: System.Security.Cryptography.ProtectedData (built-in .NET)
/// - Key Storage: File-based on persistent Docker volume
/// - Scope: DataProtectionScope.LocalMachine for service accounts
/// - Encryption: DPAPI encrypts DEK, AES-256-GCM encrypts wallet data with DEK
/// - Persistence: DEKs stored in {KeyStorePath}/*.key files
///
/// Docker Volume Configuration:
/// - Mount persistent volume to /app/keys or C:\app\keys
/// - Ensures DEKs survive container restarts
/// - Example: docker-compose.yml volumes: wallet-encryption-keys:/app/keys
///
/// Security Properties:
/// - DEKs protected by Windows machine credentials (DPAPI LocalMachine)
/// - Cannot decrypt DEKs on different machine without same credentials
/// - Additional entropy per key for defense-in-depth
/// - AES-256-GCM provides authenticated encryption for wallet data
///
/// Limitations:
/// - Windows-only (checked via IsAvailable property)
/// - Machine-specific (encrypted DEKs tied to Windows machine identity)
/// - No built-in key rotation (manual via CreateKeyAsync)
/// </summary>
public sealed class WindowsDpapiEncryptionProvider : EncryptionProviderBase
{
    private readonly string _keyStorePath;
    private readonly DataProtectionScope _scope;

    /// <summary>
    /// Checks if Windows DPAPI is available on current platform
    /// </summary>
    public static bool IsAvailable => OperatingSystem.IsWindows();

    /// <summary>
    /// Initializes Windows DPAPI encryption provider
    /// </summary>
    /// <param name="keyStorePath">Directory path for encrypted DEK storage (must exist or be creatable)</param>
    /// <param name="defaultKeyId">Default key identifier for new encryptions</param>
    /// <param name="scope">DPAPI scope (LocalMachine recommended for services, CurrentUser for desktop apps)</param>
    /// <param name="logger">Logger for diagnostics and audit trail</param>
    /// <exception cref="PlatformNotSupportedException">Thrown if not running on Windows</exception>
    public WindowsDpapiEncryptionProvider(
        string keyStorePath,
        string defaultKeyId,
        DataProtectionScope scope,
        ILogger<WindowsDpapiEncryptionProvider> logger)
        : base(defaultKeyId, logger, "WindowsDpapi")
    {
        if (!IsAvailable)
        {
            throw new PlatformNotSupportedException(
                "Windows DPAPI encryption provider is only available on Windows platforms.");
        }

        _keyStorePath = keyStorePath ?? throw new ArgumentNullException(nameof(keyStorePath));
        _scope = scope;

        // Ensure key storage directory exists
        Directory.CreateDirectory(_keyStorePath);

        // Load existing keys from disk
        LoadKeysFromDisk();

        AuditLogger.LogProviderInitialized(
            $"KeyStorePath={_keyStorePath}, Scope={_scope}, DefaultKeyId={defaultKeyId}");
    }

    /// <inheritdoc />
    protected override Task<bool> KeyExistsInStoreAsync(string keyId, CancellationToken cancellationToken)
    {
        var keyFilePath = GetKeyFilePath(_keyStorePath, keyId);
        return Task.FromResult(File.Exists(keyFilePath));
    }

    /// <inheritdoc />
    protected override async Task ProtectAndStoreKeyAsync(string keyId, byte[] dek, CancellationToken cancellationToken)
    {
        // Encrypt DEK with Windows DPAPI
        var entropy = Encoding.UTF8.GetBytes($"sorcha-wallet-{keyId}");
        var encryptedDek = ProtectedData.Protect(dek, entropy, _scope);

        // Store encrypted DEK to disk
        var keyFilePath = GetKeyFilePath(_keyStorePath, keyId);
        await File.WriteAllBytesAsync(keyFilePath, encryptedDek, cancellationToken);
    }

    /// <inheritdoc />
    protected override async Task<byte[]?> RetrieveKeyAsync(string keyId, CancellationToken cancellationToken)
    {
        var keyFilePath = GetKeyFilePath(_keyStorePath, keyId);

        if (!File.Exists(keyFilePath))
        {
            return null;
        }

        // Load and decrypt DEK from disk
        var encryptedDek = await File.ReadAllBytesAsync(keyFilePath, cancellationToken);
        var entropy = Encoding.UTF8.GetBytes($"sorcha-wallet-{keyId}");
        return ProtectedData.Unprotect(encryptedDek, entropy, _scope);
    }

    /// <summary>
    /// Loads all existing encrypted DEKs from disk into memory cache
    /// </summary>
    private void LoadKeysFromDisk()
    {
        if (!Directory.Exists(_keyStorePath))
        {
            Logger.LogWarning(
                "Key storage directory does not exist, will be created: {KeyStorePath}",
                _keyStorePath);
            return;
        }

        var keyFiles = Directory.GetFiles(_keyStorePath, "*.key");
        var loadedCount = 0;

        foreach (var keyFile in keyFiles)
        {
            try
            {
                var keyId = Path.GetFileNameWithoutExtension(keyFile);
                var encryptedDek = File.ReadAllBytes(keyFile);
                var entropy = Encoding.UTF8.GetBytes($"sorcha-wallet-{keyId}");
                var dek = ProtectedData.Unprotect(encryptedDek, entropy, _scope);

                KeyCache[keyId] = (dek, DateTime.UtcNow);
                loadedCount++;
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    ex,
                    "Failed to load encryption key from file: {KeyFile}",
                    keyFile);
            }
        }

        AuditLogger.LogKeysLoaded(loadedCount, _keyStorePath);
    }

    /// <inheritdoc />
    protected override void OnDispose()
    {
        Logger.LogDebug("WindowsDpapiEncryptionProvider disposed — DEK cache cleared");
    }
}
