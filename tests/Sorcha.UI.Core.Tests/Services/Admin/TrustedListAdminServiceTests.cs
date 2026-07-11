// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Sorcha.UI.Core.Services.Admin;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services.Admin;

/// <summary>
/// Feature 181 US3 (T037) — the platform-admin trusted-list client maps the REST surface to view
/// models: list, import success/typed-failure, and delete. HTTP is intercepted via a mocked handler.
/// </summary>
public sealed class TrustedListAdminServiceTests
{
    private static (TrustedListAdminService Service, Mock<HttpMessageHandler> Handler) Build()
    {
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        var client = new HttpClient(handler.Object) { BaseAddress = new Uri("http://localhost") };
        return (new TrustedListAdminService(client, NullLogger<TrustedListAdminService>.Instance), handler);
    }

    private static void Respond(Mock<HttpMessageHandler> handler, HttpMethod method, HttpStatusCode status, string? json)
        => handler.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(r => r.Method == method),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(status)
            {
                Content = json is null ? null : new StringContent(json, Encoding.UTF8, "application/json"),
            });

    [Fact]
    public async Task ListAsync_MapsSummaries()
    {
        var (service, handler) = Build();
        Respond(handler, HttpMethod.Get, HttpStatusCode.OK, /*lang=json,strict*/ """
        [{"trustListId":"eu-lotl","sequenceNumber":3,"schemeTerritory":"EU","freshness":"Fresh",
          "anchorCount":2,"signerCertSubject":"CN=Op","signerCertThumbprint":"ab","status":"Active",
          "listIssueDateTime":"2026-01-01T00:00:00Z","importedAt":"2026-07-01T00:00:00Z"}]
        """);

        var list = await service.ListAsync();

        list.Should().ContainSingle();
        list[0].TrustListId.Should().Be("eu-lotl");
        list[0].AnchorCount.Should().Be(2);
        list[0].IsStale.Should().BeFalse();
    }

    [Fact]
    public async Task ImportAsync_Success_ReturnsOkOutcome()
    {
        var (service, handler) = Build();
        Respond(handler, HttpMethod.Post, HttpStatusCode.Created, /*lang=json,strict*/ """
        {"trustListId":"eu-lotl","sequenceNumber":5,"freshness":"Fresh","anchorCount":1,
         "signerCertSubject":"CN=Op","signerCertThumbprint":"ab","status":"Active",
         "listIssueDateTime":"2026-01-01T00:00:00Z","importedAt":"2026-07-01T00:00:00Z"}
        """);

        var outcome = await service.ImportAsync("eu-lotl", new MemoryStream(Encoding.UTF8.GetBytes("<x/>")), "list.xml");

        outcome.Success.Should().BeTrue();
        outcome.Summary!.SequenceNumber.Should().Be(5);
    }

    [Fact]
    public async Task ImportAsync_TypedFailure_SurfacesCodeAndMessage()
    {
        var (service, handler) = Build();
        Respond(handler, HttpMethod.Post, HttpStatusCode.Conflict, /*lang=json,strict*/
            """{"error":"TRUSTLIST_SEQUENCE_REGRESSION","error_description":"not newer"}""");

        var outcome = await service.ImportAsync("eu-lotl", new MemoryStream([1, 2]), "list.xml");

        outcome.Success.Should().BeFalse();
        outcome.ErrorCode.Should().Be("TRUSTLIST_SEQUENCE_REGRESSION");
        outcome.ErrorMessage.Should().Be("not newer");
    }

    [Fact]
    public async Task GetAsync_NotFound_ReturnsNull()
    {
        var (service, handler) = Build();
        Respond(handler, HttpMethod.Get, HttpStatusCode.NotFound, null);

        (await service.GetAsync("nope")).Should().BeNull();
    }

    [Fact]
    public async Task DeleteAsync_NoContent_ReturnsTrue()
    {
        var (service, handler) = Build();
        Respond(handler, HttpMethod.Delete, HttpStatusCode.NoContent, null);

        (await service.DeleteAsync("eu-lotl")).Should().BeTrue();
    }
}
