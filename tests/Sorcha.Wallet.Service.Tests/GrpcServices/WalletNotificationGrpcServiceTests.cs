// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Service.GrpcServices;
using Sorcha.Wallet.Service.Grpc;
using Sorcha.Wallet.Service.Services.Interfaces;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Service.Tests.GrpcServices;

public class WalletNotificationGrpcServiceTests
{
    private readonly Mock<IWalletRepository> _mockRepository;
    private readonly Mock<INotificationDeliveryService> _mockDeliveryService;
    private readonly Mock<ILogger<WalletNotificationGrpcService>> _mockLogger;
    private readonly WalletNotificationGrpcService _service;
    private readonly ServerCallContext _context;

    public WalletNotificationGrpcServiceTests()
    {
        _mockRepository = new Mock<IWalletRepository>();
        _mockDeliveryService = new Mock<INotificationDeliveryService>();
        _mockLogger = new Mock<ILogger<WalletNotificationGrpcService>>();

        _service = new WalletNotificationGrpcService(
            _mockRepository.Object,
            _mockDeliveryService.Object,
            _mockLogger.Object);

        _context = new TestServerCallContext();
    }

    private static WalletEntity CreateTestWallet(
        string address = "ws11qtest123",
        string owner = "user-1",
        WalletStatus status = WalletStatus.Active,
        List<Sorcha.Wallet.Core.Domain.Entities.WalletAddress>? derivedAddresses = null)
    {
        return new WalletEntity
        {
            Address = address,
            Algorithm = "ED25519",
            EncryptedPrivateKey = "encrypted-key-data",
            EncryptionKeyId = "key-1",
            Owner = owner,
            Tenant = "tenant-1",
            Name = "Test Wallet",
            PublicKey = Convert.ToBase64String(new byte[32]),
            Status = status,
            CreatedAt = new DateTime(2025, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            Version = 1,
            Addresses = derivedAddresses ?? new List<Sorcha.Wallet.Core.Domain.Entities.WalletAddress>()
        };
    }

    private static Sorcha.Wallet.Core.Domain.Entities.WalletAddress CreateDerivedAddress(
        string address, string parentAddress = "ws11qtest123")
    {
        return new Sorcha.Wallet.Core.Domain.Entities.WalletAddress
        {
            Address = address,
            ParentWalletAddress = parentAddress,
            DerivationPath = "m/44'/0'/0'/0/0"
        };
    }

    private static NotifyInboundTransactionRequest CreateNotificationRequest(
        string recipientAddress = "ws11qtest123",
        string transactionId = "abc123def456",
        string registerId = "register-1",
        long docketNumber = 42,
        bool isRecovery = false)
    {
        return new NotifyInboundTransactionRequest
        {
            RecipientAddress = recipientAddress,
            TransactionId = transactionId,
            RegisterId = registerId,
            DocketNumber = docketNumber,
            BlueprintId = "bp-1",
            InstanceId = "inst-1",
            ActionId = 1,
            NextActionId = 2,
            SenderAddress = "ws11qsender",
            Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow),
            IsRecovery = isRecovery
        };
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullRepository_ThrowsArgumentNullException()
    {
        var act = () => new WalletNotificationGrpcService(
            null!, _mockDeliveryService.Object, _mockLogger.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("repository");
    }

    [Fact]
    public void Constructor_NullDeliveryService_ThrowsArgumentNullException()
    {
        var act = () => new WalletNotificationGrpcService(
            _mockRepository.Object, null!, _mockLogger.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("deliveryService");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        var act = () => new WalletNotificationGrpcService(
            _mockRepository.Object, _mockDeliveryService.Object, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    #endregion

    #region GetAllLocalAddresses Tests

    [Fact]
    public async Task GetAllLocalAddresses_ReturnsAllPrimaryAddresses()
    {
        var wallets = new List<WalletEntity>
        {
            CreateTestWallet(address: "ws11qaddr1", owner: "user-1"),
            CreateTestWallet(address: "ws11qaddr2", owner: "user-2")
        };

        _mockRepository
            .Setup(r => r.GetAllPagedAsync(0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallets);
        _mockRepository
            .Setup(r => r.GetAllPagedAsync(100, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<WalletEntity>());

        var streamWriter = new TestServerStreamWriter<LocalAddressEntry>();
        var request = new GetAllLocalAddressesRequest { ActiveOnly = false };

        await _service.GetAllLocalAddresses(request, streamWriter, _context);

        streamWriter.Written.Should().HaveCount(2);
        streamWriter.Written[0].Address.Should().Be("ws11qaddr1");
        streamWriter.Written[0].UserId.Should().Be("user-1");
        streamWriter.Written[1].Address.Should().Be("ws11qaddr2");
        streamWriter.Written[1].UserId.Should().Be("user-2");
    }

    [Fact]
    public async Task GetAllLocalAddresses_ReturnsDerivedChildAddresses()
    {
        var derived = new List<Sorcha.Wallet.Core.Domain.Entities.WalletAddress>
        {
            CreateDerivedAddress("ws11qchild1"),
            CreateDerivedAddress("ws11qchild2")
        };
        var wallets = new List<WalletEntity>
        {
            CreateTestWallet(address: "ws11qparent", derivedAddresses: derived)
        };

        _mockRepository
            .Setup(r => r.GetAllPagedAsync(0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallets);

        var streamWriter = new TestServerStreamWriter<LocalAddressEntry>();
        var request = new GetAllLocalAddressesRequest { ActiveOnly = false };

        await _service.GetAllLocalAddresses(request, streamWriter, _context);

        // 1 primary + 2 derived = 3 entries
        streamWriter.Written.Should().HaveCount(3);
        streamWriter.Written[0].Address.Should().Be("ws11qparent");
        streamWriter.Written[0].WalletId.Should().Be("ws11qparent");
        streamWriter.Written[1].Address.Should().Be("ws11qchild1");
        streamWriter.Written[1].WalletId.Should().Be("ws11qparent");
        streamWriter.Written[2].Address.Should().Be("ws11qchild2");
        streamWriter.Written[2].WalletId.Should().Be("ws11qparent");
    }

    [Fact]
    public async Task GetAllLocalAddresses_ActiveOnly_FiltersInactiveWallets()
    {
        var wallets = new List<WalletEntity>
        {
            CreateTestWallet(address: "ws11qactive", status: WalletStatus.Active),
            CreateTestWallet(address: "ws11qlocked", status: WalletStatus.Locked),
            CreateTestWallet(address: "ws11qarchived", status: WalletStatus.Archived)
        };

        _mockRepository
            .Setup(r => r.GetAllPagedAsync(0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallets);

        var streamWriter = new TestServerStreamWriter<LocalAddressEntry>();
        var request = new GetAllLocalAddressesRequest { ActiveOnly = true };

        await _service.GetAllLocalAddresses(request, streamWriter, _context);

        streamWriter.Written.Should().HaveCount(1);
        streamWriter.Written[0].Address.Should().Be("ws11qactive");
        streamWriter.Written[0].IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task GetAllLocalAddresses_EmptyRepository_ReturnsEmptyStream()
    {
        _mockRepository
            .Setup(r => r.GetAllPagedAsync(0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Enumerable.Empty<WalletEntity>());

        var streamWriter = new TestServerStreamWriter<LocalAddressEntry>();
        var request = new GetAllLocalAddressesRequest { ActiveOnly = false };

        await _service.GetAllLocalAddresses(request, streamWriter, _context);

        streamWriter.Written.Should().BeEmpty();
    }

    [Fact]
    public async Task GetAllLocalAddresses_CancelledToken_StopsStreaming()
    {
        var cts = new CancellationTokenSource();
        var cancelContext = new CancellableTestServerCallContext(cts.Token);

        // Return a full page so paging would continue, then cancel during the second page fetch
        var wallets = Enumerable.Range(0, 100)
            .Select(i => CreateTestWallet(address: $"ws11qaddr{i}"))
            .ToList();

        _mockRepository
            .Setup(r => r.GetAllPagedAsync(0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallets);

        // Cancel the token when the second page is requested, then return empty (simulating cancellation path)
        _mockRepository
            .Setup(r => r.GetAllPagedAsync(100, 100, It.IsAny<CancellationToken>()))
            .Callback(() => cts.Cancel())
            .ReturnsAsync(Enumerable.Empty<WalletEntity>());

        var streamWriter = new TestServerStreamWriter<LocalAddressEntry>();
        var request = new GetAllLocalAddressesRequest { ActiveOnly = false };

        await _service.GetAllLocalAddresses(request, streamWriter, cancelContext);

        // First page streamed (100 entries), second page triggered cancellation
        streamWriter.Written.Should().HaveCount(100);
        _mockRepository.Verify(r => r.GetAllPagedAsync(100, 100, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAllLocalAddresses_RepositoryThrows_ThrowsUnavailable()
    {
        _mockRepository
            .Setup(r => r.GetAllPagedAsync(0, 100, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Database connection failed"));

        var streamWriter = new TestServerStreamWriter<LocalAddressEntry>();
        var request = new GetAllLocalAddressesRequest { ActiveOnly = false };

        var act = async () => await _service.GetAllLocalAddresses(request, streamWriter, _context);

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.Unavailable);
    }

    [Fact]
    public async Task GetAllLocalAddresses_PagesUntilExhausted()
    {
        // First page: 100 wallets (full page = more pages may exist)
        var page1 = Enumerable.Range(0, 100)
            .Select(i => CreateTestWallet(address: $"ws11qp1_{i}"))
            .ToList();

        // Second page: 50 wallets (less than page size = last page)
        var page2 = Enumerable.Range(0, 50)
            .Select(i => CreateTestWallet(address: $"ws11qp2_{i}"))
            .ToList();

        _mockRepository
            .Setup(r => r.GetAllPagedAsync(0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(page1);
        _mockRepository
            .Setup(r => r.GetAllPagedAsync(100, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(page2);

        var streamWriter = new TestServerStreamWriter<LocalAddressEntry>();
        var request = new GetAllLocalAddressesRequest { ActiveOnly = false };

        await _service.GetAllLocalAddresses(request, streamWriter, _context);

        streamWriter.Written.Should().HaveCount(150);
        _mockRepository.Verify(r => r.GetAllPagedAsync(0, 100, It.IsAny<CancellationToken>()), Times.Once);
        _mockRepository.Verify(r => r.GetAllPagedAsync(100, 100, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetAllLocalAddresses_InactiveWalletsNotFiltered_WhenActiveOnlyFalse()
    {
        var wallets = new List<WalletEntity>
        {
            CreateTestWallet(address: "ws11qactive", status: WalletStatus.Active),
            CreateTestWallet(address: "ws11qlocked", status: WalletStatus.Locked),
            CreateTestWallet(address: "ws11qdeleted", status: WalletStatus.Deleted)
        };

        _mockRepository
            .Setup(r => r.GetAllPagedAsync(0, 100, It.IsAny<CancellationToken>()))
            .ReturnsAsync(wallets);

        var streamWriter = new TestServerStreamWriter<LocalAddressEntry>();
        var request = new GetAllLocalAddressesRequest { ActiveOnly = false };

        await _service.GetAllLocalAddresses(request, streamWriter, _context);

        streamWriter.Written.Should().HaveCount(3);
        streamWriter.Written[0].IsActive.Should().BeTrue();
        streamWriter.Written[1].IsActive.Should().BeFalse();
        streamWriter.Written[2].IsActive.Should().BeFalse();
    }

    #endregion

    #region NotifyInboundTransaction Tests

    [Fact]
    public async Task NotifyInboundTransaction_ValidNotification_DeliversSuccessfully()
    {
        _mockDeliveryService
            .Setup(d => d.DeliverAsync(
                "ws11qtest123", "abc123", "register-1", 42,
                "bp-1", "inst-1", 1u, 2u, "ws11qsender",
                It.IsAny<DateTimeOffset>(), false,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(NotificationDeliveryResult.DeliveredRealTime);

        var request = CreateNotificationRequest(
            recipientAddress: "ws11qtest123",
            transactionId: "abc123",
            registerId: "register-1",
            docketNumber: 42);

        var response = await _service.NotifyInboundTransaction(request, _context);

        response.Success.Should().BeTrue();
        response.Delivery.Should().Be(NotificationDelivery.RealTime);
        response.Message.Should().Be("DeliveredRealTime");
    }

    [Fact]
    public async Task NotifyInboundTransaction_NoUserFound_ReturnsNotSuccessful()
    {
        _mockDeliveryService
            .Setup(d => d.DeliverAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<string?>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(NotificationDeliveryResult.NoUserFound);

        var request = CreateNotificationRequest(recipientAddress: "ws11qunknown");

        var response = await _service.NotifyInboundTransaction(request, _context);

        response.Success.Should().BeFalse();
        response.Delivery.Should().Be(NotificationDelivery.NoUser);
        response.Message.Should().Be("NoUserFound");
    }

    [Fact]
    public async Task NotifyInboundTransaction_DeliveryServiceThrows_ThrowsInternalRpcException()
    {
        _mockDeliveryService
            .Setup(d => d.DeliverAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<string?>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Redis connection failed"));

        var request = CreateNotificationRequest();

        var act = async () => await _service.NotifyInboundTransaction(request, _context);

        var ex = await act.Should().ThrowAsync<RpcException>();
        ex.Which.StatusCode.Should().Be(StatusCode.Internal);
    }

    public static IEnumerable<object[]> DeliveryResultMappingData =>
        new List<object[]>
        {
            new object[] { NotificationDeliveryResult.DeliveredRealTime, NotificationDelivery.RealTime, true },
            new object[] { NotificationDeliveryResult.QueuedForDigest, NotificationDelivery.DigestQueued, true },
            new object[] { NotificationDeliveryResult.RateLimited, NotificationDelivery.RateLimited, true },
            new object[] { NotificationDeliveryResult.NoUserFound, NotificationDelivery.NoUser, false }
        };

    [Theory]
    [MemberData(nameof(DeliveryResultMappingData))]
    public async Task NotifyInboundTransaction_AllDeliveryResults_MappedCorrectly(
        NotificationDeliveryResult deliveryResult,
        NotificationDelivery expectedDelivery,
        bool expectedSuccess)
    {
        _mockDeliveryService
            .Setup(d => d.DeliverAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<string?>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(deliveryResult);

        var request = CreateNotificationRequest();

        var response = await _service.NotifyInboundTransaction(request, _context);

        response.Success.Should().Be(expectedSuccess);
        response.Delivery.Should().Be(expectedDelivery);
        response.Message.Should().Be(deliveryResult.ToString());
    }

    [Fact]
    public async Task NotifyInboundTransaction_RecoveryFlag_PassedThrough()
    {
        _mockDeliveryService
            .Setup(d => d.DeliverAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<string?>(),
                It.IsAny<DateTimeOffset>(), true,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(NotificationDeliveryResult.DeliveredRealTime);

        var request = CreateNotificationRequest(isRecovery: true);

        await _service.NotifyInboundTransaction(request, _context);

        _mockDeliveryService.Verify(d => d.DeliverAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<string?>(),
            It.IsAny<DateTimeOffset>(), true,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyInboundTransaction_EmptyRecipientAddress_StillCallsDeliveryService()
    {
        // The service does not validate recipient address - it delegates to the delivery service
        _mockDeliveryService
            .Setup(d => d.DeliverAsync(
                "", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<string?>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(NotificationDeliveryResult.NoUserFound);

        var request = CreateNotificationRequest(recipientAddress: "");

        var response = await _service.NotifyInboundTransaction(request, _context);

        response.Success.Should().BeFalse();
        _mockDeliveryService.Verify(d => d.DeliverAsync(
            "", It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<string?>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task NotifyInboundTransaction_NoTimestamp_UsesUtcNow()
    {
        var before = DateTimeOffset.UtcNow;

        _mockDeliveryService
            .Setup(d => d.DeliverAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<string?>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(NotificationDeliveryResult.DeliveredRealTime);

        var request = new NotifyInboundTransactionRequest
        {
            RecipientAddress = "ws11qtest123",
            TransactionId = "abc123",
            RegisterId = "register-1",
            DocketNumber = 42
            // No Timestamp set
        };

        await _service.NotifyInboundTransaction(request, _context);

        var after = DateTimeOffset.UtcNow;

        _mockDeliveryService.Verify(d => d.DeliverAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<string?>(),
            It.Is<DateTimeOffset>(ts => ts >= before && ts <= after),
            It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    #endregion

    #region NotifyInboundTransactionBatch Tests

    [Fact]
    public async Task NotifyInboundTransactionBatch_EmptyBatch_ReturnsZeroCounts()
    {
        var request = new NotifyInboundTransactionBatchRequest();

        var response = await _service.NotifyInboundTransactionBatch(request, _context);

        response.Total.Should().Be(0);
        response.DeliveredRealTime.Should().Be(0);
        response.QueuedForDigest.Should().Be(0);
        response.RateLimited.Should().Be(0);
        response.NoUserFound.Should().Be(0);
        response.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task NotifyInboundTransactionBatch_MixedResults_CountedCorrectly()
    {
        var deliveryResults = new Queue<NotificationDeliveryResult>(new[]
        {
            NotificationDeliveryResult.DeliveredRealTime,
            NotificationDeliveryResult.QueuedForDigest,
            NotificationDeliveryResult.NoUserFound,
            NotificationDeliveryResult.RateLimited,
            NotificationDeliveryResult.DeliveredRealTime
        });

        _mockDeliveryService
            .Setup(d => d.DeliverAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<string?>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => deliveryResults.Dequeue());

        var request = new NotifyInboundTransactionBatchRequest();
        for (var i = 0; i < 5; i++)
        {
            request.Transactions.Add(CreateNotificationRequest(
                recipientAddress: $"ws11qaddr{i}",
                transactionId: $"tx{i}"));
        }

        var response = await _service.NotifyInboundTransactionBatch(request, _context);

        response.Total.Should().Be(5);
        response.DeliveredRealTime.Should().Be(2);
        response.QueuedForDigest.Should().Be(1);
        response.NoUserFound.Should().Be(1);
        response.RateLimited.Should().Be(1);
        response.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task NotifyInboundTransactionBatch_SingleItem_Works()
    {
        _mockDeliveryService
            .Setup(d => d.DeliverAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<string?>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(NotificationDeliveryResult.QueuedForDigest);

        var request = new NotifyInboundTransactionBatchRequest();
        request.Transactions.Add(CreateNotificationRequest());

        var response = await _service.NotifyInboundTransactionBatch(request, _context);

        response.Total.Should().Be(1);
        response.QueuedForDigest.Should().Be(1);
        response.DeliveredRealTime.Should().Be(0);
        response.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task NotifyInboundTransactionBatch_LargeBatch_ProcessesAllItems()
    {
        _mockDeliveryService
            .Setup(d => d.DeliverAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<string?>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(NotificationDeliveryResult.DeliveredRealTime);

        var request = new NotifyInboundTransactionBatchRequest();
        for (var i = 0; i < 200; i++)
        {
            request.Transactions.Add(CreateNotificationRequest(
                recipientAddress: $"ws11qaddr{i}",
                transactionId: $"tx{i}"));
        }

        var response = await _service.NotifyInboundTransactionBatch(request, _context);

        response.Total.Should().Be(200);
        response.DeliveredRealTime.Should().Be(200);
        _mockDeliveryService.Verify(d => d.DeliverAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<string?>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Exactly(200));
    }

    [Fact]
    public async Task NotifyInboundTransactionBatch_PartialFailure_DoesNotAbortRemaining()
    {
        var callCount = 0;

        _mockDeliveryService
            .Setup(d => d.DeliverAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<string?>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 2)
                    return Task.FromException<NotificationDeliveryResult>(
                        new InvalidOperationException("Transient failure"));
                return Task.FromResult(NotificationDeliveryResult.DeliveredRealTime);
            });

        var request = new NotifyInboundTransactionBatchRequest();
        for (var i = 0; i < 3; i++)
        {
            request.Transactions.Add(CreateNotificationRequest(
                recipientAddress: $"ws11qaddr{i}",
                transactionId: $"tx{i}"));
        }

        var response = await _service.NotifyInboundTransactionBatch(request, _context);

        response.Total.Should().Be(3);
        response.DeliveredRealTime.Should().Be(2);
        response.Errors.Should().HaveCount(1);
        response.Errors[0].Should().Contain("tx1");

        // All 3 items should have been attempted
        _mockDeliveryService.Verify(d => d.DeliverAsync(
            It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
            It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string?>(),
            It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<string?>(),
            It.IsAny<DateTimeOffset>(), It.IsAny<bool>(),
            It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task NotifyInboundTransactionBatch_AllErrors_RecordsAllErrors()
    {
        _mockDeliveryService
            .Setup(d => d.DeliverAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<long>(), It.IsAny<string?>(), It.IsAny<string?>(),
                It.IsAny<uint>(), It.IsAny<uint>(), It.IsAny<string?>(),
                It.IsAny<DateTimeOffset>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Service down"));

        var request = new NotifyInboundTransactionBatchRequest();
        request.Transactions.Add(CreateNotificationRequest(transactionId: "tx1"));
        request.Transactions.Add(CreateNotificationRequest(transactionId: "tx2"));

        var response = await _service.NotifyInboundTransactionBatch(request, _context);

        response.Total.Should().Be(2);
        response.DeliveredRealTime.Should().Be(0);
        response.Errors.Should().HaveCount(2);
        response.Errors[0].Should().Contain("tx1");
        response.Errors[1].Should().Contain("tx2");
    }

    #endregion
}

/// <summary>
/// Test implementation of IServerStreamWriter for collecting streamed messages.
/// </summary>
internal class TestServerStreamWriter<T> : IServerStreamWriter<T>
{
    public List<T> Written { get; } = new();

    public WriteOptions? WriteOptions { get; set; }

    public Task WriteAsync(T message)
    {
        Written.Add(message);
        return Task.CompletedTask;
    }

    public Task WriteAsync(T message, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Written.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>
/// ServerCallContext that supports a custom cancellation token for testing cancellation scenarios.
/// </summary>
internal class CancellableTestServerCallContext : ServerCallContext
{
    private readonly CancellationToken _cancellationToken;

    public CancellableTestServerCallContext(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
    }

    protected override string MethodCore => "TestMethod";
    protected override string HostCore => "localhost";
    protected override string PeerCore => "test-peer";
    protected override DateTime DeadlineCore => DateTime.MaxValue;
    protected override Metadata RequestHeadersCore => new();
    protected override CancellationToken CancellationTokenCore => _cancellationToken;
    protected override Metadata ResponseTrailersCore => new();
    protected override Status StatusCore { get; set; }
    protected override WriteOptions? WriteOptionsCore { get; set; }
    protected override AuthContext AuthContextCore => new(string.Empty, new Dictionary<string, List<AuthProperty>>());

    protected override ContextPropagationToken CreatePropagationTokenCore(ContextPropagationOptions? options) =>
        throw new NotImplementedException();

    protected override Task WriteResponseHeadersAsyncCore(Metadata responseHeaders) =>
        Task.CompletedTask;
}
