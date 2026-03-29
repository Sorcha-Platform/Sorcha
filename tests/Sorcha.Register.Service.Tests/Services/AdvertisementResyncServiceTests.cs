// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Service.Services;
using Sorcha.ServiceClients.Peer;
using Xunit;

namespace Sorcha.Register.Service.Tests.Services;

public class AdvertisementResyncServiceTests
{
    private readonly Mock<IPeerServiceClient> _mockPeerClient;
    private readonly Mock<IRegisterRepository> _mockRepository;
    private readonly Mock<ILogger<AdvertisementResyncService>> _mockLogger;

    public AdvertisementResyncServiceTests()
    {
        _mockPeerClient = new Mock<IPeerServiceClient>();
        _mockRepository = new Mock<IRegisterRepository>();
        _mockLogger = new Mock<ILogger<AdvertisementResyncService>>();

        // Default: return empty register list
        _mockRepository.Setup(r => r.QueryRegistersAsync(
                It.IsAny<Func<Register.Models.Register, bool>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<Register.Models.Register>());

        // Default: successful bulk advertise
        _mockPeerClient.Setup(c => c.BulkAdvertiseAsync(
                It.IsAny<BulkAdvertiseRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BulkAdvertiseResponse());
    }

    private AdvertisementResyncService CreateService(TimeSpan? interval = null)
    {
        Microsoft.Extensions.Options.IOptions<AdvertisementResyncOptions>? options = null;
        if (interval.HasValue)
        {
            options = Microsoft.Extensions.Options.Options.Create(
                new AdvertisementResyncOptions { ResyncInterval = interval.Value });
        }

        return new AdvertisementResyncService(
            _mockPeerClient.Object,
            _mockRepository.Object,
            _mockLogger.Object,
            options);
    }

    #region Constructor

    [Fact]
    public void Constructor_NullPeerClient_ThrowsArgumentNullException()
    {
        Action act = () => new AdvertisementResyncService(
            null!,
            _mockRepository.Object,
            _mockLogger.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("peerClient");
    }

    [Fact]
    public void Constructor_NullRepository_ThrowsArgumentNullException()
    {
        Action act = () => new AdvertisementResyncService(
            _mockPeerClient.Object,
            null!,
            _mockLogger.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("repository");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        Action act = () => new AdvertisementResyncService(
            _mockPeerClient.Object,
            _mockRepository.Object,
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    #endregion

    #region BuildBulkAdvertiseRequest

    [Fact]
    public async Task BuildBulkAdvertiseRequestAsync_QueriesAdvertiseRegisters()
    {
        var registers = new List<Register.Models.Register>
        {
            new() { Id = "reg-1", Advertise = true, Name = "Alpha Register", Description = "First register", Height = 15 },
            new() { Id = "reg-2", Advertise = true, Name = "Beta Register", Description = "Second register", Height = 42 }
        };

        _mockRepository.Setup(r => r.QueryRegistersAsync(
                It.IsAny<Func<Register.Models.Register, bool>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(registers);

        var service = CreateService();
        var request = await service.BuildBulkAdvertiseRequestAsync(CancellationToken.None);

        request.FullSync.Should().BeTrue();
        request.Advertisements.Should().HaveCount(2);
        request.Advertisements.Select(a => a.RegisterId).Should().BeEquivalentTo(["reg-1", "reg-2"]);
        request.Advertisements.Should().AllSatisfy(a => a.IsPublic.Should().BeTrue());

        // Verify Name and LatestVersion are populated from register metadata
        request.Advertisements[0].Name.Should().Be("Alpha Register");
        request.Advertisements[0].LatestVersion.Should().Be(15);
        request.Advertisements[1].Name.Should().Be("Beta Register");
        request.Advertisements[1].LatestVersion.Should().Be(42);
    }

    [Fact]
    public async Task BuildBulkAdvertiseRequestAsync_EmptyRegisters_ReturnsEmptyAdvertisements()
    {
        var service = CreateService();
        var request = await service.BuildBulkAdvertiseRequestAsync(CancellationToken.None);

        request.FullSync.Should().BeTrue();
        request.Advertisements.Should().BeEmpty();
    }

    #endregion

    #region ExecuteAsync

    [Fact]
    public async Task ExecuteAsync_OnStartup_CallsBulkAdvertise()
    {
        var service = CreateService(interval: TimeSpan.FromHours(1));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));

        await service.StartAsync(cts.Token);
        // Give it a moment to execute the initial push
        await Task.Delay(500);
        await service.StopAsync(CancellationToken.None);

        _mockPeerClient.Verify(c => c.BulkAdvertiseAsync(
            It.Is<BulkAdvertiseRequest>(r => r.FullSync),
            It.IsAny<CancellationToken>()), Times.AtLeastOnce);
    }

    [Fact]
    public async Task ExecuteAsync_RetryOnHttpRequestException()
    {
        var callCount = 0;
        _mockPeerClient.Setup(c => c.BulkAdvertiseAsync(
                It.IsAny<BulkAdvertiseRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns<BulkAdvertiseRequest, CancellationToken>((_, _) =>
            {
                callCount++;
                if (callCount <= 2)
                    throw new HttpRequestException("Peer Service unavailable");
                return Task.FromResult<BulkAdvertiseResponse?>(new BulkAdvertiseResponse());
            });

        var service = CreateService(interval: TimeSpan.FromHours(1));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

        await service.StartAsync(cts.Token);
        // Wait for retries (1s initial + 2s second = ~3s plus some buffer)
        await Task.Delay(5000);
        await service.StopAsync(CancellationToken.None);

        callCount.Should().BeGreaterThanOrEqualTo(3, "should retry after HttpRequestException");
    }

    [Fact]
    public async Task ExecuteAsync_CancellationTokenRespected()
    {
        _mockPeerClient.Setup(c => c.BulkAdvertiseAsync(
                It.IsAny<BulkAdvertiseRequest>(),
                It.IsAny<CancellationToken>()))
            .Returns<BulkAdvertiseRequest, CancellationToken>(async (_, ct) =>
            {
                await Task.Delay(TimeSpan.FromSeconds(30), ct);
                return new BulkAdvertiseResponse();
            });

        var service = CreateService();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        await service.StartAsync(cts.Token);
        await Task.Delay(500);

        // Service should have stopped without hanging
        var act = () => service.StopAsync(CancellationToken.None);
        await act.Should().CompleteWithinAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task ExecuteAsync_WithRegisters_SendsCorrectRequest()
    {
        var registers = new List<Register.Models.Register>
        {
            new() { Id = "reg-1", Advertise = true },
            new() { Id = "reg-2", Advertise = true },
            new() { Id = "reg-3", Advertise = true }
        };

        _mockRepository.Setup(r => r.QueryRegistersAsync(
                It.IsAny<Func<Register.Models.Register, bool>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(registers);

        BulkAdvertiseRequest? capturedRequest = null;
        _mockPeerClient.Setup(c => c.BulkAdvertiseAsync(
                It.IsAny<BulkAdvertiseRequest>(),
                It.IsAny<CancellationToken>()))
            .Callback<BulkAdvertiseRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new BulkAdvertiseResponse { Processed = 3, Added = 3 });

        var service = CreateService(interval: TimeSpan.FromHours(1));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        await service.StartAsync(cts.Token);
        await Task.Delay(1000);
        await service.StopAsync(CancellationToken.None);

        capturedRequest.Should().NotBeNull();
        capturedRequest!.FullSync.Should().BeTrue();
        capturedRequest.Advertisements.Should().HaveCount(3);
    }

    #endregion
}
