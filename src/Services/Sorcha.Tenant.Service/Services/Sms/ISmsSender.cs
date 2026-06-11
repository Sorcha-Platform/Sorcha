// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

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
}

/// <summary>
/// Config-gated SMS sender. The concrete provider HTTP call (Azure Communication Services SMS, Twilio,
/// etc.) is operator-specific and intentionally left as a clearly-marked integration point — this
/// implementation logs the dispatch so the flow is exercisable in non-production environments without a
/// live provider. Registered only when <see cref="SmsSettings.AcsConnectionString"/> is present.
/// </summary>
public sealed class AcsSmsSender : ISmsSender
{
    private readonly ILogger<AcsSmsSender> _logger;

    /// <summary>Creates a new <see cref="AcsSmsSender"/>.</summary>
    public AcsSmsSender(ILogger<AcsSmsSender> logger) => _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <inheritdoc />
    public Task SendAsync(string toE164, string message, CancellationToken cancellationToken = default)
    {
        // TODO(operator): wire the configured provider's SMS REST/SDK call here. The message contains a
        // one-time code, so do NOT log the body in Production — log metadata only.
        _logger.LogInformation("SMS dispatched to {ToMasked} ({Length} chars)", Mask(toE164), message.Length);
        return Task.CompletedTask;
    }

    private static string Mask(string e164)
        => e164.Length <= 4 ? "****" : string.Concat(new string('*', e164.Length - 4), e164.AsSpan(e164.Length - 4));
}
