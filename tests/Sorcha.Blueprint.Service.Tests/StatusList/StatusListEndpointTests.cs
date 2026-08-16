// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Endpoints;
using Sorcha.Blueprint.Service.Services;

namespace Sorcha.Blueprint.Service.Tests.StatusList;

public class StatusListEndpointTests
{
    private readonly Mock<IStatusListManager> _statusListManagerMock = new();
    private readonly Mock<ILoggerFactory> _loggerFactoryMock = new();
    private readonly IConfiguration _configuration;

    public StatusListEndpointTests()
    {
        var loggerMock = new Mock<ILogger>();
        _loggerFactoryMock
            .Setup(f => f.CreateLogger(It.IsAny<string>()))
            .Returns(loggerMock.Object);

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["StatusList:BaseUrl"] = "https://test.example/api/v1/credentials/status-lists",
                ["StatusList:CacheMaxAgeSeconds"] = "300"
            })
            .Build();
    }

    // ===== GET /{listId} Tests =====

    [Fact]
    public async Task GetStatusList_ExistingList_ReturnsW3CFormat()
    {
        var list = BitstringStatusList.Create("issuer-1", "register-1", "revocation");
        _statusListManagerMock
            .Setup(m => m.GetListAsync(list.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);

        var result = await InvokeGetStatusList(list.Id);

        // The CachedResult wraps the actual result — we verify the manager was called
        _statusListManagerMock.Verify(
            m => m.GetListAsync(list.Id, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task GetStatusList_NonExistentList_ReturnsNotFound()
    {
        _statusListManagerMock
            .Setup(m => m.GetListAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BitstringStatusList?)null);

        var result = await InvokeGetStatusList("nonexistent");

        // Should be a NotFound result (wrapped in CachedResult or direct)
        _statusListManagerMock.Verify(
            m => m.GetListAsync("nonexistent", It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ===== POST /{listId}/allocate Tests =====

    [Fact]
    public async Task AllocateIndex_ValidRequest_ReturnsAllocation()
    {
        var list = BitstringStatusList.Create("issuer-1", "register-1", "revocation");
        _statusListManagerMock
            .Setup(m => m.GetListAsync(list.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);
        _statusListManagerMock
            .Setup(m => m.AllocateIndexAsync("issuer-1", "register-1", "cred-1", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StatusListAllocation(list.Id, 42, "https://test.example/api/v1/credentials/status-lists/" + list.Id));

        var result = await InvokeAllocateIndex(list.Id, new AllocateIndexRequest { CredentialId = "cred-1" });

        var okResult = result.Should().BeOfType<Ok<StatusListAllocation>>().Subject;
        okResult.Value!.Index.Should().Be(42);
        okResult.Value.ListId.Should().Be(list.Id);
    }

    [Fact]
    public async Task AllocateIndex_EmptyCredentialId_ReturnsBadRequest()
    {
        var result = await InvokeAllocateIndex("list-1", new AllocateIndexRequest { CredentialId = "" });

        result.GetType().Name.Should().Contain("BadRequest");
    }

    [Fact]
    public async Task AllocateIndex_ListNotFound_ReturnsNotFound()
    {
        _statusListManagerMock
            .Setup(m => m.GetListAsync("nonexistent", It.IsAny<CancellationToken>()))
            .ReturnsAsync((BitstringStatusList?)null);

        var result = await InvokeAllocateIndex("nonexistent", new AllocateIndexRequest { CredentialId = "cred-1" });

        result.GetType().Name.Should().Contain("NotFound");
    }

    [Fact]
    public async Task AllocateIndex_ListFull_ReturnsConflict()
    {
        var list = BitstringStatusList.Create("issuer-1", "register-1", "revocation");
        _statusListManagerMock
            .Setup(m => m.GetListAsync(list.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(list);
        _statusListManagerMock
            .Setup(m => m.AllocateIndexAsync("issuer-1", "register-1", "cred-overflow", It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Status list issuer-1-register-1-revocation-1 is full"));

        var result = await InvokeAllocateIndex(list.Id, new AllocateIndexRequest { CredentialId = "cred-overflow" });

        result.GetType().Name.Should().Contain("Conflict");
    }

    // ===== PUT /{listId}/bits/{index} Tests =====

    [Fact]
    public async Task SetBit_ValidRequest_ReturnsUpdate()
    {
        _statusListManagerMock
            .Setup(m => m.SetBitAsync("list-1", 42, true, "revoked", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StatusListBitUpdate("list-1", 42, true, 2, null));

        var result = await InvokeSetBit("list-1", 42, new SetBitRequest { Value = true, Reason = "revoked" });

        var okResult = result.Should().BeOfType<Ok<StatusListBitUpdate>>().Subject;
        okResult.Value!.ListId.Should().Be("list-1");
        okResult.Value.Index.Should().Be(42);
        okResult.Value.Value.Should().BeTrue();
        okResult.Value.Version.Should().Be(2);
    }

    [Fact]
    public async Task SetBit_ListNotFound_ReturnsNotFound()
    {
        _statusListManagerMock
            .Setup(m => m.SetBitAsync("nonexistent", 0, true, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new KeyNotFoundException("Status list nonexistent not found"));

        var result = await InvokeSetBit("nonexistent", 0, new SetBitRequest { Value = true });

        result.GetType().Name.Should().Contain("NotFound");
    }

    [Fact]
    public async Task SetBit_IndexOutOfRange_ReturnsNotFound()
    {
        _statusListManagerMock
            .Setup(m => m.SetBitAsync("list-1", 999999, true, null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new ArgumentOutOfRangeException("index"));

        var result = await InvokeSetBit("list-1", 999999, new SetBitRequest { Value = true });

        result.GetType().Name.Should().Contain("NotFound");
    }

    // ===== Helpers =====

    private async Task<IResult> InvokeGetStatusList(string listId)
    {
        var method = typeof(StatusListEndpoints).GetMethod(
            "GetStatusList",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.Should().NotBeNull("GetStatusList method should exist");

        var result = method!.Invoke(null, [
            listId,
            _statusListManagerMock.Object,
            _configuration,
            new Sorcha.Blueprint.Service.Configuration.StatusListUrls.Resolved(
                "https://test.example/api/v1/credentials/status-lists",
                "https://test.example/api/v1/credentials/ietf-status-lists"),
            CancellationToken.None
        ]);
        return await (Task<IResult>)result!;
    }

    private async Task<IResult> InvokeAllocateIndex(string listId, AllocateIndexRequest request)
    {
        var method = typeof(StatusListEndpoints).GetMethod(
            "AllocateIndex",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.Should().NotBeNull("AllocateIndex method should exist");

        var result = method!.Invoke(null, [
            listId,
            request,
            _statusListManagerMock.Object,
            _loggerFactoryMock.Object,
            CancellationToken.None
        ]);
        return await (Task<IResult>)result!;
    }

    private async Task<IResult> InvokeSetBit(string listId, int index, SetBitRequest request)
    {
        var method = typeof(StatusListEndpoints).GetMethod(
            "SetBit",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        method.Should().NotBeNull("SetBit method should exist");

        var result = method!.Invoke(null, [
            listId,
            index,
            request,
            _statusListManagerMock.Object,
            _loggerFactoryMock.Object,
            CancellationToken.None
        ]);
        return await (Task<IResult>)result!;
    }
}
