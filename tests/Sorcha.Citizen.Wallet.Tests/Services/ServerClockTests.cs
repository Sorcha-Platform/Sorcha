// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Sorcha.Citizen.Wallet.Services;
using Xunit;

namespace Sorcha.Citizen.Wallet.Tests.Services;

/// <summary>
/// Tests for the clock-skew observation pipeline (Feature 114, T101):
/// <see cref="ServerClockObserver"/> stores the most recent reading,
/// <see cref="ServerClockHandler"/> snapshots every response's Date header.
/// </summary>
public sealed class ServerClockTests
{
    [Fact]
    public void ServerClockObserver_BeforeAnyObservation_ReturnsZeroSkew()
    {
        var sut = new ServerClockObserver(TimeProvider.System);

        sut.GetLatest().Should().BeNull();
        sut.GetSkew().Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void ServerClockObserver_DeviceAheadOfServer_ReportsPositiveSkew()
    {
        var device = DateTimeOffset.Parse("2026-04-26T12:10:00Z");
        var server = DateTimeOffset.Parse("2026-04-26T12:00:00Z");
        var clock = new FixedClock(device);
        var sut = new ServerClockObserver(clock);

        sut.Record(server);

        var latest = sut.GetLatest();
        latest.Should().NotBeNull();
        latest!.Skew.Should().Be(TimeSpan.FromMinutes(10));
    }

    [Fact]
    public void ServerClockObserver_DeviceBehindServer_ReportsNegativeSkew()
    {
        var device = DateTimeOffset.Parse("2026-04-26T11:55:00Z");
        var server = DateTimeOffset.Parse("2026-04-26T12:00:00Z");
        var sut = new ServerClockObserver(new FixedClock(device));

        sut.Record(server);

        sut.GetSkew().Should().Be(TimeSpan.FromMinutes(-5));
    }

    [Fact]
    public async Task ServerClockHandler_RecordsEveryResponseDate()
    {
        var clock = new FixedClock(DateTimeOffset.Parse("2026-04-26T12:30:00Z"));
        var observer = new ServerClockObserver(clock);
        var inner = new StubHandler(req =>
        {
            var resp = new HttpResponseMessage(HttpStatusCode.OK);
            resp.Headers.Date = DateTimeOffset.Parse("2026-04-26T12:25:00Z");
            return resp;
        });
        var handler = new ServerClockHandler(observer) { InnerHandler = inner };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        await http.GetAsync("api/v1/wallet/sync");

        observer.GetSkew().Should().Be(TimeSpan.FromMinutes(5));
    }

    [Fact]
    public async Task ServerClockHandler_NoDateHeader_LeavesObserverUntouched()
    {
        var observer = new ServerClockObserver(TimeProvider.System);
        var inner = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)); // no Date
        inner.SuppressDateAutoSet = true;
        var handler = new ServerClockHandler(observer) { InnerHandler = inner };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        await http.GetAsync("api/v1/wallet/sync");

        observer.GetLatest().Should().BeNull("a missing Date header must not record a bogus observation");
    }

    private sealed class FixedClock : TimeProvider
    {
        private readonly DateTimeOffset _now;
        public FixedClock(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public bool SuppressDateAutoSet { get; set; }
        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var response = _respond(request);
            if (SuppressDateAutoSet)
            {
                // HttpClient may auto-populate Date on responses without one;
                // explicitly clear so the test sees what the SUT sees.
                response.Headers.Date = null;
            }
            return Task.FromResult(response);
        }
    }
}
