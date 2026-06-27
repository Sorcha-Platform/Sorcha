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
        captured.Severity.Should().Be("Warning");
        captured.CorrelationKey.Should().Be($"sorcha.inbox.encryption.fail:{OperationId}");
        captured.DetailHref.Should().Be($"/api/operations/{OperationId}");
        captured.Title.Should().Be("Register encryption failed");
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
}
