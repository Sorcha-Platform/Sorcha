// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Sorcha.Wallet.Core.Encryption.Providers;

/// <summary>
/// Linux Secret Service encryption provider with file-based fallback
///
/// Implementation Strategy (from 2026-01-11 clarifications):
/// - API: D-Bus Secret Service API (freedesktop.org standard)
/// - Key Storage: GNOME Keyring, KWallet, or file-based fallback
/// - Fallback: File-based with machine-derived encryption if Secret Service unavailable
/// - Encryption: AES-256-GCM for both DEK protection and wallet data
/// - Persistence: Secret Service or {FallbackKeyPath}/*.key files
///
/// Docker Volume Configuration (Fallback Mode):
/// - Mount persistent volume to /var/lib/sorcha/wallet-keys
/// - Ensures DEKs survive container restarts
/// - Example: docker-compose.yml volumes: wallet-encryption-keys:/var/lib/sorcha/wallet-keys
///
/// Secret Service Detection:
/// - Checks for `secret-tool` command availability
/// - Tests D-Bus org.freedesktop.secrets interface
/// - Falls back to file-based if unavailable (common in Docker containers)
///
/// Fallback Security Properties:
/// - DEKs encrypted with machine-derived key (username + machine-id + salt)
/// - Uses PBKDF2 with 100,000 iterations for key derivation
/// - AES-256-GCM provides authenticated encryption
/// - Machine-specific (tied to /etc/machine-id)
///
/// Limitations:
/// - Linux-only (checked via IsAvailable property)
/// - Fallback mode less secure than Secret Service (no OS keyring)
/// - Machine-specific in fallback mode (tied to machine-id)
/// - Secret Service may require user session (not always available in containers)
/// </summary>
public sealed class LinuxSecretServiceEncryptionProvider : EncryptionProviderBase
{
    private readonly string _fallbackKeyPath;
    private readonly string _serviceName;
    private readonly string? _machineKeyMaterial;
    private readonly bool _secretServiceAvailable;

