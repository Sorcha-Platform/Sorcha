// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Administrator tool for querying register statistics.
/// </summary>
[McpServerToolType]
public sealed class RegisterStatsTool
{
    private readonly IMcpSessionService _sessionService;
    private readonly IMcpAuthorizationService _authService;
    private readonly IMcpErrorHandler _errorHandler;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<RegisterStatsTool> _logger;
    private readonly string _registerServiceEndpoint;

    public RegisterStatsTool(
        IMcpSessionService sessionService,
        IMcpAuthorizationService authService,
        IMcpErrorHandler errorHandler,
        IServiceAvailabilityTracker availabilityTracker,
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<RegisterStatsTool> logger)
    {
        _sessionService = sessionService;
        _authService = authService;
        _errorHandler = errorHandler;
        _availabilityTracker = availabilityTracker;
        _httpClientFactory = httpClientFactory;
        _logger = logger;

        _registerServiceEndpoint = configuration["ServiceClients:RegisterService:Address"] ?? "http://localhost:5290";
    }

    /// <summary>
    /// Queries register statistics.
    /// </summary>
    /// <param name="registerId">Optional: Specific register ID to get detailed statistics.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Register statistics including counts, transaction metrics, and activity summary.</returns>
    [McpServerTool(Name = "sorcha_register_stats")]
    [Description("Returns either platform-wide register inventory (count plus the ten most recently created registers) or, when registerId is provided, transaction-level statistics for that single register including total transactions, unique wallets, sender and recipient counts, payload totals, and earliest/latest transaction timestamps. Call this when you need ledger volume and activity figures for capacity planning, billing analysis, or sizing a tenant's footprint; prefer this over sorcha_validator_status when the question is about how much data a register holds rather than whether consensus is healthy, and call before drilling into sorcha_log_query so the log window can be aligned to the register's actual activity span.")]
    public async Task<RegisterStatsResult> GetRegisterStatsAsync(
        [Description("Optional register ID for detailed transaction statistics")] string? registerId = null,
        CancellationToken cancellationToken = default)
    {
        // Authorization check
        if (!_authService.CanInvokeTool("sorcha_register_stats"))
        {
            return new RegisterStatsResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // Check service availability
        if (!_availabilityTracker.IsServiceAvailable("Register"))
        {
            return new RegisterStatsResult
            {
                Status = "Unavailable",
                Message = "Register service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Querying register statistics{RegisterInfo}",
            string.IsNullOrEmpty(registerId) ? "" : $" for register {registerId}");

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(10);

            // Get overall statistics
            var overallStats = await GetOverallStatsAsync(client, cancellationToken);

            // If specific register requested, get detailed stats
            RegisterTransactionStats? registerStats = null;
            if (!string.IsNullOrEmpty(registerId))
            {
                registerStats = await GetRegisterTransactionStatsAsync(client, registerId, cancellationToken);
            }

            stopwatch.Stop();

            // Record success
            _availabilityTracker.RecordSuccess("Register");

            // Determine status
            string status;
            string message;

            if (overallStats == null)
            {
                status = "Unknown";
                message = "Unable to retrieve register statistics.";
            }
            else if (!string.IsNullOrEmpty(registerId) && registerStats == null)
            {
                status = "Partial";
                message = $"Register service is operational but could not retrieve stats for register {registerId}.";
            }
            else
            {
                status = "Healthy";
                message = string.IsNullOrEmpty(registerId)
                    ? $"Register service is operational with {overallStats?.RegisterCount ?? 0} registers."
                    : $"Retrieved transaction statistics for register {registerId}.";
            }

            _logger.LogInformation(
                "Register stats query completed in {ElapsedMs}ms. Status: {Status}",
                stopwatch.ElapsedMilliseconds, status);

            return new RegisterStatsResult
            {
                Status = status,
                Message = message,
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                OverallStats = overallStats,
                RegisterStats = registerStats
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Register");

            _logger.LogWarning("Register stats query timed out");

            return new RegisterStatsResult
            {
                Status = "Timeout",
                Message = "Request to register service timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Register", ex);

            _logger.LogWarning(ex, "Failed to query register stats");

            return new RegisterStatsResult
            {
                Status = "Error",
                Message = $"Failed to connect to register service: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Register", ex);

            _logger.LogError(ex, "Unexpected error querying register stats");

            return new RegisterStatsResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while querying register statistics.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    private async Task<OverallRegisterStats?> GetOverallStatsAsync(
        HttpClient client,
        CancellationToken cancellationToken)
    {
        try
        {
            // Get register count
            var countUrl = $"{_registerServiceEndpoint.TrimEnd('/')}/api/registers/stats/count";
            var countResponse = await client.GetAsync(countUrl, cancellationToken);

            int? registerCount = null;
            if (countResponse.IsSuccessStatusCode)
            {
                var countContent = await countResponse.Content.ReadAsStringAsync(cancellationToken);
                var countData = JsonSerializer.Deserialize<RegisterCountResponse>(countContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                registerCount = countData?.Count;
            }

            // Get register list (limited view)
            var listUrl = $"{_registerServiceEndpoint.TrimEnd('/')}/api/registers/";
            var listResponse = await client.GetAsync(listUrl, cancellationToken);

            List<RegisterSummary>? registers = null;
            if (listResponse.IsSuccessStatusCode)
            {
                var listContent = await listResponse.Content.ReadAsStringAsync(cancellationToken);
                var registerList = JsonSerializer.Deserialize<List<RegisterResponse>>(listContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (registerList != null)
                {
                    // Take top 10 registers by created date for summary
                    registers = registerList
                        .OrderByDescending(r => r.CreatedAt)
                        .Take(10)
                        .Select(r => new RegisterSummary
                        {
                            RegisterId = r.Id,
                            Name = r.Name,
                            Status = r.Status,
                            TenantId = r.TenantId,
                            Height = r.Height,
                            CreatedAt = r.CreatedAt
                        })
                        .ToList();
                }
            }

            // Return null if both requests failed
            if (registerCount == null && registers == null)
            {
                _logger.LogDebug("Unable to retrieve any register statistics");
                return null;
            }

            return new OverallRegisterStats
            {
                RegisterCount = registerCount ?? 0,
                RecentRegisters = registers ?? []
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching overall register stats");
            return null;
        }
    }

    private async Task<RegisterTransactionStats?> GetRegisterTransactionStatsAsync(
        HttpClient client,
        string registerId,
        CancellationToken cancellationToken)
    {
        try
        {
            var url = $"{_registerServiceEndpoint.TrimEnd('/')}/api/query/stats?registerId={Uri.EscapeDataString(registerId)}";
            var response = await client.GetAsync(url, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to get transaction stats for register {RegisterId}: HTTP {StatusCode}",
                    registerId, response.StatusCode);
                return null;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var stats = JsonSerializer.Deserialize<TransactionStatsResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (stats == null) return null;

            return new RegisterTransactionStats
            {
                RegisterId = registerId,
                TotalTransactions = stats.TotalTransactions,
                UniqueWallets = stats.UniqueWallets,
                UniqueSenders = stats.UniqueSenders,
                UniqueRecipients = stats.UniqueRecipients,
                TotalPayloads = stats.TotalPayloads,
                EarliestTransaction = stats.EarliestTransaction,
                LatestTransaction = stats.LatestTransaction
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error fetching transaction stats for register {RegisterId}", registerId);
            return null;
        }
    }

    // Internal response models for deserialization
    private sealed class RegisterCountResponse
    {
        public int Count { get; set; }
    }

    private sealed class RegisterResponse
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string TenantId { get; set; } = string.Empty;
        public long Height { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
    }

    private sealed class TransactionStatsResponse
    {
        public int TotalTransactions { get; set; }
        public int UniqueWallets { get; set; }
        public int UniqueSenders { get; set; }
        public int UniqueRecipients { get; set; }
        public long TotalPayloads { get; set; }
        public DateTime? EarliestTransaction { get; set; }
        public DateTime? LatestTransaction { get; set; }
    }
}

/// <summary>
/// Result of a register statistics query.
/// </summary>
public sealed record RegisterStatsResult
{
    /// <summary>
    /// Overall status: Healthy, Partial, Unknown, Unavailable, Timeout, Error, or Unauthorized.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>
    /// Human-readable message about the query result.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// When the query was performed.
    /// </summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>
    /// Response time in milliseconds.
    /// </summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>
    /// Overall register statistics.
    /// </summary>
    public OverallRegisterStats? OverallStats { get; init; }

    /// <summary>
    /// Transaction statistics for a specific register (if registerId was provided).
    /// </summary>
    public RegisterTransactionStats? RegisterStats { get; init; }
}

/// <summary>
/// Overall register statistics.
/// </summary>
public sealed record OverallRegisterStats
{
    /// <summary>
    /// Total number of registers.
    /// </summary>
    public int RegisterCount { get; init; }

    /// <summary>
    /// List of recent registers (up to 10).
    /// </summary>
    public IReadOnlyList<RegisterSummary> RecentRegisters { get; init; } = [];
}

/// <summary>
/// Summary information about a register.
/// </summary>
public sealed record RegisterSummary
{
    /// <summary>
    /// Register unique identifier.
    /// </summary>
    public required string RegisterId { get; init; }

    /// <summary>
    /// Register display name.
    /// </summary>
    public string? Name { get; init; }

    /// <summary>
    /// Current status (Active, Inactive, etc.).
    /// </summary>
    public string? Status { get; init; }

    /// <summary>
    /// Tenant ID.
    /// </summary>
    public string? TenantId { get; init; }

    /// <summary>
    /// Current chain height (number of dockets).
    /// </summary>
    public long Height { get; init; }

    /// <summary>
    /// When the register was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }
}

/// <summary>
/// Transaction statistics for a specific register.
/// </summary>
public sealed record RegisterTransactionStats
{
    /// <summary>
    /// Register ID.
    /// </summary>
    public required string RegisterId { get; init; }

    /// <summary>
    /// Total number of transactions.
    /// </summary>
    public int TotalTransactions { get; init; }

    /// <summary>
    /// Number of unique wallets involved.
    /// </summary>
    public int UniqueWallets { get; init; }

    /// <summary>
    /// Number of unique sender addresses.
    /// </summary>
    public int UniqueSenders { get; init; }

    /// <summary>
    /// Number of unique recipient addresses.
    /// </summary>
    public int UniqueRecipients { get; init; }

    /// <summary>
    /// Total number of payloads across all transactions.
    /// </summary>
    public long TotalPayloads { get; init; }

    /// <summary>
    /// Timestamp of the earliest transaction.
    /// </summary>
    public DateTime? EarliestTransaction { get; init; }

    /// <summary>
    /// Timestamp of the most recent transaction.
    /// </summary>
    public DateTime? LatestTransaction { get; init; }
}
