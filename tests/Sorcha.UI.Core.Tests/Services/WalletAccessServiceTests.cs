// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Sorcha.UI.Core.Models;
using Sorcha.UI.Core.Services;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

/// <summary>
/// Unit tests for WalletAccessService (grant, list, revoke, check).
/// </summary>
public class WalletAccessServiceTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _handlerMock;
    private readonly HttpClient _httpClient;
    private readonly WalletAccessService _service;
    private readonly List<HttpResponseMessage> _responses = [];

    public WalletAccessServiceTests()
    {
        _handlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_handlerMock.Object)
        {
            BaseAddress = new Uri("http://localhost:80")
        };
        var logger = new Mock<ILogger<WalletAccessService>>();
        _service = new WalletAccessService(_httpClient, logger.Object);
    }

    public void Dispose()
    {
        foreach (var r in _responses) r.Dispose();
        _httpClient.Dispose();
        GC.SuppressFinalize(this);
    }

    private void SetupResponse(HttpStatusCode statusCode, object? content = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = content != null ? JsonContent.Create(content) : new StringContent("")
        };
        _responses.Add(response);
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);
    }

    private void SetupNetworkError()
    {
        _handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>("SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(), ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));
    }

    // --- GrantAccessAsync ---

    [Fact]
    public async Task GrantAccessAsync_Success_ReturnsGrant()
    {
        var expected = new WalletAccessGrantViewModel
        {
            Id = "grant-1", Subject = "user-1", AccessRight = "ReadWrite",
            GrantedBy = "sorcha1abc", IsActive = true,
            GrantedAt = DateTimeOffset.UtcNow
        };
        SetupResponse(HttpStatusCode.OK, expected);

        var form = new GrantAccessFormModel { Subject = "user-1", AccessRight = "ReadWrite" };
        var result = await _service.GrantAccessAsync("sorcha1abc", form);

        result.Should().NotBeNull();
        result!.Subject.Should().Be("user-1");
        result.AccessRight.Should().Be("ReadWrite");
    }

    [Fact]
    public async Task GrantAccessAsync_Forbidden_ReturnsNull()
    {
        SetupResponse(HttpStatusCode.Forbidden);

        var form = new GrantAccessFormModel { Subject = "user-1", AccessRight = "ReadWrite" };
        var result = await _service.GrantAccessAsync("sorcha1abc", form);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GrantAccessAsync_NetworkError_ReturnsNull()
    {
        SetupNetworkError();

        var form = new GrantAccessFormModel { Subject = "user-1", AccessRight = "ReadWrite" };
        var result = await _service.GrantAccessAsync("sorcha1abc", form);

        result.Should().BeNull();
    }

    // --- ListAccessAsync ---

    [Fact]
    public async Task ListAccessAsync_Success_ReturnsGrants()
    {
        var grants = new List<WalletAccessGrantViewModel>
        {
            new() { Id = "g1", Subject = "user-1", AccessRight = "ReadWrite", IsActive = true },
            new() { Id = "g2", Subject = "user-2", AccessRight = "ReadOnly", IsActive = true }
        };
        SetupResponse(HttpStatusCode.OK, grants);

        var result = await _service.ListAccessAsync("sorcha1abc");

        result.Should().HaveCount(2);
    }

    [Fact]
    public async Task ListAccessAsync_ServerError_ReturnsEmpty()
    {
        SetupResponse(HttpStatusCode.InternalServerError);

        var result = await _service.ListAccessAsync("sorcha1abc");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task ListAccessAsync_NetworkError_ReturnsEmpty()
    {
        SetupNetworkError();

        var result = await _service.ListAccessAsync("sorcha1abc");

        result.Should().BeEmpty();
    }

    // --- RevokeAccessAsync ---

    [Fact]
    public async Task RevokeAccessAsync_Success_ReturnsTrue()
    {
        SetupResponse(HttpStatusCode.NoContent);

        var result = await _service.RevokeAccessAsync("sorcha1abc", "user-1");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task RevokeAccessAsync_NotFound_ReturnsFalse()
    {
        SetupResponse(HttpStatusCode.NotFound);

        var result = await _service.RevokeAccessAsync("sorcha1abc", "user-1");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task RevokeAccessAsync_NetworkError_ReturnsFalse()
    {
        SetupNetworkError();

        var result = await _service.RevokeAccessAsync("sorcha1abc", "user-1");

        result.Should().BeFalse();
    }

    // --- CheckAccessAsync ---

    [Fact]
    public async Task CheckAccessAsync_HasAccess_ReturnsTrue()
    {
        var check = new AccessCheckResult
        {
            WalletAddress = "sorcha1abc", Subject = "user-1",
            RequiredRight = "ReadWrite", HasAccess = true
        };
        SetupResponse(HttpStatusCode.OK, check);

        var result = await _service.CheckAccessAsync("sorcha1abc", "user-1", "ReadWrite");

        result.Should().NotBeNull();
        result!.HasAccess.Should().BeTrue();
    }

    [Fact]
    public async Task CheckAccessAsync_NoAccess_ReturnsFalse()
    {
        var check = new AccessCheckResult
        {
            WalletAddress = "sorcha1abc", Subject = "user-1",
            RequiredRight = "ReadWrite", HasAccess = false
        };
        SetupResponse(HttpStatusCode.OK, check);

        var result = await _service.CheckAccessAsync("sorcha1abc", "user-1", "ReadWrite");

        result.Should().NotBeNull();
        result!.HasAccess.Should().BeFalse();
    }

    [Fact]
    public async Task CheckAccessAsync_NetworkError_ReturnsNull()
    {
        SetupNetworkError();

        var result = await _service.CheckAccessAsync("sorcha1abc", "user-1", "ReadWrite");

        result.Should().BeNull();
    }
}
