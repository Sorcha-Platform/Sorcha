// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using Microsoft.JSInterop;

namespace Sorcha.UI.Core.Services.Encryption;

/// <summary>
/// Browser-based encryption provider using Web Crypto API.
/// Persists encryption key in LocalStorage to survive page refreshes.
/// Falls back to plaintext storage when Web Crypto is unavailable (HTTP non-localhost).
/// </summary>
public class BrowserEncryptionProvider : IEncryptionProvider
{
    private const string KeyStorageKey = "sorcha:encryption-key";
    private const string PlaintextPrefix = "plain:";
    private readonly IJSRuntime _jsRuntime;
    private string? _cachedKey;
    private bool? _cryptoAvailable;

    public BrowserEncryptionProvider(IJSRuntime jsRuntime)
    {
        _jsRuntime = jsRuntime;
    }

    /// <summary>
    /// Checks and caches whether Web Crypto API is available.
    /// </summary>
    private async Task<bool> IsCryptoAvailableAsync()
    {
        _cryptoAvailable ??= await IsAvailableAsync();
        return _cryptoAvailable.Value;
    }

    /// <summary>
    /// Gets or creates the encryption key.
    /// Key is persisted in LocalStorage to survive page refreshes.
    /// </summary>
    private async Task<string> GetOrCreateKeyAsync()
    {
        // Return cached key if available
        if (!string.IsNullOrEmpty(_cachedKey))
        {
            return _cachedKey;
        }

        // Try to load existing key from LocalStorage
        var existingKey = await _jsRuntime.InvokeAsync<string?>("localStorage.getItem", KeyStorageKey);

        if (!string.IsNullOrEmpty(existingKey))
        {
            _cachedKey = existingKey;
            return existingKey;
        }

        // Generate new key and persist it
        var newKey = await GenerateKeyAsync();
        await _jsRuntime.InvokeVoidAsync("localStorage.setItem", KeyStorageKey, newKey);
        _cachedKey = newKey;

        return newKey;
    }

    /// <inheritdoc />
    public async Task<string> EncryptAsync(string plaintext)
    {
        if (string.IsNullOrEmpty(plaintext))
        {
            throw new ArgumentException("Plaintext cannot be null or empty", nameof(plaintext));
        }

        // Fall back to plaintext when Web Crypto is unavailable (HTTP non-localhost)
        if (!await IsCryptoAvailableAsync())
        {
            return PlaintextPrefix + plaintext;
        }

        var key = await GetOrCreateKeyAsync();
        return await _jsRuntime.InvokeAsync<string>(
            "EncryptionHelper.encrypt",
            plaintext,
            key
        );
    }

    /// <inheritdoc />
    public async Task<string> DecryptAsync(string ciphertext)
    {
        if (string.IsNullOrEmpty(ciphertext))
        {
            throw new ArgumentException("Ciphertext cannot be null or empty", nameof(ciphertext));
        }

        // Handle plaintext fallback values
        if (ciphertext.StartsWith(PlaintextPrefix))
        {
            return ciphertext[PlaintextPrefix.Length..];
        }

        if (!ciphertext.Contains(':'))
        {
            throw new ArgumentException("Invalid ciphertext format. Expected: {iv}:{ciphertext}", nameof(ciphertext));
        }

        var key = await GetOrCreateKeyAsync();
        return await _jsRuntime.InvokeAsync<string>(
            "EncryptionHelper.decrypt",
            ciphertext,
            key
        );
    }

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync()
    {
        try
        {
            return await _jsRuntime.InvokeAsync<bool>("EncryptionHelper.isAvailable");
        }
        catch
        {
            return false;
        }
    }

    /// <inheritdoc />
    public async Task<string> GenerateKeyAsync()
    {
        return await _jsRuntime.InvokeAsync<string>("EncryptionHelper.generateKey");
    }
}
