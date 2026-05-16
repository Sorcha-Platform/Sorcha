// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using Sorcha.AtomicCache;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// IAtomicDistributedCache-backed implementation. URL-safe base64 token id,
/// 24-hour TTL, single-use via GetAndRemoveAsync.
/// </summary>
public sealed class PairingResumptionTokenService : IPairingResumptionTokenService
{
    /// <summary>Cache key prefix for the resumption-token registry.</summary>
    public const string KeyPrefix = "pair:resumption:";

    /// <summary>Resumption-token TTL per F128 data-model.md §R6.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    private const int TokenByteLength = 32;

    private readonly IAtomicDistributedCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<PairingResumptionTokenService> _logger;

    public PairingResumptionTokenService(
        IAtomicDistributedCache cache,
        TimeProvider timeProvider,
        ILogger<PairingResumptionTokenService> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<MintedResumptionToken> MintAsync(Guid platformUserId, CancellationToken ct)
    {
        if (platformUserId == Guid.Empty)
        {
            throw new ArgumentException("PlatformUserId must be non-empty.", nameof(platformUserId));
        }

        var bytes = RandomNumberGenerator.GetBytes(TokenByteLength);
        var token = Base64UrlEncode(bytes);
        var expiresAt = _timeProvider.GetUtcNow().Add(Lifetime);

        await _cache.SetAsync(KeyPrefix + token, platformUserId.ToString("N"), Lifetime, ct)
            .ConfigureAwait(false);

        _logger.LogInformation(
            "Minted pairing-resumption token (platformUserId={PlatformUserId})",
            platformUserId);

        return new MintedResumptionToken(token, expiresAt);
    }

    /// <inheritdoc />
    public async Task<Guid?> RedeemAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        var stored = await _cache.GetAndRemoveAsync(KeyPrefix + token, ct).ConfigureAwait(false);
        if (stored is null)
        {
            return null;
        }

        return Guid.TryParseExact(stored, "N", out var platformUserId) ? platformUserId : null;
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
