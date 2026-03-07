// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Serialization;

namespace Sorcha.PeerRouter.Models;

/// <summary>
/// A network event captured by the Peer Router for debugging and monitoring.
/// </summary>
public sealed record RouterEvent
{
    public required string Id { get; init; }
    public required RouterEventType Type { get; init; }
    public required DateTimeOffset Timestamp { get; init; }
    public required string PeerId { get; init; }
    public string? NodeName { get; init; }
    public required string IpAddress { get; init; }
    public required int Port { get; init; }

    /// <summary>
    /// Type-specific payload serialized as a JSON object.
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, object?>? Detail { get; init; }

    /// <summary>
    /// Creates a new RouterEvent with a time-sortable ID and current timestamp.
    /// </summary>
    public static RouterEvent Create(
        RouterEventType type,
        string peerId,
        string ipAddress,
        int port,
        string? nodeName = null,
        Dictionary<string, object?>? detail = null) => new()
    {
        Id = $"{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():X}-{Guid.NewGuid():N}"[..24],
        Type = type,
        Timestamp = DateTimeOffset.UtcNow,
        PeerId = peerId,
        NodeName = nodeName,
        IpAddress = ipAddress,
        Port = port,
        Detail = detail
    };
}
