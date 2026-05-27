// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>Unit tests for <see cref="TenantSecurityInboxWriter"/>. Feature 118.</summary>
public sealed class TenantSecurityInboxWriterTests
{
    private readonly Mock<IInboxService> _inbox = new();
    private readonly TenantSecurityInboxWriter _sut;
    private readonly Guid _userId = Guid.NewGuid();

    public TenantSecurityInboxWriterTests()
    {
        _sut = new TenantSecurityInboxWriter(_inbox.Object, NullLogger<TenantSecurityInboxWriter>.Instance);
    }

    [Fact]
    public async Task WriteTwoFactorEnabledAsync_PostsExpectedSecurityPayload()
    {
        InboxWriteRequest? captured = null;
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWriteRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync((InboxWriteRequest r, CancellationToken _) =>
                new InboxWriteResult(new InboxEntry { Id = Guid.NewGuid() }, IsIdempotent: false));

        await _sut.WriteTwoFactorEnabledAsync(_userId);

        captured.Should().NotBeNull();
        captured!.PlatformUserId.Should().Be(_userId);
        captured.Category.Should().Be(InboxCategory.Security);
        captured.Severity.Should().Be(InboxSeverity.Info);
        captured.CorrelationKey.Should().Be($"security:two-factor-enabled:{_userId:N}");
        captured.DetailHref.Should().Be("/settings/security");
        captured.Title.Should().Be("Two-factor authentication enabled");
        captured.IconKey.Should().Be("security.2fa-enabled");
    }

    [Fact]
    public async Task WriteTwoFactorDisabledAsync_PostsExpectedSecurityPayload()
    {
        InboxWriteRequest? captured = null;
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWriteRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync((InboxWriteRequest r, CancellationToken _) =>
                new InboxWriteResult(new InboxEntry { Id = Guid.NewGuid() }, IsIdempotent: false));

        await _sut.WriteTwoFactorDisabledAsync(_userId);

        captured.Should().NotBeNull();
        captured!.Category.Should().Be(InboxCategory.Security);
        captured.CorrelationKey.Should().Be($"security:two-factor-disabled:{_userId:N}");
        captured.Title.Should().Be("Two-factor authentication disabled");
        captured.IconKey.Should().Be("security.2fa-disabled");
    }

    [Fact]
    public async Task WriteTwoFactorEnabledAsync_SourceEventIdFoldsTimestamp_AvoidsCrossEnableCollapse()
    {
        // Two enable events ~1s apart should produce different SourceEventIds so the
        // second entry isn't suppressed by the (PlatformUserId, SourceEventId) unique
        // index when a user enables, disables, and re-enables 2FA in quick succession.
        var sourceIds = new List<Guid>();
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWriteRequest, CancellationToken>((r, _) => sourceIds.Add(r.SourceEventId))
            .ReturnsAsync((InboxWriteRequest r, CancellationToken _) =>
                new InboxWriteResult(new InboxEntry { Id = Guid.NewGuid() }, IsIdempotent: false));

        await _sut.WriteTwoFactorEnabledAsync(_userId);
        await Task.Delay(1100);
        await _sut.WriteTwoFactorEnabledAsync(_userId);

        sourceIds.Should().HaveCount(2);
        sourceIds[0].Should().NotBe(sourceIds[1]);
    }

    [Fact]
    public async Task WriteTwoFactorEnabledAsync_InboxThrows_DoesNotPropagate()
    {
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("inbox down"));

        var act = () => _sut.WriteTwoFactorEnabledAsync(_userId);

        await act.Should().NotThrowAsync(
            "inbox-write failures must never block the security operation");
    }

    [Fact]
    public async Task WriteTwoFactorDisabledAsync_InboxThrows_DoesNotPropagate()
    {
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("inbox down"));

        var act = () => _sut.WriteTwoFactorDisabledAsync(_userId);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task WritePasswordResetAsync_PostsExpectedSecurityPayload()
    {
        InboxWriteRequest? captured = null;
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWriteRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync((InboxWriteRequest r, CancellationToken _) =>
                new InboxWriteResult(new InboxEntry { Id = Guid.NewGuid() }, IsIdempotent: false));

        await _sut.WritePasswordResetAsync(_userId);

        captured.Should().NotBeNull();
        captured!.Category.Should().Be(InboxCategory.Security);
        captured.CorrelationKey.Should().Be($"security:password-reset:{_userId:N}");
        captured.DetailHref.Should().Be("/settings/security");
        captured.Title.Should().Be("Password reset");
        captured.Summary.Should().Contain("contact support");
        captured.IconKey.Should().Be("security.password-reset");
    }

    [Fact]
    public async Task WritePasswordResetAsync_InboxThrows_DoesNotPropagate()
    {
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("inbox down"));

        var act = () => _sut.WritePasswordResetAsync(_userId);

        await act.Should().NotThrowAsync(
            "inbox-write failures must never block a password reset");
    }

    [Fact]
    public async Task WriteBackupCodeUsedAsync_PostsWarningSeverityPayload()
    {
        InboxWriteRequest? captured = null;
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWriteRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync((InboxWriteRequest r, CancellationToken _) =>
                new InboxWriteResult(new InboxEntry { Id = Guid.NewGuid() }, IsIdempotent: false));

        await _sut.WriteBackupCodeUsedAsync(_userId);

        captured.Should().NotBeNull();
        captured!.Category.Should().Be(InboxCategory.Security);
        captured.Severity.Should().Be(InboxSeverity.Warning);
        captured.CorrelationKey.Should().Be($"security:backup-code-used:{_userId:N}");
        captured.Title.Should().Be("Backup code used to sign in");
        captured.Summary.Should().Contain("change your password");
        captured.IconKey.Should().Be("security.backup-code-used");
    }

    [Fact]
    public async Task WriteBackupCodeUsedAsync_InboxThrows_DoesNotPropagate()
    {
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("inbox down"));

        var act = () => _sut.WriteBackupCodeUsedAsync(_userId);

        await act.Should().NotThrowAsync(
            "inbox-write failures must never block sign-in");
    }
}
