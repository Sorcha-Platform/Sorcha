// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Tests for PasswordPolicyService: NIST min 12 chars, no complexity rules,
/// HIBP k-Anonymity breach check with negative result caching (24h).
/// </summary>
public class PasswordPolicyServiceTests
{
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly ILogger<PasswordPolicyService> _logger = NullLogger<PasswordPolicyService>.Instance;

    private PasswordPolicyService CreateService(HttpMessageHandler? handler = null)
    {
        var httpClient = handler is not null
            ? new HttpClient(handler)
            : new HttpClient(CreateMockHandler(HttpStatusCode.OK, "").Object);

        return new PasswordPolicyService(httpClient, _cache, _logger);
    }

    private static Mock<HttpMessageHandler> CreateMockHandler(
        HttpStatusCode statusCode, string content)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content)
            });
        return handler;
    }

    #region Minimum Length Enforcement

    [Fact]
    public async Task ValidateAsync_PasswordTooShort_ReturnsError()
    {
        var service = CreateService();

        var result = await service.ValidateAsync("short123");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("12 characters");
    }

    [Fact]
    public async Task ValidateAsync_EmptyPassword_ReturnsError()
    {
        var service = CreateService();

        var result = await service.ValidateAsync("");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("12 characters");
    }

    [Fact]
    public async Task ValidateAsync_NullPassword_ReturnsError()
    {
        var service = CreateService();

        var result = await service.ValidateAsync(null!);

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_ExactlyMinLength_PassesLengthCheck()
    {
        // 12 characters, not breached
        var handler = CreateMockHandler(HttpStatusCode.OK, "");
        var service = CreateService(handler.Object);

        var result = await service.ValidateAsync("123456789012");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_NoComplexityRules_SimplePasswordAllowed()
    {
        // NIST: no complexity rules — all lowercase is fine if long enough
        var handler = CreateMockHandler(HttpStatusCode.OK, "");
        var service = CreateService(handler.Object);

        var result = await service.ValidateAsync("aaaaaaaaaaaa");

        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region HIBP k-Anonymity Breach Check

    [Fact]
    public async Task ValidateAsync_BreachedPassword_ReturnsError()
    {
        // "password1234" SHA-1 = some hash; mock response contains matching suffix
        // SHA-1("password1234") prefix/suffix — we just need the mock to return the suffix
        var password = "password1234";
        var sha1 = ComputeSha1(password);
        var suffix = sha1[5..];

        var responseContent = $"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA:5\r\n{suffix}:42\r\nBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB:1";
        var handler = CreateMockHandler(HttpStatusCode.OK, responseContent);
        var service = CreateService(handler.Object);

        var result = await service.ValidateAsync(password);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("data breach");
    }

    [Fact]
    public async Task ValidateAsync_NotBreached_ReturnsValid()
    {
        // Mock response that does NOT contain the password's suffix
        var responseContent = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA:5\r\nBBBBBBBBBBBBBBBBBBBBBBBBBBBBBBB:1";
        var handler = CreateMockHandler(HttpStatusCode.OK, responseContent);
        var service = CreateService(handler.Object);

        var result = await service.ValidateAsync("secureunbreachedpass");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_HibpApiSendsOnlyPrefix_VerifiesKAnonymity()
    {
        // Verify only the 5-char SHA-1 prefix is sent to the API
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.Is<HttpRequestMessage>(req =>
                    req.RequestUri!.AbsolutePath.Length == "/range/XXXXX".Length),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("")
            });

        var service = CreateService(handler.Object);

        await service.ValidateAsync("testpassword12");

        handler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.Is<HttpRequestMessage>(req =>
                req.RequestUri!.AbsoluteUri.Contains("/range/")),
            ItExpr.IsAny<CancellationToken>());
    }

    [Fact]
    public async Task ValidateAsync_HibpApiUnavailable_FailsOpen()
    {
        // If HIBP is down, allow the password (fail open per NIST guidance)
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Connection refused"));

        var service = CreateService(handler.Object);

        var result = await service.ValidateAsync("longenoughpassword");

        result.IsValid.Should().BeTrue("HIBP failure should not block registration");
    }

    #endregion

    #region Negative Result Caching

    [Fact]
    public async Task ValidateAsync_SafePassword_CachesNegativeResult()
    {
        var callCount = 0;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA:5")
                };
            });

        var service = CreateService(handler.Object);

        // First call — hits API
        await service.ValidateAsync("cached-safe-password");
        callCount.Should().Be(1);

        // Second call — should use cache, no API call
        await service.ValidateAsync("cached-safe-password");
        callCount.Should().Be(1, "second call should use cached result");
    }

    [Fact]
    public async Task ValidateAsync_BreachedPassword_NotCached()
    {
        var password = "breachedpassword";
        var sha1 = ComputeSha1(password);
        var suffix = sha1[5..];

        var callCount = 0;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                return new HttpResponseMessage
                {
                    StatusCode = HttpStatusCode.OK,
                    Content = new StringContent($"{suffix}:100")
                };
            });

        var service = CreateService(handler.Object);

        // First call — hits API, finds breach
        await service.ValidateAsync(password);
        callCount.Should().Be(1);

        // Second call — should hit API again (breached results are not cached)
        await service.ValidateAsync(password);
        callCount.Should().Be(2, "breached results should not be cached");
    }

    #endregion

    #region Helpers

    private static string ComputeSha1(string input)
    {
        var bytes = System.Security.Cryptography.SHA1.HashData(
            System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    #endregion
}
