// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Http;

namespace Sorcha.ServiceDefaults.Helpers;

/// <summary>
/// Shared helper for extracting client IP addresses from HTTP contexts.
/// Handles X-Forwarded-For headers for requests behind proxies/load balancers.
/// </summary>
public static class ClientIpHelper
{
    /// <summary>
    /// Gets the client IP address from the HTTP context.
    /// Checks X-Forwarded-For header first (for proxied requests), then falls back to RemoteIpAddress.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>The client IP address string, or "unknown" if it cannot be determined.</returns>
    public static string GetClientIp(HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            var clientIp = forwardedFor.Split(',').FirstOrDefault()?.Trim();
            if (!string.IsNullOrEmpty(clientIp))
            {
                return clientIp;
            }
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }
}
