// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Models.Registers;

/// <summary>
/// Represents the status of a SignalR connection.
/// </summary>
public enum ConnectionStatus
{
    /// <summary>
    /// Not connected to the hub
    /// </summary>
    Disconnected,

    /// <summary>
    /// Attempting to establish connection
    /// </summary>
    Connecting,

    /// <summary>
    /// Successfully connected to the hub
    /// </summary>
    Connected,

    /// <summary>
    /// Connection lost, attempting to reconnect
    /// </summary>
    Reconnecting
}

/// <summary>
/// Tracks SignalR connection state for UI display.
/// </summary>
public record ConnectionState
{
    /// <summary>
    /// Current connection status
    /// </summary>
    public ConnectionStatus Status { get; init; } = ConnectionStatus.Disconnected;

    /// <summary>
    /// Time when last successfully connected
    /// </summary>
    public DateTime? LastConnected { get; init; }

    /// <summary>
    /// Number of reconnection attempts since last successful connection
    /// </summary>
    public int ReconnectAttempts { get; init; }

    /// <summary>
    /// Error message if connection failed
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Computed: Whether the connection is healthy
    /// </summary>
    public bool IsHealthy => Status == ConnectionStatus.Connected;

    /// <summary>
    /// Computed: Status color for UI display
    /// </summary>
    public string StatusColor => Status switch
    {
        ConnectionStatus.Connected => "success",
        ConnectionStatus.Connecting => "info",
        ConnectionStatus.Reconnecting => "warning",
        ConnectionStatus.Disconnected => "error",
        _ => "default"
    };

    /// <summary>
    /// Computed: Status icon name for UI display
    /// </summary>
    public string StatusIcon => Status switch
    {
        ConnectionStatus.Connected => "SignalCellularAlt",
        ConnectionStatus.Connecting => "Sync",
        ConnectionStatus.Reconnecting => "SyncProblem",
        ConnectionStatus.Disconnected => "SignalCellularOff",
        _ => "HelpOutline"
    };

    /// <summary>
    /// Computed: Human-readable status text
    /// </summary>
    public string StatusText => Status switch
    {
        ConnectionStatus.Connected => "Connected",
        ConnectionStatus.Connecting => "Connecting...",
        ConnectionStatus.Reconnecting => $"Reconnecting ({ReconnectAttempts})...",
        ConnectionStatus.Disconnected => ErrorMessage ?? "Disconnected",
        _ => "Unknown"
    };
}
