// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.CommandLine;
using System.CommandLine.Parsing;
using System.Net;
using Refit;
using Sorcha.Cli.Infrastructure;
using Sorcha.Cli.Models;
using Sorcha.Cli.Services;

namespace Sorcha.Cli.Commands;

/// <summary>
/// Peer network monitoring commands.
/// </summary>
public class PeerCommand : Command
{
    public PeerCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("peer", "Monitor peer network and node health\n\nExamples:\n  sorcha peer list\n  sorcha peer health\n  sorcha peer subscriptions\n  sorcha peer subscribe --register-id <id>")
    {
        Subcommands.Add(new PeerListCommand(clientFactory, authService, configService));
        Subcommands.Add(new PeerGetCommand(clientFactory, authService, configService));
        Subcommands.Add(new PeerStatsCommand(clientFactory, authService, configService));
        Subcommands.Add(new PeerHealthCommand(clientFactory, authService, configService));
        Subcommands.Add(new PeerQualityCommand(clientFactory, authService, configService));
        Subcommands.Add(new PeerSubscriptionsCommand(clientFactory, authService, configService));
        Subcommands.Add(new PeerSubscribeCommand(clientFactory, authService, configService));
        Subcommands.Add(new PeerUnsubscribeCommand(clientFactory, authService, configService));
        Subcommands.Add(new PeerBanCommand(clientFactory, authService, configService));
        Subcommands.Add(new PeerResetCommand(clientFactory, authService, configService));
    }
}

