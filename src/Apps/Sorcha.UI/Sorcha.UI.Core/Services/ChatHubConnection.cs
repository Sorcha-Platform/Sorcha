// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using Sorcha.UI.Core.Models.Chat;
using Sorcha.UI.Core.Services.Authentication;
using Sorcha.UI.Core.Services.Configuration;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// SignalR client for the Chat Hub with automatic reconnection.
/// </summary>
public class ChatHubConnection : IChatHubConnection
{
    private HubConnection _connection;
    private readonly string _hubUrl;
    private readonly IAuthenticationService _authService;
    private readonly IConfigurationService _configurationService;
    private readonly ILogger<ChatHubConnection> _logger;
    private ChatConnectionState _state = ChatConnectionState.Disconnected;
    private bool _disposed;

    // Reconnection delays: 0s, 2s, 5s, 10s, 30s (per chat-hub.md)
    private static readonly TimeSpan[] ReconnectDelays =
    [
        TimeSpan.Zero,
        TimeSpan.FromSeconds(2),
        TimeSpan.FromSeconds(5),
        TimeSpan.FromSeconds(10),
        TimeSpan.FromSeconds(30)
    ];

    public ChatConnectionState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                OnStateChanged?.Invoke(value);
            }
        }
    }

    public event Action<string>? OnChunkReceived;
    public event Action<string, bool, string?>? OnToolExecuted;
    public event Action<BlueprintModel, ValidationResult>? OnBlueprintUpdated;
    public event Action<string>? OnMessageComplete;
    public event Action<string, string>? OnSessionError;
    public event Action<int>? OnMessageLimitWarning;
    public event Action<ChatConnectionState>? OnStateChanged;
    public event Action<string, BlueprintModel?, int>? OnSessionStarted;

    public ChatHubConnection(
        ChatHubOptions hubOptions,
        IAuthenticationService authService,
        IConfigurationService configurationService,
        ILogger<ChatHubConnection> logger)
    {
        _authService = authService;
        _configurationService = configurationService;
        _logger = logger;

        _hubUrl = $"{hubOptions.BlueprintServiceUrl.TrimEnd('/')}/hubs/chat";
        _connection = BuildConnection();
    }

    private HubConnection BuildConnection()
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(_hubUrl, options =>
            {
                options.AccessTokenProvider = async () =>
                {
                    // Get the current active profile name dynamically
                    var profileName = await _configurationService.GetActiveProfileNameAsync();
                    var token = await _authService.GetAccessTokenAsync(profileName);
                    _logger.LogDebug("ChatHub token provider: profile={Profile}, hasToken={HasToken}",
                        profileName, !string.IsNullOrEmpty(token));
                    return token;
                };
            })
            .WithAutomaticReconnect(new CustomRetryPolicy(ReconnectDelays))
            .Build();

        // AI tool execution can take 30-60+ seconds — increase timeout to prevent
        // the client from disconnecting while the server processes tool calls
        connection.ServerTimeout = TimeSpan.FromMinutes(3);
        connection.KeepAliveInterval = TimeSpan.FromSeconds(15);

        RegisterEventHandlers(connection);
        return connection;
    }

    private void RegisterEventHandlers(HubConnection connection)
    {
        connection.On<string>("ReceiveChunk", chunk =>
        {
            OnChunkReceived?.Invoke(chunk);
        });

        connection.On<string, bool, string?>("ToolExecuted", (toolName, success, error) =>
        {
            OnToolExecuted?.Invoke(toolName, success, error);
        });

        connection.On<BlueprintModel, ValidationResult>("BlueprintUpdated", (blueprint, validation) =>
        {
            OnBlueprintUpdated?.Invoke(blueprint, validation);
        });

        connection.On<string>("MessageComplete", messageId =>
        {
            OnMessageComplete?.Invoke(messageId);
        });

        connection.On<string, string>("SessionError", (code, message) =>
        {
            OnSessionError?.Invoke(code, message);
        });

        connection.On<int>("MessageLimitWarning", remaining =>
        {
            OnMessageLimitWarning?.Invoke(remaining);
        });

        connection.On<string, BlueprintModel?, int>("SessionStarted", (sessionId, blueprint, messageCount) =>
        {
            OnSessionStarted?.Invoke(sessionId, blueprint, messageCount);
        });

        connection.Reconnecting += error =>
        {
            _logger.LogWarning(error, "ChatHub connection lost, reconnecting...");
            State = ChatConnectionState.Reconnecting;
            return Task.CompletedTask;
        };

        connection.Reconnected += connectionId =>
        {
            _logger.LogInformation("ChatHub reconnected with connection ID: {ConnectionId}", connectionId);
            State = ChatConnectionState.Connected;
            return Task.CompletedTask;
        };

        connection.Closed += error =>
        {
            if (error != null)
            {
                _logger.LogError(error, "ChatHub connection closed with error");
            }
            else
            {
                _logger.LogInformation("ChatHub connection closed");
            }
            State = ChatConnectionState.Disconnected;
            return Task.CompletedTask;
        };
    }

    /// <inheritdoc />
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        // Rebuild connection if it was previously disposed (e.g. page navigated away and back)
        if (_disposed)
        {
            _logger.LogInformation("Rebuilding disposed ChatHub connection");
            _connection = BuildConnection();
            _disposed = false;
        }

        if (_connection.State == HubConnectionState.Connected)
        {
            return;
        }

        State = ChatConnectionState.Connecting;

        try
        {
            await _connection.StartAsync(cancellationToken);
            State = ChatConnectionState.Connected;
            _logger.LogInformation("Connected to ChatHub");
        }
        catch (Exception ex)
        {
            State = ChatConnectionState.Disconnected;
            _logger.LogError(ex, "Failed to connect to ChatHub");
            throw;
        }
    }

    /// <inheritdoc />
    public async Task DisconnectAsync()
    {
        if (_connection.State == HubConnectionState.Disconnected)
        {
            return;
        }

        await _connection.StopAsync();
        State = ChatConnectionState.Disconnected;
        _logger.LogInformation("Disconnected from ChatHub");
    }

    /// <inheritdoc />
    public async Task<string> StartSessionAsync(string? existingBlueprintId = null)
    {
        EnsureConnected();
        return await _connection.InvokeAsync<string>("StartSession", existingBlueprintId);
    }

    /// <inheritdoc />
    public async Task SendMessageAsync(string sessionId, string message)
    {
        EnsureConnected();
        await _connection.InvokeAsync("SendMessage", sessionId, message);
    }

    /// <inheritdoc />
    public async Task CancelGenerationAsync(string sessionId)
    {
        EnsureConnected();
        await _connection.InvokeAsync("CancelGeneration", sessionId);
    }

    /// <inheritdoc />
    public async Task<string> SaveBlueprintAsync(string sessionId)
    {
        EnsureConnected();
        return await _connection.InvokeAsync<string>("SaveBlueprint", sessionId);
    }

    /// <inheritdoc />
    public async Task<string> ExportBlueprintAsync(string sessionId, string format)
    {
        EnsureConnected();
        return await _connection.InvokeAsync<string>("ExportBlueprint", sessionId, format);
    }

    /// <inheritdoc />
    public async Task EndSessionAsync(string sessionId)
    {
        EnsureConnected();
        await _connection.InvokeAsync("EndSession", sessionId);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        await _connection.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private void EnsureConnected()
    {
        if (_connection.State != HubConnectionState.Connected)
        {
            throw new InvalidOperationException("Not connected to ChatHub. Call ConnectAsync first.");
        }
    }

    /// <summary>
    /// Custom retry policy for SignalR reconnection.
    /// </summary>
    private class CustomRetryPolicy : IRetryPolicy
    {
        private readonly TimeSpan[] _delays;

        public CustomRetryPolicy(TimeSpan[] delays)
        {
            _delays = delays;
        }

        public TimeSpan? NextRetryDelay(RetryContext retryContext)
        {
            if (retryContext.PreviousRetryCount < _delays.Length)
            {
                return _delays[retryContext.PreviousRetryCount];
            }

            // After all delays exhausted, keep trying with the last delay
            return _delays[^1];
        }
    }
}
