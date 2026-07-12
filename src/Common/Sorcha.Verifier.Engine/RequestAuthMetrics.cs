// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Verifier.Engine;

/// <summary>
/// Feature 181 US6 (T058 / FR-028, data-model §7) — verifier request-authentication observability on the
/// shared <c>Sorcha.Trust</c> meter. <c>sorcha_request_auth_total</c> counts request-object validation
/// outcomes by three-state verdict (plus the two hard-refusal codes), so operators can see how often
/// requests are trusted / authentic-untrusted / unverifiable / refused.
/// </summary>
/// <remarks>
/// Recorded wherever <see cref="RequestObjectValidator"/> runs. On the wallet PWA (WASM) the counter records
/// locally; OTLP export applies where the validator runs server-side (the meter is on the ServiceDefaults
/// export allowlist).
/// </remarks>
public static class RequestAuthMetrics
{
    private static readonly Meter Meter = new("Sorcha.Trust", "1.0.0");

    private static readonly Counter<long> RequestAuthTotal = Meter.CreateCounter<long>(
        "sorcha_request_auth_total",
        unit: "requests",
        description: "Verifier request-object authentication outcomes by state.");

    /// <summary>Record a validation outcome. Pass the refusal code when refused, else the three-state verdict.</summary>
    public static void Record(RequestObjectValidationResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var state = result.Refused
            ? (result.RefusalCode ?? RequestObjectErrorCodes.RequestObjectInvalid)
            : (result.AuthState?.Status.ToString() ?? nameof(VerifierAuthStatus.Unverifiable));
        RequestAuthTotal.Add(1, new KeyValuePair<string, object?>("state", state));
    }
}
