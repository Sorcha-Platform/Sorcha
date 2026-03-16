// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Sorcha.Wallet.Core.Encryption.Logging;

/// <summary>
/// Structured audit logging for encryption operations with sanitization
/// Implements clarified requirements from 2026-01-11 session:
/// - Log all operations (CreateKey, Encrypt, Decrypt, KeyExists)
/// - Include: timestamp, operation type, keyId, success/failure, user context, performance
/// - Sanitize: Never log plaintext, ciphertext, or key material
/// </summary>
public sealed class EncryptionAuditLogger
{
    private readonly ILogger _logger;
    private readonly string _providerName;

    public EncryptionAuditLogger(ILogger logger, string providerName)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _providerName = providerName ?? throw new ArgumentNullException(nameof(providerName));
    }

    /// <summary>
    /// Logs successful encryption operation
    /// </summary>
    public void LogEncryptSuccess(string keyId, long durationMs, string? userContext = null)
    {
        _logger.LogInformation(
            "Encryption operation succeeded. Provider: {ProviderName}, Operation: {Operation}, KeyId: {KeyId}, " +
            "DurationMs: {DurationMs}, UserContext: {UserContext}, Status: {Status}",
            _providerName,
            "Encrypt",
            SanitizeKeyId(keyId),
            durationMs,
            userContext ?? "None",
            "Success"
        );
    }

    /// <summary>
    /// Logs failed encryption operation
    /// </summary>
    public void LogEncryptFailure(string keyId, Exception exception, long durationMs, string? userContext = null)
    {
        _logger.LogError(
            exception,
            "Encryption operation failed. Provider: {ProviderName}, Operation: {Operation}, KeyId: {KeyId}, " +
            "DurationMs: {DurationMs}, UserContext: {UserContext}, Status: {Status}, ErrorType: {ErrorType}",
            _providerName,
            "Encrypt",
            SanitizeKeyId(keyId),
            durationMs,
            userContext ?? "None",
            "Failure",
            exception.GetType().Name
        );
    }

    /// <summary>
    /// Logs successful decryption operation
    /// </summary>
    public void LogDecryptSuccess(string keyId, long durationMs, string? userContext = null)
    {
        _logger.LogInformation(
            "Decryption operation succeeded. Provider: {ProviderName}, Operation: {Operation}, KeyId: {KeyId}, " +
            "DurationMs: {DurationMs}, UserContext: {UserContext}, Status: {Status}",
            _providerName,
            "Decrypt",
            SanitizeKeyId(keyId),
            durationMs,
            userContext ?? "None",
            "Success"
        );
    }

    /// <summary>
    /// Logs failed decryption operation
    /// </summary>
    public void LogDecryptFailure(string keyId, Exception exception, long durationMs, string? userContext = null)
    {
        _logger.LogError(
            exception,
            "Decryption operation failed. Provider: {ProviderName}, Operation: {Operation}, KeyId: {KeyId}, " +
            "DurationMs: {DurationMs}, UserContext: {UserContext}, Status: {Status}, ErrorType: {ErrorType}",
            _providerName,
            "Decrypt",
            SanitizeKeyId(keyId),
            durationMs,
            userContext ?? "None",
            "Failure",
            exception.GetType().Name
        );
    }

    /// <summary>
    /// Logs successful key creation operation
    /// </summary>
    public void LogCreateKeySuccess(string keyId, long durationMs, string? userContext = null)
    {
        _logger.LogInformation(
            "CreateKey operation succeeded. Provider: {ProviderName}, Operation: {Operation}, KeyId: {KeyId}, " +
            "DurationMs: {DurationMs}, UserContext: {UserContext}, Status: {Status}",
            _providerName,
            "CreateKey",
            SanitizeKeyId(keyId),
            durationMs,
            userContext ?? "None",
            "Success"
        );
    }

    /// <summary>
    /// Logs failed key creation operation
    /// </summary>
    public void LogCreateKeyFailure(string keyId, Exception exception, long durationMs, string? userContext = null)
    {
        _logger.LogError(
            exception,
            "CreateKey operation failed. Provider: {ProviderName}, Operation: {Operation}, KeyId: {KeyId}, " +
            "DurationMs: {DurationMs}, UserContext: {UserContext}, Status: {Status}, ErrorType: {ErrorType}",
            _providerName,
            "CreateKey",
            SanitizeKeyId(keyId),
            durationMs,
            userContext ?? "None",
            "Failure",
            exception.GetType().Name
        );
    }

    /// <summary>
    /// Logs key existence check operation
    /// </summary>
    public void LogKeyExists(string keyId, bool exists, long durationMs, string? userContext = null)
    {
        _logger.LogInformation(
            "KeyExists operation completed. Provider: {ProviderName}, Operation: {Operation}, KeyId: {KeyId}, " +
            "Exists: {Exists}, DurationMs: {DurationMs}, UserContext: {UserContext}, Status: {Status}",
            _providerName,
            "KeyExists",
            SanitizeKeyId(keyId),
            exists,
            durationMs,
            userContext ?? "None",
            "Success"
        );
    }

    /// <summary>
    /// Logs provider initialization
    /// </summary>
    public void LogProviderInitialized(string configuration)
    {
        _logger.LogInformation(
            "Encryption provider initialized. Provider: {ProviderName}, Configuration: {Configuration}",
            _providerName,
            SanitizeConfiguration(configuration)
        );
    }

    /// <summary>
    /// Logs provider key loading (e.g., from disk)
    /// </summary>
    public void LogKeysLoaded(int keyCount, string source)
    {
        _logger.LogInformation(
            "Encryption keys loaded. Provider: {ProviderName}, KeyCount: {KeyCount}, Source: {Source}",
            _providerName,
            keyCount,
            source
        );
    }

    /// <summary>
    /// Sanitizes key ID to prevent logging sensitive information
    /// Strategy: Log key ID as-is (it's a reference, not key material), but truncate if too long
    /// </summary>
    private static string SanitizeKeyId(string keyId)
    {
        if (string.IsNullOrEmpty(keyId))
            return "[empty]";

        // Truncate long key IDs (e.g., Azure Key Vault URLs)
        return keyId.Length > 100 ? $"{keyId[..97]}..." : keyId;
    }

    /// <summary>
    /// Sanitizes configuration to prevent logging secrets
    /// Strategy: Mask sensitive values like connection strings, passwords, keys
    /// </summary>
    private static string SanitizeConfiguration(string configuration)
    {
        if (string.IsNullOrEmpty(configuration))
            return "[empty]";

        // Simple sanitization: mask common sensitive keywords
        var sanitized = configuration;
        sanitized = System.Text.RegularExpressions.Regex.Replace(
            sanitized,
            @"(password|secret|key|token)[:=]\s*[^\s,;]+",
            "$1=***",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase
        );

        return sanitized;
    }
}

/// <summary>
/// Helper for measuring operation duration
/// Usage: using var timer = EncryptionOperationTimer.Start();
/// </summary>
public readonly struct EncryptionOperationTimer : IDisposable
{
    private readonly Stopwatch _stopwatch;

    private EncryptionOperationTimer(Stopwatch stopwatch)
    {
        _stopwatch = stopwatch;
    }

    /// <summary>
    /// Starts a new operation timer
    /// </summary>
    public static EncryptionOperationTimer Start()
    {
        var stopwatch = Stopwatch.StartNew();
        return new EncryptionOperationTimer(stopwatch);
    }

    /// <summary>
    /// Gets elapsed milliseconds
    /// </summary>
    public long ElapsedMilliseconds => _stopwatch.ElapsedMilliseconds;

    public void Dispose()
    {
        _stopwatch.Stop();
    }
}
