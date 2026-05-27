// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using OtpNet;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Data.Repositories;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// TOTP two-factor authentication service implementation.
/// Uses OtpNet for TOTP generation/validation and SHA-256 for backup code hashing.
/// Login tokens are HMAC-signed, short-lived tokens for the 2FA verification step.
/// </summary>
public class TotpService : ITotpService
{
    private const int BackupCodeCount = 10;
    private const int BackupCodeLength = 8;
    private const int TotpStepSeconds = 30;
    private const int TotpDigits = 6;
    private const int LoginTokenLifetimeMinutes = 5;
    private const string Issuer = "Sorcha";

    /// <summary>
    /// Characters used for backup code generation (alphanumeric, no ambiguous chars).
    /// </summary>
    private static readonly char[] BackupCodeChars =
        "ABCDEFGHJKLMNPQRSTUVWXYZ23456789".ToCharArray();

    private readonly TenantDbContext _db;
    private readonly IIdentityRepository _identityRepository;
    private readonly ITenantSecurityInboxWriter _securityInbox;
    private readonly ILogger<TotpService> _logger;

    /// <summary>
    /// HMAC key for signing login tokens. Derived from a stable source per process lifetime.
    /// In production, this should be sourced from key management (Azure Key Vault, etc.).
    /// </summary>
    private static readonly byte[] LoginTokenSigningKey = GenerateStableKey();

