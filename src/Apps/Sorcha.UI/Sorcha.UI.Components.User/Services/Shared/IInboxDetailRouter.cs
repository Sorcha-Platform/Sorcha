// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.RegularExpressions;

namespace Sorcha.UI.Components.User.Services.Shared;

/// <summary>
/// Translates an inbox entry's <c>detailHref</c> into an in-app route, or refuses it.
/// </summary>
/// <remarks>
/// <para>
/// Issue #1266: tapping a decision notice in the PWA <b>left the app</b>, opened an external browser
/// at <c>about:blank</c> and produced a zero-byte download named after the instance id. The stored
/// href is a raw API route (<c>/api/instances/{id}</c>); the client handed it straight to the browser,
/// which sent no bearer token, got a 401 with nothing renderable, and treated the response as a file.
/// </para>
/// <para>
/// F118's thin-signal contract is <i>correct</i> in naming the authenticated REST detail endpoint —
/// that is how a service consumer resolves detail. The bug is on the client side: a UI must translate
/// that reference into one of its own views and must never navigate a browser to it.
/// </para>
/// <para>
/// This is a per-host seam because the hosts genuinely differ: the PWA has
/// <c>/applications/{instanceId}</c>, the web app has no per-instance page at all (only the
/// <c>/my-workflows</c> list) until the "My Applications" view in #1163 exists. A shared rewrite
/// would have to invent a route for one of them.
/// </para>
/// </remarks>
public interface IInboxDetailRouter
{
    /// <summary>
    /// The in-app route to navigate to for <paramref name="detailHref"/>, or <see langword="null"/>
    /// when this host has no view for it — in which case the caller must render the row as
    /// non-navigable rather than navigate somewhere wrong.
    /// </summary>
    string? Resolve(string? detailHref);
}

/// <summary>
/// Host-agnostic default: passes through app-relative routes and refuses anything under
/// <c>/api/</c>.
/// </summary>
/// <remarks>
/// Refusing is deliberately the fallback. Navigating to an API URL is what produced the
/// <c>about:blank</c> download in #1266, and navigating to an unrelated page instead would be a
/// different kind of wrong — a citizen told "Confirm your email and reapply" and then dropped
/// somewhere arbitrary is worse served than one given no link at all.
/// </remarks>
public class DefaultInboxDetailRouter : IInboxDetailRouter
{
    /// <inheritdoc />
    public virtual string? Resolve(string? detailHref)
    {
        if (string.IsNullOrWhiteSpace(detailHref)) return null;

        var href = detailHref.Trim();

        // An absolute URL is never an in-app route. Refuse rather than leave the app.
        if (href.Contains("://", StringComparison.Ordinal)) return null;

        // Raw API routes are service-consumer references, not destinations for a browser.
        if (href.StartsWith("/api/", StringComparison.OrdinalIgnoreCase)
            || href.StartsWith("api/", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return href;
    }

    /// <summary>
    /// Extracts the instance id from an <c>/api/instances/{id}</c> href, or null.
    /// Shared so a host mapping it to its own page does not re-parse by hand.
    /// </summary>
    protected static string? TryReadInstanceId(string href)
    {
        var match = InstanceHref.Match(href);
        return match.Success ? match.Groups["id"].Value : null;
    }

    private static readonly Regex InstanceHref = new(
        @"^/?api/instances/(?<id>[0-9a-fA-F-]{36})/?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);
}
