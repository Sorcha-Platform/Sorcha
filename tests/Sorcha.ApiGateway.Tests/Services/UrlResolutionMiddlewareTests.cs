// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

using Moq;
using Moq.Protected;

using Sorcha.ApiGateway.Services;

namespace Sorcha.ApiGateway.Tests.Services;

public class UrlResolutionMiddlewareTests
{
    private readonly IMemoryCache _cache;
    private readonly Mock<IHttpClientFactory> _httpClientFactoryMock;
    private readonly IConfiguration _configuration;
    private readonly UrlResolutionMiddleware _middleware;

    public UrlResolutionMiddlewareTests()
    {
        _cache = new MemoryCache(new MemoryCacheOptions());
        _httpClientFactoryMock = new Mock<IHttpClientFactory>();
        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["UrlResolution:BaseDomain"] = "sorcha.io"
            })
            .Build();

        _middleware = new UrlResolutionMiddleware(
            _ => Task.CompletedTask,
            _cache,
            _httpClientFactoryMock.Object,
            Mock.Of<ILogger<UrlResolutionMiddleware>>(),
            _configuration);
    }

    // ── Tier 1: Path-based resolution ──────────────────────

    [Fact]
    public void ResolvePath_OrgSubdomainPath_ExtractsSubdomain()
    {
        var context = CreateContext(path: "/org/acme/api/users");

        var result = _middleware.ResolvePath(context);

        result.Should().Be("acme");
        context.Request.Path.Value.Should().Be("/api/users");
    }

    [Fact]
    public void ResolvePath_OrgSubdomainWithoutTrailingPath_ExtractsSubdomain()
    {
        var context = CreateContext(path: "/org/acme");

        var result = _middleware.ResolvePath(context);

        result.Should().Be("acme");
        context.Request.Path.Value.Should().Be("/");
    }

    [Fact]
    public void ResolvePath_NonOrgPath_ReturnsNull()
    {
        var context = CreateContext(path: "/api/organizations/123");

        var result = _middleware.ResolvePath(context);

        result.Should().BeNull();
    }

    [Fact]
    public void ResolvePath_EmptySubdomain_ReturnsNull()
    {
        var context = CreateContext(path: "/org/");

        var result = _middleware.ResolvePath(context);

        result.Should().BeNull();
    }

    [Fact]
    public void ResolvePath_NormalizesToLowercase()
    {
        var context = CreateContext(path: "/org/ACME/dashboard");

        var result = _middleware.ResolvePath(context);

        result.Should().Be("acme");
    }

    // ── Tier 2: Subdomain-based resolution ─────────────────

    [Fact]
    public void ResolveSubdomain_SubdomainHost_ExtractsSubdomain()
    {
        var context = CreateContext(host: "acme.sorcha.io");

        var result = _middleware.ResolveSubdomain(context);

        result.Should().Be("acme");
    }

    [Fact]
    public void ResolveSubdomain_WwwSubdomain_ReturnsNull()
    {
        var context = CreateContext(host: "www.sorcha.io");

        var result = _middleware.ResolveSubdomain(context);

        result.Should().BeNull();
    }

    [Fact]
    public void ResolveSubdomain_BaseDomainOnly_ReturnsNull()
    {
        var context = CreateContext(host: "sorcha.io");

        var result = _middleware.ResolveSubdomain(context);

        result.Should().BeNull();
    }

    [Fact]
    public void ResolveSubdomain_NonSorchaHost_ReturnsNull()
    {
        var context = CreateContext(host: "acme.example.com");

        var result = _middleware.ResolveSubdomain(context);

        result.Should().BeNull();
    }

    [Fact]
    public void ResolveSubdomain_NormalizesToLowercase()
    {
        var context = CreateContext(host: "ACME.sorcha.io");

        var result = _middleware.ResolveSubdomain(context);

        result.Should().Be("acme");
    }

    // ── Tier 3: Custom domain resolution ───────────────────

    [Fact]
    public async Task ResolveCustomDomainAsync_KnownDomain_ReturnsSubdomain()
    {
        var mockHandler = CreateMockHandler(HttpStatusCode.OK,
            new { subdomain = "acme" });
        SetupHttpClient(mockHandler);

        var context = CreateContext(host: "login.acme.com");

        var result = await _middleware.ResolveCustomDomainAsync(context);

        result.Should().Be("acme");
    }

    [Fact]
    public async Task ResolveCustomDomainAsync_UnknownDomain_ReturnsNull()
    {
        var mockHandler = CreateMockHandler(HttpStatusCode.NotFound, null);
        SetupHttpClient(mockHandler);

        var context = CreateContext(host: "unknown.example.com");

        var result = await _middleware.ResolveCustomDomainAsync(context);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveCustomDomainAsync_CachesResult()
    {
        var mockHandler = CreateMockHandler(HttpStatusCode.OK,
            new { subdomain = "cached" });
        SetupHttpClient(mockHandler);

        var context = CreateContext(host: "cached.example.com");

        // First call — hits service
        var result1 = await _middleware.ResolveCustomDomainAsync(context);
        result1.Should().Be("cached");

        // Second call — should use cache, so resetting mock should not matter
        _httpClientFactoryMock.Reset();
        var result2 = await _middleware.ResolveCustomDomainAsync(context);
        result2.Should().Be("cached");
    }

    [Fact]
    public async Task ResolveCustomDomainAsync_Localhost_ReturnsNull()
    {
        var context = CreateContext(host: "localhost");

        var result = await _middleware.ResolveCustomDomainAsync(context);

        result.Should().BeNull();
    }

    [Fact]
    public async Task ResolveCustomDomainAsync_IpAddress_ReturnsNull()
    {
        var context = CreateContext(host: "192.168.1.1");

        var result = await _middleware.ResolveCustomDomainAsync(context);

        result.Should().BeNull();
    }

    // ── Full middleware pipeline ────────────────────────────

    [Fact]
    public async Task InvokeAsync_PathBased_SetsXOrgSubdomainHeader()
    {
        string? capturedHeader = null;
        var middleware = new UrlResolutionMiddleware(
            ctx =>
            {
                capturedHeader = ctx.Request.Headers["X-Org-Subdomain"].FirstOrDefault();
                return Task.CompletedTask;
            },
            _cache,
            _httpClientFactoryMock.Object,
            Mock.Of<ILogger<UrlResolutionMiddleware>>(),
            _configuration);

        var context = CreateContext(path: "/org/acme/api/users");

        await middleware.InvokeAsync(context);

        capturedHeader.Should().Be("acme");
    }

    [Fact]
    public async Task InvokeAsync_SubdomainBased_SetsXOrgSubdomainHeader()
    {
        string? capturedHeader = null;
        var middleware = new UrlResolutionMiddleware(
            ctx =>
            {
                capturedHeader = ctx.Request.Headers["X-Org-Subdomain"].FirstOrDefault();
                return Task.CompletedTask;
            },
            _cache,
            _httpClientFactoryMock.Object,
            Mock.Of<ILogger<UrlResolutionMiddleware>>(),
            _configuration);

        var context = CreateContext(host: "acme.sorcha.io", path: "/api/users");

        await middleware.InvokeAsync(context);

        capturedHeader.Should().Be("acme");
    }

    [Fact]
    public async Task InvokeAsync_NoOrgResolved_NoHeaderSet()
    {
        string? capturedHeader = null;
        var middleware = new UrlResolutionMiddleware(
            ctx =>
            {
                capturedHeader = ctx.Request.Headers["X-Org-Subdomain"].FirstOrDefault();
                return Task.CompletedTask;
            },
            _cache,
            _httpClientFactoryMock.Object,
            Mock.Of<ILogger<UrlResolutionMiddleware>>(),
            _configuration);

        var context = CreateContext(host: "localhost", path: "/api/users");

        await middleware.InvokeAsync(context);

        capturedHeader.Should().BeNull();
    }

    // ── Helpers ─────────────────────────────────────────────

    private static DefaultHttpContext CreateContext(string? host = null, string? path = null)
    {
        var context = new DefaultHttpContext();
        if (host is not null)
            context.Request.Host = new HostString(host);
        if (path is not null)
            context.Request.Path = new PathString(path);
        return context;
    }

    private static Mock<HttpMessageHandler> CreateMockHandler(HttpStatusCode statusCode, object? content)
    {
        var mock = new Mock<HttpMessageHandler>();
        var response = new HttpResponseMessage(statusCode);
        if (content is not null)
        {
            response.Content = new StringContent(
                JsonSerializer.Serialize(content, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        }

        mock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        return mock;
    }

    private void SetupHttpClient(Mock<HttpMessageHandler> handler)
    {
        var client = new HttpClient(handler.Object)
        {
            BaseAddress = new Uri("http://tenant-service")
        };
        _httpClientFactoryMock.Setup(f => f.CreateClient("TenantService")).Returns(client);
    }
}
