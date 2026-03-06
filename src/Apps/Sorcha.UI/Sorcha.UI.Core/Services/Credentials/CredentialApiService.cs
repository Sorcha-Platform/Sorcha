// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Extensions;
using Sorcha.UI.Core.Models.Credentials;

namespace Sorcha.UI.Core.Services.Credentials;

/// <summary>
/// HttpClient implementation for the Wallet Service credential endpoints.
/// </summary>
public class CredentialApiService : ICredentialApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<CredentialApiService> _logger;

    private static readonly JsonSerializerOptions JsonOptions = JsonDefaults.Api;

    public CredentialApiService(HttpClient httpClient, ILogger<CredentialApiService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<List<CredentialCardViewModel>> GetCredentialsAsync(
        string walletAddress, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/v1/wallets/{walletAddress}/credentials", ct);

            if (!response.IsSuccessStatusCode)
                return [];

            var credentials = await response.Content
                .ReadFromJsonAsync<List<CredentialListItem>>(JsonOptions, ct);

            return credentials?.Select(MapToCardViewModel).ToList() ?? [];
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch credentials for wallet {WalletAddress}", walletAddress);
            return [];
        }
    }

    public async Task<CredentialDetailViewModel?> GetCredentialDetailAsync(
        string walletAddress, string credentialId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/v1/wallets/{walletAddress}/credentials/{credentialId}", ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var entity = await response.Content
                .ReadFromJsonAsync<CredentialDetailResponse>(JsonOptions, ct);

            return entity == null ? null : MapToDetailViewModel(entity);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch credential {CredentialId} for wallet {WalletAddress}", credentialId, walletAddress);
            return null;
        }
    }

    public async Task<bool> UpdateCredentialStatusAsync(
        string walletAddress, string credentialId, string newStatus, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PatchAsJsonAsync(
                $"/api/v1/wallets/{walletAddress}/credentials/{credentialId}/status",
                new { Status = newStatus }, ct);

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to update status for credential {CredentialId}", credentialId);
            return false;
        }
    }

    public async Task<bool> DeleteCredentialAsync(
        string walletAddress, string credentialId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.DeleteAsync(
                $"/api/v1/wallets/{walletAddress}/credentials/{credentialId}", ct);

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to delete credential {CredentialId}", credentialId);
            return false;
        }
    }

    public async Task<List<PresentationRequestViewModel>> GetPresentationRequestsAsync(
        string walletAddress, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/v1/presentations?wallet={walletAddress}", ct);

            if (!response.IsSuccessStatusCode)
                return [];

            var requests = await response.Content
                .ReadFromJsonAsync<List<PresentationRequestItem>>(JsonOptions, ct);

            return requests?.Select(MapToPresentationViewModel).ToList() ?? [];
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch presentation requests for wallet {WalletAddress}", walletAddress);
            return [];
        }
    }

    public async Task<PresentationRequestViewModel?> GetPresentationRequestDetailAsync(
        string requestId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(
                $"/api/v1/presentations/{requestId}", ct);

            if (!response.IsSuccessStatusCode)
                return null;

            var request = await response.Content
                .ReadFromJsonAsync<PresentationRequestItem>(JsonOptions, ct);

            return request == null ? null : MapToPresentationViewModel(request);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to fetch presentation request {RequestId}", requestId);
            return null;
        }
    }

    public async Task<PresentationSubmitResult> SubmitPresentationAsync(
        string requestId, string credentialId, List<string> disclosedClaims,
        string vpToken, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(
                $"/api/v1/presentations/{requestId}/submit",
                new { credentialId, disclosedClaims, vpToken }, ct);

            if (!response.IsSuccessStatusCode)
            {
                return new PresentationSubmitResult
                {
                    Success = false,
                    ErrorMessage = $"Server returned {response.StatusCode}"
                };
            }

            var result = await response.Content
                .ReadFromJsonAsync<PresentationSubmitResponse>(JsonOptions, ct);

            return new PresentationSubmitResult
            {
                Success = true,
                Status = result?.Status ?? "Verified"
            };
        }
        catch (HttpRequestException ex)
        {
            return new PresentationSubmitResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }

    public async Task<bool> DenyPresentationAsync(string requestId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PostAsync(
                $"/api/v1/presentations/{requestId}/deny", null, ct);

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to deny presentation request {RequestId}", requestId);
            return false;
        }
    }

    public async Task<CredentialOperationResult> SuspendCredentialAsync(
        string credentialId, string issuerWallet, string? reason = null, CancellationToken ct = default)
    {
        return await ExecuteLifecycleOperationAsync(
            $"/api/v1/credentials/{credentialId}/suspend",
            new { issuerWallet, reason },
            credentialId, "suspend", ct);
    }

    public async Task<CredentialOperationResult> ReinstateCredentialAsync(
        string credentialId, string issuerWallet, string? reason = null, CancellationToken ct = default)
    {
        return await ExecuteLifecycleOperationAsync(
            $"/api/v1/credentials/{credentialId}/reinstate",
            new { issuerWallet, reason },
            credentialId, "reinstate", ct);
    }

    public async Task<CredentialOperationResult> RefreshCredentialAsync(
        string credentialId, string issuerWallet, string? newExpiryDuration = null, CancellationToken ct = default)
    {
        return await ExecuteLifecycleOperationAsync(
            $"/api/v1/credentials/{credentialId}/refresh",
            new { issuerWallet, newExpiryDuration },
            credentialId, "refresh", ct);
    }

    private async Task<CredentialOperationResult> ExecuteLifecycleOperationAsync(
        string url, object payload, string credentialId, string operation, CancellationToken ct)
    {
        try
        {
            var response = await _httpClient.PostAsJsonAsync(url, payload, ct);

            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadFromJsonAsync<CredentialLifecycleResult>(JsonOptions, ct);
                return result != null
                    ? CredentialOperationResult.Ok(result)
                    : CredentialOperationResult.Fail(CredentialErrorType.ServerError, "Empty response from server");
            }

            _logger.LogWarning("Failed to {Operation} credential {CredentialId}: {StatusCode}",
                operation, credentialId, response.StatusCode);
            return CredentialOperationResult.FromStatusCode((int)response.StatusCode);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Network error during {Operation} for credential {CredentialId}",
                operation, credentialId);
            return CredentialOperationResult.NetworkError(ex.Message);
        }
    }

    private static PresentationRequestViewModel MapToPresentationViewModel(PresentationRequestItem item)
    {
        return new PresentationRequestViewModel
        {
            RequestId = item.RequestId ?? string.Empty,
            VerifierIdentity = item.VerifierIdentity ?? "Unknown Verifier",
            CredentialType = item.CredentialType ?? string.Empty,
            RequestedClaims = item.RequiredClaims ?? [],
            ExpiresAt = item.ExpiresAt,
            Status = item.Status ?? "Pending",
            Nonce = item.Nonce,
            MatchingCredentials = item.MatchingCredentials?.Select(m => new MatchingCredentialViewModel
            {
                CredentialId = m.CredentialId ?? string.Empty,
                Type = m.Type ?? string.Empty,
                IssuerDid = m.IssuerDid ?? string.Empty,
                AvailableClaims = m.AvailableClaims ?? [],
                ExpiresAt = m.ExpiresAt
            }).ToList() ?? []
        };
    }

    private static CredentialCardViewModel MapToCardViewModel(CredentialListItem item)
    {
        var vm = new CredentialCardViewModel
        {
            CredentialId = item.Id ?? string.Empty,
            Type = item.Type ?? string.Empty,
            IssuerDid = item.IssuerDid ?? string.Empty,
            IssuerName = ExtractIssuerName(item.IssuerDid),
            SubjectDid = item.SubjectDid ?? string.Empty,
            Status = item.Status ?? CredentialStatus.Active,
            IssuedAt = item.IssuedAt,
            ExpiresAt = item.ExpiresAt
        };

        vm.AvailableActions = GetAvailableActions(vm.Status);
        return vm;
    }

    private static CredentialDetailViewModel MapToDetailViewModel(CredentialDetailResponse entity)
    {
        var claims = new Dictionary<string, object>();
        if (!string.IsNullOrEmpty(entity.ClaimsJson))
        {
            try
            {
                claims = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    entity.ClaimsJson, JsonOptions) ?? new();
            }
            catch (JsonException)
            {
                // Resilience: malformed claims JSON falls back to empty dictionary
            }
        }

        var displayConfig = new CredentialDisplayViewModel();
        if (!string.IsNullOrEmpty(entity.DisplayConfigJson))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<CredentialDisplayViewModel>(
                    entity.DisplayConfigJson, JsonOptions);
                if (parsed != null)
                    displayConfig = parsed;
            }
            catch (JsonException)
            {
                // Resilience: malformed display config JSON falls back to default
            }
        }

        return new CredentialDetailViewModel
        {
            CredentialId = entity.Id ?? string.Empty,
            Type = entity.Type ?? string.Empty,
            IssuerDid = entity.IssuerDid ?? string.Empty,
            SubjectDid = entity.SubjectDid ?? string.Empty,
            Status = entity.Status ?? CredentialStatus.Active,
            IssuedAt = entity.IssuedAt,
            ExpiresAt = entity.ExpiresAt,
            UsagePolicy = entity.UsagePolicy ?? "Reusable",
            MaxPresentations = entity.MaxPresentations,
            PresentationCount = entity.PresentationCount,
            Claims = claims,
            DisplayConfig = displayConfig,
            StatusListUrl = entity.StatusListUrl,
            IssuanceBlueprintId = entity.IssuanceBlueprintId
        };
    }

    private static string ExtractIssuerName(string? issuerDid)
    {
        if (string.IsNullOrEmpty(issuerDid)) return "Unknown Issuer";
        if (issuerDid.StartsWith("did:web:")) return issuerDid["did:web:".Length..];
        if (issuerDid.StartsWith("did:sorcha:w:"))
        {
            var suffix = issuerDid["did:sorcha:w:".Length..];
            return (suffix.Length > 8 ? suffix[..8] : suffix) + "...";
        }
        return issuerDid.Length > 20 ? issuerDid[..20] + "..." : issuerDid;
    }

    private static List<string> GetAvailableActions(string status) => status switch
    {
        CredentialStatus.Active => ["View", "Present", "Export", "Delete"],
        CredentialStatus.Suspended => ["View", "Delete"],
        CredentialStatus.Revoked => ["View", "Delete"],
        CredentialStatus.Expired => ["View", "Delete"],
        CredentialStatus.Consumed => ["View", "Delete"],
        _ => ["View"]
    };

    // DTOs matching the Wallet Service API response shape
    private class CredentialListItem
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? IssuerDid { get; set; }
        public string? SubjectDid { get; set; }
        public DateTimeOffset IssuedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public string? Status { get; set; }
    }

    private class CredentialDetailResponse
    {
        public string? Id { get; set; }
        public string? Type { get; set; }
        public string? IssuerDid { get; set; }
        public string? SubjectDid { get; set; }
        public DateTimeOffset IssuedAt { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
        public string? Status { get; set; }
        public string? ClaimsJson { get; set; }
        public string? UsagePolicy { get; set; }
        public int? MaxPresentations { get; set; }
        public int PresentationCount { get; set; }
        public string? DisplayConfigJson { get; set; }
        public string? StatusListUrl { get; set; }
        public string? IssuanceBlueprintId { get; set; }
    }

    private class PresentationRequestItem
    {
        public string? RequestId { get; set; }
        public string? VerifierIdentity { get; set; }
        public string? CredentialType { get; set; }
        public List<string>? RequiredClaims { get; set; }
        public DateTimeOffset ExpiresAt { get; set; }
        public string? Status { get; set; }
        public string? Nonce { get; set; }
        public List<MatchingCredentialItem>? MatchingCredentials { get; set; }
    }

    private class MatchingCredentialItem
    {
        public string? CredentialId { get; set; }
        public string? Type { get; set; }
        public string? IssuerDid { get; set; }
        public List<string>? AvailableClaims { get; set; }
        public DateTimeOffset? ExpiresAt { get; set; }
    }

    private class PresentationSubmitResponse
    {
        public string? Status { get; set; }
    }
}
