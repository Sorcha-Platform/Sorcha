// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace Sorcha.ServiceClients.Http.Hub;

/// <summary>
/// Shared SignalR hub connection builder with JWT auth and automatic reconnection.
/// Used by Sorcha.UI (web), SorchaMobile (MAUI), Sorcha.Agent, and Sorcha.Cli.
/// </summary>
public static class SorchaHubConnectionBuilder
{
    /// <summary>
    /// Builds a HubConnection with JWT authentication and infinite reconnection
    /// using exponential backoff with jitter (1s → 2s → 5s → 10s → 30s, then
    /// 30s indefinitely; each delay is jittered ±20 % to avoid thundering-herd
    /// reconnect after a deploy).
    /// </summary>
    /// <remarks>
    /// The connection retries indefinitely — it never gives up. This is critical for
    /// mobile apps that may lose connectivity for extended periods. Callers should
    /// still handle the <see cref="HubConnection.Closed"/> event for non-retryable
    /// errors (e.g., auth failure, server rejection).
    /// </remarks>
    /// <param name="hubUrl">Full URL to the SignalR hub (e.g., https://gateway.sorcha.dev/hubs/blueprint)</param>
    /// <param name="tokenProvider">Async function that returns the current JWT token</param>
    /// <param name="configureLogging">Optional logging configuration</param>
    public static HubConnection Build(
        string hubUrl,
        Func<Task<string?>> tokenProvider,
        Action<ILoggingBuilder>? configureLogging = null)
    {
        ArgumentNullException.ThrowIfNull(hubUrl);
        ArgumentNullException.ThrowIfNull(tokenProvider);

        var builder = new HubConnectionBuilder()
            .WithUrl(hubUrl, options =>
            {
                options.AccessTokenProvider = tokenProvider;
            })
            .WithAutomaticReconnect(new InfiniteRetryPolicy());

        if (configureLogging is not null)
        {
            builder.ConfigureLogging(configureLogging);
        }

        return builder.Build();
    }

    /// <summary>
    /// Reconnection policy with exponential backoff that retries indefinitely.
    /// Nominal delays: 1s, 2s, 5s, 10s, then 30s forever. Each delay is
    /// jittered ±20 % uniformly to avoid synchronised reconnects across
    /// many clients after a deploy. Floor at 100 ms.
    /// </summary>
    /// <remarks>
    /// Per Feature 118 research R-003. Jitter algorithm follows AWS / Google SRE
    /// guidance. Internal-visible to the test assembly via
    /// <see cref="System.Runtime.CompilerServices.InternalsVisibleToAttribute" />
    /// for direct unit-test coverage.
    /// </remarks>
    internal sealed class InfiniteRetryPolicy : IRetryPolicy
    {
        internal static readonly TimeSpan[] NominalDelays =
        [
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30)
        ];

        internal const double JitterFraction = 0.20;
        internal static readonly TimeSpan MinDelay = TimeSpan.FromMilliseconds(100);

        private readonly Func<double> _randomNextDouble;

        public InfiniteRetryPolicy()
            : this(static () => Random.Shared.NextDouble())
        {
        }

        // Constructor for deterministic unit testing.
        internal InfiniteRetryPolicy(Func<double> randomNextDouble)
        {
            _randomNextDouble = randomNextDouble;
        }

        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            var nominal = retryContext.PreviousRetryCount < NominalDelays.Length
                ? NominalDelays[retryContext.PreviousRetryCount]
                : NominalDelays[^1];

            return ApplyJitter(nominal, _randomNextDouble());
        }

        // Multiplier is in [1 - JitterFraction, 1 + JitterFraction].
        internal static TimeSpan ApplyJitter(TimeSpan nominal, double random)
        {
            var multiplier = 1.0 + (random * 2.0 - 1.0) * JitterFraction;
            var jittered = TimeSpan.FromMilliseconds(nominal.TotalMilliseconds * multiplier);
            return jittered < MinDelay ? MinDelay : jittered;
        }
    }
}
