// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Models.Identity;

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

        // #1703 — the writer now confirms platformUserId names a real platform user before writing
        // (this writer calls IInboxService directly, bypassing the HTTP endpoint's own #1506 guard
        // and InboxEntry.PlatformUserId has no database FK, so nothing else would catch a wrong id).
        // Default fixture: any id verifies as existing; the dangling-id test overrides this per-id.
        _inbox.Setup(i => i.PlatformUserExistsAsync(It.IsAny<PlatformUserId>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task WriteTwoFactorEnabledAsync_PostsExpectedSecurityPayload()
    {
        InboxWriteRequest? captured = null;
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWriteRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync((InboxWriteRequest r, CancellationToken _) =>
                new InboxWriteResult(new InboxEntry { Id = Guid.NewGuid() }, IsIdempotent: false));

        await _sut.WriteTwoFactorEnabledAsync(new PlatformUserId(_userId));

        captured.Should().NotBeNull();
        captured!.PlatformUserId.Value.Should().Be(_userId);
        captured.Category.Should().Be(InboxCategory.Security);
        captured.Severity.Should().Be(InboxSeverity.Info);
        captured.CorrelationKey.Should().Be($"security:two-factor-enabled:{_userId:N}");
        captured.DetailHref.Should().Be("/security");
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

        await _sut.WriteTwoFactorDisabledAsync(new PlatformUserId(_userId));

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

        await _sut.WriteTwoFactorEnabledAsync(new PlatformUserId(_userId));
        await Task.Delay(1100);
        await _sut.WriteTwoFactorEnabledAsync(new PlatformUserId(_userId));

        sourceIds.Should().HaveCount(2);
        sourceIds[0].Should().NotBe(sourceIds[1]);
    }

    [Fact]
    public async Task WriteTwoFactorEnabledAsync_InboxThrows_DoesNotPropagate()
    {
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("inbox down"));

        var act = () => _sut.WriteTwoFactorEnabledAsync(new PlatformUserId(_userId));

        await act.Should().NotThrowAsync(
            "inbox-write failures must never block the security operation");
    }

    [Fact]
    public async Task WriteTwoFactorDisabledAsync_InboxThrows_DoesNotPropagate()
    {
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("inbox down"));

        var act = () => _sut.WriteTwoFactorDisabledAsync(new PlatformUserId(_userId));

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

        await _sut.WritePasswordResetAsync(new PlatformUserId(_userId));

        captured.Should().NotBeNull();
        captured!.Category.Should().Be(InboxCategory.Security);
        captured.CorrelationKey.Should().Be($"security:password-reset:{_userId:N}");
        captured.DetailHref.Should().Be("/security");
        captured.Title.Should().Be("Password reset");
        captured.Summary.Should().Contain("contact support");
        captured.IconKey.Should().Be("security.password-reset");
    }

    [Fact]
    public async Task WritePasswordResetAsync_InboxThrows_DoesNotPropagate()
    {
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("inbox down"));

        var act = () => _sut.WritePasswordResetAsync(new PlatformUserId(_userId));

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

        await _sut.WriteBackupCodeUsedAsync(new PlatformUserId(_userId));

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

        var act = () => _sut.WriteBackupCodeUsedAsync(new PlatformUserId(_userId));

        await act.Should().NotThrowAsync(
            "inbox-write failures must never block sign-in");
    }

    /// <summary>
    /// #1703 sweep — this writer calls <c>IInboxService</c> directly (Tenant-internal, no HTTP hop),
    /// so it bypasses the <c>POST /api/internal/inbox</c> endpoint's own #1506
    /// <c>PlatformUserExistsAsync</c> guard entirely, and <c>InboxEntry.PlatformUserId</c> carries no
    /// database foreign key. Before this fix a wrong-kind id (e.g. a UserIdentity id passed where a
    /// PlatformUser id was expected — the live #1703 defect in
    /// <c>TotpService.ValidateBackupCodeAsync</c>) would be written verbatim with NO error, NO log,
    /// and NO signal at all — worse than a swallowed 400, because there is no signal to swallow. The
    /// writer must confirm existence itself before writing.
    /// </summary>
    [Fact]
    public async Task WriteBackupCodeUsedAsync_PlatformUserIdDoesNotExist_SkipsWrite_NoPhantomEntry()
    {
        _inbox.Setup(i => i.PlatformUserExistsAsync(new PlatformUserId(_userId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await _sut.WriteBackupCodeUsedAsync(new PlatformUserId(_userId));

        _inbox.Verify(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
