// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Register.Core.Events;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Constants;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Tests.Services;

public class SystemBlueprintCacheEvictionServiceTests
{
    private readonly Mock<IBlueprintCache> _cache = new();
    private readonly Mock<IEventSubscriber> _subscriber = new();
    private Func<DocketConfirmedEvent, Task>? _handler;
    private string? _channel;

    private async Task StartAsync()
    {
        _subscriber
            .Setup(s => s.SubscribeAsync(It.IsAny<string>(), It.IsAny<Func<DocketConfirmedEvent, Task>>(), It.IsAny<CancellationToken>()))
            .Callback<string, Func<DocketConfirmedEvent, Task>, CancellationToken>((c, h, _) => { _channel = c; _handler = h; })
            .Returns(Task.CompletedTask);
        _cache.Setup(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);

        var svc = new SystemBlueprintCacheEvictionService(
            _cache.Object, NullLogger<SystemBlueprintCacheEvictionService>.Instance, _subscriber.Object);
        await svc.StartAsync(CancellationToken.None);
        if (svc.ExecuteTask is { } t) await t;
    }

    private static DocketConfirmedEvent Docket(string registerId) =>
        new() { RegisterId = registerId, DocketId = 7, Hash = "h", TimeStamp = DateTime.UtcNow };

    [Fact]
    public async Task StartAsync_SubscribesToDocketConfirmedChannel()
    {
        await StartAsync();

        _channel.Should().Be(RegisterEventChannels.DocketConfirmed);
        _handler.Should().NotBeNull();
    }

    [Fact]
    public async Task Handler_SystemRegisterDocket_EvictsEveryCatalogBlueprint()
    {
        await StartAsync();

        await _handler!(Docket(SystemRegisterConstants.SystemRegisterId));

        SystemBlueprintCatalog.Ids.Should().HaveCount(4);
        foreach (var id in SystemBlueprintCatalog.Ids)
        {
            _cache.Verify(c => c.RemoveAsync(id, It.IsAny<CancellationToken>()), Times.Once);
        }
        _cache.Verify(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    [Fact]
    public async Task Handler_OtherRegisterDocket_EvictsNothing()
    {
        await StartAsync();

        await _handler!(Docket("0123456789abcdef0123456789abcdef"));

        _cache.Verify(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handler_RemoveThrows_IsSwallowedAndRemainingIdsStillEvicted()
    {
        await StartAsync();
        _cache.Setup(c => c.RemoveAsync(SystemBlueprintCatalog.Ids[0], It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("redis down"));

        var act = () => _handler!(Docket(SystemRegisterConstants.SystemRegisterId));

        await act.Should().NotThrowAsync();
        _cache.Verify(c => c.RemoveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Exactly(4));
    }

    [Fact]
    public async Task StartAsync_NullSubscriber_IsNoOp()
    {
        var svc = new SystemBlueprintCacheEvictionService(
            _cache.Object, NullLogger<SystemBlueprintCacheEvictionService>.Instance, null);

        await svc.StartAsync(CancellationToken.None);
        if (svc.ExecuteTask is { } t) await t;

        _cache.VerifyNoOtherCalls();
    }
}
