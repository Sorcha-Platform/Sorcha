// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Security.Cryptography;
using System.Text;

namespace Sorcha.Cli.Infrastructure;

/// <summary>
/// Linux encryption provider using AES-256-GCM.
/// Uses a machine-specific key derived from user information.
/// Fallback encryption for Linux systems without Secret Service.
/// </summary>
public class LinuxEncryption : IEncryptionProvider
{
    /// <inheritdoc/>
    public bool IsAvailable => OperatingSystem.IsLinux();

    /// <summary>
    /// Derives a machine-specific encryption key from user information.
    /// This provides some protection, though not as strong as OS-managed keystores.
    /// </summary>
    private static byte[] DeriveKey()
    {
        // Combine user-specific and machine-specific data
        var username = Environment.UserName;
        var homePath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var machineId = GetMachineId();

        var keyMaterial = $"{username}:{homePath}:{machineId}:sorcha-cli-v1";

        // Use PBKDF2 to derive a 256-bit key
        return Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(keyMaterial),
            Encoding.UTF8.GetBytes("sorcha-cli-salt"), // Static salt
            iterations: 100000,
            HashAlgorithmName.SHA256,
            outputLength: 32); // 256 bits
    }

    /// <summary>
    /// Gets a machine-specific identifier.
    /// On Linux, tries to read /etc/machine-id or /var/lib/dbus/machine-id.
    /// </summary>
    private static string GetMachineId()
    {
        var machineIdPaths = new[]
        {
            "/etc/machine-id",
            "/var/lib/dbus/machine-id"
        };

        foreach (var path in machineIdPaths)
        {
            if (File.Exists(path))
            {
                try
                {
                    return File.ReadAllText(path).Trim();
                }
                catch (IOException)
                {
                    // File exists but unreadable (permissions, etc.) — try next path
                }
            }
        }

        // Fallback to hostname if machine-id is not available
        return Environment.MachineName;
    }

    /// <inheritdoc/>
    public Task<byte[]> EncryptAsync(string plaintext)
    {
        if (!IsAvailable)
        {
            throw new PlatformNotSupportedException("Linux encryption is only available on Linux platforms.");
        }

        if (string.IsNullOrEmpty(plaintext))
        {
            throw new ArgumentException("Plaintext cannot be null or empty.", nameof(plaintext));
        }

        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var key = DeriveKey();

        using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);

        // Generate a random nonce (12 bytes for GCM)
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);

        // Allocate space for ciphertext and tag
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];

        // Encrypt
        aes.Encrypt(nonce, plaintextBytes, ciphertext, tag);

        // Combine: nonce + tag + ciphertext
        var result = new byte[nonce.Length + tag.Length + ciphertext.Length];
        Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
        Buffer.BlockCopy(tag, 0, result, nonce.Length, tag.Length);
        Buffer.BlockCopy(ciphertext, 0, result, nonce.Length + tag.Length, ciphertext.Length);

        return Task.FromResult(result);
    }

    /// <inheritdoc/>
    public Task<string> DecryptAsync(byte[] ciphertext)
    {
        if (!IsAvailable)
        {
            throw new PlatformNotSupportedException("Linux encryption is only available on Linux platforms.");
        }

        if (ciphertext == null || ciphertext.Length == 0)
        {
            throw new ArgumentException("Ciphertext cannot be null or empty.", nameof(ciphertext));
        }

        var key = DeriveKey();

        using var aes = new AesGcm(key, AesGcm.TagByteSizes.MaxSize);

        var nonceSize = AesGcm.NonceByteSizes.MaxSize;
        var tagSize = AesGcm.TagByteSizes.MaxSize;

        if (ciphertext.Length < nonceSize + tagSize)
        {
            throw new InvalidOperationException("Ciphertext is too short.");
        }

        // Extract nonce, tag, and ciphertext
        var nonce = new byte[nonceSize];
        var tag = new byte[tagSize];
        var encryptedData = new byte[ciphertext.Length - nonceSize - tagSize];

        Buffer.BlockCopy(ciphertext, 0, nonce, 0, nonceSize);
        Buffer.BlockCopy(ciphertext, nonceSize, tag, 0, tagSize);
        Buffer.BlockCopy(ciphertext, nonceSize + tagSize, encryptedData, 0, encryptedData.Length);

        // Decrypt
        var plaintextBytes = new byte[encryptedData.Length];
        aes.Decrypt(nonce, encryptedData, tag, plaintextBytes);

        var plaintext = Encoding.UTF8.GetString(plaintextBytes);

        return Task.FromResult(plaintext);
    }
}
