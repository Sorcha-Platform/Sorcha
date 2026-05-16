// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Globalization;
using System.Security.Cryptography;
using Sorcha.AtomicCache;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// 6-digit pairing short-code transport for F128. Wraps an underlying
/// standalone <see cref="EnrolSessionService"/> token so the redeem flow,
/// telemetry, and single-use semantics are uniform across token-direct
/// and short-code redemptions.
/// </summary>
/// <remarks>
/// <list type="bullet">
///   <item><description>Codes are 6-digit numeric, drawn uniformly from
///   <c>100000..999999</c>. Up to 3 collision retries at mint; exhaustion
///   throws (operational alert).</description></item>
///   <item><description>Cache key: <c>pair:shortcode:{code}</c>; value is
///   the underlying enrol-session JWT. TTL 5 minutes. Single-use enforced
///   by <see cref="IAtomicDistributedCache.GetAndRemoveAsync"/>.</description></item>
///   <item><description>Per-code redeem attempts are throttled via a sibling
///   key <c>pair:shortcode:{code}:attempts</c> with a 5-attempt cap over the
///   code's lifetime — well below the brute-force probability budget for the
///   ~1M code keyspace.</description></item>
/// </list>
/// </remarks>
public sealed class PairingShortCodeService : IPairingShortCodeService
{
    /// <summary>Cache key prefix for the short-code → underlying-token registry.</summary>
    public const string ShortCodeKeyPrefix = "pair:shortcode:";

    /// <summary>Short-code TTL — 5 minutes, per research R3.</summary>
    public static readonly TimeSpan ShortCodeLifetime = TimeSpan.FromMinutes(5);

    /// <summary>Maximum redeem attempts per code over its lifetime.</summary>
    public const int MaxRedeemAttempts = 5;

    /// <summary>Maximum collision retries at mint before throwing.</summary>
    public const int MaxMintCollisionRetries = 3;

    private const int CodeLower = 100_000;
    private const int CodeUpper = 1_000_000;

    private readonly IEnrolSessionService _enrolSessionService;
    private readonly IAtomicDistributedCache _cache;
    private readonly EnrolSessionMetrics _metrics;
    private readonly ILogger<PairingShortCodeService> _logger;

