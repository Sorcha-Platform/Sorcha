// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.ServiceClients.Inbox;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>Unit tests for <see cref="EncryptionInboxWriter"/>. Feature 169.</summary>
public sealed class EncryptionInboxWriterTests
{
    private readonly Mock<IPlatformInboxClient> _inbox = new();
    private const string OperationId = "op-test-abc123";
    private readonly Guid _userId = Guid.NewGuid();

    private EncryptionInboxWriter BuildSut(ILogger<EncryptionInboxWriter>? logger = null) =>
        new(_inbox.Object, logger ?? NullLogger<EncryptionInboxWriter>.Instance);

    private void SetupInboxSuccess()
    {
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));
    }

    [Fact]
    public async Task WriteEncryptionCompleteAsync_PostsExpectedWorkflowPayload()
    {
        InboxWritePayload? captured = null;
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        var sut = BuildSut();
        await sut.WriteEncryptionCompleteAsync(_userId, OperationId);

        captured.Should().NotBeNull();
        captured!.PlatformUserId.Should().Be(_userId);
        captured.Category.Should().Be("Workflow");
        captured.Severity.Should().Be("Info");
        captured.CorrelationKey.Should().Be($"sorcha.inbox.encryption.complete:{OperationId}");
        captured.DetailHref.Should().Be($"/api/operations/{OperationId}");
        captured.Title.Should().Be("Register encrypted");
    }

    [Fact]
    public async Task WriteEncryptionFailedAsync_PostsExpectedWorkflowPayload()
    {
        InboxWritePayload? captured = null;
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => captured = p)
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        var sut = BuildSut();
        await sut.WriteEncryptionFailedAsync(_userId, OperationId);

        captured.Should().NotBeNull();
        captured!.PlatformUserId.Should().Be(_userId);
        captured.Category.Should().Be("Workflow");
        captured.Severity.Should().Be("ActionRequired");
        captured.CorrelationKey.Should().Be($"sorcha.inbox.encryption.fail:{OperationId}");
        captured.DetailHref.Should().Be($"/api/operations/{OperationId}");
        captured.Title.Should().Be("Register encryption failed");
    }

    [Fact]
    public async Task WriteEncryptionCompleteAsync_SameOperationId_ProducesSameSourceEventId()
    {
        var ids = new List<Guid>();
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => ids.Add(p.SourceEventId))
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        var sut = BuildSut();
        await sut.WriteEncryptionCompleteAsync(_userId, OperationId);
        await sut.WriteEncryptionCompleteAsync(_userId, OperationId);

        ids.Should().HaveCount(2);
        ids[0].Should().Be(ids[1], "retried writes with the same operationId must collapse via the unique-index constraint");
    }

    [Fact]
    public async Task WriteEncryptionFailedAsync_SameOperationId_ProducesSameSourceEventId()
    {
        var ids = new List<Guid>();
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) => ids.Add(p.SourceEventId))
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        var sut = BuildSut();
        await sut.WriteEncryptionFailedAsync(_userId, OperationId);
        await sut.WriteEncryptionFailedAsync(_userId, OperationId);

        ids.Should().HaveCount(2);
        ids[0].Should().Be(ids[1], "retried writes with the same operationId must collapse via the unique-index constraint");
    }

    [Fact]
    public async Task WriteEncryptionCompleteAsync_DifferentFromFailSourceEventId()
    {
        InboxWritePayload? cap1 = null, cap2 = null;
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWritePayload, CancellationToken>((p, _) =>
            {
                if (cap1 is null) cap1 = p; else cap2 = p;
            })
            .ReturnsAsync(new InboxWriteOutcome(Guid.NewGuid(), Idempotent: false));

        var sut = BuildSut();
        await sut.WriteEncryptionCompleteAsync(_userId, OperationId);
        await sut.WriteEncryptionFailedAsync(_userId, OperationId);

        cap1!.SourceEventId.Should().NotBe(cap2!.SourceEventId,
            "complete and fail events for the same operationId must have distinct SourceEventIds");
    }

    [Fact]
    public async Task WriteEncryptionCompleteAsync_WhenClientThrows_LogsWarningAndDoesNotThrow()
    {
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Tenant unavailable"));

        var loggerMock = new Mock<ILogger<EncryptionInboxWriter>>();
        var sut = BuildSut(loggerMock.Object);

        await sut.Awaiting(s => s.WriteEncryptionCompleteAsync(_userId, OperationId))
            .Should().NotThrowAsync("inbox-write failures must never block the encryption operation");

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task WriteEncryptionFailedAsync_WhenClientThrows_LogsWarningAndDoesNotThrow()
    {
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Tenant unavailable"));

        var loggerMock = new Mock<ILogger<EncryptionInboxWriter>>();
        var sut = BuildSut(loggerMock.Object);

        await sut.Awaiting(s => s.WriteEncryptionFailedAsync(_userId, OperationId))
            .Should().NotThrowAsync("inbox-write failures must never block the encryption operation");

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task WriteEncryptionCompleteAsync_WhenPlatformUserIdIsEmpty_SkipsWriteAndLogs()
    {
        var loggerMock = new Mock<ILogger<EncryptionInboxWriter>>();
        var sut = BuildSut(loggerMock.Object);

        await sut.Awaiting(s => s.WriteEncryptionCompleteAsync(Guid.Empty, OperationId))
            .Should().NotThrowAsync("empty userId is a known edge case that must be skipped gracefully");

        _inbox.Verify(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()), Times.Never,
            "no write must reach the inbox client when userId is empty");

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "a warning must be emitted so operators can diagnose anonymous operations");
    }

    [Fact]
    public async Task WriteEncryptionFailedAsync_WhenPlatformUserIdIsEmpty_SkipsWriteAndLogs()
    {
        var loggerMock = new Mock<ILogger<EncryptionInboxWriter>>();
        var sut = BuildSut(loggerMock.Object);

        await sut.Awaiting(s => s.WriteEncryptionFailedAsync(Guid.Empty, OperationId))
            .Should().NotThrowAsync("empty userId is a known edge case that must be skipped gracefully");

        _inbox.Verify(i => i.WriteAsync(It.IsAny<InboxWritePayload>(), It.IsAny<CancellationToken>()), Times.Never,
            "no write must reach the inbox client when userId is empty");

        loggerMock.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.IsAny<It.IsAnyType>(),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once,
            "a warning must be emitted so operators can diagnose anonymous operations");
    }
}
