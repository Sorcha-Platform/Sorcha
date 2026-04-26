// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// OpenTelemetry metrics for the social-login subsystem. Feature 115 FR-018.
/// Operators consume these counters via the Aspire dashboard or Grafana to
/// detect anomalies in signup-refusal volumes without needing to read raw
/// logs. PII is never tagged on these metrics.
/// </summary>
public static class SocialLoginMetrics
{
    /// <summary>Meter name; matches the existing project-level convention.</summary>
    public const string MeterName = "Sorcha.Tenant";

    private static readonly Meter Meter = new(MeterName, "1.0.0");

    private static readonly Counter<long> RefusalCounter = Meter.CreateCounter<long>(
        "sorcha_social_login_refusal_total",
        unit: "{refusal}",
        description: "Count of refused social-login attempts, tagged by provider and reason.");

    /// <summary>
    /// Record a refusal under the strict link policy. Maps a
    /// <see cref="SocialLoginRefusal"/> enum value to a stable string tag
    /// so dashboards can group reliably.
    /// </summary>
    /// <param name="provider">The provider name (e.g. "Google", "GitHub").</param>
    /// <param name="refusal">The refusal reason. <see cref="SocialLoginRefusal.None"/> is treated as a no-op.</param>
    public static void RecordRefusal(string provider, SocialLoginRefusal refusal)
    {
        if (refusal == SocialLoginRefusal.None)
        {
            return;
        }

        var reason = refusal switch
        {
            SocialLoginRefusal.ProviderUnverified => "provider_unverified",
            SocialLoginRefusal.ExistingUnverified => "existing_unverified",
            _ => "unknown",
        };

        RefusalCounter.Add(1,
            new KeyValuePair<string, object?>("provider", provider),
            new KeyValuePair<string, object?>("reason", reason));
    }

    /// <summary>
    /// Record an upstream failure (state invalid, code-exchange failed) so the
    /// same dashboard panel can surface non-policy-gate refusals too.
    /// </summary>
    /// <param name="provider">The provider name; may be empty when state cannot be deserialised.</param>
    /// <param name="reasonTag">A short stable tag (e.g. "state_invalid", "code_exchange_failed").</param>
    public static void RecordUpstreamFailure(string provider, string reasonTag)
    {
        RefusalCounter.Add(1,
            new KeyValuePair<string, object?>("provider", string.IsNullOrEmpty(provider) ? "unknown" : provider),
            new KeyValuePair<string, object?>("reason", reasonTag));
    }
}
