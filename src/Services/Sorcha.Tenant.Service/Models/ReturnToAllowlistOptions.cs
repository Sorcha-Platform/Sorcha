// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Configuration shape for the trusted-return-to-URL allowlist used by
/// Feature 116 signup endpoints when honouring <c>?returnTo=</c>.
/// Feature 126 — research §R-004.
/// </summary>
/// <remarks>
/// Matcher rules:
/// <list type="bullet">
///   <item><description>Scheme MUST be <c>https</c>, except <c>http://localhost</c> for dev.</description></item>
///   <item><description>Host MUST be an exact match in <see cref="Hosts"/>, OR match a wildcard entry
///   prefixed with <c>*.</c> as a suffix match (<c>api.strathcarron.gov</c> matches <c>*.strathcarron.gov</c>).</description></item>
///   <item><description>Open-redirects fail closed — any malformed URL or unmatched host returns <c>false</c>.</description></item>
/// </list>
/// </remarks>
public sealed class ReturnToAllowlistOptions
{
    /// <summary>Configuration section key.</summary>
    public const string SectionName = "Auth:ReturnToAllowlist";

    /// <summary>
    /// The allowlisted hosts. Exact-host entries match the URI's <c>Host</c>
    /// case-insensitively; entries prefixed with <c>*.</c> match any host
    /// ending with the suffix (e.g. <c>*.strathcarron.gov</c> matches
    /// <c>api.strathcarron.gov</c> but NOT <c>strathcarron.gov</c> itself).
    /// </summary>
    public IReadOnlyList<string> Hosts { get; init; } = Array.Empty<string>();

    /// <summary>
    /// Returns <c>true</c> if <paramref name="returnTo"/> is a well-formed,
    /// HTTPS-only (or <c>http://localhost</c>) absolute URL whose host matches
    /// an entry in <see cref="Hosts"/>. Returns <c>false</c> for any malformed
    /// input or unmatched host — fail closed.
    /// </summary>
    public bool IsAllowed(string? returnTo)
    {
        if (string.IsNullOrWhiteSpace(returnTo))
        {
            return false;
        }

        if (!Uri.TryCreate(returnTo, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var isHttps = string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase);
        var isLocalhostHttp = string.Equals(uri.Scheme, "http", StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase);

        if (!isHttps && !isLocalhostHttp)
        {
            return false;
        }

        foreach (var entry in Hosts)
        {
            if (string.IsNullOrWhiteSpace(entry))
            {
                continue;
            }

            if (entry.StartsWith("*.", StringComparison.Ordinal))
            {
                var suffix = entry.Substring(1); // ".strathcarron.gov"
                if (uri.Host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            else if (string.Equals(uri.Host, entry, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
