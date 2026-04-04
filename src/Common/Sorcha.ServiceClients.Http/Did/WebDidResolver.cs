// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Sorcha.ServiceClients.Did;

/// <summary>
/// Resolves did:web DIDs by fetching a DID Document over HTTPS.
///   - did:web:{domain}               → GET https://{domain}/.well-known/did.json
///   - did:web:{domain}:{path}:{...}  → GET https://{domain}/{path}/.../did.json
/// </summary>
public class WebDidResolver : IDidResolver
{
    private const string Method = "web";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _httpClient;
    private readonly ILogger<WebDidResolver> _logger;
    private readonly bool _allowPrivateAddresses;

    public WebDidResolver(HttpClient httpClient, ILogger<WebDidResolver> logger, IConfiguration? configuration = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _allowPrivateAddresses = configuration?.GetValue<bool>("DidResolver:AllowPrivateAddresses") ?? false;
    }

    /// <inheritdoc />
    public bool CanResolve(string didMethod) =>
        string.Equals(didMethod, Method, StringComparison.OrdinalIgnoreCase);

    /// <inheritdoc />
    public async Task<DidDocument?> ResolveAsync(string did, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(did))
            return null;

        var url = BuildUrl(did);
        if (url is null)
            return null;

        // SSRF protection: validate the resolved host IP is not private/reserved
        if (!_allowPrivateAddresses && !await IsHostAllowedAsync(url.Host))
        {
            _logger.LogWarning(
                "did:web resolution blocked by SSRF protection for {Did} (host: {Host})",
                did, url.Host);
            return null;
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(RequestTimeout);

        try
        {
            using var response = await _httpClient.GetAsync(url, cts.Token);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "did:web resolution failed for {Did}: HTTP {StatusCode}",
                    did, (int)response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cts.Token);
            var doc = JsonSerializer.Deserialize<DidDocument>(json, JsonOptions);

            if (doc is null)
            {
                _logger.LogWarning("did:web resolution returned null document for {Did}", did);
                return null;
            }

            // Verify the resolved document ID matches the DID we requested
            if (!string.Equals(doc.Id, did, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning(
                    "did:web document ID mismatch: expected {Expected}, got {Actual}",
                    did, doc.Id);
                return null;
            }

            return doc;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.LogWarning("did:web resolution timed out for {Did}", did);
            return null;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "did:web resolution network error for {Did}", did);
            return null;
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "did:web resolution returned invalid JSON for {Did}", did);
            return null;
        }
    }

    /// <summary>
    /// Resolves the hostname to IP addresses and verifies none are private or reserved.
    /// </summary>
    private async Task<bool> IsHostAllowedAsync(string host)
    {
        try
        {
            // Check if the host is already an IP address
            if (IPAddress.TryParse(host, out var directIp))
                return !IsPrivateOrReservedAddress(directIp);

            var addresses = await Dns.GetHostAddressesAsync(host);
            foreach (var address in addresses)
            {
                if (IsPrivateOrReservedAddress(address))
                    return false;
            }

            return addresses.Length > 0;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "DNS resolution failed for host {Host}", host);
            return false;
        }
    }

    /// <summary>
    /// Checks whether an IP address is private, loopback, link-local, or otherwise reserved.
    /// </summary>
    public static bool IsPrivateOrReservedAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address))
            return true;

        var bytes = address.GetAddressBytes();

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            // IPv4 checks
            return bytes[0] switch
            {
                0 => true,                                              // 0.0.0.0/8
                10 => true,                                             // 10.0.0.0/8
                127 => true,                                            // 127.0.0.0/8 (loopback)
                169 when bytes[1] == 254 => true,                       // 169.254.0.0/16 (link-local)
                172 when bytes[1] >= 16 && bytes[1] <= 31 => true,      // 172.16.0.0/12
                192 when bytes[1] == 168 => true,                       // 192.168.0.0/16
                _ => false
            };
        }

        if (address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
        {
            // IPv6 link-local (fe80::/10)
            if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0x80)
                return true;

            // IPv6 site-local (fec0::/10) — deprecated but still reserved
            if (bytes[0] == 0xfe && (bytes[1] & 0xc0) == 0xc0)
                return true;

            // IPv6 unique local (fc00::/7)
            if ((bytes[0] & 0xfe) == 0xfc)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Builds the HTTPS URL for the DID Document.
    /// Colons after the domain are converted to path separators.
    /// Percent-encoded domain segments are decoded.
    /// </summary>
    private Uri? BuildUrl(string did)
    {
        // did:web:{domain}  or  did:web:{domain}:{path1}:{path2}
        var parts = did.Split(':');
        if (parts.Length < 3)
        {
            _logger.LogWarning("Invalid did:web format: {Did}", did);
            return null;
        }

        // Decode percent-encoded domain (e.g., example%2Ecom → example.com)
        var domain = Uri.UnescapeDataString(parts[2]);

        if (string.IsNullOrWhiteSpace(domain))
        {
            _logger.LogWarning("did:web has empty domain: {Did}", did);
            return null;
        }

        // Build path from remaining segments
        string path;
        if (parts.Length > 3)
        {
            var pathSegments = parts[3..].Select(Uri.UnescapeDataString);
            path = $"https://{domain}/{string.Join('/', pathSegments)}/did.json";
        }
        else
        {
            path = $"https://{domain}/.well-known/did.json";
        }

        if (!Uri.TryCreate(path, UriKind.Absolute, out var uri))
        {
            _logger.LogWarning("Could not construct valid URL from did:web {Did}", did);
            return null;
        }

        // HTTPS only
        if (uri.Scheme != Uri.UriSchemeHttps)
        {
            _logger.LogWarning("did:web requires HTTPS, got {Scheme} for {Did}", uri.Scheme, did);
            return null;
        }

        return uri;
    }
}
