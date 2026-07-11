// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Sorcha.Blueprint.Engine.Credentials;
using Sorcha.Blueprint.Engine.Credentials.Sources;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Storage;
using Sorcha.Tenant.Service.Tests.Fixtures.TrustLists;
using Sorcha.Tenant.Service.Trust;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Integration;

/// <summary>
/// Feature 181 US3 (T038 / SC-004) — the full trusted-list rail end-to-end: import a signed ETSI
/// TS 119 612 list whose granted CA/QC anchor is the fixture CA, then verify a credential issued
/// under that CA against the imported snapshot via the engine's <see cref="TrustListSourceResolver"/>.
/// The decision vouches and the evidence names the snapshot (<c>{trustListId}#{sequence}</c>);
/// deleting the snapshot makes the same verification fail closed.
/// </summary>
public sealed class TrustListVerificationTests
{
    /// <summary>Reads the store's Active snapshot and maps its anchors to a <see cref="TrustAnchorSet"/>
    /// (the production adapter over the wire provider does the same; here we read the store directly).</summary>
    private sealed class StoreBackedAnchorProvider(ITrustedListSnapshotStore store) : ITenantTrustAnchorProvider
    {
        public async Task<TrustAnchorSet?> GetAnchorsAsync(string? anchorId, CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(anchorId)) return null;
            var snapshot = await store.GetActiveAsync(anchorId, ct);
            if (snapshot is null || snapshot.Anchors.Count == 0) return null;
            return new TrustAnchorSet
            {
                Roots = snapshot.Anchors.ConvertAll(a => a.CertificateDer),
                AnchorSetId = $"{snapshot.TrustListId}#{snapshot.SequenceNumber}",
                Freshness = snapshot.NextUpdate,
                CheckRevocation = false,
            };
        }
    }

    private static IssuerContext IssuerWithChain(TrustListFixture fixture)
    {
        // Issue a leaf under the fixture CA — the same CA the imported list vouches for.
        using var leafKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        using var leaf = fixture.IssueLeaf("Assured Identity Issuer", leafKey);
        return new IssuerContext
        {
            IssuerId = "did:sorcha:org:trustlisted",
            SignatureVerified = true,
            X5cChain = [leaf.Export(X509ContentType.Cert), fixture.TestCa.Export(X509ContentType.Cert)],
        };
    }

    [Fact]
    public async Task ImportedList_VerifiesCredentialUnderFixtureCa_WithSnapshotEvidence_ThenFailsClosedOnDelete()
    {
        using var fixture = new TrustListFixture();
        var store = new InMemoryTrustedListSnapshotStore();
        var import = new TrustedListImportService(store);

        // Import a signed list whose granted CA/QC anchor is the fixture CA (sequence 3).
        var xml = fixture.BuildSignedTrustList(fixture.DefaultOptions(sequenceNumber: 3));
        var imported = await import.ImportAsync("eu-lotl", Encoding.UTF8.GetBytes(xml), null, Guid.NewGuid());
        imported.Success.Should().BeTrue(imported.ErrorMessage);
        imported.Snapshot!.Anchors.Should().ContainSingle();

        var resolver = new TrustListSourceResolver(new StoreBackedAnchorProvider(store));
        var sourceRef = new TrustSourceRef { Kind = TrustSourceKind.TrustList, TrustListId = "eu-lotl" };

        // SC-004 positive: the credential's x5c chains to the imported anchor → vouched + snapshot evidence.
        var vouch = await resolver.VouchAsync(IssuerWithChain(fixture), sourceRef);
        vouch.Vouched.Should().BeTrue("the credential chains to the imported trusted-list CA");
        var evidence = new TrustEvidence();
        vouch.ApplyEvidence!(evidence);
        evidence.TrustListId.Should().Be("eu-lotl#3", "the evidence names the vouching snapshot (FR-015)");

        // SC-004 negative: delete the snapshot → the same verification fails closed (FR-014).
        (await store.DeleteAsync("eu-lotl")).Should().BeTrue();
        var afterDelete = await resolver.VouchAsync(IssuerWithChain(fixture), sourceRef);
        afterDelete.Vouched.Should().BeFalse("no snapshot remains to anchor the chain");
        afterDelete.Reason.Should().Be(TrustFailureReason.SourceUnavailable);
    }
}
