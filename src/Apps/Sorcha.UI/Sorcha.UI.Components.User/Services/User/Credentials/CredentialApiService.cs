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

    /// <summary>
    /// Initializes a new instance of <see cref="CredentialApiService"/> with the HTTP client and logger.
    /// </summary>
    public CredentialApiService(HttpClient httpClient, ILogger<CredentialApiService> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<List<CredentialCardViewModel>> GetCredentialsAsync(
        string walletAddress, CancellationToken ct = default)
    {
        try
        {
            // Feature 106 — request All so PendingAcceptance + Declined rows land alongside
            // Active/Expired/Revoked. The MyCredentials page splits them client-side into tabs.
            var response = await _httpClient.GetAsync(
                $"/api/v1/wallets/{walletAddress}/credentials?status=All", ct);

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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public async Task<CredentialOperationResult> SuspendCredentialAsync(
        string credentialId, string issuerWallet, string? reason = null, CancellationToken ct = default)
    {
        return await ExecuteLifecycleOperationAsync(
            $"/api/v1/credentials/{credentialId}/suspend",
            new { issuerWallet, reason },
            credentialId, "suspend", ct);
    }

    /// <inheritdoc/>
    public async Task<CredentialOperationResult> ReinstateCredentialAsync(
        string credentialId, string issuerWallet, string? reason = null, CancellationToken ct = default)
    {
        return await ExecuteLifecycleOperationAsync(
            $"/api/v1/credentials/{credentialId}/reinstate",
            new { issuerWallet, reason },
            credentialId, "reinstate", ct);
    }

    /// <inheritdoc/>
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
        var displayConfig = ParseDisplayConfig(item.DisplayConfigJson);
        var claims = ParseClaims(item.ClaimsJson);

        var vm = new CredentialCardViewModel
        {
            CredentialId = item.Id ?? string.Empty,
            Type = item.Type ?? string.Empty,
            IssuerDid = item.IssuerDid ?? string.Empty,
            IssuerName = item.IssuerOrgName ?? ExtractIssuerName(item.IssuerDid),
            IssuerOrgName = item.IssuerOrgName,
            SubjectDid = item.SubjectDid ?? string.Empty,
            Status = item.Status ?? CredentialStatus.Active,
            IssuedAt = item.IssuedAt,
            ExpiresAt = item.ExpiresAt,
            UsagePolicy = string.IsNullOrWhiteSpace(item.UsagePolicy) ? "Reusable" : item.UsagePolicy,
            IssuanceBlueprintId = item.IssuanceBlueprintId,
            IssuanceInstanceId = item.IssuanceInstanceId,
            IssuanceActionId = item.IssuanceActionId,
            ClaimActionId = item.ClaimActionId,
            RegisterId = item.RegisterId,
            DisplayConfig = displayConfig,
            HighlightClaims = BuildHighlightClaims(claims, displayConfig),
            DisclosableClaims = item.DisclosableClaims ?? [],
            DisplayName = Humanise(item.Type),
            ClaimSummary = BuildClaimSummary(claims),
            // Feature 106 — rows with the new PendingAcceptance status flow into
            // the MyCredentials PENDING tab via CredentialCardViewModel.IsPending.
            IsPending = string.Equals(item.Status, CredentialStatus.PendingAcceptance, StringComparison.Ordinal),
        };

        vm.AvailableActions = GetAvailableActions(vm.Status);
        return vm;
    }

    /// <summary>
    /// "AssuredIdentityCredential" → "Assured Identity". Splits PascalCase and drops
    /// the redundant "Credential" suffix — every card on the page is a credential.
    /// </summary>
    private static string Humanise(string? type)
    {
        if (string.IsNullOrWhiteSpace(type)) return string.Empty;

        var trimmed = type.EndsWith("Credential", StringComparison.Ordinal) && type.Length > "Credential".Length
            ? type[..^"Credential".Length]
            : type;

        var spaced = System.Text.RegularExpressions.Regex.Replace(
            trimmed, "(?<=[a-z0-9])(?=[A-Z])", " ");

        return spaced.Length == 0 ? type : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    /// <summary>
    /// A single line naming what the credential holds — names only, never values.
    /// Caps at four names so a fat credential cannot blow the card open.
    /// </summary>
    private static string BuildClaimSummary(IReadOnlyDictionary<string, object?> claims)
    {
        var names = claims.Keys
            .Where(k => !k.StartsWith('_'))
            .Select(HumaniseClaimName)
            .ToList();

        if (names.Count == 0) return string.Empty;
        if (names.Count <= 4) return string.Join(", ", names);

        return string.Join(", ", names.Take(4)) + $" and {names.Count - 4} more";
    }

    /// <summary>"dateOfBirth" → "Date of birth". Sentence case, not Title Case.</summary>
    private static string HumaniseClaimName(string key)
    {
        var spaced = System.Text.RegularExpressions.Regex.Replace(
            key, "(?<=[a-z0-9])(?=[A-Z])", " ").ToLowerInvariant();
        return spaced.Length == 0 ? key : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }

    /// <summary>
    /// Resolve the claims map to display on a card. Honours
    /// <c>displayConfig.highlightClaims</c> (key = JSON pointer, value = display
    /// label) when the issuer specified one; otherwise falls back to the first
    /// six claim entries with their raw keys so credentials without an explicit
    /// display contract still render meaningfully.
    /// </summary>
    private static Dictionary<string, string> BuildHighlightClaims(
        IReadOnlyDictionary<string, object?> claims,
        CredentialDisplayViewModel displayConfig)
    {
        if (claims.Count == 0) return new();

        if (displayConfig.HighlightClaims is { Count: > 0 })
        {
            var result = new Dictionary<string, string>();
            foreach (var (pointer, label) in displayConfig.HighlightClaims)
            {
                var value = ResolveJsonPointer(claims, pointer);
                if (value is null) continue;
                result[label] = value;
            }
            if (result.Count > 0) return result;
        }

        return claims
            .Where(kvp => kvp.Value is not null && !kvp.Key.StartsWith('_'))
            .Take(6)
            .ToDictionary(kvp => kvp.Key, kvp => StringifyClaimValue(kvp.Value));
    }

    private static Dictionary<string, object?> ParseClaims(string? claimsJson)
    {
        if (string.IsNullOrWhiteSpace(claimsJson)) return new();
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(claimsJson, JsonOptions)
                ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private static CredentialDisplayViewModel ParseDisplayConfig(string? displayConfigJson)
    {
        if (string.IsNullOrWhiteSpace(displayConfigJson)) return new();
        try
        {
            return JsonSerializer.Deserialize<CredentialDisplayViewModel>(displayConfigJson, JsonOptions)
                ?? new();
        }
        catch (JsonException)
        {
            return new();
        }
    }

    private static string? ResolveJsonPointer(IReadOnlyDictionary<string, object?> root, string pointer)
    {
        // Minimal RFC 6901 resolver — supports "/a", "/a/b", or bare "a" keys.
        var path = pointer.StartsWith('/') ? pointer[1..] : pointer;
        if (path.Length == 0) return null;
        var segments = path.Split('/');
        object? cursor = root;
        foreach (var segment in segments)
        {
            switch (cursor)
            {
                case IReadOnlyDictionary<string, object?> dict when dict.TryGetValue(segment, out var next):
                    cursor = next;
                    break;
                case JsonElement el when el.ValueKind == JsonValueKind.Object && el.TryGetProperty(segment, out var next):
                    cursor = next;
                    break;
                default:
                    return null;
            }
        }
        return StringifyClaimValue(cursor);
    }

    /// <summary>
    /// Renders a claim value for display. An object or array NEVER renders as raw
    /// JSON — that is how an unresolved SD-JWT digest array reached a citizen's
    /// card on n1. The server should not send one, and this layer must not be
    /// capable of printing it if it does.
    /// </summary>
    private static string StringifyClaimValue(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        JsonElement el => el.ValueKind switch
        {
            JsonValueKind.String => el.GetString() ?? string.Empty,
            JsonValueKind.Number => el.ToString(),
            JsonValueKind.True or JsonValueKind.False => el.GetBoolean().ToString(),
            JsonValueKind.Null => string.Empty,
            JsonValueKind.Object => SummariseObject(el),
            JsonValueKind.Array => $"{el.GetArrayLength()} item{(el.GetArrayLength() == 1 ? "" : "s")}",
            _ => string.Empty
        },
        _ => value.ToString() ?? string.Empty
    };

    /// <summary>
    /// A nested object renders as its field names, not its JSON. Protocol keys are
    /// dropped so a stray digest array degrades to an empty string, never a blob.
    /// </summary>
    private static string SummariseObject(JsonElement el)
    {
        var fields = el.EnumerateObject()
            .Where(p => !p.Name.StartsWith('_'))
            .Select(p => p.Name)
            .ToList();

        return fields.Count == 0 ? string.Empty : string.Join(", ", fields);
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
            IssuerName = ExtractIssuerName(entity.IssuerDid),
            SubjectDid = entity.SubjectDid ?? string.Empty,
            Status = entity.Status ?? CredentialStatus.Active,
            IssuedAt = entity.IssuedAt,
            ExpiresAt = entity.ExpiresAt,
            UsagePolicy = entity.UsagePolicy ?? "Reusable",
            MaxPresentations = entity.MaxPresentations,
            PresentationCount = entity.PresentationCount,
            Claims = claims,
            DisplayClaims = BuildDisplayClaims(claims),
            DisplayConfig = displayConfig,
            StatusListUrl = entity.StatusListUrl,
            IssuanceBlueprintId = entity.IssuanceBlueprintId
        };
    }

    /// <summary>
    /// The detail-dialog counterpart of <see cref="BuildHighlightClaims"/>. Every claim gets
    /// a safe display string — protocol keys (top-level and nested) are dropped, and nested
    /// objects render as structural name/value pairs rather than raw JSON. This is the fix
    /// for the second door onto the n1 `{"_sd":[...]}` leak: <c>MapToDetailViewModel</c> used
    /// to hand the raw <see cref="Dictionary{TKey,TValue}"/> straight to the view, and the
    /// Razor markup called <c>@claim.Value?.ToString()</c> — for a boxed <see cref="JsonElement"/>
    /// of <see cref="JsonValueKind.Object"/> that returns <c>GetRawText()</c>.
    /// </summary>
    private static Dictionary<string, string> BuildDisplayClaims(IReadOnlyDictionary<string, object> claims)
    {
        return claims
            .Where(kvp => !kvp.Key.StartsWith('_'))
            .ToDictionary(kvp => kvp.Key, kvp => FormatClaimForDetailDisplay(kvp.Value));
    }

    /// <summary>
    /// Detail-dialog claim formatter. Unlike <see cref="StringifyClaimValue"/> (which reduces
    /// a nested object to its field names for the compact card surface), the detail dialog has
    /// room to be useful: nested objects render as "Name: value" pairs, recursively, still
    /// dropping <c>_</c>-prefixed protocol keys at every level and never emitting raw JSON.
    /// </summary>
    private static string FormatClaimForDetailDisplay(object? value) => value switch
    {
        null => string.Empty,
        string s => s,
        JsonElement el => FormatJsonElementForDetailDisplay(el),
        _ => value.ToString() ?? string.Empty
    };

    private static string FormatJsonElementForDetailDisplay(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString() ?? string.Empty,
        JsonValueKind.Number => el.ToString(),
        JsonValueKind.True or JsonValueKind.False => el.GetBoolean().ToString(),
        JsonValueKind.Null => string.Empty,
        JsonValueKind.Object => string.Join(", ", el.EnumerateObject()
            .Where(p => !p.Name.StartsWith('_'))
            .Select(p => $"{HumaniseClaimName(p.Name)}: {FormatJsonElementForDetailDisplay(p.Value)}")),
        JsonValueKind.Array => string.Join(", ", el.EnumerateArray()
            .Select(FormatJsonElementForDetailDisplay)
            .Where(s => s.Length > 0)),
        _ => string.Empty
    };

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

        // Issuer identity
        public string? IssuerOrgName { get; set; }

        // Feature 106 — deep-link fields for MyCredentials PENDING tab and
        // the holder accept/decline orchestration.
        public string? IssuanceBlueprintId { get; set; }
        public string? IssuanceTxId { get; set; }

        // Feature 106 SC-003 — metadata for Action 3 execute on accept/decline.
        public string? IssuanceInstanceId { get; set; }
        public string? IssuanceActionId { get; set; }
        public string? ClaimActionId { get; set; }
        public string? RegisterId { get; set; }

        // Holder needs the claim payload + display config to make an informed
        // Accept/Decline decision on the Pending tab — see CredentialAcceptCard.
        public string? ClaimsJson { get; set; }
        public string? DisplayConfigJson { get; set; }
        public string? UsagePolicy { get; set; }

        /// <summary>Claims the holder may withhold when presenting. Server-derived from the raw token.</summary>
        public List<string>? DisclosableClaims { get; set; }
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

    /// <inheritdoc/>
    // TODO: Pending/Declined status support requires Wallet Service endpoint update.
    // Currently the PATCH status endpoint supports Active/Suspended/Revoked/Consumed.
    // Until backend is updated, these methods will return false (status 400).
    public async Task<List<CredentialCardViewModel>> GetPendingCredentialsAsync(
        string walletAddress, CancellationToken ct = default)
    {
        var all = await GetCredentialsAsync(walletAddress, ct);
        return all.Where(c => c.IsPending).ToList();
    }

    /// <inheritdoc/>
    // Feature 106 Wave E — holder accept path. Hits the Feature 106 PATCH endpoint
    // (without the /status suffix) which enforces the PendingAcceptance → Active
    // state-machine transition. Parallel holder sessions stay in sync via the
    // WalletHub typed CredentialStatusChanged event.
    public async Task<bool> AcceptCredentialAsync(
        string walletAddress, string credentialId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PatchAsJsonAsync(
                $"/api/v1/wallets/{Uri.EscapeDataString(walletAddress)}/credentials/{Uri.EscapeDataString(credentialId)}",
                new { Status = nameof(CredentialStatus.Active) }, ct);

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to accept credential {CredentialId} for wallet {WalletAddress}",
                credentialId, walletAddress);
            return false;
        }
    }

    /// <inheritdoc/>
    // Feature 106 Wave E — holder decline path. Same endpoint as accept; the server
    // routes the transition through CredentialStore.PatchStatusAsync which enforces
    // the state machine and retains the row for audit (data-model INV-3).
    public async Task<bool> DeclineCredentialAsync(
        string walletAddress, string credentialId, CancellationToken ct = default)
    {
        try
        {
            var response = await _httpClient.PatchAsJsonAsync(
                $"/api/v1/wallets/{Uri.EscapeDataString(walletAddress)}/credentials/{Uri.EscapeDataString(credentialId)}",
                new { Status = nameof(CredentialStatus.Declined) }, ct);

            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning(ex, "Failed to decline credential {CredentialId} for wallet {WalletAddress}",
                credentialId, walletAddress);
            return false;
        }
    }

    /// <inheritdoc/>
    public async Task<List<CredentialMatchResult>> MatchCredentialsAsync(
        string walletAddress,
        List<Sorcha.Blueprint.Models.Credentials.CredentialRequirement> requirements,
        CancellationToken ct = default)
    {
        try
        {
            _logger.LogDebug("Matching credentials for wallet {WalletAddress} against {Count} requirements",
                walletAddress, requirements.Count);

            var response = await _httpClient.PostAsJsonAsync(
                $"/api/v1/wallets/{Uri.EscapeDataString(walletAddress)}/credentials/match",
                requirements, JsonOptions, ct);

            if (response.IsSuccessStatusCode)
            {
                var results = await response.Content.ReadFromJsonAsync<List<CredentialMatchResult>>(JsonOptions, ct);
                return results ?? [];
            }

            _logger.LogWarning("Credential match failed: {StatusCode}", response.StatusCode);
            return [];
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error matching credentials for wallet {WalletAddress}", walletAddress);
            return [];
        }
    }

    private class PresentationSubmitResponse
    {
        public string? Status { get; set; }
    }
}
