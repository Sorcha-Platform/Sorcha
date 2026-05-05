// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.UI.Core.Services;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

/// <summary>Unit tests for <see cref="InboxApiService"/>. Phase 5 of Feature 118.</summary>
public class InboxApiServiceTests
{
    private static InboxApiService Build(StubHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://localhost") },
            NullLogger<InboxApiService>.Instance);

    [Fact]
    public async Task ListAsync_BuildsExpectedQueryString()
    {
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent("""{"entries":[],"page":2,"pageSize":10,"totalCount":0}""")
            });

        var sut = Build(handler);
        var page = await sut.ListAsync(page: 2, pageSize: 10, category: "Action", unreadOnly: true);

        page.Should().NotBeNull();
        handler.LastRequest!.RequestUri!.PathAndQuery.Should().Be(
            "/api/me/inbox?page=2&pageSize=10&category=Action&unreadOnly=true");
    }

    [Fact]
    public async Task ListAsync_OnNon200_ReturnsNull()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        var sut = Build(handler);

        var page = await sut.ListAsync();

        page.Should().BeNull();
    }

    [Fact]
    public async Task GetByIdAsync_On404_ReturnsNullIndistinguishably()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var sut = Build(handler);

        var entry = await sut.GetByIdAsync(Guid.NewGuid());

        entry.Should().BeNull();
    }

    [Fact]
    public async Task GetUnreadCountAsync_ReturnsServerValue()
    {
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent("""{"unread":7}""") });
        var sut = Build(handler);

        (await sut.GetUnreadCountAsync()).Should().Be(7);
    }

    [Fact]
    public async Task GetUnreadCountAsync_OnError_ReturnsZero()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var sut = Build(handler);

        (await sut.GetUnreadCountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task MarkReadAsync_PostsToCorrectPath()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = Build(handler);

        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var ok = await sut.MarkReadAsync(id);

        ok.Should().BeTrue();
        handler.LastRequest!.Method.Should().Be(HttpMethod.Post);
        handler.LastRequest.RequestUri!.PathAndQuery.Should().Be(
            "/api/me/inbox/11111111-2222-3333-4444-555555555555/read");
    }

    [Fact]
    public async Task DismissAsync_PostsToCorrectPath()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var sut = Build(handler);

        var id = Guid.Parse("11111111-2222-3333-4444-555555555555");
        var ok = await sut.DismissAsync(id);

        ok.Should().BeTrue();
        handler.LastRequest!.RequestUri!.PathAndQuery.Should().Be(
            "/api/me/inbox/11111111-2222-3333-4444-555555555555/dismiss");
    }

    [Fact]
    public async Task MarkAllReadAsync_ReturnsCount()
    {
        var handler = new StubHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent("""{"marked":3}""") });
        var sut = Build(handler);

        (await sut.MarkAllReadAsync()).Should().Be(3);
    }

    [Fact]
    public async Task MarkAllReadAsync_OnError_ReturnsZero()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        var sut = Build(handler);

        (await sut.MarkAllReadAsync()).Should().Be(0);
    }

    [Fact]
    public async Task TransportException_OnList_ReturnsNullDoesNotPropagate()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("network down"));
        var sut = Build(handler);

        var page = await sut.ListAsync();

        page.Should().BeNull();
    }

    private static StringContent JsonContent(string json) =>
        new(json, Encoding.UTF8, "application/json");

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public HttpRequestMessage? LastRequest { get; private set; }

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
        {
            _respond = respond;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_respond(request));
        }
    }
}
