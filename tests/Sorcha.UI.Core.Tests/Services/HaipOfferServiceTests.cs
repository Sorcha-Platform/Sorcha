// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Sorcha.UI.Core.Services.Credentials;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

/// <summary>
/// Unit tests for <see cref="HaipOfferService.GetVerificationResultAsync"/>.
/// Covers: happy-path states, no-result-yet, and all transport failure paths.
/// </summary>
public class HaipOfferServiceTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _handlerMock;
    private readonly HttpClient _httpClient;
    private readonly HaipOfferService _service;
    private readonly List<HttpResponseMessage> _responses = [];

    public HaipOfferServiceTests()
    {
        _handlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:80")
        };
        _service = new HaipOfferService(_httpClient, Mock.Of<ILogger<HaipOfferService>>());
    }

    public void Dispose()
    {
        foreach (var r in _responses) r.Dispose();
        _httpClient?.Dispose();
        GC.SuppressFinalize(this);
    }

    private void SetupStatusResponse(Guid requestId, HttpStatusCode statusCode, object? body = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = body != null
                ? JsonContent.Create(body, options: new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase })
                : new StringContent(string.Empty)
        };
        _responses.Add(response);

        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.Method == HttpMethod.Get &&
                    req.RequestUri!.ToString().Contains($"/api/presentations/{requestId}/status")),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    // ── Happy-path: terminal states ───────────────────────────────────────────

    [Fact]
    public async Task GetVerificationResultAsync_BffReturnsSuccess_ReturnsVerifiedOutcome()
    {
        var id = Guid.NewGuid();
        SetupStatusResponse(id, HttpStatusCode.OK, new { state = "success" });

        var outcome = await _service.GetVerificationResultAsync(id);

        outcome.IsTransportError.Should().BeFalse();
        outcome.Result.Should().NotBeNull();
        outcome.Result!.State.Should().Be(HaipVerificationStates.Verified);
        outcome.Result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task GetVerificationResultAsync_BffReturnsDecline_ReturnsDeniedOutcome()
    {
        var id = Guid.NewGuid();
        SetupStatusResponse(id, HttpStatusCode.OK, new { state = "decline" });

        var outcome = await _service.GetVerificationResultAsync(id);

        outcome.IsTransportError.Should().BeFalse();
        outcome.Result!.State.Should().Be(HaipVerificationStates.Denied);
    }

    [Fact]
    public async Task GetVerificationResultAsync_BffReturnsAbandoned_ReturnsCancelledOutcome()
    {
        var id = Guid.NewGuid();
        SetupStatusResponse(id, HttpStatusCode.OK, new { state = "abandoned" });

        var outcome = await _service.GetVerificationResultAsync(id);

        outcome.IsTransportError.Should().BeFalse();
        outcome.Result!.State.Should().Be(HaipVerificationStates.Cancelled);
    }

    [Fact]
    public async Task GetVerificationResultAsync_BffReturnsAbandonedWithLateOutcome_ReturnsCancelledOutcome()
    {
        var id = Guid.NewGuid();
        SetupStatusResponse(id, HttpStatusCode.OK, new { state = "abandoned-with-late-outcome" });

        var outcome = await _service.GetVerificationResultAsync(id);

        outcome.IsTransportError.Should().BeFalse();
        outcome.Result!.State.Should().Be(HaipVerificationStates.Cancelled);
    }

    [Fact]
    public async Task GetVerificationResultAsync_BffReturnsExpiredState_ReturnsExpiredOutcome()
    {
        var id = Guid.NewGuid();
        SetupStatusResponse(id, HttpStatusCode.OK, new { state = "expired" });

        var outcome = await _service.GetVerificationResultAsync(id);

        outcome.IsTransportError.Should().BeFalse();
        outcome.Result!.State.Should().Be(HaipVerificationStates.Expired);
    }

    // ── No-result-yet: continue-polling paths ─────────────────────────────────

    [Fact]
    public async Task GetVerificationResultAsync_BffReturnsAwaitingPresentation_ReturnsNullResultNoError()
    {
        var id = Guid.NewGuid();
        SetupStatusResponse(id, HttpStatusCode.OK, new { state = "awaiting-presentation" });

        var outcome = await _service.GetVerificationResultAsync(id);

        outcome.IsTransportError.Should().BeFalse();
        outcome.Result.Should().BeNull();
    }

    [Fact]
    public async Task GetVerificationResultAsync_BffReturnsUnknownState_ReturnsNullResultNoError()
    {
        var id = Guid.NewGuid();
        SetupStatusResponse(id, HttpStatusCode.OK, new { state = "unknown" });

        var outcome = await _service.GetVerificationResultAsync(id);

        outcome.IsTransportError.Should().BeFalse();
        outcome.Result.Should().BeNull();
    }

    // ── Transport failure paths ───────────────────────────────────────────────

    [Fact]
    public async Task GetVerificationResultAsync_Bff401_ReturnsTransportError()
    {
        var id = Guid.NewGuid();
        SetupStatusResponse(id, HttpStatusCode.Unauthorized);

        var outcome = await _service.GetVerificationResultAsync(id);

        outcome.IsTransportError.Should().BeTrue();
        outcome.ErrorMessage.Should().NotBeNullOrEmpty();
        outcome.Result.Should().BeNull();
    }

    [Fact]
    public async Task GetVerificationResultAsync_Bff403_ReturnsTransportError()
    {
        var id = Guid.NewGuid();
        SetupStatusResponse(id, HttpStatusCode.Forbidden);

        var outcome = await _service.GetVerificationResultAsync(id);

        outcome.IsTransportError.Should().BeTrue();
        outcome.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetVerificationResultAsync_Bff500_ReturnsTransportError()
    {
        var id = Guid.NewGuid();
        SetupStatusResponse(id, HttpStatusCode.InternalServerError);

        var outcome = await _service.GetVerificationResultAsync(id);

        outcome.IsTransportError.Should().BeTrue();
        outcome.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetVerificationResultAsync_NetworkError_ReturnsTransportError()
    {
        var id = Guid.NewGuid();
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var outcome = await _service.GetVerificationResultAsync(id);

        outcome.IsTransportError.Should().BeTrue();
        outcome.ErrorMessage.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetVerificationResultAsync_Bff404_ReturnsExpiredOutcome()
    {
        var id = Guid.NewGuid();
        SetupStatusResponse(id, HttpStatusCode.NotFound);

        var outcome = await _service.GetVerificationResultAsync(id);

        outcome.IsTransportError.Should().BeFalse();
        outcome.Result!.State.Should().Be(HaipVerificationStates.Expired);
    }
}
