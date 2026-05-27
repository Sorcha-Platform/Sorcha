// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Microsoft.AspNetCore.Http;

using Sorcha.ServiceClients.Trust;
using Sorcha.Tenant.Service.Endpoints;

using Xunit;

namespace Sorcha.Tenant.Service.Tests.Endpoints;

/// <summary>
/// Feature 135 / T040 — trust-list admin endpoint contract: PUT stores a snapshot and reports the
/// root count; GET returns metadata (404 when unknown); list returns all snapshots. Handlers are
/// invoked directly against the operator-snapshot provider.
/// </summary>
public class TrustListAdminEndpointTests
{
    private static int? Status(IResult result) =>
        result.GetType().GetProperty("StatusCode")?.GetValue(result) as int?;

    private static object? Value(IResult result) =>
        result.GetType().GetProperty("Value")?.GetValue(result);

    private static UploadTrustListRequest Request(params byte[][] roots) => new()
    {
        Source = "EU LOTL 2026-Q2 manual export",
        Roots = roots.Select(Convert.ToBase64String).ToList(),
        Freshness = new DateTimeOffset(2026, 4, 30, 0, 0, 0, TimeSpan.Zero)
    };

    [Fact]
    public async Task Put_ValidRoots_StoresSnapshot_AndReturnsSummary()
    {
        var provider = new OperatorSnapshotTrustListProvider();

        var result = TrustEndpoints.PutTrustList("eu-lotl-2026q2", Request([1, 2, 3], [4, 5, 6]), provider);

        var summary = Value(result).Should().BeOfType<TrustListSummaryResponse>().Subject;
        summary.TrustListId.Should().Be("eu-lotl-2026q2");
        summary.RootCount.Should().Be(2);
        summary.Freshness.Should().Be(new DateTimeOffset(2026, 4, 30, 0, 0, 0, TimeSpan.Zero));

        var stored = await provider.GetSnapshotAsync("eu-lotl-2026q2");
        stored.Should().NotBeNull();
        stored!.Roots.Should().HaveCount(2);
    }

    [Fact]
    public void Put_NoRoots_ReturnsBadRequest()
    {
        var provider = new OperatorSnapshotTrustListProvider();
        var result = TrustEndpoints.PutTrustList("x", new UploadTrustListRequest { Roots = [] }, provider);
        Status(result).Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void Put_InvalidBase64_ReturnsBadRequest()
    {
        var provider = new OperatorSnapshotTrustListProvider();
        var result = TrustEndpoints.PutTrustList("x",
            new UploadTrustListRequest { Roots = ["not valid base64 !!!"] }, provider);
        Status(result).Should().Be(StatusCodes.Status400BadRequest);
    }

    [Fact]
    public void Get_KnownSnapshot_ReturnsMetadata()
    {
        var provider = new OperatorSnapshotTrustListProvider();
        TrustEndpoints.PutTrustList("snap-1", Request([1, 2, 3]), provider);

        var result = TrustEndpoints.GetTrustList("snap-1", provider);

        var summary = Value(result).Should().BeOfType<TrustListSummaryResponse>().Subject;
        summary.TrustListId.Should().Be("snap-1");
        summary.RootCount.Should().Be(1);
        summary.Source.Should().Be("EU LOTL 2026-Q2 manual export");
    }

    [Fact]
    public void Get_UnknownSnapshot_ReturnsNotFound()
    {
        var provider = new OperatorSnapshotTrustListProvider();
        var result = TrustEndpoints.GetTrustList("missing", provider);
        Status(result).Should().Be(StatusCodes.Status404NotFound);
    }

    [Fact]
    public void List_ReturnsAllSnapshots()
    {
        var provider = new OperatorSnapshotTrustListProvider();
        TrustEndpoints.PutTrustList("a", Request([1]), provider);
        TrustEndpoints.PutTrustList("b", Request([2]), provider);

        var result = TrustEndpoints.ListTrustLists(provider);

        var list = Value(result).Should().BeAssignableTo<IEnumerable<TrustListSummaryResponse>>().Subject;
        list.Select(s => s.TrustListId).Should().BeEquivalentTo(["a", "b"]);
    }
}
