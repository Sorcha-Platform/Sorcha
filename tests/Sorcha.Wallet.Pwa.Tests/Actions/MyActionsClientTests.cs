// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Sorcha.Wallet.Pwa.Services.Actions;
using Sorcha.Wallet.Pwa.Services.Actions.Models;
using Xunit;

namespace Sorcha.Wallet.Pwa.Tests.Actions;

/// <summary>
/// Feature 151 — contract test for <see cref="HttpMyActionsClient"/>: maps the existing
/// <c>/api/actions/pending</c> and <c>/api/actions/pending/count</c> JSON shapes onto the inbox
/// DTOs, with tolerant urgency parsing and title fallback. Guards drift against
/// <c>contracts/consumed-endpoints.md</c>.
/// </summary>
public sealed class MyActionsClientTests
{
    [Fact]
    public async Task GetPendingAsync_MapsItems_WithTitleAndUrgencyAndDeadline()
    {
        const string json = """
        {
          "items": [
            {
              "instanceId": "11111111-1111-1111-1111-111111111111",
              "actionId": 2,
              "actionTitle": "Upload proof of address",
              "blueprintTitle": "Blue Badge Application",
              "instanceReference": "BB-RIV-14-A7K3",
              "summary": "We need a recent utility bill.",
              "urgency": "urgent",
              "deadline": "2026-07-01T00:00:00+00:00",
              "receivedAt": "2026-06-13T09:00:00+00:00"
            }
          ],
          "totalCount": 1, "page": 1, "pageSize": 20
        }
        """;
        var client = Create((req, _) =>
        {
            req.RequestUri!.AbsolutePath.Should().Be("/api/actions/pending");
            return Ok(json);
        });

        var items = await client.GetPendingAsync();

        items.Should().ContainSingle();
        var item = items[0];
        item.InstanceId.Should().Be("11111111-1111-1111-1111-111111111111");
        item.ActionId.Should().Be(2);
        item.Title.Should().Be("Upload proof of address");
        item.WorkflowTitle.Should().Be("Blue Badge Application");
        item.Reference.Should().Be("BB-RIV-14-A7K3");
        item.Urgency.Should().Be(ActionUrgency.Urgent);
        item.Deadline.Should().Be(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task GetPendingAsync_UnknownUrgency_MapsToNormal()
    {
        var client = Create((_, _) => Ok(ItemWith(urgency: "bananas")));

        var items = await client.GetPendingAsync();

        items[0].Urgency.Should().Be(ActionUrgency.Normal);
    }

    [Fact]
    public async Task GetPendingAsync_NoActionTitle_FallsBackToBlueprintTitle()
    {
        var client = Create((_, _) => Ok(ItemWith(urgency: "normal", actionTitle: "")));

        var items = await client.GetPendingAsync();

        items[0].Title.Should().Be("Blue Badge Application");
    }

    [Fact]
    public async Task GetPendingAsync_NoTitlesAtAll_FallsBackToActionId()
    {
        const string json = """
        { "items": [ { "instanceId": "i", "actionId": 7, "receivedAt": "2026-06-13T09:00:00+00:00" } ],
          "totalCount": 1, "page": 1, "pageSize": 20 }
        """;
        var client = Create((_, _) => Ok(json));

        var items = await client.GetPendingAsync();

        items[0].Title.Should().Be("Action 7");
    }

    [Fact]
    public async Task GetPendingAsync_TransientFailure_Throws()
    {
        var client = Create((_, _) => throw new HttpRequestException("offline"));

        await client.Invoking(c => c.GetPendingAsync())
            .Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetCountAsync_MapsCount()
    {
        var client = Create((req, _) =>
        {
            req.RequestUri!.AbsolutePath.Should().Be("/api/actions/pending/count");
            return Ok("""{ "count": 4, "urgentCount": 0 }""");
        });

        var count = await client.GetCountAsync();

        count.Count.Should().Be(4);
    }

    [Fact]
    public async Task GetCountAsync_TransientFailure_Throws()
    {
        var client = Create((_, _) => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        await client.Invoking(c => c.GetCountAsync())
            .Should().ThrowAsync<HttpRequestException>();
    }

    private static string ItemWith(string urgency, string actionTitle = "Some action") => $$"""
    {
      "items": [
        {
          "instanceId": "11111111-1111-1111-1111-111111111111",
          "actionId": 1,
          "actionTitle": "{{actionTitle}}",
          "blueprintTitle": "Blue Badge Application",
          "urgency": "{{urgency}}",
          "receivedAt": "2026-06-13T09:00:00+00:00"
        }
      ],
      "totalCount": 1, "page": 1, "pageSize": 20
    }
    """;

    private static HttpMyActionsClient Create(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond)
    {
        var http = new HttpClient(new StubHandler(respond)) { BaseAddress = new Uri("https://test.example.com") };
        return new HttpMyActionsClient(http);
    }

    private static HttpResponseMessage Ok(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _respond;
        public StubHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(_respond(request, ct));
    }
}
