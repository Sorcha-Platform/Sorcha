// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;
using Sorcha.Tenant.Service.Tests.Helpers;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>Unit tests for <see cref="TenantMembershipInboxWriter"/>. Phase 5 follow-up #3 of Feature 118.</summary>
public sealed class TenantMembershipInboxWriterTests : IDisposable
{
    private readonly Sorcha.Tenant.Service.Data.TenantDbContext _db;
    private readonly Mock<IInboxService> _inbox = new();
    private readonly TenantMembershipInboxWriter _sut;
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _orgId = Guid.NewGuid();

    public TenantMembershipInboxWriterTests()
    {
        _db = InMemoryDbContextFactory.Create();
        _sut = new TenantMembershipInboxWriter(_inbox.Object, _db, NullLogger<TenantMembershipInboxWriter>.Instance);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task WriteOrgMembershipAddedAsync_LooksUpOrgName_AndPostsExpectedPayload()
    {
        _db.Organizations.Add(new Organization
        {
            Id = _orgId,
            Name = "Acme Inc.",
            Subdomain = "acme",
            CreatedAt = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();

        InboxWriteRequest? captured = null;
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWriteRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync((InboxWriteRequest r, CancellationToken _) =>
                new InboxWriteResult(new InboxEntry { Id = Guid.NewGuid() }, IsIdempotent: false));

        await _sut.WriteOrgMembershipAddedAsync(_userId, _orgId, "Member");

        captured.Should().NotBeNull();
        captured!.PlatformUserId.Should().Be(_userId);
        captured.Category.Should().Be(InboxCategory.Membership);
        captured.Severity.Should().Be(InboxSeverity.Info);
        captured.CorrelationKey.Should().Be($"membership:{_orgId:N}");
        captured.Title.Should().Be("Welcome to Acme Inc.");
        captured.Summary.Should().Be("Role: Member");
    }

    [Fact]
    public async Task WriteOrgMembershipAddedAsync_OrgMissing_FallsBackToGenericTitle()
    {
        InboxWriteRequest? captured = null;
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWriteRequest, CancellationToken>((r, _) => captured = r)
            .ReturnsAsync((InboxWriteRequest r, CancellationToken _) =>
                new InboxWriteResult(new InboxEntry { Id = Guid.NewGuid() }, IsIdempotent: false));

        await _sut.WriteOrgMembershipAddedAsync(_userId, _orgId, "Admin");

        captured!.Title.Should().Be("Welcome to your new organisation");
        captured.Summary.Should().Be("Role: Admin");
    }

    [Fact]
    public async Task WriteOrgMembershipAddedAsync_DeterministicSourceEventId_CollapsesDuplicates()
    {
        var sourceIds = new List<Guid>();
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .Callback<InboxWriteRequest, CancellationToken>((r, _) => sourceIds.Add(r.SourceEventId))
            .ReturnsAsync((InboxWriteRequest r, CancellationToken _) =>
                new InboxWriteResult(new InboxEntry { Id = Guid.NewGuid() }, IsIdempotent: true));

        await _sut.WriteOrgMembershipAddedAsync(_userId, _orgId, "Member");
        await _sut.WriteOrgMembershipAddedAsync(_userId, _orgId, "Member");

        sourceIds.Should().HaveCount(2);
        sourceIds[0].Should().Be(sourceIds[1]);
    }

    [Fact]
    public async Task WriteOrgMembershipAddedAsync_InboxThrows_DoesNotPropagate()
    {
        _inbox.Setup(i => i.WriteAsync(It.IsAny<InboxWriteRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("inbox down"));

        var act = () => _sut.WriteOrgMembershipAddedAsync(_userId, _orgId, "Member");

        await act.Should().NotThrowAsync(
            "inbox-write failures must never block the org membership operation");
    }
}
