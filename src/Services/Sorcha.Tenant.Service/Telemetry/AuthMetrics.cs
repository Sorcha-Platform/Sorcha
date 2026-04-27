// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using Sorcha.Tenant.Service.Models;

namespace Sorcha.Tenant.Service.Telemetry;

/// <summary>
/// OpenTelemetry instruments for the Tenant Service auth-method surface
/// (Feature 116). Exported on the <c>Sorcha.Tenant.Auth</c> meter so dashboards
/// can show challenge throughput, success rate, last-method-floor blocks,
/// and email-collision rejections without scraping logs.
/// </summary>
public sealed class AuthMetrics : IDisposable
{
    /// <summary>Meter name under which counters are published.</summary>
    public const string MeterName = "Sorcha.Tenant.Auth";

    private readonly Meter _meter;
    private readonly Counter<long> _challengeIssued;
    private readonly Counter<long> _challengeConsumed;
    private readonly Counter<long> _methodAdded;
    private readonly Counter<long> _methodRemoved;
    private readonly Counter<long> _floorBlocked;
    private readonly Counter<long> _linkCollision;

    /// <summary>Creates a new <see cref="AuthMetrics"/> instance.</summary>
    public AuthMetrics(IMeterFactory meterFactory)
    {
        ArgumentNullException.ThrowIfNull(meterFactory);
        _meter = meterFactory.Create(MeterName);

        _challengeIssued = _meter.CreateCounter<long>(
            "sorcha_auth_challenge_issued_total",
            description: "Re-authentication challenge initiate+verify pairs that produced a token.");

        _challengeConsumed = _meter.CreateCounter<long>(
            "sorcha_auth_challenge_consumed_total",
            description: "Filter-side consume attempts. Tagged with outcome ∈ {success, mismatch, expired, replay}.");

        _methodAdded = _meter.CreateCounter<long>(
            "sorcha_auth_method_added_total",
            description: "Sign-in methods successfully added (kind ∈ {password, social, passkey}).");

        _methodRemoved = _meter.CreateCounter<long>(
            "sorcha_auth_method_removed_total",
            description: "Sign-in methods successfully removed.");

        _floorBlocked = _meter.CreateCounter<long>(
            "sorcha_auth_floor_blocked_total",
            description: "Server-side last-method-floor rejections.");

        _linkCollision = _meter.CreateCounter<long>(
            "sorcha_auth_link_collision_total",
            description: "Social-link rejections due to email collision against another PlatformUser.");
    }

    /// <summary>Record a challenge issuance attempt result.</summary>
    public void RecordChallengeIssued(ChallengeMethod method, ScopedOperation scope, bool success)
    {
        _challengeIssued.Add(1,
            new KeyValuePair<string, object?>("method", method.ToString()),
            new KeyValuePair<string, object?>("scope", scope.ToString()),
            new KeyValuePair<string, object?>("success", success));
    }

    /// <summary>Record a filter-side consume outcome.</summary>
    public void RecordChallengeConsumed(ChallengeMethod method, ScopedOperation scope, ChallengeConsumeOutcome outcome)
    {
        _challengeConsumed.Add(1,
            new KeyValuePair<string, object?>("method", method.ToString()),
            new KeyValuePair<string, object?>("scope", scope.ToString()),
            new KeyValuePair<string, object?>("outcome", outcome.ToString()));
    }

    /// <summary>Record a successful method addition.</summary>
    public void RecordMethodAdded(AuthMethodKindTag kind)
        => _methodAdded.Add(1, new KeyValuePair<string, object?>("kind", kind.ToString()));

    /// <summary>Record a successful method removal.</summary>
    public void RecordMethodRemoved(AuthMethodKindTag kind)
        => _methodRemoved.Add(1, new KeyValuePair<string, object?>("kind", kind.ToString()));

    /// <summary>Record a server-side floor-protection rejection.</summary>
    public void RecordFloorBlocked(AuthMethodKindTag kind)
        => _floorBlocked.Add(1, new KeyValuePair<string, object?>("kind", kind.ToString()));

    /// <summary>Record a social-link rejection due to email collision.</summary>
    public void RecordLinkCollision(string provider)
        => _linkCollision.Add(1, new KeyValuePair<string, object?>("provider", provider));

    /// <inheritdoc />
    public void Dispose() => _meter.Dispose();
}

/// <summary>Filter-side consume outcomes recorded on <c>sorcha_auth_challenge_consumed_total</c>.</summary>
public enum ChallengeConsumeOutcome
{
    /// <summary>Token presented, atomic-consume succeeded, mutation proceeded.</summary>
    Success = 0,

    /// <summary>Token belonged to a different PlatformUser or was for the wrong operation.</summary>
    Mismatch = 1,

    /// <summary>Token's <c>ExpiresAt</c> was in the past at the time of presentation.</summary>
    Expired = 2,

    /// <summary>Token had already been consumed (replay attempt).</summary>
    Replay = 3,

    /// <summary>No <c>X-Auth-Challenge</c> header on the request.</summary>
    Missing = 4
}

/// <summary>String tag for the <c>kind</c> dimension on add/remove/floor counters.</summary>
public enum AuthMethodKindTag
{
    /// <summary>The account password.</summary>
    Password = 0,

    /// <summary>A linked social provider.</summary>
    Social = 1,

    /// <summary>A registered passkey.</summary>
    Passkey = 2
}
