// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Models.Auth;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services.Sms;
using Sorcha.Tenant.Service.Telemetry;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// SMS one-time-code verification channel (Feature 150 US3) — a Basic second factor, registered ONLY
/// when an <see cref="ISmsSender"/> is configured. Generates + stores the code via
/// <see cref="IServerSentOtpService"/> and delivers it to the user's verified phone number.
/// </summary>
public sealed class SmsOtpChannel : IVerificationChannel
{
    private readonly IServerSentOtpService _otp;
    private readonly ISmsSender _sms;
    private readonly TenantDbContext _db;
    private readonly AuthMetrics _metrics;
    private readonly ILogger<SmsOtpChannel> _logger;

    /// <summary>Creates a new <see cref="SmsOtpChannel"/>.</summary>
    public SmsOtpChannel(IServerSentOtpService otp, ISmsSender sms, TenantDbContext db, AuthMetrics metrics, ILogger<SmsOtpChannel> logger)
    {
        _otp = otp ?? throw new ArgumentNullException(nameof(otp));
        _sms = sms ?? throw new ArgumentNullException(nameof(sms));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public ChallengeMethod Kind => ChallengeMethod.SmsOtp;

    /// <inheritdoc />
    public AuthAssuranceTier Tier => AssurancePolicy.TierOfProof(ChallengeMethod.SmsOtp);

    /// <inheritdoc />
    public async Task<ChannelSendOutcome> SendAsync(Guid platformUserId, OtpPurpose purpose, CancellationToken ct = default)
    {
        var user = await _db.PlatformUsers
            .AsNoTracking()
            .Where(u => u.Id == platformUserId)
            .Select(u => new { u.PhoneNumber, u.PhoneVerifiedAt })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        // A verified number is required to receive SMS codes (the PhoneVerify flow is the exception:
        // it sends to a number that isn't verified yet — handled by the phone-verify service directly).
        if (user is null || string.IsNullOrWhiteSpace(user.PhoneNumber) || user.PhoneVerifiedAt is null)
        {
            _metrics.RecordOtpSend(Kind, "unavailable");
            return ChannelSendOutcome.Unavailable;
        }

        var generated = await _otp.GenerateAsync(SubjectKey(platformUserId), purpose, ct).ConfigureAwait(false);
        if (generated.Outcome == OtpSendOutcome.RateLimited || generated.Code is null)
        {
            _metrics.RecordOtpSend(Kind, "rate_limited");
            return ChannelSendOutcome.RateLimited;
        }

        await _sms.SendAsync(user.PhoneNumber, $"Your Sorcha code is {generated.Code}. It expires in "
            + $"{(int)ServerSentOtpService.CodeLifetime.TotalMinutes} minutes.", ct).ConfigureAwait(false);

        _metrics.RecordOtpSend(Kind, "sent");
        _logger.LogInformation("SMS OTP dispatched for {PlatformUserId} purpose={Purpose}", platformUserId, purpose);
        return ChannelSendOutcome.Sent;
    }

    /// <inheritdoc />
    public async Task<OtpVerifyOutcome> VerifyAsync(Guid platformUserId, OtpPurpose purpose, string code, CancellationToken ct = default)
    {
        var outcome = await _otp.VerifyAsync(SubjectKey(platformUserId), purpose, code, ct).ConfigureAwait(false);
        _metrics.RecordOtpVerify(Kind, outcome.ToString());
        return outcome;
    }

    private static string SubjectKey(Guid platformUserId) => $"sms:{platformUserId:N}";
}
