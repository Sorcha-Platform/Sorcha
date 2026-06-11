// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sorcha.AtomicCache;

namespace Sorcha.Tenant.Service.Services;

/// <summary>What a server-sent one-time code is for (Feature 150). Scopes the cache key so a
/// login-2FA code can never be replayed against a step-up or phone-verify challenge.</summary>
public enum OtpPurpose
{
    /// <summary>Second factor at login.</summary>
    Login2Fa,
    /// <summary>Step-up proof for a sensitive auth-method mutation.</summary>
    StepUp,
    /// <summary>Confirming delivery when enabling a server-sent 2FA channel.</summary>
    ChannelEnable,
    /// <summary>Verifying ownership of a phone number at SMS-enable time (US3).</summary>
    PhoneVerify
}

/// <summary>Outcome of generating + sending a code.</summary>
public enum OtpSendOutcome
{
    /// <summary>A fresh code was generated and stored; the caller should deliver it.</summary>
    Generated,
    /// <summary>The per-subject send cooldown is still active — caller should not deliver.</summary>
    RateLimited
}

/// <summary>Outcome of verifying a submitted code.</summary>
public enum OtpVerifyOutcome
{
    /// <summary>Code matched and was consumed (single-use).</summary>
    Verified,
    /// <summary>Code did not match (an attempt was consumed).</summary>
    Invalid,
    /// <summary>No live code — never sent, already used, or past its expiry.</summary>
    Expired,
    /// <summary>The attempt cap was reached; the code is now void and a new one must be sent.</summary>
    TooManyAttempts
}

/// <summary>Result of <see cref="IServerSentOtpService.GenerateAsync"/> — carries the plaintext code
/// ONLY on <see cref="OtpSendOutcome.Generated"/>, for the channel to deliver. Never persisted.</summary>
public sealed record OtpGenerateResult(OtpSendOutcome Outcome, string? Code);

/// <summary>
/// Centralises generate → hash + store → verify for the server-sent OTP channels (Email US2, SMS US3).
/// Backed by <see cref="IAtomicDistributedCache"/> (Redis GETDEL / in-memory) so a code is single-use,
/// short-lived, attempt-capped, and send-rate-limited. The plaintext is never stored — only a SHA-256
/// hash. Expiry + attempts are tracked inside the value and checked against an injected
/// <see cref="TimeProvider"/>, with the cache TTL as a backstop.
/// </summary>
public interface IServerSentOtpService
{
    /// <summary>Generate, hash, and store a code for (<paramref name="subjectKey"/>, <paramref name="purpose"/>),
    /// honouring the per-subject send cooldown. Returns the plaintext for the caller to deliver.</summary>
    Task<OtpGenerateResult> GenerateAsync(string subjectKey, OtpPurpose purpose, CancellationToken ct = default);

    /// <summary>Verify a submitted code. Consumes the stored code on success (single-use); counts an
    /// attempt on mismatch and voids the code once the cap is reached.</summary>
    Task<OtpVerifyOutcome> VerifyAsync(string subjectKey, OtpPurpose purpose, string submittedCode, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class ServerSentOtpService : IServerSentOtpService
{
    /// <summary>Code lifetime — long enough to fetch from email/SMS, short enough to limit replay.</summary>
    public static readonly TimeSpan CodeLifetime = TimeSpan.FromMinutes(10);

    /// <summary>Minimum interval between sends for the same subject+purpose.</summary>
    public static readonly TimeSpan SendCooldown = TimeSpan.FromSeconds(30);

    /// <summary>Max wrong guesses before the code is voided.</summary>
    public const int MaxAttempts = 5;

    /// <summary>Number of decimal digits in a code.</summary>
    public const int CodeDigits = 6;

    private readonly IAtomicDistributedCache _cache;
    private readonly TimeProvider _time;

    /// <summary>Creates a new <see cref="ServerSentOtpService"/>.</summary>
    public ServerSentOtpService(IAtomicDistributedCache cache, TimeProvider time)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _time = time ?? throw new ArgumentNullException(nameof(time));
    }

    /// <inheritdoc />
    public async Task<OtpGenerateResult> GenerateAsync(string subjectKey, OtpPurpose purpose, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectKey);

        // Send cooldown — atomic via TrySetIfAbsent. False => a send happened within the cooldown window.
        var cooldownTaken = await _cache.TrySetIfAbsentAsync(CooldownKey(subjectKey, purpose), "1", SendCooldown, ct)
            .ConfigureAwait(false);
        if (!cooldownTaken)
            return new OtpGenerateResult(OtpSendOutcome.RateLimited, null);

        var code = GenerateCode();
        var entry = new OtpEntry(HashCode(code), 0, _time.GetUtcNow().Add(CodeLifetime).ToUnixTimeSeconds());
        // Cache TTL is a backstop slightly beyond the logical expiry; the in-value expiry is authoritative.
        await _cache.SetAsync(CodeKey(subjectKey, purpose), JsonSerializer.Serialize(entry), CodeLifetime + TimeSpan.FromMinutes(1), ct)
            .ConfigureAwait(false);

        return new OtpGenerateResult(OtpSendOutcome.Generated, code);
    }

