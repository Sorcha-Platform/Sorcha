// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.PeerRouter.Models;

/// <summary>
/// Router configuration parsed from CLI arguments and environment variables.
/// CLI arguments take precedence over environment variables, which take precedence over defaults.
/// </summary>
/// <remarks>
/// <para>Environment variables use the prefix <c>PEERROUTER__</c>:</para>
/// <list type="table">
/// <item><term>PEERROUTER__PORT</term><description>gRPC port (default: 5000)</description></item>
/// <item><term>PEERROUTER__HTTP_PORT</term><description>HTTP port (default: 8080)</description></item>
/// <item><term>PEERROUTER__ENABLE_RELAY</term><description>Enable relay mode (default: false)</description></item>
/// <item><term>PEERROUTER__EVENT_BUFFER</term><description>Event buffer size (default: 1000)</description></item>
/// <item><term>PEERROUTER__PEER_TIMEOUT</term><description>Peer timeout seconds (default: 60)</description></item>
/// <item><term>PEERROUTER__PEER_ID</term><description>Router's peer network identity for self-registration prevention</description></item>
/// <item><term>PEERROUTER__NODE_NAME</term><description>Router node name (default: peer-router)</description></item>
/// </list>
/// </remarks>
public sealed record RouterConfiguration
{
    /// <summary>gRPC listen port for peer discovery, heartbeat, and relay services.</summary>
    public int GrpcPort { get; init; } = 5000;

    /// <summary>HTTP listen port for debug page, event stream, health, and peer endpoints.</summary>
    public int HttpPort { get; init; } = 8080;

    /// <summary>When true, the router can forward messages between peers (relay mode).</summary>
    public bool EnableRelay { get; init; }

    /// <summary>Maximum number of events to buffer for the debug event stream.</summary>
    public int EventBufferSize { get; init; } = 1000;

    /// <summary>Seconds without a heartbeat before a peer is marked unhealthy.</summary>
    public int PeerTimeoutSeconds { get; init; } = 60;

    /// <summary>
    /// The router's peer identity on the network. Peers using this ID will be rejected
    /// from registration to prevent the router from appearing in its own peer table.
    /// </summary>
    public string PeerId { get; init; } = "";

    /// <summary>Human-readable name for this router instance.</summary>
    public string NodeName { get; init; } = "peer-router";

    /// <summary>
    /// Parses configuration from CLI arguments, falling back to environment variables, then defaults.
    /// CLI arguments take precedence over environment variables.
    /// </summary>
    public static RouterConfiguration FromArgs(string[] args)
    {
        // Start with environment variable overrides
        var config = FromEnvironment();

        // CLI arguments override environment variables
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--port" when i + 1 < args.Length:
                    config = config with { GrpcPort = int.Parse(args[++i]) };
                    break;
                case "--http-port" when i + 1 < args.Length:
                    config = config with { HttpPort = int.Parse(args[++i]) };
                    break;
                case "--enable-relay":
                    config = config with { EnableRelay = true };
                    break;
                case "--event-buffer" when i + 1 < args.Length:
                    config = config with { EventBufferSize = int.Parse(args[++i]) };
                    break;
                case "--peer-timeout" when i + 1 < args.Length:
                    config = config with { PeerTimeoutSeconds = int.Parse(args[++i]) };
                    break;
                case "--peer-id" when i + 1 < args.Length:
                    config = config with { PeerId = args[++i] };
                    break;
                case "--node-name" when i + 1 < args.Length:
                    config = config with { NodeName = args[++i] };
                    break;
            }
        }

        return config;
    }

    private static RouterConfiguration FromEnvironment()
    {
        var config = new RouterConfiguration();

        if (int.TryParse(Environment.GetEnvironmentVariable("PEERROUTER__PORT"), out var port))
            config = config with { GrpcPort = port };

        if (int.TryParse(Environment.GetEnvironmentVariable("PEERROUTER__HTTP_PORT"), out var httpPort))
            config = config with { HttpPort = httpPort };

        if (bool.TryParse(Environment.GetEnvironmentVariable("PEERROUTER__ENABLE_RELAY"), out var relay))
            config = config with { EnableRelay = relay };

        if (int.TryParse(Environment.GetEnvironmentVariable("PEERROUTER__EVENT_BUFFER"), out var buffer))
            config = config with { EventBufferSize = buffer };

        if (int.TryParse(Environment.GetEnvironmentVariable("PEERROUTER__PEER_TIMEOUT"), out var timeout))
            config = config with { PeerTimeoutSeconds = timeout };

        var peerId = Environment.GetEnvironmentVariable("PEERROUTER__PEER_ID");
        if (!string.IsNullOrEmpty(peerId))
            config = config with { PeerId = peerId };

        var nodeName = Environment.GetEnvironmentVariable("PEERROUTER__NODE_NAME");
        if (!string.IsNullOrEmpty(nodeName))
            config = config with { NodeName = nodeName };

        return config;
    }
}
