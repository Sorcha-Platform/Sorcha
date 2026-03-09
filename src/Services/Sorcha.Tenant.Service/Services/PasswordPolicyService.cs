// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// NIST SP 800-63B password policy: min 12 chars, no complexity rules,
/// HIBP k-Anonymity breach check (SHA-1 prefix lookup).
/// </summary>
public class PasswordPolicyService : IPasswordPolicyService
{
    private const int MinimumLength = 12;
    private const string HibpApiBaseUrl = "https://api.pwnedpasswords.com/range/";
    private static readonly TimeSpan NegativeCacheDuration = TimeSpan.FromHours(24);

    private readonly HttpClient _httpClient;
    private readonly IMemoryCache _cache;
    private readonly ILogger<PasswordPolicyService> _logger;

    /// <inheritdoc cref="PasswordPolicyService"/>
    public PasswordPolicyService(
        HttpClient httpClient,
        IMemoryCache cache,
        ILogger<PasswordPolicyService> logger)
    {
        _httpClient = httpClient;
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task<PasswordValidationResult> ValidateAsync(
        string password, CancellationToken cancellationToken = default)
    {
        var errors = new List<string>();

        // Check minimum length (NIST: no complexity rules, just length)
        if (string.IsNullOrEmpty(password) || password.Length < MinimumLength)
        {
            errors.Add($"Password must be at least {MinimumLength} characters.");
        }

        // Only check breach if length is valid (avoid unnecessary API calls)
        if (errors.Count == 0)
        {
            var isBreached = await CheckBreachedAsync(password, cancellationToken);
            if (isBreached)
            {
                errors.Add("This password has appeared in a data breach and cannot be used.");
            }
        }

        return new PasswordValidationResult
        {
            IsValid = errors.Count == 0,
            Errors = errors
        };
    }

    /// <summary>
    /// Checks if a password appears in the HIBP breach database using k-Anonymity.
    /// Only sends the first 5 characters of the SHA-1 hash to the API.
    /// Negative results are cached for 24 hours to reduce API calls.
    /// </summary>
    private async Task<bool> CheckBreachedAsync(
        string password, CancellationToken cancellationToken)
    {
        // Compute SHA-1 hash of the password
        var sha1Hash = ComputeSha1Hash(password);
        var prefix = sha1Hash[..5];
        var suffix = sha1Hash[5..];

        // Check cache for known-safe passwords
        var cacheKey = $"hibp:safe:{sha1Hash}";
        if (_cache.TryGetValue(cacheKey, out _))
        {
            return false; // Known safe (cached negative result)
        }

        try
        {
            // Query HIBP API with the 5-char prefix
            var response = await _httpClient.GetStringAsync(
                $"{HibpApiBaseUrl}{prefix}", cancellationToken);

            // Parse response: each line is "SUFFIX:COUNT"
            foreach (var line in response.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(':');
                if (parts.Length >= 2
                    && parts[0].Trim().Equals(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true; // Password is breached
                }
            }

            // Not found — cache the negative result for 24h
            _cache.Set(cacheKey, true, NegativeCacheDuration);
            return false;
        }
        catch (HttpRequestException ex)
        {
            // If HIBP API is unavailable, allow the password (fail open)
            // Log for monitoring but don't block registration
            _logger.LogWarning(ex,
                "HIBP API unavailable during password breach check. Allowing password.");
            return false;
        }
    }

    private static string ComputeSha1Hash(string input)
    {
        var bytes = SHA1.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}
