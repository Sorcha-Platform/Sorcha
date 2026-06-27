// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>Unit tests for <see cref="PersonaInboxWriter"/>. Feature 169.</summary>
public sealed class PersonaInboxWriterTests
{
    private readonly Mock<IInboxService> _inbox = new();
    private readonly Guid _userId = Guid.NewGuid();
    private const string PersonaName = "Test Persona";

    private PersonaInboxWriter BuildSut(ILogger<PersonaInboxWriter>? logger = null) =>
        new(_inbox.Object, logger ?? NullLogger<PersonaInboxWriter>.Instance);

    private void SetupInboxSuccess()
    {
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((InboxWriteRequest r, CancellationToken _) =>
                new InboxWriteResult(new InboxEntry { Id = Guid.NewGuid() }, IsIdempotent: false));
    }

    [Fact]
    public async Task WritePersonaSavedAsync_PostsExpectedSystemPayload()
    {
        InboxWriteRequest? captured = null;
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWriteRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync((InboxWriteRequest r, CancellationToken _) =>
                new InboxWriteResult(new InboxEntry { Id = Guid.NewGuid() }, IsIdempotent: false));

        var sut = BuildSut();
        await sut.WritePersonaSavedAsync(_userId, PersonaName);

        captured.Should().NotBeNull();
        captured!.PlatformUserId.Should().Be(_userId);
        captured.Category.Should().Be(InboxCategory.System);
        captured.Severity.Should().Be(InboxSeverity.Info);
        captured.CorrelationKey.Should().Contain("persona:saved");
        captured.Title.Should().Be("Profile updated");
        captured.IconKey.Should().Be("person");
    }

    [Fact]
    public async Task WritePersonaDeletedAsync_PostsExpectedSystemPayload()
    {
        InboxWriteRequest? captured = null;
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWriteRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync((InboxWriteRequest r, CancellationToken _) =>
                new InboxWriteResult(new InboxEntry { Id = Guid.NewGuid() }, IsIdempotent: false));

        var sut = BuildSut();
        await sut.WritePersonaDeletedAsync(_userId, PersonaName);

        captured.Should().NotBeNull();
        captured!.PlatformUserId.Should().Be(_userId);
        captured.Category.Should().Be(InboxCategory.System);
        captured.Severity.Should().Be(InboxSeverity.Info);
        captured.CorrelationKey.Should().Contain("persona:deleted");
        captured.Title.Should().Be("Profile deleted");
        captured.IconKey.Should().Be("person");
    }

    [Fact]
    public async Task WritePersonaSavedAsync_WhenInboxThrows_LogsWarningAndDoesNotThrow()
    {
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("inbox down"));

        var loggerMock = new Mock<ILogger<PersonaInboxWriter>>();
        var sut = BuildSut(loggerMock.Object);

        await sut.Awaiting(s => s.WritePersonaSavedAsync(_userId, PersonaName))
            .Should().NotThrowAsync("inbox-write failures must never block the persona operation");

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
    public async Task WritePersonaDeletedAsync_WhenInboxThrows_LogsWarningAndDoesNotThrow()
    {
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("inbox down"));

        var loggerMock = new Mock<ILogger<PersonaInboxWriter>>();
        var sut = BuildSut(loggerMock.Object);

        await sut.Awaiting(s => s.WritePersonaDeletedAsync(_userId, PersonaName))
            .Should().NotThrowAsync("inbox-write failures must never block the persona operation");

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
