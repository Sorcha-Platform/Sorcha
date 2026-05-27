// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.ApiGateway.Services;

/// <summary>
/// Service for aggregating statistics from backend services for the dashboard
/// </summary>
public class DashboardStatisticsService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<DashboardStatisticsService> _logger;
    private readonly IConfiguration _configuration;

    public DashboardStatisticsService(
        IHttpClientFactory httpClientFactory,
        ILogger<DashboardStatisticsService> logger,
        IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
        _configuration = configuration;
    }

    /// <summary>
    /// Gets aggregated platform-wide dashboard statistics by fanning out to every backend
    /// <c>/api/stats</c>. Used by the SystemAdmin-only platform view of <c>/api/dashboard</c>.
    /// </summary>
    public async Task<DashboardStatistics> GetDashboardStatisticsAsync(CancellationToken cancellationToken = default)
    {
        var stats = new DashboardStatistics
        {
            Scope = "platform",
            OrgId = null,
            Timestamp = DateTimeOffset.UtcNow
        };

        // Run all queries in parallel
        await Task.WhenAll(
            GetBlueprintStatisticsAsync(stats, cancellationToken),
            GetWalletStatisticsAsync(stats, cancellationToken),
            GetRegisterStatisticsAsync(stats, cancellationToken),
            GetTenantStatisticsAsync(stats, cancellationToken),
            GetPeerStatisticsAsync(stats, cancellationToken)
        );

        return stats;
    }

    /// <summary>
    /// Feature 131 / UX-005 — fetches the compact org-scoped summary from Tenant Service.
    /// Forwards the caller's bearer token so Tenant's <c>RequireAuthenticated</c> policy is
    /// satisfied.
    /// </summary>
    /// <param name="orgId">Organisation id whose summary is requested.</param>
    /// <param name="bearerToken">Caller's JWT (without the "Bearer " prefix).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Stats wrapped with <c>Scope = "org"</c>; on failure, returns a zeroed shape.</returns>
    public async Task<DashboardStatistics> GetOrgSummaryAsync(
        Guid orgId,
        string? bearerToken,
        CancellationToken cancellationToken = default)
    {
        var stats = new DashboardStatistics
        {
            Scope = "org",
            OrgId = orgId,
            Timestamp = DateTimeOffset.UtcNow,
            // Zero defaults; overwritten on success below.
            ActiveUsers = 0,
            PendingInvitations = 0,
            SubscribedRegisters = 0,
            RecentTransactions = 0
        };

        try
        {
            var client = _httpClientFactory.CreateClient("TenantService");
            client.Timeout = TimeSpan.FromSeconds(5);
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"/api/organizations/{orgId}/dashboard-summary");
            if (!string.IsNullOrEmpty(bearerToken))
            {
                request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", bearerToken);
            }

            var response = await client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Tenant dashboard-summary returned {StatusCode} for org {OrgId}",
                    response.StatusCode, orgId);
                return stats;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);
            var root = doc.RootElement;
            if (root.TryGetProperty("activeUsers", out var au)) stats.ActiveUsers = au.GetInt32();
            if (root.TryGetProperty("pendingInvitations", out var pi)) stats.PendingInvitations = pi.GetInt32();
            if (root.TryGetProperty("subscribedRegisters", out var sr)) stats.SubscribedRegisters = sr.GetInt32();
            if (root.TryGetProperty("recentTransactions", out var rt)) stats.RecentTransactions = rt.GetInt32();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch org-summary for {OrgId}", orgId);
        }

        return stats;
    }

    private async Task GetBlueprintStatisticsAsync(DashboardStatistics stats, CancellationToken cancellationToken)
    {
        try
        {
            var baseUrl = _configuration["Services:Blueprint:Url"] ?? "http://blueprint-service:8080";
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var response = await client.GetAsync($"{baseUrl}/api/stats", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(content);

                if (doc.RootElement.TryGetProperty("blueprintCount", out var blueprintCount))
                {
                    stats.TotalBlueprints = blueprintCount.GetInt32();
                }

                if (doc.RootElement.TryGetProperty("instanceCount", out var instanceCount))
                {
                    stats.TotalBlueprintInstances = instanceCount.GetInt32();
                }

                if (doc.RootElement.TryGetProperty("activeInstanceCount", out var activeCount))
                {
                    stats.ActiveBlueprintInstances = activeCount.GetInt32();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get blueprint statistics");
        }
    }

    private async Task GetWalletStatisticsAsync(DashboardStatistics stats, CancellationToken cancellationToken)
    {
        try
        {
            var baseUrl = _configuration["Services:Wallet:Url"] ?? "http://wallet-service:8080";
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var response = await client.GetAsync($"{baseUrl}/api/stats", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(content);

                if (doc.RootElement.TryGetProperty("walletCount", out var walletCount))
                {
                    stats.TotalWallets = walletCount.GetInt32();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get wallet statistics");
        }
    }

    private async Task GetRegisterStatisticsAsync(DashboardStatistics stats, CancellationToken cancellationToken)
    {
        try
        {
            var baseUrl = _configuration["Services:Register:Url"] ?? "http://register-service:8080";
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            var response = await client.GetAsync($"{baseUrl}/api/stats", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(content);

                if (doc.RootElement.TryGetProperty("registerCount", out var registerCount))
                {
                    stats.TotalRegisters = registerCount.GetInt32();
                }

                if (doc.RootElement.TryGetProperty("transactionCount", out var transactionCount))
                {
                    stats.TotalTransactions = transactionCount.GetInt32();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get register statistics");
        }
    }

    private async Task GetTenantStatisticsAsync(DashboardStatistics stats, CancellationToken cancellationToken)
    {
        try
        {
            var baseUrl = _configuration["Services:Tenant:Url"] ?? "http://tenant-service:8080";
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            // Query organization stats endpoint (public, no auth required)
            var response = await client.GetAsync($"{baseUrl}/api/organizations/stats", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(content);

                // The stats endpoint returns { "totalOrganizations": 5, "totalUsers": 10 }
                if (doc.RootElement.TryGetProperty("totalOrganizations", out var totalOrgsElement))
                {
                    stats.TotalTenants = totalOrgsElement.GetInt32();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get tenant statistics");
        }
    }

    private async Task GetPeerStatisticsAsync(DashboardStatistics stats, CancellationToken cancellationToken)
    {
        try
        {
            var baseUrl = _configuration["Services:Peer:Url"] ?? "http://peer-service:8080";
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(5);

            // Query connected peers endpoint
            var response = await client.GetAsync($"{baseUrl}/api/peers/connected", cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(content);

                if (doc.RootElement.TryGetProperty("ConnectedPeerCount", out var countElement))
                {
                    stats.ConnectedPeers = countElement.GetInt32();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get peer statistics");
        }
    }
}

/// <summary>
/// Dashboard statistics model. Feature 131 / UX-005 adds a <see cref="Scope"/> discriminator
/// and org-only fields; platform-only fields stay populated only in the platform shape.
/// Nullable fields are serialised as <c>null</c> when not in scope; the client treats null as
/// "card not present in this view".
/// </summary>
public class DashboardStatistics
{
    /// <summary>"org" | "platform" — distinguishes the two response shapes.</summary>
    public string Scope { get; set; } = "platform";

    /// <summary>Organization id (only set when <see cref="Scope"/> = "org").</summary>
    public Guid? OrgId { get; set; }

    public DateTimeOffset Timestamp { get; set; }

    // Platform-scope fields. Nullable so the JSON omits them in org responses.
    public int? TotalBlueprints { get; set; }
    public int? TotalBlueprintInstances { get; set; }
    public int? ActiveBlueprintInstances { get; set; }
    public int? TotalWallets { get; set; }
    public int? TotalRegisters { get; set; }
    public int? TotalTransactions { get; set; }
    public int? TotalTenants { get; set; }
    public int? ConnectedPeers { get; set; }

    // Org-scope fields. Nullable so the JSON omits them in platform responses.
    public int? ActiveUsers { get; set; }
    public int? PendingInvitations { get; set; }
    public int? SubscribedRegisters { get; set; }
    public int? RecentTransactions { get; set; }
}
