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
            // Issue #1278: prefer the issuer-authored display name over anything derived from the
            // type. Post-#1187 the vct is a URI by design, so deriving a title from it is a last
            // resort, not the plan.
            DisplayName = !string.IsNullOrWhiteSpace(displayConfig.CredentialName)
                ? displayConfig.CredentialName!
                : Humanise(item.Type),
            ClaimSummary = BuildClaimSummary(claims),
            // Feature 106 — rows with the new PendingAcceptance status flow into
            // the MyCredentials PENDING tab via CredentialCardViewModel.IsPending.
            IsPending = string.Equals(item.Status, CredentialStatus.PendingAcceptance, StringComparison.Ordinal),
        };

        vm.AvailableActions = GetAvailableActions(vm.Status);
        return vm;
    }

    /// <summary>
    /// "AssuredIdentityCredential" → "Assured Identity"; a vct URI →
    /// "Assured Identity" via its last segment.
    /// </summary>
    /// <remarks>
    /// Issue #1278: this used to split PascalCase only, with no URI-tail extraction, so a vct URI
    /// passed straight through and the card title read
    /// <c>https://sorcha.dev/vc/assured-identity/v1</c>. It now delegates to
    /// <see cref="Components.Wallet.CredentialDisplay.Humanize"/> — the one implementation that already handled URIs
    /// correctly, and which the PWA has always used. Three near-copies of this logic existed and
    /// only that one was right; delegating removes the drift rather than adding a fourth.
    /// </remarks>
    private static string Humanise(string? type)
        => string.IsNullOrWhiteSpace(type)
            ? string.Empty
            : Components.Wallet.CredentialDisplay.Humanize(type);

    /// <summary>
    /// A single line naming what the credential holds — names only, never values.
    /// Caps at four names so a fat credential cannot blow the card open.
    /// </summary>
    private static string BuildClaimSummary(IReadOnlyDictionary<string, object?> claims)
    {
        var names = claims.Keys
            .Where(k => !k.StartsWith('_'))
            .Select(CredentialClaimDisplayFormatter.HumaniseClaimName)
            .ToList();

        if (names.Count == 0) return string.Empty;
        if (names.Count <= 4) return string.Join(", ", names);

        return string.Join(", ", names.Take(4)) + $" and {names.Count - 4} more";
    }

    /// <summary>
    /// Resolve the claims map to display on a card. Honours
    /// <c>displayConfig.highlightClaims</c> (key = JSON pointer, value = display
    /// label) when the issuer specified one; otherwise falls back to the first
    /// six claim entries with their raw keys so credentials without an explicit
    /// display contract still render meaningfully.
    /// <para>
    /// Each entry carries both the display label and the underlying claim name (the
    /// pointer's first segment) — the card's padlock resolves disclosability against
    /// the claim name, never the label. Keying a flat dictionary by label (the
    /// previous shape) made <c>DisclosableClaims.Contains(label)</c> false for every
    /// issuer that actually used <c>highlightClaims</c>, so every claim rendered
    /// "Always disclosed" regardless of who controlled it.
    /// </para>
    /// </summary>
    private static List<HighlightClaimEntry> BuildHighlightClaims(
        IReadOnlyDictionary<string, object?> claims,
        CredentialDisplayViewModel displayConfig)
    {
        if (claims.Count == 0) return new();

        if (displayConfig.HighlightClaims is { Count: > 0 })
        {
            var result = new List<HighlightClaimEntry>();
            foreach (var (pointer, label) in displayConfig.HighlightClaims)
            {
                var value = ResolveJsonPointer(claims, pointer);
                if (value is null) continue;
                result.Add(new HighlightClaimEntry(ClaimNameFromPointer(pointer), label, value));
            }
            if (result.Count > 0) return result;
        }

        return claims
            .Where(kvp => kvp.Value is not null && !kvp.Key.StartsWith('_'))
            .Take(6)
            .Select(kvp => new HighlightClaimEntry(kvp.Key, kvp.Key, StringifyClaimValue(kvp.Value)))
            .ToList();
    }

    /// <summary>
    /// The claim name a JSON Pointer (or bare key) is rooted at — <c>/address/town</c>
    /// and <c>address</c> both resolve to <c>address</c>, which is the identifier
    /// <see cref="CredentialCardViewModel.DisclosableClaims"/> is expressed in.
    /// </summary>
    private static string ClaimNameFromPointer(string pointer)
    {
        var path = pointer.StartsWith('/') ? pointer[1..] : pointer;
        var slash = path.IndexOf('/');
        return slash < 0 ? path : path[..slash];
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
    /// Summarises a nested object claim for a credential card as its VALUES, comma-joined —
    /// an <c>address</c> renders "10 High St, Falkirk, FK1 1AA". It used to render the field
    /// NAMES ("line1, town, postcode, country"), which read as placeholder text to the citizen
    /// (#1188). Values only, without the "Line1: " labels the detail view uses, because on a
    /// compact card an address reads naturally as a plain address.
    ///
    /// The blob-proofing this method was built for is preserved and in fact tightened: only
    /// SCALAR properties contribute. Anything array-valued is skipped rather than named, so an
    /// unresolved SD-JWT digest array cannot reach a card even under a key that escaped the
    /// <c>_</c>-prefix drop — which is how one reached a citizen's card on n1. Nested objects
    /// recurse through this same scalar-only path, so depth can never turn into raw JSON.
    /// An object with nothing scalar to say renders as an empty string, as before.
    /// </summary>
    private static string SummariseObject(JsonElement el)
    {
        var values = el.EnumerateObject()
            .Where(p => !p.Name.StartsWith('_'))
            .Select(p => p.Value.ValueKind switch
            {
                JsonValueKind.String => p.Value.GetString() ?? string.Empty,
                JsonValueKind.Number => p.Value.ToString(),
                JsonValueKind.True or JsonValueKind.False => p.Value.GetBoolean().ToString(),
                JsonValueKind.Object => SummariseObject(p.Value),
                _ => string.Empty
            })
            // Issue #1278: trim each leaf before joining. The reported
            // "6/2 Warrender Park Terrace , Edinburgh , Eh91ja, Gb" was NOT a " , " separator —
            // this join has always used ", ". The stray pre-comma spaces are trailing whitespace
            // the citizen typed into individual address fields, previously carried through verbatim.
            .Select(v => v.Trim())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .ToList();

        return values.Count == 0 ? string.Empty : string.Join(", ", values);
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
            // Issue #1278: prefer the authored name, then the org name, before falling back to
            // anything derived from an identifier. On an identity card, naming the issuer is the
            // primary job — a truncated wallet address does not do it.
            DisplayName = !string.IsNullOrWhiteSpace(displayConfig.CredentialName)
                ? displayConfig.CredentialName!
                : Humanise(entity.Type),
            IssuerDid = entity.IssuerDid ?? string.Empty,
            IssuerName = !string.IsNullOrWhiteSpace(entity.IssuerOrgName)
                ? entity.IssuerOrgName!
                : ExtractIssuerName(entity.IssuerDid),
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
    /// of <see cref="JsonValueKind.Object"/> that returns <c>GetRawText()</c>. Delegates to
    /// <see cref="CredentialClaimDisplayFormatter"/> — the same formatter <see cref="CredentialIdCard"/>
    /// uses for the ID-card face, so there is one safe-rendering implementation, not two.
    /// </summary>
    private static Dictionary<string, string> BuildDisplayClaims(IReadOnlyDictionary<string, object> claims)
        => CredentialClaimDisplayFormatter.BuildDisplayClaims(claims);

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

        /// <summary>The issuing organisation's human-readable name.</summary>
        /// <remarks>
        /// Issue #1278: this property was MISSING from the DTO even though the server has always
        /// sent it (the detail endpoint returns the whole CredentialEntity), so it was silently
        /// discarded on deserialisation. That is why the view dialog's id-card named the issuer
        /// with a truncated wallet address while the list card, one screen away, managed the real
        /// org name.
        /// </remarks>
        public string? IssuerOrgName { get; set; }
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

            // Do NOT return an empty list here. "The request failed" and "you hold no matching
            // credential" are different facts, and CredentialGatePanel renders an empty list as
            // "No matching credential — check your wallet". A 400 from a serialisation mismatch
            // therefore accused citizens of not owning credentials they demonstrably held, and
            // pointed them at their wallet instead of at the bug (n1, 2026-07-28). Throw so the
            // caller can show its error state.
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            _logger.LogError(
                "Credential match request failed for wallet {WalletAddress}: {StatusCode} {Body}",
                walletAddress, response.StatusCode, body);
            throw new HttpRequestException(
                $"Credential match failed with {(int)response.StatusCode} {response.StatusCode}.",
                inner: null,
                statusCode: response.StatusCode);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error matching credentials for wallet {WalletAddress}", walletAddress);
            throw;
        }
    }

    private class PresentationSubmitResponse
    {
        public string? Status { get; set; }
    }
}
