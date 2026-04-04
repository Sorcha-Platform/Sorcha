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
    private static readonly TimeSpan[] ReconnectDelays =
    [
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    ];

    /// <summary>
    /// Builds a HubConnection with JWT authentication and exponential backoff reconnection.
    /// </summary>
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
            .WithAutomaticReconnect(ReconnectDelays);

        if (configureLogging is not null)
        {
            builder.ConfigureLogging(configureLogging);
        }

        return builder.Build();
    }
}
