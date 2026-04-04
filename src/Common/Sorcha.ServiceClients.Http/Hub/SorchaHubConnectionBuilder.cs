// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;

namespace Sorcha.ServiceClients.Http.Hub;

/// <summary>
/// Shared SignalR hub connection builder with JWT auth and automatic reconnection.
/// Used by both Sorcha.UI (web) and SorchaMobile (MAUI).
/// </summary>
public static class SorchaHubConnectionBuilder
{
    /// <summary>
    /// Builds a HubConnection with JWT authentication and infinite reconnection
    /// using exponential backoff (1s → 2s → 5s → 10s → 30s, then 30s indefinitely).
    /// </summary>
    /// <remarks>
    /// The connection retries indefinitely — it never gives up. This is critical for
    /// mobile apps that may lose connectivity for extended periods. Callers should
    /// still handle the <see cref="HubConnection.Closed"/> event for non-retryable
    /// errors (e.g., auth failure, server rejection).
    /// </remarks>
    /// <param name="hubUrl">Full URL to the SignalR hub (e.g., https://gateway.sorcha.dev/hubs/actions)</param>
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
    /// Delays: 1s, 2s, 5s, 10s, then 30s forever.
    /// </summary>
    private sealed class InfiniteRetryPolicy : IRetryPolicy
    {
        private static readonly TimeSpan[] Delays =
        [
            TimeSpan.FromSeconds(1),
            TimeSpan.FromSeconds(2),
            TimeSpan.FromSeconds(5),
            TimeSpan.FromSeconds(10),
            TimeSpan.FromSeconds(30)
        ];

        public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
            retryContext.PreviousRetryCount < Delays.Length
                ? Delays[retryContext.PreviousRetryCount]
                : Delays[^1]; // 30s indefinitely
    }
}
