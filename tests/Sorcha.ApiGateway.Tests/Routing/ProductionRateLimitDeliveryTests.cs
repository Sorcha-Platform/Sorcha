// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.RegularExpressions;

using FluentAssertions;

using Xunit;

namespace Sorcha.ApiGateway.Tests.Routing;

/// <summary>
/// Regression guard for issue #1437 — the production rate limits must be DELIVERED to the deployed
/// gateway container, not merely present in a config file the image discards.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a green "values-when-loaded" test is not enough.</b> <c>appsettings.Production.json</c> is
/// stripped from every image by <c>.dockerignore</c> ("**/appsettings.*.json"), and deployments do
/// not run <c>ASPNETCORE_ENVIRONMENT=Production</c> on the gateway — so a unit test that binds the
/// <c>RateLimiting</c> section and sees 600/60/60 proves nothing about the running edge, which fell
/// back to the coded <see cref="Sorcha.ServiceDefaults.RateLimitSettings"/> defaults (ApiPermitLimit
/// 100000). The public gateway answered ~100k API req/min/IP unthrottled. The actual config channel
/// for a container is environment variables, so this test asserts <c>docker-compose.yml</c> DELIVERS
/// the limits to the api-gateway service via <c>RateLimiting__*</c> env vars.
/// </para>
/// <para>
/// <b>QueueLimit=0 is load-bearing.</b> A FixedWindow limiter QUEUES overflow up to QueueLimit rather
/// than rejecting; with the coded default queue of 1000 a burst never emits a 429 until it exceeds
/// PermitLimit+QueueLimit in one window. The delivered queue limits must therefore be 0 for a clean
/// 429 at the limit — a permit-limit-only delivery looks fixed but silently still absorbs bursts.
/// </para>
/// <para>
/// The compose defaults are also cross-checked against <c>appsettings.Production.json</c> so the two
/// cannot drift: the file remains the documented source of the intended production posture, and the
/// env-var delivery must match it.
/// </para>
/// </remarks>
public class ProductionRateLimitDeliveryTests
{
    // Keys that MUST be delivered to the gateway container, mapped to their appsettings.Production.json
    // property name so the compose default and the file value are asserted equal.
    private static readonly (string EnvKey, string JsonProperty)[] RequiredLimits =
    [
        ("RateLimiting__ApiPermitLimit", "ApiPermitLimit"),
        ("RateLimiting__ApiQueueLimit", "ApiQueueLimit"),
        ("RateLimiting__AuthenticationPermitLimit", "AuthenticationPermitLimit"),
        ("RateLimiting__AuthenticationQueueLimit", "AuthenticationQueueLimit"),
        ("RateLimiting__StrictTokenLimit", "StrictTokenLimit"),
        ("RateLimiting__StrictQueueLimit", "StrictQueueLimit"),
    ];

    [Fact]
    public void DockerCompose_DeliversProductionRateLimits_ToGateway()
    {
        var env = GatewayEnvironment();

        foreach (var (envKey, _) in RequiredLimits)
        {
            env.Should().ContainKey(envKey,
                $"docker-compose.yml must deliver {envKey} to the api-gateway container — " +
                "appsettings.Production.json is stripped from the image, so env vars are the only " +
                "channel that reaches the deployed edge (issue #1437).");
        }
    }

    [Fact]
    public void DeliveredQueueLimits_AreZero_SoOverflowIsRejectedNotQueued()
    {
        var env = GatewayEnvironment();

        foreach (var queueKey in new[]
                 {
                     "RateLimiting__ApiQueueLimit",
                     "RateLimiting__AuthenticationQueueLimit",
                     "RateLimiting__StrictQueueLimit",
                 })
        {
            ResolveDefault(env[queueKey]).Should().Be("0",
                $"{queueKey} must be 0 so the limiter emits a clean 429 at the permit limit; a " +
                "non-zero queue (the coded default is 1000) absorbs bursts and never rejects.");
        }
    }

    [Fact]
    public void DeliveredLimits_MatchAppsettingsProduction_SoTheyCannotDrift()
    {
        var env = GatewayEnvironment();
        using var doc = JsonDocument.Parse(File.ReadAllText(
            Path.Combine(RepoRoot(), "src", "Services", "Sorcha.ApiGateway", "appsettings.Production.json")));
        var rl = doc.RootElement.GetProperty("RateLimiting");

        foreach (var (envKey, jsonProperty) in RequiredLimits)
        {
            var composeValue = ResolveDefault(env[envKey]);
            var fileValue = rl.GetProperty(jsonProperty).GetRawText();
            composeValue.Should().Be(fileValue,
                $"the docker-compose default for {envKey} must match appsettings.Production.json " +
                $"{jsonProperty} — the file documents the intended production posture and the env-var " +
                "delivery is what actually reaches the container.");
        }
    }

    /// <summary>
    /// Parses the <c>environment:</c> block of the <c>api-gateway</c> service from docker-compose.yml
    /// into a key→raw-value map. A focused indentation-aware scan avoids adding a YAML dependency.
    /// </summary>
    private static IReadOnlyDictionary<string, string> GatewayEnvironment()
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot(), "docker-compose.yml"));
        var result = new Dictionary<string, string>(StringComparer.Ordinal);

        var inGateway = false;
        var inEnv = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimEnd().Length == 0 || line.TrimStart().StartsWith('#'))
            {
                continue;
            }

            var indent = line.Length - line.TrimStart().Length;

            if (indent == 2 && line.TrimStart().StartsWith("api-gateway:", StringComparison.Ordinal))
            {
                inGateway = true;
                inEnv = false;
                continue;
            }

            // A new 2-space service key ends the api-gateway block.
            if (inGateway && indent == 2 && line.TrimStart().EndsWith(':') && !line.TrimStart().StartsWith("api-gateway:"))
            {
                break;
            }

            if (inGateway && indent == 4 && line.TrimStart().StartsWith("environment:", StringComparison.Ordinal))
            {
                inEnv = true;
                continue;
            }

            // A new 4-space key (volumes:, depends_on:, …) ends the environment block.
            if (inGateway && inEnv && indent <= 4)
            {
                inEnv = false;
            }

            if (inGateway && inEnv && indent >= 6)
            {
                var trimmed = line.TrimStart();
                var colon = trimmed.IndexOf(':');
                if (colon <= 0)
                {
                    continue;
                }

                var key = trimmed[..colon].Trim();
                var value = trimmed[(colon + 1)..].Trim();
                result[key] = value;
            }
        }

        return result;
    }

    /// <summary>
    /// Resolves a compose value that may be a <c>${VAR:-default}</c> interpolation to its default,
    /// stripping any wrapping quotes. A plain value is returned unquoted as-is.
    /// </summary>
    private static string ResolveDefault(string raw)
    {
        var m = Regex.Match(raw, @"^\$\{[A-Za-z0-9_]+:-(?<def>[^}]*)\}$");
        var value = m.Success ? m.Groups["def"].Value : raw;
        return value.Trim().Trim('"');
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "docker-compose.yml"))
                && Directory.Exists(Path.Combine(dir.FullName, "src")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException(
            "Could not locate the repository root (a directory containing both docker-compose.yml and " +
            $"src/) by walking up from {AppContext.BaseDirectory}.");
    }
}
