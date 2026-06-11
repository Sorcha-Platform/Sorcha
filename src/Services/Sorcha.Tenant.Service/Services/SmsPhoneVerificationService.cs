// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services.Sms;

namespace Sorcha.Tenant.Service.Services;

/// <summary>Outcome of capturing/verifying a phone or enabling SMS (Feature 150 US3).</summary>
public enum SmsFlowOutcome
{
    /// <summary>The action succeeded.</summary>
    Ok,
    /// <summary>The verification code didn't match.</summary>
    InvalidCode,
    /// <summary>No live code (expired / never sent / used).</summary>
    Expired,
    /// <summary>Send cooldown / attempt cap hit.</summary>
    RateLimited,
    /// <summary>No verified phone on file (enable requires a verified number).</summary>
    PhoneNotVerified,
    /// <summary>The supplied phone number was missing or malformed.</summary>
    InvalidPhone
}

/// <summary>
/// Captures + verifies a mobile number and toggles SMS OTP (Feature 150 US3). Capturing a (new)
/// number clears any prior verification and disables SMS; verification sets <c>PhoneVerifiedAt</c>;
/// enabling requires a verified number. The verification code is sent directly via
/// <see cref="ISmsSender"/> (the number isn't verified yet, so the SMS channel can't be used).
/// </summary>
public interface ISmsPhoneVerificationService
{
    /// <summary>Store a candidate number and send it a verification code. Clears prior verification.</summary>
    Task<SmsFlowOutcome> CapturePhoneAsync(Guid platformUserId, string phoneE164, CancellationToken ct = default);

    /// <summary>Verify the code sent to the captured number; on success sets <c>PhoneVerifiedAt</c>.</summary>
    Task<SmsFlowOutcome> VerifyPhoneAsync(Guid platformUserId, string code, CancellationToken ct = default);

    /// <summary>Enable SMS OTP (requires a verified number).</summary>
    Task<SmsFlowOutcome> EnableAsync(Guid platformUserId, CancellationToken ct = default);

    /// <summary>Disable SMS OTP.</summary>
    Task DisableAsync(Guid platformUserId, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class SmsPhoneVerificationService : ISmsPhoneVerificationService
{
    private readonly TenantDbContext _db;
    private readonly IServerSentOtpService _otp;
    private readonly ISmsSender _sms;

    /// <summary>Creates a new <see cref="SmsPhoneVerificationService"/>.</summary>
    public SmsPhoneVerificationService(TenantDbContext db, IServerSentOtpService otp, ISmsSender sms)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _otp = otp ?? throw new ArgumentNullException(nameof(otp));
        _sms = sms ?? throw new ArgumentNullException(nameof(sms));
    }

    /// <inheritdoc />
    public async Task<SmsFlowOutcome> CapturePhoneAsync(Guid platformUserId, string phoneE164, CancellationToken ct = default)
    {
        if (!IsPlausibleE164(phoneE164)) return SmsFlowOutcome.InvalidPhone;

        var user = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Id == platformUserId, ct);
        if (user is null) return SmsFlowOutcome.InvalidPhone;

        // Per-number send guard via the OTP cooldown (subject = the number itself).
        var generated = await _otp.GenerateAsync(PhoneSubject(phoneE164), OtpPurpose.PhoneVerify, ct);
        if (generated.Outcome == OtpSendOutcome.RateLimited || generated.Code is null)
            return SmsFlowOutcome.RateLimited;

        // Changing the number clears prior verification + disables SMS until re-verified.
        user.PhoneNumber = phoneE164;
        user.PhoneVerifiedAt = null;
        await SetSmsEnabledAsync(platformUserId, false, ct, saveUser: false);
        await _db.SaveChangesAsync(ct);

        await _sms.SendAsync(phoneE164, $"Your Sorcha verification code is {generated.Code}.", ct);
        return SmsFlowOutcome.Ok;
    }

    /// <inheritdoc />
    public async Task<SmsFlowOutcome> VerifyPhoneAsync(Guid platformUserId, string code, CancellationToken ct = default)
    {
        var user = await _db.PlatformUsers.FirstOrDefaultAsync(u => u.Id == platformUserId, ct);
        if (user is null || string.IsNullOrWhiteSpace(user.PhoneNumber)) return SmsFlowOutcome.InvalidPhone;

        var outcome = await _otp.VerifyAsync(PhoneSubject(user.PhoneNumber), OtpPurpose.PhoneVerify, code, ct);
        switch (outcome)
        {
            case OtpVerifyOutcome.Verified:
                user.PhoneVerifiedAt = DateTimeOffset.UtcNow;
                await _db.SaveChangesAsync(ct);
                return SmsFlowOutcome.Ok;
            case OtpVerifyOutcome.Invalid:
                return SmsFlowOutcome.InvalidCode;
            case OtpVerifyOutcome.TooManyAttempts:
                return SmsFlowOutcome.RateLimited;
            default:
                return SmsFlowOutcome.Expired;
        }
    }

    /// <inheritdoc />
    public async Task<SmsFlowOutcome> EnableAsync(Guid platformUserId, CancellationToken ct = default)
    {
        var verified = await _db.PlatformUsers
            .AsNoTracking()
            .AnyAsync(u => u.Id == platformUserId && u.PhoneVerifiedAt != null, ct);
        if (!verified) return SmsFlowOutcome.PhoneNotVerified;

        await SetSmsEnabledAsync(platformUserId, true, ct);
        return SmsFlowOutcome.Ok;
    }

    /// <inheritdoc />
    public Task DisableAsync(Guid platformUserId, CancellationToken ct = default)
        => SetSmsEnabledAsync(platformUserId, false, ct);

    private async Task SetSmsEnabledAsync(Guid platformUserId, bool enabled, CancellationToken ct, bool saveUser = true)
    {
        var row = await _db.PlatformUserTwoFactors.FirstOrDefaultAsync(t => t.PlatformUserId == platformUserId, ct);
        if (row is null)
        {
            row = new PlatformUserTwoFactor { PlatformUserId = platformUserId };
            _db.PlatformUserTwoFactors.Add(row);
        }
        row.SmsOtpEnabled = enabled;
        row.UpdatedAt = DateTimeOffset.UtcNow;
        if (saveUser) await _db.SaveChangesAsync(ct);
    }

    private static string PhoneSubject(string phoneE164) => $"phone:{phoneE164}";

    // Minimal E.164 plausibility check: '+' followed by 8–15 digits.
    private static bool IsPlausibleE164(string? phone)
        => !string.IsNullOrWhiteSpace(phone) && phone.Length is >= 9 and <= 16
           && phone[0] == '+' && phone.AsSpan(1).ToArray().All(char.IsDigit);
}