/// <summary>
/// Lists all known peers.
/// </summary>
public class PeerListCommand : Command
{
    public PeerListCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("list", "List all known peers in the network")
    {
        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token (peer monitoring may not require auth, but we'll try)
                var token = await authService.GetAccessTokenAsync(profileName);
                var authHeader = string.IsNullOrEmpty(token) ? "" : $"Bearer {token}";

                // Create Peer Service client
                var client = await clientFactory.CreatePeerServiceClientAsync(profileName);

                // Call API
                var peers = await client.ListPeersAsync(authHeader);

                // Display results
                if (peers == null || peers.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No peers found in the network.");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Found {peers.Count} peer(s) in the network:");
                Console.WriteLine();

                // Display as table
                Console.WriteLine($"{"Peer ID",-40} {"Address",-25} {"Status",-10} {"Latency",-10} {"Last Seen"}");
                Console.WriteLine(new string('-', 110));

                foreach (var peer in peers.OrderBy(p => p.PeerId))
                {
                    var status = peer.FailureCount == 0 ? "Healthy" :
                                peer.FailureCount < 3 ? "Degraded" : "Unhealthy";
                    var lastSeen = peer.LastSeen.ToString("yyyy-MM-dd HH:mm");
                    var address = $"{peer.Address}:{peer.Port}";
                    var latency = $"{peer.AverageLatencyMs}ms";

                    Console.WriteLine($"{peer.PeerId,-40} {address,-25} {status,-10} {latency,-10} {lastSeen}");
                }

                // Summary
                Console.WriteLine();
                var healthy = peers.Count(p => p.FailureCount == 0);
                var degraded = peers.Count(p => p.FailureCount > 0 && p.FailureCount < 3);
                var unhealthy = peers.Count(p => p.FailureCount >= 3);
                ConsoleHelper.WriteInfo($"Network Summary: {healthy} healthy, {degraded} degraded, {unhealthy} unhealthy");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.ServiceUnavailable)
            {
                ConsoleHelper.WriteError("Peer Service is unavailable. Make sure the service is running.");
                return ExitCodes.GeneralError;
            }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Failed to connect to Peer Service: {ex.Message}");
                ConsoleHelper.WriteInfo("Make sure the Peer Service is running and the profile URL is correct.");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to list peers: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Gets details about a specific peer.
/// </summary>
public class PeerGetCommand : Command
{
    private readonly Option<string> _peerIdOption;

    public PeerGetCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("get", "Get detailed information about a specific peer")
    {
        _peerIdOption = new Option<string>("--id", "-i")
        {
            Description = "Peer ID",
            Required = true
        };

        Options.Add(_peerIdOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var peerId = parseResult.GetValue(_peerIdOption)!;

            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                var authHeader = string.IsNullOrEmpty(token) ? "" : $"Bearer {token}";

                // Create Peer Service client
                var client = await clientFactory.CreatePeerServiceClientAsync(profileName);

                // Call API
                var peer = await client.GetPeerAsync(peerId, authHeader);

                // Display results
                ConsoleHelper.WriteSuccess("Peer details:");
                Console.WriteLine();
                Console.WriteLine($"  Peer ID:         {peer.PeerId}");
                Console.WriteLine($"  Address:         {peer.Address}");
                Console.WriteLine($"  Port:            {peer.Port}");
                Console.WriteLine($"  Protocols:       {string.Join(", ", peer.SupportedProtocols)}");
                Console.WriteLine($"  First Seen:      {peer.FirstSeen:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"  Last Seen:       {peer.LastSeen:yyyy-MM-dd HH:mm:ss}");
                Console.WriteLine($"  Failure Count:   {peer.FailureCount}");
                Console.WriteLine($"  Bootstrap Node:  {(peer.IsBootstrapNode ? "Yes" : "No")}");
                Console.WriteLine($"  Avg Latency:     {peer.AverageLatencyMs}ms");

                // Status assessment
                Console.WriteLine();
                if (peer.FailureCount == 0)
                {
                    ConsoleHelper.WriteSuccess("Status: Healthy - peer is responding normally");
                }
                else if (peer.FailureCount < 3)
                {
                    ConsoleHelper.WriteWarning($"Status: Degraded - peer has {peer.FailureCount} recent failure(s)");
                }
                else
                {
                    ConsoleHelper.WriteError($"Status: Unhealthy - peer has {peer.FailureCount} failures");
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Peer '{peerId}' not found.");
                return ExitCodes.NotFound;
            }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Failed to connect to Peer Service: {ex.Message}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to get peer details: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Shows peer network statistics.
/// </summary>
public class PeerStatsCommand : Command
{
    public PeerStatsCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("stats", "Display peer network statistics")
    {
        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                var authHeader = string.IsNullOrEmpty(token) ? "" : $"Bearer {token}";

                // Create Peer Service client
                var client = await clientFactory.CreatePeerServiceClientAsync(profileName);

                // Call API
                var stats = await client.GetStatisticsAsync(authHeader);

                // Display results
                ConsoleHelper.WriteSuccess("Peer Network Statistics:");
                Console.WriteLine();
                Console.WriteLine($"  Timestamp:       {stats.Timestamp:yyyy-MM-dd HH:mm:ss}");

                // Peer Statistics
                Console.WriteLine();
                Console.WriteLine("  PEER NETWORK:");
                Console.WriteLine($"    Total Peers:     {stats.PeerStats.TotalPeers}");
                Console.WriteLine($"    Healthy Peers:   {stats.PeerStats.HealthyPeers}");
                Console.WriteLine($"    Unhealthy Peers: {stats.PeerStats.UnhealthyPeers}");
                Console.WriteLine($"    Seed Nodes:      {stats.PeerStats.SeedNodes}");
                Console.WriteLine($"    Avg Latency:     {stats.PeerStats.AverageLatencyMs:F2}ms");
                Console.WriteLine($"    Total Failures:  {stats.PeerStats.TotalFailures}");

                // Quality Statistics
                Console.WriteLine();
                Console.WriteLine("  CONNECTION QUALITY:");
                Console.WriteLine($"    Tracked Peers:   {stats.QualityStats.TotalTrackedPeers}");
                Console.WriteLine($"    Excellent:       {stats.QualityStats.ExcellentPeers}");
                Console.WriteLine($"    Good:            {stats.QualityStats.GoodPeers}");
                Console.WriteLine($"    Fair:            {stats.QualityStats.FairPeers}");
                Console.WriteLine($"    Poor:            {stats.QualityStats.PoorPeers}");
                Console.WriteLine($"    Avg Quality:     {stats.QualityStats.AverageQualityScore:F1}/100");

                // Queue Statistics
                Console.WriteLine();
                Console.WriteLine("  TRANSACTION QUEUE:");
                Console.WriteLine($"    Queue Size:      {stats.QueueStats.QueueSize}");
                Console.WriteLine($"    Status:          {(stats.QueueStats.IsEmpty ? "Empty" : "Processing")}");

                // Circuit Breaker Statistics
                Console.WriteLine();
                Console.WriteLine("  CIRCUIT BREAKERS:");
                Console.WriteLine($"    Total Breakers:  {stats.CircuitBreakerStats.TotalCircuitBreakers}");
                Console.WriteLine($"    Open:            {stats.CircuitBreakerStats.OpenCircuits}");
                Console.WriteLine($"    Half-Open:       {stats.CircuitBreakerStats.HalfOpenCircuits}");
                Console.WriteLine($"    Closed:          {stats.CircuitBreakerStats.ClosedCircuits}");

                // Health assessment
                Console.WriteLine();
                var healthPercentage = stats.PeerStats.TotalPeers > 0
                    ? (double)stats.PeerStats.HealthyPeers / stats.PeerStats.TotalPeers * 100
                    : 0;

                if (healthPercentage >= 80)
                {
                    ConsoleHelper.WriteSuccess($"Network Health: Excellent ({healthPercentage:F1}% healthy)");
                }
                else if (healthPercentage >= 60)
                {
                    ConsoleHelper.WriteWarning($"Network Health: Good ({healthPercentage:F1}% healthy)");
                }
                else if (healthPercentage >= 40)
                {
                    ConsoleHelper.WriteWarning($"Network Health: Degraded ({healthPercentage:F1}% healthy)");
                }
                else
                {
                    ConsoleHelper.WriteError($"Network Health: Critical ({healthPercentage:F1}% healthy)");
                }

                return ExitCodes.Success;
            }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Failed to connect to Peer Service: {ex.Message}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to get peer statistics: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Checks peer network health.
/// </summary>
public class PeerHealthCommand : Command
{
    public PeerHealthCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("health", "Check peer network health status")
    {
        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            try
            {
                // Get active profile
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await authService.GetAccessTokenAsync(profileName);
                var authHeader = string.IsNullOrEmpty(token) ? "" : $"Bearer {token}";

                // Create Peer Service client
                var client = await clientFactory.CreatePeerServiceClientAsync(profileName);

                // Call API
                var health = await client.GetHealthAsync(authHeader);

                // Display results
                ConsoleHelper.WriteSuccess("Peer Network Health:");
                Console.WriteLine();
                Console.WriteLine($"  Total Peers:      {health.TotalPeers}");
                Console.WriteLine($"  Healthy Peers:    {health.HealthyPeers}");
                Console.WriteLine($"  Unhealthy Peers:  {health.UnhealthyPeers}");
                Console.WriteLine($"  Health:           {health.HealthPercentage:F1}%");

                // Display healthy peers
                if (health.Peers.Count > 0)
                {
                    Console.WriteLine();
                    ConsoleHelper.WriteInfo($"Healthy Peers ({health.Peers.Count}):");
                    Console.WriteLine($"  {"Peer ID",-40} {"Address",-25} {"Latency",-10} {"Last Seen"}");
                    Console.WriteLine($"  {new string('-', 95)}");

                    foreach (var peer in health.Peers.Take(10))
                    {
                        var address = $"{peer.Address}:{peer.Port}";
                        var latency = $"{peer.AverageLatencyMs}ms";
                        var lastSeen = peer.LastSeen.ToString("yyyy-MM-dd HH:mm");
                        Console.WriteLine($"  {peer.PeerId,-40} {address,-25} {latency,-10} {lastSeen}");
                    }

                    if (health.Peers.Count > 10)
                    {
                        Console.WriteLine($"  ... and {health.Peers.Count - 10} more");
                    }
                }

                // Health status
                Console.WriteLine();
                if (health.HealthPercentage >= 80)
                {
                    ConsoleHelper.WriteSuccess("Network is healthy");
                }
                else if (health.HealthPercentage >= 60)
                {
                    ConsoleHelper.WriteWarning("Network health is degraded");
                }
                else if (health.HealthPercentage >= 40)
                {
                    ConsoleHelper.WriteError("Network health is poor");
                }
                else
                {
                    ConsoleHelper.WriteError("Network health is critical");
                }

                return ExitCodes.Success;
            }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Failed to connect to Peer Service: {ex.Message}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to get peer health: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Shows connection quality scores for all peers.
/// </summary>
public class PeerQualityCommand : Command
{
    public PeerQualityCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("quality", "Display connection quality scores for all peers")
    {
        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            try
            {
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";
                var token = await authService.GetAccessTokenAsync(profileName);
                var authHeader = string.IsNullOrEmpty(token) ? "" : $"Bearer {token}";
                var client = await clientFactory.CreatePeerServiceClientAsync(profileName);

                var qualities = await client.GetQualityScoresAsync(authHeader);

                if (qualities == null || qualities.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No quality data available yet.");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"Connection quality for {qualities.Count} peer(s):");
                Console.WriteLine();
                Console.WriteLine($"{"Peer ID",-35} {"Score",-8} {"Rating",-12} {"Latency",-10} {"Success",-10} {"Requests"}");
                Console.WriteLine(new string('-', 100));

                foreach (var q in qualities.OrderByDescending(q => q.QualityScore))
                {
                    Console.WriteLine($"{q.PeerId,-35} {q.QualityScore,-8:F1} {q.QualityRating,-12} {q.AverageLatencyMs,-10:F0}ms {q.SuccessRate,-10:P0} {q.TotalRequests}");
                }

                return ExitCodes.Success;
            }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Failed to connect to Peer Service: {ex.Message}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to get quality data: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Lists register subscriptions.
/// </summary>
public class PeerSubscriptionsCommand : Command
{
    public PeerSubscriptionsCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("subscriptions", "List register subscriptions and sync progress")
    {
        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            try
            {
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";
                var token = await authService.GetAccessTokenAsync(profileName);
                var authHeader = string.IsNullOrEmpty(token) ? "" : $"Bearer {token}";
                var client = await clientFactory.CreatePeerServiceClientAsync(profileName);

                var subs = await client.GetSubscriptionsAsync(authHeader);

                if (subs == null || subs.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No register subscriptions.");
                    return ExitCodes.Success;
                }

                ConsoleHelper.WriteSuccess($"{subs.Count} subscription(s):");
                Console.WriteLine();
                Console.WriteLine($"{"Register ID",-30} {"Mode",-15} {"State",-20} {"Progress",-10} {"Last Sync"}");
                Console.WriteLine(new string('-', 100));

                foreach (var s in subs)
                {
                    var progress = $"{s.SyncProgressPercent:F1}%";
                    var lastSync = s.LastSyncAt?.ToString("yyyy-MM-dd HH:mm") ?? "Never";
                    Console.WriteLine($"{s.RegisterId,-30} {s.Mode,-15} {s.SyncState,-20} {progress,-10} {lastSync}");
                }

                return ExitCodes.Success;
            }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Failed to connect to Peer Service: {ex.Message}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to get subscriptions: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Subscribes to a register.
/// </summary>
public class PeerSubscribeCommand : Command
{
    private readonly Option<string> _registerIdOption;
    private readonly Option<string> _modeOption;

    public PeerSubscribeCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("subscribe", "Subscribe to a register for replication")
    {
        _registerIdOption = new Option<string>("--register-id", "-r")
        {
            Description = "Register ID to subscribe to",
            Required = true
        };
        _modeOption = new Option<string>("--mode", "-m")
        {
            Description = "Replication mode: forward-only or full-replica",
            Required = true
        };

        Options.Add(_registerIdOption);
        Options.Add(_modeOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var mode = parseResult.GetValue(_modeOption)!;

            if (mode != "forward-only" && mode != "full-replica")
            {
                ConsoleHelper.WriteError("Invalid mode. Use 'forward-only' or 'full-replica'.");
                return ExitCodes.ValidationError;
            }

            try
            {
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";
                var token = await authService.GetAccessTokenAsync(profileName);
                var authHeader = string.IsNullOrEmpty(token) ? "" : $"Bearer {token}";
                var client = await clientFactory.CreatePeerServiceClientAsync(profileName);

                var result = await client.SubscribeToRegisterAsync(registerId, new CliSubscribeRequest { Mode = mode }, authHeader);

                ConsoleHelper.WriteSuccess($"Subscribed to register '{result.RegisterId}':");
                Console.WriteLine($"  Mode:       {result.Mode}");
                Console.WriteLine($"  State:      {result.SyncState}");
                Console.WriteLine($"  Progress:   {result.SyncProgressPercent:F1}%");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                ConsoleHelper.WriteError($"Already subscribed to register '{registerId}'.");
                return ExitCodes.GeneralError;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Register '{registerId}' not found in network advertisements.");
                return ExitCodes.NotFound;
            }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Failed to connect to Peer Service: {ex.Message}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to subscribe: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Unsubscribes from a register.
/// </summary>
public class PeerUnsubscribeCommand : Command
{
    private readonly Option<string> _registerIdOption;
    private readonly Option<bool> _purgeOption;

    public PeerUnsubscribeCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("unsubscribe", "Unsubscribe from a register")
    {
        _registerIdOption = new Option<string>("--register-id", "-r")
        {
            Description = "Register ID to unsubscribe from",
            Required = true
        };
        _purgeOption = new Option<bool>("--purge")
        {
            Description = "Also delete cached data"
        };

        Options.Add(_registerIdOption);
        Options.Add(_purgeOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var registerId = parseResult.GetValue(_registerIdOption)!;
            var purge = parseResult.GetValue(_purgeOption);

            try
            {
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";
                var token = await authService.GetAccessTokenAsync(profileName);
                var authHeader = string.IsNullOrEmpty(token) ? "" : $"Bearer {token}";
                var client = await clientFactory.CreatePeerServiceClientAsync(profileName);

                var result = await client.UnsubscribeFromRegisterAsync(registerId, purge, authHeader);

                ConsoleHelper.WriteSuccess($"Unsubscribed from register '{result.RegisterId}'.");
                if (result.CacheRetained)
                {
                    ConsoleHelper.WriteInfo("Cached data retained. Use --purge to delete.");
                }
                else
                {
                    ConsoleHelper.WriteInfo("Cached data purged.");
                }

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"No subscription found for register '{registerId}'.");
                return ExitCodes.NotFound;
            }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Failed to connect to Peer Service: {ex.Message}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to unsubscribe: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Bans a peer.
/// </summary>
public class PeerBanCommand : Command
{
    private readonly Option<string> _peerIdOption;
    private readonly Option<string?> _reasonOption;

    public PeerBanCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("ban", "Ban a peer from communication")
    {
        _peerIdOption = new Option<string>("--peer-id", "-p")
        {
            Description = "Peer ID to ban",
            Required = true
        };
        _reasonOption = new Option<string?>("--reason", "-r")
        {
            Description = "Reason for the ban"
        };

        Options.Add(_peerIdOption);
        Options.Add(_reasonOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var peerId = parseResult.GetValue(_peerIdOption)!;
            var reason = parseResult.GetValue(_reasonOption);

            try
            {
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";
                var token = await authService.GetAccessTokenAsync(profileName);
                var authHeader = string.IsNullOrEmpty(token) ? "" : $"Bearer {token}";
                var client = await clientFactory.CreatePeerServiceClientAsync(profileName);

                var result = await client.BanPeerAsync(peerId, new CliBanRequest { Reason = reason }, authHeader);

                ConsoleHelper.WriteSuccess($"Banned peer '{result.PeerId}'.");
                if (!string.IsNullOrEmpty(result.BanReason))
                {
                    Console.WriteLine($"  Reason:     {result.BanReason}");
                }
                Console.WriteLine($"  Banned at:  {result.BannedAt:yyyy-MM-dd HH:mm:ss}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Peer '{peerId}' not found.");
                return ExitCodes.NotFound;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.Conflict)
            {
                ConsoleHelper.WriteError($"Peer '{peerId}' is already banned.");
                return ExitCodes.GeneralError;
            }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Failed to connect to Peer Service: {ex.Message}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to ban peer: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}

/// <summary>
/// Resets a peer's failure count.
/// </summary>
public class PeerResetCommand : Command
{
    private readonly Option<string> _peerIdOption;

    public PeerResetCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("reset", "Reset a peer's failure count")
    {
        _peerIdOption = new Option<string>("--peer-id", "-p")
        {
            Description = "Peer ID to reset",
            Required = true
        };

        Options.Add(_peerIdOption);

        this.SetAction(async (ParseResult parseResult, CancellationToken ct) =>
        {
            var peerId = parseResult.GetValue(_peerIdOption)!;

            try
            {
                var profile = await configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";
                var token = await authService.GetAccessTokenAsync(profileName);
                var authHeader = string.IsNullOrEmpty(token) ? "" : $"Bearer {token}";
                var client = await clientFactory.CreatePeerServiceClientAsync(profileName);

                var result = await client.ResetPeerAsync(peerId, authHeader);

                ConsoleHelper.WriteSuccess($"Reset failure count for peer '{result.PeerId}'.");
                Console.WriteLine($"  Previous failures: {result.PreviousFailureCount}");
                Console.WriteLine($"  Current failures:  {result.FailureCount}");

                return ExitCodes.Success;
            }
            catch (ApiException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                ConsoleHelper.WriteError($"Peer '{peerId}' not found.");
                return ExitCodes.NotFound;
            }
            catch (HttpRequestException ex)
            {
                ConsoleHelper.WriteError($"Failed to connect to Peer Service: {ex.Message}");
                return ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to reset peer: {ex.Message}");
                return ExitCodes.GeneralError;
            }
        });
    }
}
