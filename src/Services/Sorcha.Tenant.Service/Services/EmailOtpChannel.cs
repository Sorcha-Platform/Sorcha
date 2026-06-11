// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.EntityFrameworkCore;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Telemetry;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// Emailed one-time-code verification channel (Feature 150 US2) — a Basic second factor. Generates +
/// stores the code via <see cref="IServerSentOtpService"/> and delivers it through the F112
/// transactional-email facade (the Sorcha-branded <c>twofactor-code</c> template). Always registered
/// (every install has email); the subject key is the account-wide PlatformUser id.
/// </summary>
public sealed class EmailOtpChannel : IVerificationChannel
{
    private readonly IServerSentOtpService _otp;
    private readonly ITransactionalEmailService _email;
    private readonly TenantDbContext _db;
    private readonly AuthMetrics _metrics;
    private readonly ILogger<EmailOtpChannel> _logger;

    /// <summary>Creates a new <see cref="EmailOtpChannel"/>.</summary>
    public EmailOtpChannel(
        IServerSentOtpService otp,
        ITransactionalEmailService email,
        TenantDbContext db,
        AuthMetrics metrics,
        ILogger<EmailOtpChannel> logger)
    {
        _otp = otp ?? throw new ArgumentNullException(nameof(otp));
        _email = email ?? throw new ArgumentNullException(nameof(email));
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public ChallengeMethod Kind => ChallengeMethod.EmailOtp;

    /// <inheritdoc />
    public AuthAssuranceTier Tier => AssurancePolicy.TierOfProof(ChallengeMethod.EmailOtp);

    /// <inheritdoc />
    public async Task<ChannelSendOutcome> SendAsync(Guid platformUserId, OtpPurpose purpose, CancellationToken ct = default)
    {
        var user = await _db.PlatformUsers
            .AsNoTracking()
            .Where(u => u.Id == platformUserId)
            .Select(u => new { u.Email, u.DisplayName })
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (user is null || string.IsNullOrWhiteSpace(user.Email))
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

        await _email.SendTwoFactorCodeAsync(
            new TwoFactorCodeDispatch(
                ToEmail: user.Email,
                DisplayName: user.DisplayName,
                Code: generated.Code,
                ExpiresInMinutes: (int)ServerSentOtpService.CodeLifetime.TotalMinutes),
            ct).ConfigureAwait(false);

        _metrics.RecordOtpSend(Kind, "sent");
        _logger.LogInformation("Email OTP dispatched for {PlatformUserId} purpose={Purpose}", platformUserId, purpose);
        return ChannelSendOutcome.Sent;
    }

    /// <inheritdoc />
    public async Task<OtpVerifyOutcome> VerifyAsync(Guid platformUserId, OtpPurpose purpose, string code, CancellationToken ct = default)
    {
        var outcome = await _otp.VerifyAsync(SubjectKey(platformUserId), purpose, code, ct).ConfigureAwait(false);
        _metrics.RecordOtpVerify(Kind, outcome.ToString());
        return outcome;
    }

    // Account-wide subject — an email code follows the person across orgs/hosts.
    private static string SubjectKey(Guid platformUserId) => $"email:{platformUserId:N}";
}
