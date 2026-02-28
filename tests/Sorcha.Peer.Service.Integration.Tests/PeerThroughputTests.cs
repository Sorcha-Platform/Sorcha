// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Net;

using FluentAssertions;

using Sorcha.Peer.Service.Integration.Tests.Infrastructure;
using Sorcha.Peer.Service.Protos;

namespace Sorcha.Peer.Service.Integration.Tests;

/// <summary>
/// Integration tests for peer service throughput and performance.
/// Tests high-volume gRPC Ping and REST endpoint performance.
///
/// Note: Transaction streaming throughput tests will be added when the
/// TransactionDistribution gRPC server is implemented (Wave 3).
/// </summary>
[Collection("PeerIntegration")]
public class PeerThroughputTests : IClassFixture<PeerTestFixture>
{
    private readonly PeerTestFixture _fixture;

    public PeerThroughputTests(PeerTestFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task High_Volume_Ping_Should_Maintain_Performance()
    {
        // Arrange
        var peer = _fixture.Peers[0];
        var pingCount = 100;
        var stopwatch = Stopwatch.StartNew();

        // Act
        for (int i = 0; i < pingCount; i++)
        {
            var response = await peer.GrpcClient.PingAsync(new PingRequest
            {
                PeerId = $"perf-ping-{i}"
            });
            response.Status.Should().Be(PeerStatus.Online);
        }

        stopwatch.Stop();

        // Assert
        var throughput = pingCount / stopwatch.Elapsed.TotalSeconds;
        throughput.Should().BeGreaterThan(10, "System should handle at least 10 pings per second");
    }

    [Fact]
    public async Task Concurrent_Pings_Should_All_Succeed()
    {
        // Arrange
        var peer = _fixture.Peers[0];
        var concurrency = 50;
        var stopwatch = Stopwatch.StartNew();

        // Act
        var tasks = Enumerable.Range(0, concurrency).Select(async i =>
        {
            var response = await peer.GrpcClient.PingAsync(new PingRequest
            {
                PeerId = $"concurrent-{i}"
            });
            return response;
        });

        var responses = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        responses.Should().HaveCount(concurrency);
        responses.Should().OnlyContain(r => r.Status == PeerStatus.Online);

        var throughput = concurrency / stopwatch.Elapsed.TotalSeconds;
        throughput.Should().BeGreaterThan(5, "Concurrent pings should complete efficiently");
    }

    [Fact]
    public async Task Sustained_REST_Requests_Should_Not_Degrade()
    {
        // Arrange
        var peer = _fixture.Peers[0];
        var batchSize = 20;
        var batchCount = 5;
        var batchThroughputs = new List<double>();

        // Act - Run multiple batches of REST calls
        for (int batch = 0; batch < batchCount; batch++)
        {
            var stopwatch = Stopwatch.StartNew();

            for (int i = 0; i < batchSize; i++)
            {
                var response = await peer.HttpClient.GetAsync("/api/peers/connected");
                response.StatusCode.Should().Be(HttpStatusCode.OK);
            }

            stopwatch.Stop();

            var throughput = batchSize / stopwatch.Elapsed.TotalSeconds;
            batchThroughputs.Add(throughput);

            // Small delay between batches
            await Task.Delay(50);
        }

        // Assert - Performance should not degrade significantly
        var firstBatchThroughput = batchThroughputs.First();
        var lastBatchThroughput = batchThroughputs.Last();

        // Last batch should be at least 50% as fast as first batch
        lastBatchThroughput.Should().BeGreaterThan(firstBatchThroughput * 0.5,
            "Performance should not degrade more than 50% under sustained load");
    }

    [Fact]
    public async Task Parallel_Peer_REST_Throughput_Test()
    {
        // Arrange
        var peers = _fixture.Peers.Take(3).ToList();
        var requestsPerPeer = 20;
        var stopwatch = Stopwatch.StartNew();

        // Act - All peers make requests concurrently
        var tasks = peers.Select(async peer =>
        {
            var successCount = 0;
            for (int i = 0; i < requestsPerPeer; i++)
            {
                var response = await peer.HttpClient.GetAsync("/api/peers/connected");
                if (response.StatusCode == HttpStatusCode.OK)
                    successCount++;
            }
            return successCount;
        });

        var results = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        var totalRequests = results.Sum();
        results.Should().OnlyContain(count => count == requestsPerPeer);

        var throughput = totalRequests / stopwatch.Elapsed.TotalSeconds;
        throughput.Should().BeGreaterThan(10, "Combined REST throughput should be at least 10 req/sec");
    }

    [Fact]
    public async Task Multiple_Endpoint_Throughput_Test()
    {
        // Arrange
        var peer = _fixture.Peers[0];
        var endpoints = new[]
        {
            "/api/peers/connected",
            "/api/peers/health",
            "/api/peers/stats",
            "/api/peers/quality",
            "/api/health"
        };
        var requestsPerEndpoint = 10;
        var stopwatch = Stopwatch.StartNew();

        // Act
        var totalSuccess = 0;
        foreach (var endpoint in endpoints)
        {
            for (int i = 0; i < requestsPerEndpoint; i++)
            {
                var response = await peer.HttpClient.GetAsync(endpoint);
                if (response.StatusCode == HttpStatusCode.OK)
                    totalSuccess++;
            }
        }

        stopwatch.Stop();

        // Assert
        var totalRequests = endpoints.Length * requestsPerEndpoint;
        totalSuccess.Should().Be(totalRequests, "All REST endpoint requests should succeed");

        var throughput = totalRequests / stopwatch.Elapsed.TotalSeconds;
        throughput.Should().BeGreaterThan(5, "Multiple endpoint throughput should be at least 5 req/sec");
    }

    [Fact]
    public async Task RegisterPeer_Throughput_Test()
    {
        // Arrange
        var peer = _fixture.Peers[0];
        var registrationCount = 50;
        var stopwatch = Stopwatch.StartNew();

        // Act
        var tasks = Enumerable.Range(0, registrationCount).Select(async i =>
        {
            var response = await peer.GrpcClient.RegisterPeerAsync(new RegisterPeerRequest
            {
                PeerInfo = new PeerInfo
                {
                    PeerId = $"throughput-peer-{i}",
                    Address = "10.0.0.1",
                    Port = 5000 + i
                }
            });
            return response.Success;
        });

        var results = await Task.WhenAll(tasks);
        stopwatch.Stop();

        // Assert
        results.Should().OnlyContain(success => success);

        var throughput = registrationCount / stopwatch.Elapsed.TotalSeconds;
        throughput.Should().BeGreaterThan(5, "Peer registration should handle at least 5 registrations/sec");
    }

    [Fact(Skip = "TransactionDistribution gRPC server not yet implemented (Wave 3)")]
    public async Task High_Volume_Transaction_Stream_Should_Maintain_Performance()
    {
        // This test will be implemented when the TransactionDistribution gRPC service
        // (StreamTransaction) is wired up in Program.cs
        await Task.CompletedTask;
    }

    [Fact(Skip = "TransactionDistribution gRPC server not yet implemented (Wave 3)")]
    public async Task Burst_Traffic_Should_Be_Handled_Gracefully()
    {
        // This test will be implemented when transaction streaming is available
        await Task.CompletedTask;
    }

    [Fact(Skip = "TransactionDistribution gRPC server not yet implemented (Wave 3)")]
    public async Task Memory_Usage_Should_Remain_Stable_Under_Load()
    {
        // This test will be implemented when transaction streaming is available
        await Task.CompletedTask;
    }
}
