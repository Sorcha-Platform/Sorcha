// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Centralised rate limiting configuration bound from the "RateLimiting" section of appsettings.json.
/// Default values are deliberately very relaxed for pre-release/development use.
/// Tighten these in production appsettings before going live.
/// </summary>
public sealed class RateLimitSettings
{
    public const string SectionName = "RateLimiting";

    // ── Default API policy (fixed window, per IP) ──────────────────────────
    /// <summary>Requests per minute for the default API policy.</summary>
    public int ApiPermitLimit { get; set; } = 100_000;

    /// <summary>Queue depth for the default API policy.</summary>
    public int ApiQueueLimit { get; set; } = 1_000;

    // ── Authentication policy (sliding window, per IP) ─────────────────────
    /// <summary>Requests per minute for authentication endpoints.</summary>
    public int AuthenticationPermitLimit { get; set; } = 100_000;

    /// <summary>Queue depth for authentication endpoints.</summary>
    public int AuthenticationQueueLimit { get; set; } = 1_000;

    // ── Strict policy (token bucket, per IP) ───────────────────────────────
    /// <summary>Token bucket capacity for strict-policy endpoints (e.g. wallet operations).</summary>
    public int StrictTokenLimit { get; set; } = 100_000;

    /// <summary>Tokens added per replenishment period.</summary>
    public int StrictTokensPerPeriod { get; set; } = 10_000;

    /// <summary>Replenishment period in seconds.</summary>
    public int StrictReplenishmentPeriodSeconds { get; set; } = 1;

    /// <summary>Queue depth for strict-policy endpoints.</summary>
    public int StrictQueueLimit { get; set; } = 1_000;

    // ── Heavy operations policy (concurrency limiter, global) ──────────────
    /// <summary>Maximum concurrent requests globally for heavy operations.</summary>
    public int HeavyPermitLimit { get; set; } = 10_000;

    /// <summary>Queue depth for heavy operations.</summary>
    public int HeavyQueueLimit { get; set; } = 10_000;

    // ── Relaxed policy (fixed window, per IP — health checks, metrics) ─────
    /// <summary>Requests per minute for relaxed endpoints.</summary>
    public int RelaxedPermitLimit { get; set; } = 100_000;

    /// <summary>Queue depth for relaxed endpoints.</summary>
    public int RelaxedQueueLimit { get; set; } = 10_000;

    // ── TOTP validation policy (fixed window, per IP) ──────────────────────
    /// <summary>Requests per minute for TOTP/2FA validation endpoints.</summary>
    public int TotpPermitLimit { get; set; } = 100_000;

    /// <summary>Queue depth for TOTP validation endpoints.</summary>
    public int TotpQueueLimit { get; set; } = 1_000;

    // ── Platform auth policy (fixed window, per IP) ────────────────────────
    /// <summary>Requests per minute for platform auth endpoints (social login, registration, passkeys).</summary>
    public int PlatformAuthPermitLimit { get; set; } = 100_000;

    /// <summary>Queue depth for platform auth endpoints.</summary>
    public int PlatformAuthQueueLimit { get; set; } = 1_000;

    // ── MCP Server policies ────────────────────────────────────────────────
    /// <summary>Maximum requests per minute per MCP user.</summary>
    public int McpPerUserRequestsPerMinute { get; set; } = 100_000;

    /// <summary>Maximum requests per minute per MCP tenant.</summary>
    public int McpPerTenantRequestsPerMinute { get; set; } = 100_000;

    /// <summary>Maximum admin tool requests per minute per MCP user.</summary>
    public int McpAdminToolsRequestsPerMinute { get; set; } = 100_000;

    // ── Notification rate limiting (wallet service, Redis-backed) ──────────
    /// <summary>Maximum real-time notifications per minute per user.</summary>
    public int NotificationRealTimePerMinute { get; set; } = 100_000;
}
