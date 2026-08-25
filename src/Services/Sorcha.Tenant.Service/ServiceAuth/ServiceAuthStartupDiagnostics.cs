// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.WorkloadIdentity;

namespace Sorcha.Tenant.Service.ServiceAuth;

/// <summary>
/// Startup-time operator diagnostics for the F191 service-auth posture (#1420).
/// </summary>
/// <remarks>
/// <para>
/// This exists as a named, injectable method rather than an inline <c>if</c> in <c>Program.cs</c>
/// so the requirement — <b>a mis-flipped deployment must be diagnosable from startup logs</b> — can
/// be asserted deterministically, with a logger the test owns.
/// </para>
/// <para>
/// The assertion previously lived in an integration test that read a log sink attached to a
/// <c>WebApplicationFactory</c> host. Serilog is configured process-wide
/// (<c>AddSerilogLogging</c> → <c>UseSerilog(..., writeToProviders: true)</c>), and with other
/// Serilog-configured hosts alive in the same test process that sink received <b>no events at
/// all</b> — so the test failed, saying shared secrets were not disabled, while the sibling tests
/// in the same class proved every secret-presenting mint was in fact refused (#1507). A test that
/// raises a false alarm about a security control is worse than no test: the trained response to
/// seeing it red is to assume the harness is flaky, which is exactly the reasoning that would wave
/// a real regression through.
/// </para>
/// </remarks>
public static class ServiceAuthStartupDiagnostics
{
    /// <summary>
    /// The warning emitted when shared-secret service authentication has been retired on this
    /// deployment. Public so a test pins the operator-facing wording rather than restating it.
    /// </summary>
    public const string SharedSecretsDisabledMessage =
        "ServiceAuth:DisableSharedSecrets is ENABLED — shared-secret service authentication is disabled; " +
        "only workload-certificate credentials mint service tokens (F191/#1420)";

    /// <summary>
    /// Logs the shared-secret posture at startup when — and only when — secrets are retired.
    /// </summary>
    /// <remarks>
    /// Deliberately silent when the flag is off. The normal posture is not news, and a line on
    /// every start would be noise an operator learns to skip past, which defeats the point.
    /// </remarks>
    /// <param name="logger">The host logger.</param>
    /// <param name="configuration">Configuration carrying <c>ServiceAuth:DisableSharedSecrets</c>.</param>
    /// <returns><c>true</c> when the warning was emitted; otherwise <c>false</c>.</returns>
    public static bool LogSharedSecretPosture(ILogger logger, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentNullException.ThrowIfNull(configuration);

        if (!configuration.GetValue<bool>(WorkloadIdentityConfig.DisableSharedSecrets))
        {
            return false;
        }

        logger.LogWarning("{Message}", SharedSecretsDisabledMessage);
        return true;
    }
}
