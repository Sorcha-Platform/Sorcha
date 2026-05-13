// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.UI.Core.Utilities;

/// <summary>
/// Validates URLs for security, preventing open redirect attacks.
/// </summary>
public static class UrlValidator
{
    /// <summary>
    /// Validates that a return URL is safe to redirect to.
    /// A URL is considered safe if it is a relative path starting with a single slash,
    /// or an absolute URL with the same origin (scheme, host, and port) as the base URI.
    /// </summary>
    /// <param name="url">The URL to validate.</param>
    /// <param name="baseUri">The application's base URI for same-origin comparison.</param>
    /// <returns><c>true</c> if the URL is safe to redirect to; otherwise, <c>false</c>.</returns>
    /// <remarks>
    /// This method rejects:
    /// <list type="bullet">
    /// <item><description>Null, empty, or whitespace URLs</description></item>
    /// <item><description>Protocol-relative URLs (starting with //)</description></item>
    /// <item><description>URLs with dangerous schemes (javascript:, data:)</description></item>
    /// <item><description>Absolute URLs pointing to different origins</description></item>
    /// </list>
    /// </remarks>
    public static bool IsValidReturnUrl(string? url, Uri baseUri)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        // Relative paths starting with single / are safe
        // Reject protocol-relative URLs (//evil.com)
        if (url.StartsWith('/') && !url.StartsWith("//"))
            return true;

        // Check for dangerous schemes
        if (url.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase) ||
            url.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            return false;

        // Absolute URLs must be same-origin
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return string.Equals(uri.Scheme, baseUri.Scheme, StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(uri.Host, baseUri.Host, StringComparison.OrdinalIgnoreCase) &&
                   uri.Port == baseUri.Port;
        }

        return false;
    }

    /// <summary>
    /// Validates that a return URL is safe to redirect to.
    /// </summary>
    /// <param name="url">The URL to validate.</param>
    /// <param name="baseUri">The application's base URI as a string.</param>
    /// <returns><c>true</c> if the URL is safe to redirect to; otherwise, <c>false</c>.</returns>
    public static bool IsValidReturnUrl(string? url, string baseUri)
    {
        if (!Uri.TryCreate(baseUri, UriKind.Absolute, out var uri))
            return false;

        return IsValidReturnUrl(url, uri);
    }
}