    /// <inheritdoc />
    public async Task<OtpVerifyOutcome> VerifyAsync(string subjectKey, OtpPurpose purpose, string submittedCode, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectKey);
        var key = CodeKey(subjectKey, purpose);

        var raw = await _cache.GetAsync(key, ct).ConfigureAwait(false);
        if (raw is null)
            return OtpVerifyOutcome.Expired; // never sent, already consumed, or cache-TTL-expired

        OtpEntry entry;
        try { entry = JsonSerializer.Deserialize<OtpEntry>(raw)!; }
        catch (JsonException) { await _cache.RemoveAsync(key, ct).ConfigureAwait(false); return OtpVerifyOutcome.Expired; }

        if (_time.GetUtcNow().ToUnixTimeSeconds() > entry.ExpiresAt)
        {
            await _cache.RemoveAsync(key, ct).ConfigureAwait(false);
            return OtpVerifyOutcome.Expired;
        }

        if (entry.Attempts >= MaxAttempts)
        {
            await _cache.RemoveAsync(key, ct).ConfigureAwait(false);
            return OtpVerifyOutcome.TooManyAttempts;
        }

        if (FixedTimeEquals(entry.Hash, HashCode(submittedCode ?? string.Empty)))
        {
            // Single-use: consume the code atomically so a concurrent verify can't reuse it.
            await _cache.GetAndRemoveAsync(key, ct).ConfigureAwait(false);
            return OtpVerifyOutcome.Verified;
        }

        // Wrong code — burn an attempt, preserving the original expiry (don't extend the code's life).
        var attempts = entry.Attempts + 1;
        if (attempts >= MaxAttempts)
        {
            await _cache.RemoveAsync(key, ct).ConfigureAwait(false);
            return OtpVerifyOutcome.TooManyAttempts;
        }

        var remaining = TimeSpan.FromSeconds(Math.Max(1, entry.ExpiresAt - _time.GetUtcNow().ToUnixTimeSeconds()));
        await _cache.SetAsync(key, JsonSerializer.Serialize(entry with { Attempts = attempts }), remaining, ct).ConfigureAwait(false);
        return OtpVerifyOutcome.Invalid;
    }

    private static string CodeKey(string subjectKey, OtpPurpose purpose) => $"otp:{purpose}:{subjectKey}";
    private static string CooldownKey(string subjectKey, OtpPurpose purpose) => $"otp:cd:{purpose}:{subjectKey}";

    private static string GenerateCode()
    {
        // Uniform 6-digit code (000000–999999), zero-padded.
        var n = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return n.ToString("D" + CodeDigits);
    }

    private static string HashCode(string code)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(Encoding.UTF8.GetBytes(code), hash);
        return Convert.ToHexString(hash);
    }

    private static bool FixedTimeEquals(string a, string b)
        => CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(a), Encoding.ASCII.GetBytes(b));

    /// <summary>Persisted code state. The plaintext is never stored — only its SHA-256 hex.</summary>
    private sealed record OtpEntry(string Hash, int Attempts, long ExpiresAt);
}
