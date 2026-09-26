// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using Sorcha.ServiceClients.Shared;

namespace Sorcha.McpServer.Infrastructure;

/// <summary>
/// Turns a failed <see cref="ServiceReadResult"/> into the status an agent should act on, the
/// sentence that says which failure it was, and whether it counts against the service's health.
/// </summary>
/// <remarks>
/// <para>
/// #1673. The Tenant-backed tools collapsed every non-success to null, so a 403 read as "Failed to
/// retrieve tenants" or "Could not list organisations", and — worse — several of them then called
/// <c>RecordFailure</c>. Three deterministic refusals in a row trip the availability tracker, and
/// every Tenant tool then reports "service unavailable" against a healthy service. The publish tool
/// learned the same lesson on the Blueprint side.
/// </para>
/// <para>
/// So only a 5xx is an outage. A 4xx is the service answering, and is recorded as such.
/// </para>
/// </remarks>
internal static class ServiceReadExplanation
{
    /// <summary>True when the failure is evidence the service is unhealthy (a 5xx).</summary>
    internal static bool IsOutage(ServiceReadResult read) => (int)read.Status >= 500;

    /// <summary>The tool status for a failed read.</summary>
    internal static string StatusFor(ServiceReadResult read) => read.Status switch
    {
        HttpStatusCode.Forbidden => "Refused",
        HttpStatusCode.NotFound => "NotFound",
        HttpStatusCode.Unauthorized => "Unauthorized",
        _ when (int)read.Status >= 500 => "Error",
        _ => "Refused",
    };

    /// <summary>
    /// A sentence for a failed read. <paramref name="attempted"/> completes "Could not …";
    /// <paramref name="requiredAuthority"/>, when given, says what a 403 means here.
    /// </summary>
    internal static string Explain(ServiceReadResult read, string attempted, string? requiredAuthority = null)
    {
        var said = string.IsNullOrWhiteSpace(read.Reason) ? string.Empty : $" The service said: {read.Reason.TrimEnd('.')}.";

        return read.Status switch
        {
            HttpStatusCode.Forbidden =>
                $"You are not permitted to {attempted}. This is an authorisation refusal, not a missing "
                + "resource or an outage." + (requiredAuthority is null ? string.Empty : $" {requiredAuthority}") + said,
            HttpStatusCode.NotFound => $"Could not {attempted}: it was not found.{said}",
            HttpStatusCode.Unauthorized =>
                "Your session is not authenticated. The access token has probably expired; reconnect and retry.",
            _ when (int)read.Status >= 500 =>
                $"Could not {attempted}: the service failed ({(int)read.Status}).{said} Try again shortly.",
            _ => $"Could not {attempted}: the service answered {(int)read.Status}.{said}",
        };
    }
}