    public PairingShortCodeService(
        IEnrolSessionService enrolSessionService,
        IAtomicDistributedCache cache,
        EnrolSessionMetrics metrics,
        ILogger<PairingShortCodeService> logger)
    {
        _enrolSessionService = enrolSessionService ?? throw new ArgumentNullException(nameof(enrolSessionService));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<MintPairingShortCodeResponse> MintAsync(
        Guid platformUserId,
        PairingShortCodeRoute route,
        CancellationToken ct)
    {
        if (platformUserId == Guid.Empty)
        {
            throw new ArgumentException("PlatformUserId must be non-empty.", nameof(platformUserId));
        }

        // Mint the underlying standalone enrol-session token first. The short
        // code is purely a transport wrapper — it gives nothing the token
        // didn't already give.
        var session = await _enrolSessionService
            .MintAsync(platformUserId, EnrolSessionMode.Standalone, ct)
            .ConfigureAwait(false);

        // Choose a non-colliding 6-digit code. Cache TTL is 5 minutes so
        // collisions are statistically rare; cap retries to bound the rare
        // case where the cache is artificially saturated.
        string? code = null;
        for (var attempt = 0; attempt < MaxMintCollisionRetries; attempt++)
        {
            var candidate = GenerateCode();
            var key = ShortCodeKeyPrefix + candidate;
            var existing = await _cache.GetAsync(key, ct).ConfigureAwait(false);
            if (existing is null)
            {
                await _cache.SetAsync(key, session.SessionToken, ShortCodeLifetime, ct).ConfigureAwait(false);
                code = candidate;
                break;
            }
        }

        if (code is null)
        {
            _logger.LogError(
                "PairingShortCode mint exhausted {Attempts} collision retries for platformUserId={PlatformUserId}",
                MaxMintCollisionRetries, platformUserId);
            throw new InvalidOperationException(
                $"Could not allocate a unique pairing short code after {MaxMintCollisionRetries} attempts.");
        }

        _logger.LogInformation(
            "Minted pairing short code (codeIdHash={Hash}, platformUserId={PlatformUserId}, route={Route})",
            HashCode(code), platformUserId, route);

        return new MintPairingShortCodeResponse(code, session.ExpiresAt);
    }

    /// <inheritdoc />
    public async Task<RedeemShortCodeResult> RedeemAsync(string code, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != 6 || !code.All(char.IsDigit))
        {
            return RedeemShortCodeResult.Fail(
                RedeemPairingShortCodeErrorCode.MalformedCode,
                "Pairing code must be 6 digits.");
        }

        var key = ShortCodeKeyPrefix + code;
        var attemptKey = key + ":attempts";

        // Per-code attempt rate limit. Increment before consuming so that a
        // burst of redeems against a single guessed code locks out quickly.
        var attemptsRaw = await _cache.GetAsync(attemptKey, ct).ConfigureAwait(false);
        var attempts = int.TryParse(attemptsRaw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;

        if (attempts >= MaxRedeemAttempts)
        {
            _logger.LogWarning(
                "PairingShortCode redeem rate-limited (codeIdHash={Hash}, attempts={Attempts})",
                HashCode(code), attempts);
            return RedeemShortCodeResult.Fail(
                RedeemPairingShortCodeErrorCode.RateLimited,
                "Too many attempts. Request a new pairing code.");
        }

        await _cache.SetAsync(attemptKey, (attempts + 1).ToString(CultureInfo.InvariantCulture), ShortCodeLifetime, ct)
            .ConfigureAwait(false);

        var sessionToken = await _cache.GetAndRemoveAsync(key, ct).ConfigureAwait(false);
        if (sessionToken is null)
        {
            // Either the code was never minted, expired, or was already used.
            // The attempt counter we just incremented stays alive (within its
            // own TTL) so brute-force probing is still throttled.
            _logger.LogInformation(
                "PairingShortCode redeem miss (codeIdHash={Hash})",
                HashCode(code));
            return RedeemShortCodeResult.Fail(
                RedeemPairingShortCodeErrorCode.ExpiredCode,
                "This pairing code has expired or already been used.");
        }

        // Hand off to the standard redeem path — single-use of the underlying
        // session token is enforced there. The short-code mapping is already
        // consumed above; replay of the same code falls into the miss branch.
        var inner = await _enrolSessionService.RedeemAsync(sessionToken, ct).ConfigureAwait(false);
        if (inner.IsSuccess)
        {
            _logger.LogInformation(
                "Redeemed pairing short code (codeIdHash={Hash})",
                HashCode(code));
            return RedeemShortCodeResult.Ok(inner.Success!);
        }

        // Map underlying redeem failures into short-code-flavoured copy.
        return inner.Error!.Code switch
        {
            RedeemEnrolSessionErrorCode.Expired =>
                RedeemShortCodeResult.Fail(RedeemPairingShortCodeErrorCode.ExpiredCode, "This pairing code has expired."),
            RedeemEnrolSessionErrorCode.AlreadyUsed =>
                RedeemShortCodeResult.Fail(RedeemPairingShortCodeErrorCode.AlreadyUsedCode, "This pairing code has already been used."),
            _ =>
                RedeemShortCodeResult.Fail(RedeemPairingShortCodeErrorCode.MalformedCode, inner.Error.Message),
        };
    }

    private static string GenerateCode()
    {
        var value = RandomNumberGenerator.GetInt32(CodeLower, CodeUpper);
        return value.ToString("D6", CultureInfo.InvariantCulture);
    }

    private static string HashCode(string code)
    {
        // Surface a short hash in logs so we can correlate mint + redeem
        // without exposing the typed code itself.
        var bytes = System.Text.Encoding.UTF8.GetBytes(code);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash, 0, 4);
    }
}
