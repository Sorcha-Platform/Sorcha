// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.Admin;

namespace Sorcha.UI.Core.Services;

/// <summary>
/// Implementation of <see cref="IValidatorAdminService"/> that calls the Validator Service API.
/// </summary>
public class ValidatorAdminService : IValidatorAdminService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<ValidatorAdminService> _logger;

    public ValidatorAdminService(HttpClient httpClient, ILogger<ValidatorAdminService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<ValidatorStatusViewModel> GetMempoolStatusAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/admin/mempool", cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to fetch mempool status: {StatusCode}", response.StatusCode);
                return new ValidatorStatusViewModel { IsLoaded = false };
            }

            var status = await response.Content.ReadFromJsonAsync<ValidatorStatusViewModel>(cancellationToken: cancellationToken);
            return status ?? new ValidatorStatusViewModel { IsLoaded = false };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching mempool status");
            return new ValidatorStatusViewModel { IsLoaded = false };
        }
    }

    public async Task<RegisterMempoolStat> GetRegisterMempoolAsync(string registerId, CancellationToken cancellationToken = default)
    {
        try
        {
            var stat = await _httpClient.GetFromJsonAsync<RegisterMempoolStat>(
                $"/api/v1/transactions/mempool/{registerId}", cancellationToken);
            return stat ?? new RegisterMempoolStat { RegisterId = registerId };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching mempool for register {RegisterId}", registerId);
            return new RegisterMempoolStat { RegisterId = registerId };
        }
    }

    // --- Consent Queue ---

    public async Task<ConsentQueueViewModel> GetConsentQueueAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/admin/validators/consent-queue", ct);
            if (!response.IsSuccessStatusCode) return new ConsentQueueViewModel();
            return await response.Content.ReadFromJsonAsync<ConsentQueueViewModel>(cancellationToken: ct) ?? new ConsentQueueViewModel();
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching consent queue"); return new ConsentQueueViewModel(); }
    }

    public async Task<List<PendingValidatorViewModel>> GetPendingValidatorsAsync(string registerId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync($"/api/validators/{registerId}/pending", ct);
            if (!response.IsSuccessStatusCode) return [];
            return await response.Content.ReadFromJsonAsync<List<PendingValidatorViewModel>>(cancellationToken: ct) ?? [];
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching pending validators for {RegisterId}", registerId); return []; }
    }

    public async Task<bool> ApproveValidatorAsync(string registerId, string validatorId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsync($"/api/validators/{registerId}/{validatorId}/approve", null, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { _logger.LogError(ex, "Error approving validator {ValidatorId}", validatorId); return false; }
    }

    public async Task<bool> RejectValidatorAsync(string registerId, string validatorId, string? reason = null, CancellationToken ct = default)
    {
        try
        {
            var content = reason != null ? JsonContent.Create(new { reason }) : null;
            var response = await _httpClient.PostAsync($"/api/validators/{registerId}/{validatorId}/reject", content, ct);
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex) { _logger.LogError(ex, "Error rejecting validator {ValidatorId}", validatorId); return false; }
    }

    public async Task<List<ApprovedValidatorInfo>> RefreshApprovedValidatorsAsync(string registerId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsync($"/api/validators/{registerId}/refresh", null, ct);
            if (!response.IsSuccessStatusCode) return [];
            return await response.Content.ReadFromJsonAsync<List<ApprovedValidatorInfo>>(cancellationToken: ct) ?? [];
        }
        catch (Exception ex) { _logger.LogError(ex, "Error refreshing validators for {RegisterId}", registerId); return []; }
    }

    // --- Metrics ---

    public async Task<AggregatedMetricsViewModel> GetAggregatedMetricsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/validator/metrics", ct);
            if (!response.IsSuccessStatusCode) return new AggregatedMetricsViewModel();
            return await response.Content.ReadFromJsonAsync<AggregatedMetricsViewModel>(cancellationToken: ct) ?? new AggregatedMetricsViewModel();
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching aggregated metrics"); return new AggregatedMetricsViewModel(); }
    }

    public async Task<ValidationSummaryViewModel> GetValidationMetricsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/validator/metrics/validation", ct);
            if (!response.IsSuccessStatusCode) return new ValidationSummaryViewModel();
            return await response.Content.ReadFromJsonAsync<ValidationSummaryViewModel>(cancellationToken: ct) ?? new ValidationSummaryViewModel();
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching validation metrics"); return new ValidationSummaryViewModel(); }
    }

    public async Task<ConsensusSummaryViewModel> GetConsensusMetricsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/validator/metrics/consensus", ct);
            if (!response.IsSuccessStatusCode) return new ConsensusSummaryViewModel();
            return await response.Content.ReadFromJsonAsync<ConsensusSummaryViewModel>(cancellationToken: ct) ?? new ConsensusSummaryViewModel();
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching consensus metrics"); return new ConsensusSummaryViewModel(); }
    }

    public async Task<PoolSummaryViewModel> GetPoolMetricsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/validator/metrics/pools", ct);
            if (!response.IsSuccessStatusCode) return new PoolSummaryViewModel();
            return await response.Content.ReadFromJsonAsync<PoolSummaryViewModel>(cancellationToken: ct) ?? new PoolSummaryViewModel();
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching pool metrics"); return new PoolSummaryViewModel(); }
    }

    public async Task<CacheSummaryViewModel> GetCacheMetricsAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/validator/metrics/caches", ct);
            if (!response.IsSuccessStatusCode) return new CacheSummaryViewModel();
            return await response.Content.ReadFromJsonAsync<CacheSummaryViewModel>(cancellationToken: ct) ?? new CacheSummaryViewModel();
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching cache metrics"); return new CacheSummaryViewModel(); }
    }

    // --- Threshold ---

    public async Task<List<ThresholdConfigViewModel>> GetThresholdStatusAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/v1/validators/threshold/status", ct);
            if (!response.IsSuccessStatusCode) return [];
            return await response.Content.ReadFromJsonAsync<List<ThresholdConfigViewModel>>(cancellationToken: ct) ?? [];
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching threshold status"); return []; }
    }

    public async Task<ThresholdConfigViewModel> SetupThresholdAsync(ThresholdSetupRequest request, CancellationToken ct = default)
    {
        var response = await _httpClient.PostAsJsonAsync("/api/v1/validators/threshold/setup", request, ct);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<ThresholdConfigViewModel>(cancellationToken: ct)
            ?? throw new InvalidOperationException("Failed to deserialize threshold setup response");
    }

    // --- Config ---

    public async Task<ValidatorConfigViewModel> GetConfigAsync(CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync("/api/validator/metrics/config", ct);
            if (!response.IsSuccessStatusCode) return new ValidatorConfigViewModel();
            return await response.Content.ReadFromJsonAsync<ValidatorConfigViewModel>(cancellationToken: ct) ?? new ValidatorConfigViewModel();
        }
        catch (Exception ex) { _logger.LogError(ex, "Error fetching validator config"); return new ValidatorConfigViewModel(); }
    }
}
