// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Storage;
using Sorcha.Tenant.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Storage;

/// <summary>
/// Feature 181 US3 (T031) — the trusted-list snapshot store: import supersedes the prior Active
/// version, the newest Active per list identity is authoritative, and delete removes every version.
/// Runs the same behaviour contract against both the EF store (in-memory provider) and the
/// in-memory fallback.
/// </summary>
public sealed class TrustedListSnapshotStoreTests
{
    private static TrustedListSnapshot Snapshot(string trustListId, long seq, params string[] anchorSubjects)
    {
        var s = new TrustedListSnapshot
        {
            Id = Guid.NewGuid(),
            TrustListId = trustListId,
            SequenceNumber = seq,
            ListIssueDateTime = DateTimeOffset.UtcNow,
            SignerCertSubject = "CN=Scheme Operator",
            SignerCertThumbprint = "abc123",
            ImportedAt = DateTimeOffset.UtcNow,
            ImportedByPlatformUserId = Guid.NewGuid(),
            RawDocumentSha256 = "deadbeef",
            Status = TrustedListSnapshotStatus.Active,
            ExtractionSummary = "{}",
        };
        foreach (var subject in anchorSubjects)
        {
            s.Anchors.Add(new TrustedListAnchor
            {
                Id = Guid.NewGuid(),
                SnapshotId = s.Id,
                CertificateDer = [1, 2, 3],
                SubjectDn = subject,
                Thumbprint = Guid.NewGuid().ToString("N"),
                ServiceTypeIdentifier = "http://uri.etsi.org/TrstSvc/Svctype/CA/QC",
                ServiceStatus = "http://uri.etsi.org/TrstSvc/TrustedList/Svcstatus/granted",
                NotBefore = DateTimeOffset.UtcNow.AddYears(-1),
                NotAfter = DateTimeOffset.UtcNow.AddYears(1),
            });
        }
        return s;
    }

    public static TheoryData<Func<ITrustedListSnapshotStore>> Stores() => new()
    {
        () => new InMemoryTrustedListSnapshotStore(),
        () => new EfTrustedListSnapshotStore(InMemoryDbContextFactory.Create()),
    };

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task AddAsync_SupersedesPriorActive_NewestIsAuthoritative(Func<ITrustedListSnapshotStore> factory)
    {
        var store = factory();
        await store.AddAsync(Snapshot("eu-lotl", 10, "CN=CA One"));
        await store.AddAsync(Snapshot("eu-lotl", 11, "CN=CA Two"));

        var active = await store.GetActiveAsync("eu-lotl");
        active.Should().NotBeNull();
        active!.SequenceNumber.Should().Be(11);
        active.Anchors.Should().ContainSingle().Which.SubjectDn.Should().Be("CN=CA Two");

        (await store.GetActiveSequenceAsync("eu-lotl")).Should().Be(11);
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task GetActiveAsync_UnknownListId_ReturnsNull(Func<ITrustedListSnapshotStore> factory)
    {
        var store = factory();
        (await store.GetActiveAsync("nope")).Should().BeNull();
        (await store.GetActiveSequenceAsync("nope")).Should().BeNull();
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task ListActiveAsync_ReturnsOnlyActive_OnePerListIdentity(Func<ITrustedListSnapshotStore> factory)
    {
        var store = factory();
        await store.AddAsync(Snapshot("eu-lotl", 1));
        await store.AddAsync(Snapshot("eu-lotl", 2));   // supersedes seq 1
        await store.AddAsync(Snapshot("ie-tl", 5));

        var active = await store.ListActiveAsync();
        active.Should().HaveCount(2);
        active.Select(s => s.TrustListId).Should().BeEquivalentTo(new[] { "eu-lotl", "ie-tl" });
    }

    [Theory]
    [MemberData(nameof(Stores))]
    public async Task DeleteAsync_RemovesEveryVersion_AndIsIdempotent(Func<ITrustedListSnapshotStore> factory)
    {
        var store = factory();
        await store.AddAsync(Snapshot("eu-lotl", 1));
        await store.AddAsync(Snapshot("eu-lotl", 2));

        (await store.DeleteAsync("eu-lotl")).Should().BeTrue();
        (await store.GetActiveAsync("eu-lotl")).Should().BeNull();
        (await store.DeleteAsync("eu-lotl")).Should().BeFalse("delete is idempotent");
    }
}
