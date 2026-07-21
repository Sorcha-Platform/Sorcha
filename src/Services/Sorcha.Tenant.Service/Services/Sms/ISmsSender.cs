// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Options;

namespace Sorcha.Tenant.Service.Services.Sms;

/// <summary>
/// Backend-agnostic SMS sender (Feature 150 US3). Mirrors <c>IEmailSender</c>: an implementation is
/// registered <b>only when an operator configures a provider</b> (e.g. <c>Sms:AcsConnectionString</c>),
/// so an unconfigured installation has no <see cref="ISmsSender"/> in DI — the SMS verification channel
/// is therefore never registered and the option never renders.
/// </summary>
public interface ISmsSender
{
    /// <summary>Sends a plain-text SMS to an E.164 number.</summary>
    Task SendAsync(string toE164, string message, CancellationToken cancellationToken = default);
}

/// <summary>Configuration bound from the <c>Sms</c> section. Presence of a provider gates the feature.</summary>
public class SmsSettings
{
    /// <summary>Azure Communication Services connection string. When set, SMS is enabled.</summary>
    public string? AcsConnectionString { get; set; }

    /// <summary>The sender phone number / short code (E.164) the provider sends from.</summary>
    public string? FromNumber { get; set; }

    /// <summary>
    /// Development escape hatch: when <c>true</c>, <see cref="AcsSmsSender"/> logs the dispatch and
    /// returns without sending, so the SMS flows are exercisable locally without a live provider.
    /// Default <c>false</c> — a configured-but-unimplemented provider then fails loud rather than
    /// silently dropping one-time codes. MUST NOT be set in Production.
    /// </summary>
    public bool AllowNoOpSender { get; set; }
}

/// <summary>
/// Config-gated SMS sender. The concrete provider HTTP call (Azure Communication Services SMS, Twilio,
/// etc.) is operator-specific and <b>not yet implemented</b>. Registered only when
/// <see cref="SmsSettings.AcsConnectionString"/> is present — which is exactly the setting an operator
/// sets when they intend to turn SMS ON.
/// </summary>
/// <remarks>
/// This previously logged "SMS dispatched" and returned without sending — so setting the connection
/// string switched the SMS 2FA / phone-verification surfaces ON while every code silently vanished,
/// and the log claimed success. A user who made SMS their only second factor was then locked out with
/// nothing in the logs to explain it. Configured no longer means implemented: absent the explicit
/// <see cref="SmsSettings.AllowNoOpSender"/> dev flag, a send now throws so the misconfiguration
/// surfaces immediately instead of as undeliverable codes. See issue #1214 for the real-provider work.
/// </remarks>
public sealed class AcsSmsSender : ISmsSender
{
    private readonly ILogger<AcsSmsSender> _logger;
    private readonly SmsSettings _settings;

    /// <summary>Creates a new <see cref="AcsSmsSender"/>.</summary>
    public AcsSmsSender(ILogger<AcsSmsSender> logger, IOptions<SmsSettings> settings)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _settings = settings?.Value ?? throw new ArgumentNullException(nameof(settings));
    }

    /// <inheritdoc />
    public Task SendAsync(string toE164, string message, CancellationToken cancellationToken = default)
    {
        if (!_settings.AllowNoOpSender)
        {
            // Fail loud: do NOT report success for a message that was never sent. The caller
            // (2FA send / phone capture) surfaces this as a failure the user actually sees.
            throw new NotSupportedException(
                "SMS sending is not implemented: AcsSmsSender has no provider call wired in, but "
                + "Sms:AcsConnectionString is set so the SMS surfaces are enabled. Implement the provider "
                + "call (issue #1214), or set Sms:AllowNoOpSender=true to use a logging no-op in "
                + "development. Refusing to silently drop a one-time code.");
        }

        // Dev no-op path (AllowNoOpSender=true). The message contains a one-time code, so log
        // metadata only, and make it unmistakable that nothing was actually delivered.
        _logger.LogWarning(
            "SMS NOT SENT (Sms:AllowNoOpSender dev no-op) to {ToMasked} ({Length} chars) — no provider is wired",
            Mask(toE164), message.Length);
        return Task.CompletedTask;
    }

    private static string Mask(string e164)
        => e164.Length <= 4 ? "****" : string.Concat(new string('*', e164.Length - 4), e164.AsSpan(e164.Length - 4));
}
