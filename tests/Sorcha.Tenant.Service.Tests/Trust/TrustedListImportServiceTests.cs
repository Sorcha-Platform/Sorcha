// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FluentAssertions;
using Sorcha.Tenant.Service.Storage;
using Sorcha.Tenant.Service.Tests.Fixtures.TrustLists;
using Sorcha.Tenant.Service.Trust;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Trust;

/// <summary>
/// Feature 181 US3 (T033) — the trusted-list import service verifies the enveloped XMLDSig signature,
/// parses TS 119 612 scheme information, extracts granted CA/QC anchors with a summary, and enforces
/// sequence monotonicity. Exercised end-to-end over the ETSI TS 119 612 fixture generator.
/// </summary>
public sealed class TrustedListImportServiceTests
{
    private static byte[] Bytes(string xml) => Encoding.UTF8.GetBytes(xml);

    private static TrustedListImportService NewService(out ITrustedListSnapshotStore store)
    {
        store = new InMemoryTrustedListSnapshotStore();
        return new TrustedListImportService(store);
    }

    [Fact]
    public async Task ImportAsync_ValidSignedList_ExtractsAnchorsAndSchemeInfo()
    {
        using var fixture = new TrustListFixture();
        var xml = fixture.BuildSignedTrustList(fixture.DefaultOptions(sequenceNumber: 7));
        var service = NewService(out _);

        var result = await service.ImportAsync("eu-lotl", Bytes(xml), sourceUrl: null, Guid.NewGuid());

        result.Success.Should().BeTrue(result.ErrorMessage);
        var snap = result.Snapshot!;
        snap.SequenceNumber.Should().Be(7);
        snap.SchemeTerritory.Should().Be("IE");
        snap.SchemeOperatorName.Should().Be("Sorcha Test Scheme Operator");
        snap.NextUpdate.Should().NotBeNull();
        snap.SignerCertSubject.Should().Contain("Sorcha Test Scheme Operator");
        snap.SignerCertThumbprint.Should().NotBeNullOrEmpty();
        snap.RawDocumentSha256.Should().HaveLength(64);
        snap.Anchors.Should().ContainSingle();
        snap.Anchors[0].SubjectDn.Should().Contain("Sorcha Test Trust CA");
        snap.Anchors[0].ServiceTypeIdentifier.Should().Be(TrustListFixture.CaQcServiceType);
        snap.ExtractionSummary.Should().Contain("\"extracted\":1");
    }

    [Fact]
    public async Task ImportAsync_TamperedList_FailsSignatureInvalid()
    {
        using var fixture = new TrustListFixture();
        var signed = fixture.BuildSignedTrustList(fixture.DefaultOptions());
        var tampered = fixture.Tamper(signed);
        var service = NewService(out _);

        var result = await service.ImportAsync("eu-lotl", Bytes(tampered), null, Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(TrustListErrorCodes.SignatureInvalid);
    }

    [Fact]
    public async Task ImportAsync_UnsignedList_FailsSignatureInvalid()
    {
        using var fixture = new TrustListFixture();
        var unsigned = fixture.BuildTrustListXml(fixture.DefaultOptions());
        var service = NewService(out _);

        var result = await service.ImportAsync("eu-lotl", Bytes(unsigned), null, Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(TrustListErrorCodes.SignatureInvalid);
    }

    [Fact]
    public async Task ImportAsync_MalformedXml_FailsMalformed()
    {
        var service = NewService(out _);

        var result = await service.ImportAsync("eu-lotl", Bytes("<not-a-trust-list"), null, Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(TrustListErrorCodes.Malformed);
    }

    [Fact]
    public async Task ImportAsync_SequenceNotNewer_FailsSequenceRegression()
    {
        using var fixture = new TrustListFixture();
        var service = NewService(out _);

        (await service.ImportAsync("eu-lotl", Bytes(fixture.BuildSignedTrustList(fixture.DefaultOptions(10))), null, Guid.NewGuid()))
            .Success.Should().BeTrue();

        // Same sequence → regression.
        var same = await service.ImportAsync("eu-lotl", Bytes(fixture.BuildSignedTrustList(fixture.DefaultOptions(10))), null, Guid.NewGuid());
        same.Success.Should().BeFalse();
        same.ErrorCode.Should().Be(TrustListErrorCodes.SequenceRegression);

        // Lower sequence → regression.
        var lower = await service.ImportAsync("eu-lotl", Bytes(fixture.BuildSignedTrustList(fixture.DefaultOptions(9))), null, Guid.NewGuid());
        lower.ErrorCode.Should().Be(TrustListErrorCodes.SequenceRegression);
    }

    [Fact]
    public async Task ImportAsync_NewerSequence_SupersedesPrior()
    {
        using var fixture = new TrustListFixture();
        var service = NewService(out var store);

        await service.ImportAsync("eu-lotl", Bytes(fixture.BuildSignedTrustList(fixture.DefaultOptions(1))), null, Guid.NewGuid());
        var newer = await service.ImportAsync("eu-lotl", Bytes(fixture.BuildSignedTrustList(fixture.DefaultOptions(2))), null, Guid.NewGuid());

        newer.Success.Should().BeTrue();
        var active = await store.GetActiveAsync("eu-lotl");
        active!.SequenceNumber.Should().Be(2);
        (await store.ListActiveAsync()).Should().ContainSingle();
    }

    [Fact]
    public async Task ImportAsync_OddButValid_UnknownServiceTypesSkipped_NoAnchorsExtracted()
    {
        using var fixture = new TrustListFixture();
        // A validly-signed list whose only service is an unrelated type + a non-granted status.
        var opts = fixture.DefaultOptions() with
        {
            Services =
            [
                new TrustListServiceEntry(
                    "http://uri.etsi.org/TrstSvc/Svctype/unspecified",
                    "http://uri.etsi.org/TrstSvc/TrustedList/Svcstatus/withdrawn",
                    fixture.TestCa,
                    "Unrelated Service")
            ],
        };
        var service = NewService(out _);

        var result = await service.ImportAsync("eu-lotl", Bytes(fixture.BuildSignedTrustList(opts)), null, Guid.NewGuid());

        result.Success.Should().BeTrue("an odd-but-validly-signed list still imports");
        result.Snapshot!.Anchors.Should().BeEmpty();
        result.Snapshot.ExtractionSummary.Should().Contain("\"extracted\":0").And.Contain("\"skipped\":1");
    }

    [Fact]
    public async Task ImportAsync_NoNextUpdate_ImportsWithNullNextUpdate()
    {
        using var fixture = new TrustListFixture();
        var opts = fixture.DefaultOptions() with { NextUpdate = null };
        var service = NewService(out _);

        var result = await service.ImportAsync("eu-lotl", Bytes(fixture.BuildSignedTrustList(opts)), null, Guid.NewGuid());

        result.Success.Should().BeTrue();
        result.Snapshot!.NextUpdate.Should().BeNull();
    }
}