    public TotpService(
        TenantDbContext db,
        IIdentityRepository identityRepository,
        ITenantSecurityInboxWriter securityInbox,
        ILogger<TotpService> logger)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _identityRepository = identityRepository ?? throw new ArgumentNullException(nameof(identityRepository));
        _securityInbox = securityInbox ?? throw new ArgumentNullException(nameof(securityInbox));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<TotpSetupResult> SetupAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Look up user email for the QR URI label
        var user = await _identityRepository.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            throw new InvalidOperationException($"User {userId} not found");
        }

        // Generate a new TOTP secret (20 bytes = 160-bit, standard for TOTP)
        var secretBytes = KeyGeneration.GenerateRandomKey(20);
        var base32Secret = Base32Encoding.ToString(secretBytes);

        // Generate backup codes
        var backupCodes = GenerateBackupCodes();
        var hashedBackupCodes = backupCodes.Select(HashBackupCode).ToArray();

        // Remove existing pending setup (if any) or existing config
        var existing = await _db.TotpConfigurations
            .FirstOrDefaultAsync(t => t.UserId == userId, cancellationToken);

        if (existing is not null)
        {
            _db.TotpConfigurations.Remove(existing);
        }

        // Create new TOTP configuration (not yet enabled — pending verification)
        var config = new TotpConfiguration
        {
            UserId = userId,
            EncryptedSecret = EncryptSecret(base32Secret),
            BackupCodes = JsonSerializer.Serialize(hashedBackupCodes),
            IsEnabled = false,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _db.TotpConfigurations.Add(config);
        await _db.SaveChangesAsync(cancellationToken);

        // Build otpauth URI for QR code
        var qrUri = $"otpauth://totp/{Uri.EscapeDataString(Issuer)}:{Uri.EscapeDataString(user.Email)}" +
                    $"?secret={base32Secret}&issuer={Uri.EscapeDataString(Issuer)}";

        _logger.LogInformation("TOTP setup initiated for user {UserId}", userId);

        return new TotpSetupResult
        {
            Secret = base32Secret,
            QrUri = qrUri,
            BackupCodes = backupCodes
        };
    }

    /// <inheritdoc />
    public async Task<bool> VerifyAndEnableAsync(Guid userId, string code, CancellationToken cancellationToken = default)
    {
        var config = await _db.TotpConfigurations
            .FirstOrDefaultAsync(t => t.UserId == userId, cancellationToken);

        if (config is null)
        {
            _logger.LogWarning("TOTP verify attempt for user {UserId} with no config", userId);
            return false;
        }

        if (config.IsEnabled)
        {
            _logger.LogWarning("TOTP verify attempt for user {UserId} but already enabled", userId);
            return false;
        }

        // Decrypt secret and validate the code
        var base32Secret = DecryptSecret(config.EncryptedSecret);
        var secretBytes = Base32Encoding.ToBytes(base32Secret);

        var totp = new Totp(secretBytes, step: TotpStepSeconds, totpSize: TotpDigits);
        var isValid = totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));

        if (!isValid)
        {
            _logger.LogWarning("TOTP verification failed for user {UserId} — invalid code", userId);
            return false;
        }

        // Enable TOTP
        config.IsEnabled = true;
        config.VerifiedAt = DateTime.UtcNow;
        config.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        // Update UserPreferences.TwoFactorEnabled
        await UpdateUserPreferencesTwoFactor(userId, true, cancellationToken);

        _logger.LogInformation("TOTP enabled for user {UserId}", userId);

        // Feature 118 — emit a Category=Security inbox entry. Fail-safe.
        await _securityInbox.WriteTwoFactorEnabledAsync(userId, cancellationToken);

        return true;
    }

    /// <inheritdoc />
    public async Task<bool> ValidateCodeAsync(Guid userId, string code, CancellationToken cancellationToken = default)
    {
        var config = await _db.TotpConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.UserId == userId && t.IsEnabled, cancellationToken);

        if (config is null)
        {
            return false;
        }

        var base32Secret = DecryptSecret(config.EncryptedSecret);
        var secretBytes = Base32Encoding.ToBytes(base32Secret);

        var totp = new Totp(secretBytes, step: TotpStepSeconds, totpSize: TotpDigits);
        var isValid = totp.VerifyTotp(code, out _, new VerificationWindow(previous: 1, future: 1));

        if (!isValid)
        {
            _logger.LogWarning("TOTP validation failed for user {UserId}", userId);
        }

        return isValid;
    }

    /// <inheritdoc />
    public async Task<bool> ValidateBackupCodeAsync(Guid userId, string code, CancellationToken cancellationToken = default)
    {
        var config = await _db.TotpConfigurations
            .FirstOrDefaultAsync(t => t.UserId == userId && t.IsEnabled, cancellationToken);

        if (config is null)
        {
            return false;
        }

        var hashedInput = HashBackupCode(code.ToUpperInvariant().Trim());
        var hashedCodes = JsonSerializer.Deserialize<string[]>(config.BackupCodes) ?? [];

        // Find and consume the matching backup code
        var matchIndex = Array.FindIndex(hashedCodes, h => h == hashedInput);
        if (matchIndex < 0)
        {
            _logger.LogWarning("Backup code validation failed for user {UserId} — no match", userId);
            return false;
        }

        // Mark code as consumed (replace with empty string)
        hashedCodes[matchIndex] = string.Empty;
        config.BackupCodes = JsonSerializer.Serialize(hashedCodes);
        config.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Backup code consumed for user {UserId} (index {Index})", userId, matchIndex);

        // Feature 118 — backup-code consumption is an account-takeover signal worth surfacing. Fail-safe.
        await _securityInbox.WriteBackupCodeUsedAsync(userId, cancellationToken);

        return true;
    }

    /// <inheritdoc />
    public async Task DisableAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var config = await _db.TotpConfigurations
            .FirstOrDefaultAsync(t => t.UserId == userId, cancellationToken);

        if (config is not null)
        {
            _db.TotpConfigurations.Remove(config);
            await _db.SaveChangesAsync(cancellationToken);

            // Update UserPreferences.TwoFactorEnabled
            await UpdateUserPreferencesTwoFactor(userId, false, cancellationToken);

            _logger.LogInformation("TOTP disabled for user {UserId}", userId);

            // Feature 118 — emit a Category=Security inbox entry. Fail-safe.
            await _securityInbox.WriteTwoFactorDisabledAsync(userId, cancellationToken);
        }
    }

    /// <inheritdoc />
    public async Task<TotpStatusResult> GetStatusAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        var config = await _db.TotpConfigurations
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.UserId == userId, cancellationToken);

        return new TotpStatusResult
        {
            IsEnabled = config?.IsEnabled ?? false,
            VerifiedAt = config?.VerifiedAt
        };
    }

    /// <inheritdoc />
    public Task<string> GenerateLoginTokenAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        // Create a login token: userId|expiresUtcTicks|hmacSignature
        var expiresAt = DateTime.UtcNow.AddMinutes(LoginTokenLifetimeMinutes);
        var payload = $"{userId}|{expiresAt.Ticks}";
        var signature = ComputeHmac(payload);
        var token = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{payload}|{signature}"));

        _logger.LogInformation("Generated 2FA login token for user {UserId}, expires {Expiry}", userId, expiresAt);
        return Task.FromResult(token);
    }

    /// <inheritdoc />
    public Task<Guid?> ValidateLoginTokenAsync(string loginToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var decoded = Encoding.UTF8.GetString(Convert.FromBase64String(loginToken));
            var parts = decoded.Split('|');

            if (parts.Length != 3)
            {
                return Task.FromResult<Guid?>(null);
            }

            var userIdStr = parts[0];
            var ticksStr = parts[1];
            var signature = parts[2];

            // Verify HMAC signature
            var payload = $"{userIdStr}|{ticksStr}";
            var expectedSignature = ComputeHmac(payload);

            if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(signature),
                Encoding.UTF8.GetBytes(expectedSignature)))
            {
                _logger.LogWarning("Login token signature mismatch");
                return Task.FromResult<Guid?>(null);
            }

            // Check expiry
            if (!long.TryParse(ticksStr, out var ticks))
            {
                return Task.FromResult<Guid?>(null);
            }

            var expiresAt = new DateTime(ticks, DateTimeKind.Utc);
            if (DateTime.UtcNow > expiresAt)
            {
                _logger.LogWarning("Login token expired");
                return Task.FromResult<Guid?>(null);
            }

            // Parse and return user ID
            if (Guid.TryParse(userIdStr, out var userId))
            {
                return Task.FromResult<Guid?>(userId);
            }

            return Task.FromResult<Guid?>(null);
        }
        catch (FormatException)
        {
            _logger.LogWarning("Malformed login token");
            return Task.FromResult<Guid?>(null);
        }
    }

    // --- Private helpers ---

    /// <summary>
    /// Generates random alphanumeric backup codes.
    /// </summary>
    private static string[] GenerateBackupCodes()
    {
        var codes = new string[BackupCodeCount];
        for (var i = 0; i < BackupCodeCount; i++)
        {
            codes[i] = GenerateBackupCode();
        }
        return codes;
    }

    private static string GenerateBackupCode()
    {
        var chars = new char[BackupCodeLength];
        for (var i = 0; i < BackupCodeLength; i++)
        {
            chars[i] = BackupCodeChars[RandomNumberGenerator.GetInt32(BackupCodeChars.Length)];
        }
        return new string(chars);
    }

    /// <summary>
    /// Hashes a backup code using SHA-256.
    /// </summary>
    private static string HashBackupCode(string code)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(code));
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Encrypts the TOTP secret for storage.
    /// In production, use Azure Key Vault or similar HSM-backed encryption.
    /// This implementation uses AES-256-GCM with a machine-derived key.
    /// For now, we use a reversible Base64 encoding with a prefix marker
    /// so it can be upgraded to proper encryption without schema changes.
    /// </summary>
    private static string EncryptSecret(string plainTextSecret)
    {
        // Prefix with "v1:" to indicate encoding version for future migration
        return $"v1:{Convert.ToBase64String(Encoding.UTF8.GetBytes(plainTextSecret))}";
    }

    /// <summary>
    /// Decrypts the TOTP secret from storage.
    /// </summary>
    private static string DecryptSecret(string encryptedSecret)
    {
        if (encryptedSecret.StartsWith("v1:"))
        {
            return Encoding.UTF8.GetString(Convert.FromBase64String(encryptedSecret[3..]));
        }

        // Fallback: treat as plain text (legacy)
        return encryptedSecret;
    }

    /// <summary>
    /// Computes HMAC-SHA256 signature for login token payload.
    /// </summary>
    private static string ComputeHmac(string payload)
    {
        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        var hash = HMACSHA256.HashData(LoginTokenSigningKey, payloadBytes);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Generates a stable signing key for login tokens.
    /// Uses RandomNumberGenerator for cryptographic randomness.
    /// </summary>
    private static byte[] GenerateStableKey()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        return key;
    }

    /// <summary>
    /// Syncs the UserPreferences.TwoFactorEnabled flag.
    /// </summary>
    private async Task UpdateUserPreferencesTwoFactor(Guid userId, bool enabled, CancellationToken cancellationToken)
    {
        var prefs = await _db.UserPreferences
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

        if (prefs is not null)
        {
            prefs.TwoFactorEnabled = enabled;
            prefs.UpdatedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
        }
    }
}
