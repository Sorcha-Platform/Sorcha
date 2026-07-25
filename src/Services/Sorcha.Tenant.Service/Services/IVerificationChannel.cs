// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Tenant.Models.Auth;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Services;

/// <summary>Outcome of asking a channel to send a one-time code.</summary>
public enum ChannelSendOutcome
{
    /// <summary>A code was generated and dispatched.</summary>
    Sent,
    /// <summary>The per-subject send cooldown is still active — try again shortly.</summary>
    RateLimited,
    /// <summary>The channel cannot send for this user (e.g. no verified destination).</summary>
    Unavailable
}

/// <summary>
/// A second-factor / step-up verification channel (Feature 150). Server-sent channels (Email US2,
/// SMS US3) generate → deliver → verify a one-time code; the authenticator (TOTP) channel keeps its
/// own verify-only path and is not modelled here. Consumed by BOTH the login-2FA flow and the step-up
/// <see cref="IAuthChallengeService"/>, resolved through <see cref="IVerificationChannelRegistry"/>.
/// </summary>
public interface IVerificationChannel
{
    /// <summary>The proof method this channel produces.</summary>
    ChallengeMethod Kind { get; }

    /// <summary>The assurance tier of a proof from this channel (drives the floor rule).</summary>
    AuthAssuranceTier Tier { get; }

    /// <summary>Generate and deliver a one-time code for the user, scoped to <paramref name="purpose"/>.</summary>
    Task<ChannelSendOutcome> SendAsync(Guid platformUserId, OtpPurpose purpose, CancellationToken ct = default);

    /// <summary>Verify a submitted code (single-use on success).</summary>
    Task<OtpVerifyOutcome> VerifyAsync(Guid platformUserId, OtpPurpose purpose, string code, CancellationToken ct = default);
}

/// <summary>
/// Resolves <see cref="IVerificationChannel"/>s by kind. The set is fixed at DI time: the Email channel
/// is always registered; the SMS channel is registered ONLY when an <c>ISmsSender</c> is configured
/// (US3), so an unconfigured installation never resolves — and therefore never offers — SMS.
/// </summary>
public interface IVerificationChannelRegistry
{
    /// <summary>The channel for <paramref name="kind"/>, or null if not registered on this installation.</summary>
    IVerificationChannel? Resolve(ChallengeMethod kind);

    /// <summary>All registered channels.</summary>
    IReadOnlyList<IVerificationChannel> All { get; }

    /// <summary>True iff an SMS channel is registered (an operator has configured a provider).</summary>
    bool SmsAvailable { get; }
}

/// <inheritdoc />
public sealed class VerificationChannelRegistry : IVerificationChannelRegistry
{
    private readonly Dictionary<ChallengeMethod, IVerificationChannel> _byKind;

    /// <summary>Builds the registry from every <see cref="IVerificationChannel"/> registered in DI.</summary>
    public VerificationChannelRegistry(IEnumerable<IVerificationChannel> channels)
    {
        ArgumentNullException.ThrowIfNull(channels);
        _byKind = channels.ToDictionary(c => c.Kind);
        All = _byKind.Values.ToList();
    }

    /// <inheritdoc />
    public IReadOnlyList<IVerificationChannel> All { get; }

    /// <inheritdoc />
    public IVerificationChannel? Resolve(ChallengeMethod kind)
        => _byKind.TryGetValue(kind, out var channel) ? channel : null;

    /// <inheritdoc />
    public bool SmsAvailable => _byKind.ContainsKey(ChallengeMethod.SmsOtp);
}
