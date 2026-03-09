// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;

using Microsoft.Extensions.Logging;

namespace Sorcha.Tenant.Service.Services;

/// <summary>
/// DNS resolver that verifies CNAME records using system DNS resolution.
/// </summary>
public class DnsResolver : IDnsResolver
{
    private readonly ILogger<DnsResolver> _logger;

    public DnsResolver(ILogger<DnsResolver> logger)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<bool> VerifyCnameAsync(string domain, string expectedCnameTarget, CancellationToken cancellationToken = default)
    {
        try
        {
            var hostEntry = await Dns.GetHostEntryAsync(domain, cancellationToken);

            // Check if the domain resolves to the same IP addresses as the expected target
            var targetEntry = await Dns.GetHostEntryAsync(expectedCnameTarget, cancellationToken);

            // Compare resolved addresses — if the domain resolves to the same IPs as the target, the CNAME is valid
            var domainAddresses = hostEntry.AddressList.Select(a => a.ToString()).ToHashSet();
            var targetAddresses = targetEntry.AddressList.Select(a => a.ToString()).ToHashSet();

            var match = domainAddresses.Overlaps(targetAddresses);

            _logger.LogDebug(
                "DNS verification for {Domain}: resolved={DomainAddresses}, target={TargetAddresses}, match={Match}",
                domain,
                string.Join(", ", domainAddresses),
                string.Join(", ", targetAddresses),
                match);

            return match;
        }
        catch (Exception ex) when (ex is System.Net.Sockets.SocketException or ArgumentException)
        {
            _logger.LogDebug(ex, "DNS verification failed for {Domain}: {Message}", domain, ex.Message);
            return false;
        }
    }
}
