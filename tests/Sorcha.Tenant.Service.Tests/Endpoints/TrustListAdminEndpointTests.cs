// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Moq;
using Sorcha.Tenant.Service.Endpoints;
using Sorcha.Tenant.Service.Storage;
using Sorcha.Tenant.Service.Tests.Fixtures.TrustLists;
using Sorcha.Tenant.Service.Trust;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// Feature 181 US3 (T034) — trusted-list admin endpoint contract: import verifies + persists a
/// snapshot (201) or fails typed (400/409); list/detail read the store; delete is 204 and makes the
/// anchor read fail closed (404 TRUSTLIST_UNAVAILABLE). Handlers invoked directly against an in-memory
/// store + real import service.
/// </summary>
public class TrustListAdminEndpointTests
{
    private static int? Status(IResult result) =>
        result.GetType().GetProperty("StatusCode")?.GetValue(result) as int?;

    private static object? Value(IResult result) =>
        result.GetType().GetProperty("Value")?.GetValue(result);

    private static (ITrustedListSnapshotStore Store, TrustedListImportService Import) NewBackend()
    {
        var store = new InMemoryTrustedListSnapshotStore();
        return (store, new TrustedListImportService(store));
    }

    private static IFormFile FileFrom(string xml)
    {
        var bytes = Encoding.UTF8.GetBytes(xml);
        return new FormFile(new MemoryStream(bytes), 0, bytes.Length, "document", "trustlist.xml");
    }

    private static Task<IResult> Import(TrustedListImportService import, ITrustedListSnapshotStore _, string trustListId, string xml)
        => TrustEndpoints.ImportTrustList(
            trustListId, new DefaultHttpContext(), import, Mock.Of<IHttpClientFactory>(),
            FileFrom(xml), sourceUrl: null, CancellationToken.None);

    [Fact]
    public async Task Import_ValidList_Returns201WithSummary()
    {
        using var fixture = new TrustListFixture();
        var (store, import) = NewBackend();

        var result = await Import(import, store, "eu-lotl", fixture.BuildSignedTrustList(fixture.DefaultOptions(5)));

        Status(result).Should().Be(StatusCodes.Status201Created);
        var summary = Value(result).Should().BeOfType<TrustListSnapshotSummaryResponse>().Subject;
        summary.TrustListId.Should().Be("eu-lotl");
        summary.SequenceNumber.Should().Be(5);
        summary.AnchorCount.Should().Be(1);
        summary.Freshness.Should().Be("Fresh");
        summary.Status.Should().Be("Active");
    }

    [Fact]
    public async Task Import_TamperedList_Returns400SignatureInvalid()
    {
        using var fixture = new TrustListFixture();
        var (store, import) = NewBackend();
        var tampered = fixture.Tamper(fixture.BuildSignedTrustList(fixture.DefaultOptions()));

        var result = await Import(import, store, "eu-lotl", tampered);

        Status(result).Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task Import_SequenceRegression_Returns409()
    {
        using var fixture = new TrustListFixture();
        var (store, import) = NewBackend();
        await Import(import, store, "eu-lotl", fixture.BuildSignedTrustList(fixture.DefaultOptions(10)));

        var result = await Import(import, store, "eu-lotl", fixture.BuildSignedTrustList(fixture.DefaultOptions(9)));

        Status(result).Should().Be(StatusCodes.Status409Conflict);
    }

    [Fact]
    public async Task Import_NoDocumentOrUrl_Returns400()
    {
        var (_, import) = NewBackend();
        var result = await TrustEndpoints.ImportTrustList(
            "eu-lotl", new DefaultHttpContext(), import, Mock.Of<IHttpClientFactory>(),
            document: null, sourceUrl: null, CancellationToken.None);

        Status(result).Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public async Task List_ReturnsActiveSummaries()
    {
        using var fixture = new TrustListFixture();
        var (store, import) = NewBackend();
        await Import(import, store, "eu-lotl", fixture.BuildSignedTrustList(fixture.DefaultOptions(1)));
        await Import(import, store, "ie-tl", fixture.BuildSignedTrustList(fixture.DefaultOptions(1)));

        var result = await TrustEndpoints.ListTrustLists(store, CancellationToken.None);

        var list = Value(result).Should().BeAssignableTo<System.Collections.Generic.IEnumerable<TrustListSnapshotSummaryResponse>>().Subject;
        list.Select(s => s.TrustListId).Should().BeEquivalentTo(["eu-lotl", "ie-tl"]);
    }

    [Fact]
    public async Task GetDetail_ReturnsAnchorsAndExtractionSummary_404WhenUnknown()
    {
        using var fixture = new TrustListFixture();
        var (store, import) = NewBackend();
        await Import(import, store, "eu-lotl", fixture.BuildSignedTrustList(fixture.DefaultOptions()));

        var ok = await TrustEndpoints.GetTrustList("eu-lotl", store, CancellationToken.None);
        var detail = Value(ok).Should().BeOfType<TrustListSnapshotDetailResponse>().Subject;
        detail.Anchors.Should().ContainSingle();
        detail.ExtractionSummary.Should().Contain("\"extracted\":1");

        var missing = await TrustEndpoints.GetTrustList("nope", store, CancellationToken.None);
        Status(missing).Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public async Task Anchors_ReturnsDerRoots_AndFreshness()
    {
        using var fixture = new TrustListFixture();
        var (store, import) = NewBackend();
        await Import(import, store, "eu-lotl", fixture.BuildSignedTrustList(fixture.DefaultOptions()));

        var result = await TrustEndpoints.GetTrustListAnchors("eu-lotl", store, CancellationToken.None);

        var anchors = Value(result).Should().BeOfType<TrustListAnchorsResponse>().Subject;
        anchors.RootsDerBase64.Should().ContainSingle();
        anchors.Freshness.Should().Be("Fresh");
    }

    [Fact]
    public async Task Delete_RemovesSnapshot_AnchorReadThenFailsClosed()
    {
        using var fixture = new TrustListFixture();
        var (store, import) = NewBackend();
        await Import(import, store, "eu-lotl", fixture.BuildSignedTrustList(fixture.DefaultOptions()));

        var del = await TrustEndpoints.DeleteTrustList("eu-lotl", store, CancellationToken.None);
        Status(del).Should().Be(StatusCodes.Status204NoContent);

        var anchors = await TrustEndpoints.GetTrustListAnchors("eu-lotl", store, CancellationToken.None);
        Status(anchors).Should().Be(StatusCodes.Status404NotFound);
        Value(anchors)!.ToString().Should().Contain(TrustListErrorCodes.Unavailable);
    }
}
