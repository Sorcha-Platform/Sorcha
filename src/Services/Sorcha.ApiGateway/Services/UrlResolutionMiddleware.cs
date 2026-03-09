// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Caching.Memory;

namespace Sorcha.ApiGateway.Services;

/// <summary>
/// Middleware that resolves organization subdomain from incoming requests using 3-tier resolution:
/// 1. Path-based: /org/{subdomain}/... → extracts subdomain from URL path
/// 2. Subdomain-based: {sub}.sorcha.io → extracts subdomain from Host header
/// 3. Custom domain: login.acme.com → looks up mapping via Tenant Service (cached 5min)
///
/// Sets the X-Org-Subdomain header on the request for downstream services.
/// </summary>
public class UrlResolutionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly IMemoryCache _cache;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<UrlResolutionMiddleware> _logger;
    private readonly string _baseDomain;
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

    public UrlResolutionMiddleware(
        RequestDelegate next,
        IMemoryCache cache,
        IHttpClientFactory httpClientFactory,
        ILogger<UrlResolutionMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _cache = cache;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _baseDomain = configuration.GetValue<string>("UrlResolution:BaseDomain") ?? "sorcha.io";
    }

    public async Task InvokeAsync(HttpContext context)
    {
        // Remove any incoming X-Org-Subdomain header to prevent spoofing
        context.Request.Headers.Remove("X-Org-Subdomain");

        var subdomain = ResolvePath(context)
                     ?? ResolveSubdomain(context)
                     ?? await ResolveCustomDomainAsync(context);

        if (subdomain is not null)
        {
            context.Request.Headers["X-Org-Subdomain"] = subdomain;
            _logger.LogDebug("Resolved org subdomain: {Subdomain} from {Source}", subdomain,
                context.Request.Path.StartsWithSegments("/org") ? "path" :
                context.Request.Host.Host.EndsWith(_baseDomain) ? "subdomain" : "custom-domain");
        }

        await _next(context);
    }

    /// <summary>
    /// Tier 1: Path-based resolution — /org/{subdomain}/...
    /// Rewrites the path to remove the /org/{subdomain} prefix.
    /// </summary>
    internal string? ResolvePath(HttpContext context)
    {
        var path = context.Request.Path.Value;
        if (path is null || !path.StartsWith("/org/", StringComparison.OrdinalIgnoreCase))
            return null;

        // Extract subdomain from /org/{subdomain}/...
        var remaining = path[5..]; // skip "/org/"
        var slashIndex = remaining.IndexOf('/');

        string subdomain;
        if (slashIndex >= 0)
        {
            subdomain = remaining[..slashIndex];
            context.Request.Path = new PathString(remaining[slashIndex..]);
        }
        else
        {
            subdomain = remaining;
            context.Request.Path = new PathString("/");
        }

        return string.IsNullOrEmpty(subdomain) ? null : subdomain.ToLowerInvariant();
    }

    /// <summary>
    /// Tier 2: Subdomain-based resolution — {sub}.sorcha.io
    /// </summary>
    internal string? ResolveSubdomain(HttpContext context)
    {
        var host = context.Request.Host.Host;

        if (!host.EndsWith($".{_baseDomain}", StringComparison.OrdinalIgnoreCase))
            return null;

        var subdomain = host[..^(_baseDomain.Length + 1)]; // strip ".sorcha.io"

        // Ignore www or empty subdomain
        if (string.IsNullOrEmpty(subdomain) || subdomain.Equals("www", StringComparison.OrdinalIgnoreCase))
            return null;

        return subdomain.ToLowerInvariant();
    }

    /// <summary>
    /// Tier 3: Custom domain resolution — lookup via Tenant Service with 5min cache.
    /// </summary>
    internal async Task<string?> ResolveCustomDomainAsync(HttpContext context)
    {
        var host = context.Request.Host.Host;

        // Skip localhost, IP addresses, and the base domain itself
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.Equals(_baseDomain, StringComparison.OrdinalIgnoreCase)
            || System.Net.IPAddress.TryParse(host, out _))
        {
            return null;
        }

        var cacheKey = $"domain-resolution:{host}";

        if (_cache.TryGetValue(cacheKey, out string? cachedSubdomain))
        {
            return cachedSubdomain;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("TenantService");
            var response = await client.GetAsync($"/api/internal/resolve-domain/{Uri.EscapeDataString(host)}");

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<DomainResolutionResult>();
                if (result?.Subdomain is not null)
                {
                    _cache.Set(cacheKey, result.Subdomain, CacheDuration);
                    return result.Subdomain;
                }
            }

            // Cache negative results too (domain not found) to avoid repeated lookups
            _cache.Set(cacheKey, (string?)null, CacheDuration);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to resolve custom domain {Host} via Tenant Service", host);
        }

        return null;
    }

    internal record DomainResolutionResult(string? Subdomain);
}
