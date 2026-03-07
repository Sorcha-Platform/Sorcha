// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.PeerRouter.Models;

/// <summary>
/// Router configuration parsed from CLI arguments and environment variables.
/// </summary>
public sealed record RouterConfiguration
{
    public int GrpcPort { get; init; } = 5000;
    public int HttpPort { get; init; } = 8080;
    public bool EnableRelay { get; init; }
    public int EventBufferSize { get; init; } = 1000;
    public int PeerTimeoutSeconds { get; init; } = 60;

    /// <summary>
    /// Parses configuration from command-line arguments.
    /// </summary>
    public static RouterConfiguration FromArgs(string[] args)
    {
        var config = new RouterConfiguration();
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
            }
        }

        return config;
    }
}
