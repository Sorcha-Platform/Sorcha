// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Models.Identity;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>Unit tests for <see cref="PersonaInboxWriter"/>. Feature 169.</summary>
public sealed class PersonaInboxWriterTests
{
    private readonly Mock<IInboxService> _inbox = new();
    private readonly Guid _userId = Guid.NewGuid();
    private const string PersonaName = "Test Persona";

    public PersonaInboxWriterTests()
    {
        // #1703 — the writer now confirms platformUserId names a real platform user before writing
        // (this writer calls IInboxService directly, bypassing the HTTP endpoint's own #1506 guard).
        // Default fixture: any id verifies as existing; the dangling-id tests override this per-id.
        _inbox.Setup(i => i.PlatformUserExistsAsync(It.IsAny<PlatformUserId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

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
        await sut.WritePersonaSavedAsync(new PlatformUserId(_userId), PersonaName);

        captured.Should().NotBeNull();
        captured!.PlatformUserId.Value.Should().Be(_userId);
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
        await sut.WritePersonaDeletedAsync(new PlatformUserId(_userId), PersonaName);

        captured.Should().NotBeNull();
        captured!.PlatformUserId.Value.Should().Be(_userId);
        captured.Category.Should().Be(InboxCategory.System);
        captured.Severity.Should().Be(InboxSeverity.Warning);
        captured.CorrelationKey.Should().Contain("persona:deleted");
        captured.Title.Should().Be("Profile deleted");
        captured.IconKey.Should().Be("person");
    }

    [Fact]
    public async Task WritePersonaSavedAsync_SameUserWithinSameSecond_ProducesSameSourceEventId()
    {
        var ids = new List<Guid>();
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWriteRequest, CancellationToken>((r, _) => ids.Add(r.SourceEventId))
            .ReturnsAsync((InboxWriteRequest r, CancellationToken _) =>
                new InboxWriteResult(new InboxEntry { Id = Guid.NewGuid() }, IsIdempotent: false));

        var sut = BuildSut();
        // Two calls within the same wall-clock second produce the same key and collapse via the unique index.
        await sut.WritePersonaSavedAsync(new PlatformUserId(_userId), PersonaName);
        await sut.WritePersonaSavedAsync(new PlatformUserId(_userId), PersonaName);

        // Both SourceEventIds should be identical (same user, same second).
        ids.Should().HaveCount(2);
        ids[0].Should().Be(ids[1], "writes within the same second must be idempotent");
    }

    [Fact]
    public async Task WritePersonaDeletedAsync_SameUserWithinSameSecond_ProducesSameSourceEventId()
    {
        var ids = new List<Guid>();
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWriteRequest, CancellationToken>((r, _) => ids.Add(r.SourceEventId))
            .ReturnsAsync((InboxWriteRequest r, CancellationToken _) =>
                new InboxWriteResult(new InboxEntry { Id = Guid.NewGuid() }, IsIdempotent: false));

        var sut = BuildSut();
        await sut.WritePersonaDeletedAsync(new PlatformUserId(_userId), PersonaName);
        await sut.WritePersonaDeletedAsync(new PlatformUserId(_userId), PersonaName);

        ids.Should().HaveCount(2);
        ids[0].Should().Be(ids[1], "writes within the same second must be idempotent");
    }

    [Fact]
    public async Task WritePersonaSavedAsync_WhenInboxThrows_LogsWarningAndDoesNotThrow()
    {
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("inbox down"));

        var loggerMock = new Mock<ILogger<PersonaInboxWriter>>();
        var sut = BuildSut(loggerMock.Object);

        await sut.Awaiting(s => s.WritePersonaSavedAsync(new PlatformUserId(_userId), PersonaName))
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

        await sut.Awaiting(s => s.WritePersonaDeletedAsync(new PlatformUserId(_userId), PersonaName))
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

    /// <summary>
    /// #1703 sweep — this writer calls <c>IInboxService</c> directly, bypassing the HTTP endpoint's
    /// own #1506 guard, and <c>InboxEntry.PlatformUserId</c> carries no database foreign key. A
    /// non-existent id must be skipped here, not written verbatim as an unaddressable phantom entry.
    /// </summary>
    [Fact]
    public async Task WritePersonaSavedAsync_PlatformUserIdDoesNotExist_SkipsWrite()
    {
        _inbox.Setup(i => i.PlatformUserExistsAsync(new PlatformUserId(_userId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var sut = BuildSut();

        await sut.WritePersonaSavedAsync(new PlatformUserId(_userId), PersonaName);

        _inbox.Verify(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task WritePersonaDeletedAsync_PlatformUserIdDoesNotExist_SkipsWrite()
    {
        _inbox.Setup(i => i.PlatformUserExistsAsync(new PlatformUserId(_userId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var sut = BuildSut();

        await sut.WritePersonaDeletedAsync(new PlatformUserId(_userId), PersonaName);

        _inbox.Verify(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
