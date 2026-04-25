// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Models;

/// <summary>
/// Configuration flag for shared demo deployments (e.g. n1.sorcha.dev).
/// When enabled, public auth pages render an informational banner warning
/// new sign-ups that the environment is a technology demonstrator and may
/// be reset without notice. Default off — production deployments stay clean.
///
/// Bound from the "DemoEnvironment" config section. Override per environment
/// via env vars (DemoEnvironment__Enabled, DemoEnvironment__Message) — the
/// expected place to flip this on is docker-compose.&lt;target&gt;.yml.
/// </summary>
public class DemoEnvironmentSettings
{
    /// <summary>Whether the demo banner is displayed on auth pages.</summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Banner copy shown to visitors. Plain text — rendered HTML-encoded by
    /// the page so the value cannot inject markup. A sensible default is
    /// supplied so the banner is informative even if the deployment leaves
    /// Message unset.
    /// </summary>
    public string Message { get; set; } =
        "This is a public technology demonstrator. Accounts and data may be reset at any time without notice. Please don't sign up with personal data you wouldn't be comfortable losing.";
}