    private const string ServiceName = "sorcha-wallet-service";
    private const int Pbkdf2Iterations = 100000;
    private static readonly Regex SafeKeyIdPattern = new("^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

    private static void ValidateKeyId(string keyId)
    {
        if (!SafeKeyIdPattern.IsMatch(keyId))
            throw new ArgumentException("Invalid key ID format. Only alphanumeric, dash, and underscore are allowed.", nameof(keyId));
    }

    /// <summary>
    /// Checks if Linux Secret Service provider is available on current platform
    /// </summary>
    public static bool IsAvailable => OperatingSystem.IsLinux();

    /// <summary>
    /// Initializes Linux Secret Service encryption provider with fallback
    /// </summary>
    /// <param name="fallbackKeyPath">Directory path for fallback file-based DEK storage</param>
    /// <param name="defaultKeyId">Default key identifier for new encryptions</param>
    /// <param name="logger">Logger for diagnostics and audit trail</param>
    /// <param name="machineKeyMaterial">Optional stable key material for Docker environments (overrides /etc/machine-id)</param>
    /// <exception cref="PlatformNotSupportedException">Thrown if not running on Linux</exception>
    public LinuxSecretServiceEncryptionProvider(
        string fallbackKeyPath,
        string defaultKeyId,
        ILogger<LinuxSecretServiceEncryptionProvider> logger,
        string? machineKeyMaterial = null)
        : base(defaultKeyId, logger, "LinuxSecretService")
    {
        if (!IsAvailable)
        {
            throw new PlatformNotSupportedException(
                "Linux Secret Service encryption provider is only available on Linux platforms.");
        }

        _fallbackKeyPath = fallbackKeyPath ?? throw new ArgumentNullException(nameof(fallbackKeyPath));
        _machineKeyMaterial = machineKeyMaterial;
        _serviceName = ServiceName;

        // Check Secret Service availability
        _secretServiceAvailable = CheckSecretServiceAvailable();

        if (_secretServiceAvailable)
        {
            Logger.LogInformation("Linux Secret Service detected and available");
            AuditLogger.LogProviderInitialized($"Mode=SecretService, ServiceName={_serviceName}");
        }
        else
        {
            var keySource = string.IsNullOrWhiteSpace(_machineKeyMaterial) ? "machine-id" : "configured";
            Logger.LogWarning(
                "Linux Secret Service not available, using file-based fallback: {FallbackPath} (KEK source: {KeySource})",
                _fallbackKeyPath, keySource);

            // Ensure fallback directory exists and is writable
            EnsureFallbackDirectoryIsWritable();

            // Load existing keys from fallback storage
            LoadKeysFromFallbackStorage();

            AuditLogger.LogProviderInitialized(
                $"Mode=Fallback, FallbackPath={_fallbackKeyPath}, DefaultKeyId={defaultKeyId}, KekSource={keySource}");
        }
    }

    /// <summary>
    /// Ensures the fallback key storage directory exists and is writable.
    /// Provides clear error messages for common Docker permission issues.
    /// </summary>
    private void EnsureFallbackDirectoryIsWritable()
    {
        try
        {
            // Create directory if it doesn't exist
            if (!Directory.Exists(_fallbackKeyPath))
            {
                Directory.CreateDirectory(_fallbackKeyPath);
                Logger.LogInformation("Created fallback key storage directory: {FallbackPath}", _fallbackKeyPath);
            }

            // Verify directory is writable by creating and deleting a test file
            var testFilePath = Path.Combine(_fallbackKeyPath, $".write-test-{Guid.NewGuid():N}");
            try
            {
                File.WriteAllBytes(testFilePath, [0x00]);
                File.Delete(testFilePath);
                Logger.LogDebug("Fallback key storage directory is writable: {FallbackPath}", _fallbackKeyPath);
            }
            catch (UnauthorizedAccessException)
            {
                throw new InvalidOperationException(
                    $"Wallet encryption key storage directory is not writable: {_fallbackKeyPath}\n\n" +
                    "This typically occurs on fresh Docker installations when the volume has incorrect permissions.\n\n" +
                    "To fix this issue, run one of the following commands:\n\n" +
                    "Option 1 - Fix permissions on existing volume:\n" +
                    "  docker run --rm -v wallet-encryption-keys:/data alpine chown -R 1654:1654 /data\n\n" +
                    "Option 2 - Delete and recreate the volume:\n" +
                    "  docker-compose down\n" +
                    "  docker volume rm sorcha_wallet-encryption-keys\n" +
                    "  docker-compose up -d\n\n" +
                    "Option 3 - Run the setup script:\n" +
                    "  ./scripts/setup.ps1 (Windows) or ./scripts/setup.sh (Linux/macOS)\n\n" +
                    "For more information, see: docs/FIRST-RUN-SETUP.md");
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            Logger.LogError(ex,
                "Cannot create wallet encryption key storage directory: {FallbackPath}. " +
                "Check that the container has write permissions to this path.",
                _fallbackKeyPath);

            throw new InvalidOperationException(
                $"Cannot create wallet encryption key storage directory: {_fallbackKeyPath}\n\n" +
                "This typically occurs when:\n" +
                "1. The Docker volume was created with root ownership\n" +
                "2. The container is running as a non-root user (UID 1654)\n\n" +
                "To fix this issue, run:\n" +
                "  docker run --rm -v wallet-encryption-keys:/data alpine chown -R 1654:1654 /data\n\n" +
                "Or run the setup script:\n" +
                "  ./scripts/setup.ps1 (Windows) or ./scripts/setup.sh (Linux/macOS)",
                ex);
        }
        catch (IOException ex) when (ex.Message.Contains("Permission denied") || ex.Message.Contains("Access"))
        {
            Logger.LogError(ex,
                "Permission denied accessing wallet encryption key storage: {FallbackPath}",
                _fallbackKeyPath);

            throw new InvalidOperationException(
                $"Permission denied accessing wallet encryption key storage: {_fallbackKeyPath}\n\n" +
                "Fix the volume permissions with:\n" +
                "  docker run --rm -v wallet-encryption-keys:/data alpine chown -R 1654:1654 /data",
                ex);
        }
    }

    /// <inheritdoc />
    protected override async Task<bool> KeyExistsInStoreAsync(string keyId, CancellationToken cancellationToken)
    {
        if (_secretServiceAvailable)
        {
            // Check Secret Service
            return await CheckSecretServiceKeyExists(keyId, cancellationToken);
        }

        // Check fallback file storage
        var keyFilePath = GetKeyFilePath(_fallbackKeyPath, keyId);
        return File.Exists(keyFilePath);
    }

    /// <inheritdoc />
    protected override async Task ProtectAndStoreKeyAsync(string keyId, byte[] dek, CancellationToken cancellationToken)
    {
        if (_secretServiceAvailable)
        {
            // Store in Secret Service
            await StoreDekInSecretService(keyId, dek, cancellationToken);
        }
        else
        {
            // Store in fallback file storage
            await StoreDekInFallbackStorage(keyId, dek, cancellationToken);
        }
    }

    /// <inheritdoc />
    protected override async Task<byte[]?> RetrieveKeyAsync(string keyId, CancellationToken cancellationToken)
    {
        if (_secretServiceAvailable)
        {
            return await RetrieveDekFromSecretService(keyId, cancellationToken);
        }

        var keyFilePath = GetKeyFilePath(_fallbackKeyPath, keyId);
        if (!File.Exists(keyFilePath))
        {
            return null;
        }

        return await RetrieveDekFromFallbackStorage(keyId, cancellationToken);
    }

    /// <summary>
    /// Overrides base to add stale key recovery for fallback mode.
    /// When the KEK has changed (e.g., Docker container rebuild changed /etc/machine-id),
    /// stored DEKs become undecryptable. This override catches the authentication failure,
    /// deletes the stale file, and allows the base class to regenerate.
    /// </summary>
    protected override async Task<byte[]> GetOrCreateKeyAsync(string keyId, CancellationToken cancellationToken)
    {
        // Check cache first (with TTL)
        if (KeyCache.TryGetValue(keyId, out var cached))
        {
            if (DateTime.UtcNow - cached.loadedAt < CacheTtl)
            {
                return cached.key;
            }

            // Cache expired -- evict and re-fetch from storage
            if (KeyCache.TryRemove(keyId, out var expired))
            {
                Array.Clear(expired.key, 0, expired.key.Length);
            }
        }

        // Try to retrieve from storage
        byte[]? dek = null;

        if (_secretServiceAvailable)
        {
            dek = await RetrieveDekFromSecretService(keyId, cancellationToken);
        }
        else
        {
            var keyFilePath = GetKeyFilePath(_fallbackKeyPath, keyId);
            if (File.Exists(keyFilePath))
            {
                try
                {
                    dek = await RetrieveDekFromFallbackStorage(keyId, cancellationToken);
                }
                catch (AuthenticationTagMismatchException)
                {
                    // KEK has changed (e.g., /etc/machine-id regenerated after Docker rebuild).
                    // The stored DEK is encrypted with the old KEK and cannot be recovered.
                    // Delete the stale file and regenerate a new DEK.
                    Logger.LogWarning(
                        "Stale encryption key detected for '{KeyId}': the machine key (KEK) has changed, " +
                        "likely due to a Docker container rebuild. Deleting stale key file and regenerating. " +
                        "Any wallets encrypted with the old key will need to be re-created. " +
                        "To prevent this, set EncryptionProvider__LinuxSecretService__MachineKeyMaterial " +
                        "to a stable value in docker-compose.yml.",
                        keyId);

                    try
                    {
                        File.Delete(keyFilePath);
                    }
                    catch (Exception deleteEx)
                    {
                        Logger.LogError(deleteEx, "Failed to delete stale key file: {KeyFile}", keyFilePath);
                    }

                    // dek remains null, will be created below
                }
            }
        }

        // Create if not found
        if (dek == null)
        {
            await CreateKeyAsync(keyId, cancellationToken);
            return KeyCache[keyId].key;
        }

        // Cache and return
        KeyCache[keyId] = (dek, DateTime.UtcNow);
        return dek;
    }

    /// <summary>
    /// Checks if Secret Service is available
    /// </summary>
    private bool CheckSecretServiceAvailable()
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = "secret-tool",
                Arguments = "search --all service sorcha-wallet",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            process?.WaitForExit(5000); // 5 second timeout
            return process?.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Checks if key exists in Secret Service
    /// </summary>
    private async Task<bool> CheckSecretServiceKeyExists(string keyId, CancellationToken cancellationToken)
    {
        ValidateKeyId(keyId);
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "secret-tool",
                    Arguments = $"lookup service {_serviceName} account {keyId}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            await process.WaitForExitAsync(cancellationToken);

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Stores DEK in Secret Service
    /// </summary>
    private async Task StoreDekInSecretService(string keyId, byte[] dek, CancellationToken cancellationToken)
    {
        ValidateKeyId(keyId);
        var dekBase64 = Convert.ToBase64String(dek);

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "secret-tool",
                Arguments = $"store --label=\"Sorcha Wallet Key {keyId}\" service {_serviceName} account {keyId}",
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };

        process.Start();
        await process.StandardInput.WriteAsync(dekBase64);
        await process.StandardInput.FlushAsync();
        process.StandardInput.Close();

        await process.WaitForExitAsync(cancellationToken);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(cancellationToken);
            throw new InvalidOperationException($"Failed to store key in Secret Service: {error}");
        }
    }

    /// <summary>
    /// Retrieves DEK from Secret Service
    /// </summary>
    private async Task<byte[]?> RetrieveDekFromSecretService(string keyId, CancellationToken cancellationToken)
    {
        ValidateKeyId(keyId);
        try
        {
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "secret-tool",
                    Arguments = $"lookup service {_serviceName} account {keyId}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);

            if (process.ExitCode != 0)
                return null;

            return Convert.FromBase64String(output.Trim());
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Stores DEK in fallback file storage (encrypted with machine-derived key)
    /// </summary>
    private async Task StoreDekInFallbackStorage(string keyId, byte[] dek, CancellationToken cancellationToken)
    {
        var machineKey = DeriveMachineKey();

        using var aes = new AesGcm(machineKey, AesGcm.TagByteSizes.MaxSize);
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        var encryptedDek = new byte[dek.Length];

        aes.Encrypt(nonce, dek, encryptedDek, tag);

        // Store nonce + tag + encryptedDek
        var keyFile = GetKeyFilePath(_fallbackKeyPath, keyId);
        var combined = new byte[nonce.Length + tag.Length + encryptedDek.Length];
        Buffer.BlockCopy(nonce, 0, combined, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, combined, nonce.Length, tag.Length);
        Buffer.BlockCopy(encryptedDek, 0, combined, nonce.Length + tag.Length, encryptedDek.Length);

        await File.WriteAllBytesAsync(keyFile, combined, cancellationToken);
    }

    /// <summary>
    /// Retrieves DEK from fallback file storage
    /// </summary>
    private async Task<byte[]> RetrieveDekFromFallbackStorage(string keyId, CancellationToken cancellationToken)
    {
        var keyFile = GetKeyFilePath(_fallbackKeyPath, keyId);
        var combined = await File.ReadAllBytesAsync(keyFile, cancellationToken);

        var nonceSize = AesGcm.NonceByteSizes.MaxSize;
        var tagSize = AesGcm.TagByteSizes.MaxSize;

        var nonce = new byte[nonceSize];
        var tag = new byte[tagSize];
        var encryptedDek = new byte[combined.Length - nonceSize - tagSize];

        Buffer.BlockCopy(combined, 0, nonce, 0, nonceSize);
        Buffer.BlockCopy(combined, nonceSize, tag, 0, tagSize);
        Buffer.BlockCopy(combined, nonceSize + tagSize, encryptedDek, 0, encryptedDek.Length);

        var machineKey = DeriveMachineKey();

        using var aes = new AesGcm(machineKey, AesGcm.TagByteSizes.MaxSize);
        var dek = new byte[encryptedDek.Length];

        aes.Decrypt(nonce, encryptedDek, tag, dek);

        return dek;
    }

    /// <summary>
    /// Loads all existing encrypted DEKs from fallback storage into memory cache
    /// </summary>
    private void LoadKeysFromFallbackStorage()
    {
        // Directory existence and writability is already verified by EnsureFallbackDirectoryIsWritable()
        if (!Directory.Exists(_fallbackKeyPath))
        {
            Logger.LogDebug(
                "Fallback key storage directory is empty (fresh installation): {FallbackPath}",
                _fallbackKeyPath);
            return;
        }

        var keyFiles = Directory.GetFiles(_fallbackKeyPath, "*.key");
        var loadedCount = 0;

        foreach (var keyFile in keyFiles)
        {
            try
            {
                var keyId = Path.GetFileNameWithoutExtension(keyFile);
                var dek = RetrieveDekFromFallbackStorage(keyId, CancellationToken.None).GetAwaiter().GetResult();

                KeyCache[keyId] = (dek, DateTime.UtcNow);
                loadedCount++;
            }
            catch (AuthenticationTagMismatchException)
            {
                Logger.LogWarning(
                    "Stale encryption key file detected: {KeyFile}. " +
                    "The machine key (KEK) has changed since this DEK was stored " +
                    "(likely due to Docker container rebuild changing /etc/machine-id). " +
                    "The key will be regenerated on next use. " +
                    "Set EncryptionProvider__LinuxSecretService__MachineKeyMaterial to prevent this.",
                    keyFile);
            }
            catch (Exception ex)
            {
                Logger.LogError(
                    ex,
                    "Failed to load encryption key from fallback file: {KeyFile}",
                    keyFile);
            }
        }

        AuditLogger.LogKeysLoaded(loadedCount, $"{_fallbackKeyPath} (fallback)");
    }

    /// <summary>
    /// Derives machine-specific encryption key for fallback mode.
    /// When MachineKeyMaterial is configured, uses that stable value instead of
    /// /etc/machine-id (which changes on every Docker container rebuild).
    /// </summary>
    private byte[] DeriveMachineKey()
    {
        string keyMaterial;

        if (!string.IsNullOrWhiteSpace(_machineKeyMaterial))
        {
            // Use configured stable key material (Docker environments)
            var username = Environment.UserName;
            var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            keyMaterial = $"{username}:{homePath}:{_machineKeyMaterial}:sorcha-wallet-v1";
        }
        else
        {
            // Original behavior: derive from machine-id (native Linux)
            var username = Environment.UserName;
            var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var machineId = GetMachineId();
            keyMaterial = $"{username}:{homePath}:{machineId}:sorcha-wallet-v1";
        }

        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(keyMaterial),
            Encoding.UTF8.GetBytes("sorcha-wallet-salt"),
            Pbkdf2Iterations,
            HashAlgorithmName.SHA256,
            outputLength: 32);
    }

    /// <summary>
    /// Gets machine-specific identifier from /etc/machine-id
    /// </summary>
    private string GetMachineId()
    {
        var machineIdPaths = new[] { "/etc/machine-id", "/var/lib/dbus/machine-id" };

        foreach (var path in machineIdPaths)
        {
            if (File.Exists(path))
            {
                try
                {
                    return File.ReadAllText(path).Trim();
                }
                catch
                {
                    // Continue to next path
                }
            }
        }

        // Fallback to hostname if machine-id not available
        return Environment.MachineName;
    }

    /// <inheritdoc />
    protected override void OnDispose()
    {
        Logger.LogDebug("LinuxSecretServiceEncryptionProvider disposed — DEK cache cleared");
    }
}
