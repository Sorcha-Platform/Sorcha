// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.UI.Components.User.Services.Shared;

namespace Sorcha.UI.Web.Client.Services;

/// <summary>
/// Feature 186 (#1163) — web-host inbox detail routing. Maps the API instance reference an inbox
/// entry carries onto the "My Applications" detail view, and inherits the base router's refusal of
/// everything else it cannot render in-app.
/// </summary>
/// <remarks>
/// <para>
/// Until this existed the web host registered nothing and fell through to
/// <see cref="DefaultInboxDetailRouter"/>, which refuses <c>/api/*</c> hrefs outright — correctly,
/// since navigating a browser at an API URL is what produced the <c>about:blank</c> zero-byte
/// download in #1266. The consequence was that a decision notice, the very thing Feature 184 exists
/// to deliver, rendered as a dead row on the web: the citizen was told a decision had been made and
/// given nowhere to go.
/// </para>
/// <para>
/// Base-relative, matching the PWA's router: the web client is mounted under <c>/app</c> by the
/// gateway, so a rooted path escapes the app's base.
/// </para>
/// </remarks>
public sealed class WebInboxDetailRouter : DefaultInboxDetailRouter
{
    /// <inheritdoc />
    public override string? Resolve(string? detailHref)
    {
        if (string.IsNullOrWhiteSpace(detailHref)) return null;

        var instanceId = TryReadInstanceId(detailHref.Trim());
        if (instanceId is not null)
        {
            return $"my-applications/{instanceId}";
        }

        return base.Resolve(detailHref);
    }
}
