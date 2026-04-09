// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sorcha.Cryptography.SdJwt;
using Sorcha.ServiceClients.Did;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Service.Credentials;
using Sorcha.Wallet.Service.Models;

namespace Sorcha.Wallet.Service.Services;

/// <summary>
/// Manages OID4VP presentation requests — creation, matching, verification, and lifecycle.
/// </summary>
public interface IPresentationRequestService
{
    /// <summary>
    /// Creates a new presentation request.
    /// </summary>
    Task<PresentationRequest> CreateRequestAsync(CreatePresentationRequestDto dto, CancellationToken ct = default);

    /// <summary>
    /// Gets a presentation request by ID. Returns null if not found.
    /// </summary>
    Task<PresentationRequest?> GetRequestAsync(string requestId, CancellationToken ct = default);

    /// <summary>
    /// Finds credentials matching the request in the target wallet.
    /// </summary>
    Task<IReadOnlyList<MatchedCredentialInfo>> FindMatchingCredentialsAsync(
        PresentationRequest request, string walletAddress, CancellationToken ct = default);

    /// <summary>
    /// Submits a presentation for verification.
    /// </summary>
    Task<PresentationRequest> SubmitPresentationAsync(
        string requestId, string credentialId, string[] disclosedClaims, string vpToken,
        CancellationToken ct = default);

    /// <summary>
    /// Denies a presentation request.
    /// </summary>
    Task<PresentationRequest?> DenyRequestAsync(string requestId, CancellationToken ct = default);
}

/// <summary>
/// DTO for creating a presentation request.
/// </summary>
public class CreatePresentationRequestDto
{
    public required string CredentialType { get; init; }
    public string[]? AcceptedIssuers { get; init; }
    public ClaimConstraint[]? RequiredClaims { get; init; }
    public required string CallbackUrl { get; init; }
    public string? TargetWalletAddress { get; init; }
    public int TtlSeconds { get; init; } = 300;
    public string VerifierIdentity { get; init; } = "Unknown Verifier";
}

/// <summary>
/// Info about a credential that matches a presentation request.
/// </summary>
public class MatchedCredentialInfo
{
    public required string CredentialId { get; init; }
    public required string Type { get; init; }
    public required string IssuerDid { get; init; }
    public required string[] DisclosableClaims { get; init; }
    public required string[] RequestedClaims { get; init; }
}

public class PresentationRequestService : IPresentationRequestService
{
    private readonly ConcurrentDictionary<string, PresentationRequest> _requests = new();
    private readonly ICredentialStore _credentialStore;
    private readonly ISdJwtService? _sdJwtService;
    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IDidResolverRegistry? _didRegistry;
    private readonly IHttpClientFactory? _httpClientFactory;
    private readonly ILogger<PresentationRequestService> _logger;

    public PresentationRequestService(
        ICredentialStore credentialStore,
        ILogger<PresentationRequestService> logger,
        ISdJwtService? sdJwtService = null,
        IServiceScopeFactory? scopeFactory = null,
        IDidResolverRegistry? didRegistry = null,
        IHttpClientFactory? httpClientFactory = null)
    {
        _credentialStore = credentialStore;
        _logger = logger;
        _sdJwtService = sdJwtService;
        _scopeFactory = scopeFactory;
        _didRegistry = didRegistry;
        _httpClientFactory = httpClientFactory;
    }

    public Task<PresentationRequest> CreateRequestAsync(
        CreatePresentationRequestDto dto, CancellationToken ct = default)
    {
        var request = new PresentationRequest
        {
            VerifierIdentity = dto.VerifierIdentity,
            CredentialType = dto.CredentialType,
            AcceptedIssuers = dto.AcceptedIssuers,
            RequiredClaims = dto.RequiredClaims,
            CallbackUrl = dto.CallbackUrl,
            TargetWalletAddress = dto.TargetWalletAddress,
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(dto.TtlSeconds)
        };

        _requests[request.Id] = request;

        _logger.LogInformation(
            "Created presentation request {RequestId} for {CredentialType} from {Verifier}, expires {ExpiresAt}",
            request.Id, request.CredentialType, request.VerifierIdentity, request.ExpiresAt);

        return Task.FromResult(request);
    }

    public Task<PresentationRequest?> GetRequestAsync(string requestId, CancellationToken ct = default)
    {
        _requests.TryGetValue(requestId, out var request);

        if (request is { Status: PresentationStatus.Pending, IsExpired: true })
        {
            request.Status = PresentationStatus.Expired;
        }

        return Task.FromResult(request);
    }

    public async Task<IReadOnlyList<MatchedCredentialInfo>> FindMatchingCredentialsAsync(
        PresentationRequest request, string walletAddress, CancellationToken ct = default)
    {
        var credentials = await _credentialStore.MatchAsync(
            walletAddress,
            request.CredentialType,
            request.AcceptedIssuers,
            ct);

        var result = new List<MatchedCredentialInfo>();

        foreach (var cred in credentials)
        {
            var claims = ParseClaims(cred.ClaimsJson);
            if (claims == null) continue;

            var disclosable = claims.Keys.ToArray();
            var requested = request.RequiredClaims?
                .Select(c => c.ClaimName)
                .Where(n => claims.ContainsKey(n))
                .ToArray() ?? [];

            result.Add(new MatchedCredentialInfo
            {
                CredentialId = cred.Id,
                Type = cred.Type,
                IssuerDid = cred.IssuerDid,
                DisclosableClaims = disclosable,
                RequestedClaims = requested
            });
        }

        return result;
    }

    public async Task<PresentationRequest> SubmitPresentationAsync(
        string requestId, string credentialId, string[] disclosedClaims, string vpToken,
        CancellationToken ct = default)
    {
        if (!_requests.TryGetValue(requestId, out var request))
            throw new KeyNotFoundException($"Presentation request '{requestId}' not found");

        if (request.IsExpired)
        {
            request.Status = PresentationStatus.Expired;
            throw new InvalidOperationException("Presentation request has expired");
        }

        if (request.Status != PresentationStatus.Pending)
            throw new InvalidOperationException($"Request is in '{request.Status}' state, expected Pending");

        request.VpToken = vpToken;
        request.Status = PresentationStatus.Submitted;

        // Verify the presentation
        var verification = await VerifyPresentationAsync(request, credentialId, ct);
        request.VerificationResult = JsonSerializer.Serialize(verification);

        if (verification.IsValid)
        {
            request.Status = PresentationStatus.Verified;

            // Record usage against credential
            await _credentialStore.RecordPresentationAsync(credentialId, ct);

            _logger.LogInformation(
                "Presentation request {RequestId} verified successfully for credential {CredentialId}",
                requestId, credentialId);
        }
        else
        {
            request.Status = PresentationStatus.Denied;

            _logger.LogWarning(
                "Presentation request {RequestId} verification failed for credential {CredentialId}: {Errors}",
                requestId, credentialId,
                string.Join(", ", verification.Errors?.Select(e => e.Message) ?? []));
        }

        return request;
    }

    public Task<PresentationRequest?> DenyRequestAsync(string requestId, CancellationToken ct = default)
    {
        if (!_requests.TryGetValue(requestId, out var request))
            return Task.FromResult<PresentationRequest?>(null);

        if (request.Status == PresentationStatus.Pending)
        {
            request.Status = PresentationStatus.Denied;
            _logger.LogInformation("Presentation request {RequestId} denied by holder", requestId);
        }

        return Task.FromResult<PresentationRequest?>(request);
    }

    private async Task<VerificationResult> VerifyPresentationAsync(
        PresentationRequest request, string credentialId, CancellationToken ct)
    {
        var errors = new List<VerificationError>();

        // 1. Look up credential
        var credential = await _credentialStore.GetByIdAsync(credentialId, ct);
        if (credential == null)
        {
            errors.Add(new VerificationError
            {
                RequirementType = request.CredentialType,
                FailureReason = "NotFound",
                Message = "Credential not found"
            });
            return new VerificationResult { IsValid = false, Errors = errors };
        }

        // 2. Cryptographic signature verification of the presented vpToken (Feature 093 FR-001 to FR-005).
        //    Claim values from this point on MUST come from the verified token, not from the
        //    server-side credential row. If the SdJwt service or the wallet repository is not
        //    wired into this service instance (legacy constructor path used by some unit tests),
        //    we fall back to the pre-fix behaviour with a warning. Production DI always wires both.
        Dictionary<string, object>? verifiedTokenClaims = null;
        if (_sdJwtService != null && _scopeFactory != null && !string.IsNullOrEmpty(request.VpToken))
        {
            var verifyOutcome = await VerifyVpTokenSignatureAsync(
                request.VpToken, credential, ct);

            if (!verifyOutcome.Success)
            {
                errors.Add(new VerificationError
                {
                    RequirementType = request.CredentialType,
                    FailureReason = verifyOutcome.FailureReason ?? "SignatureInvalid",
                    Message = verifyOutcome.Message ?? "vpToken signature verification failed"
                });

                _logger.LogWarning(
                    "Presentation verification failed for credential {CredentialId}: {FailureReason} — {Message}",
                    credentialId, verifyOutcome.FailureReason, verifyOutcome.Message);

                return new VerificationResult
                {
                    IsValid = false,
                    Errors = errors,
                    CredentialType = credential.Type,
                    IssuerDid = credential.IssuerDid,
                    StatusListCheck = "NotChecked"
                };
            }

            verifiedTokenClaims = verifyOutcome.VerifiedClaims;

            // Cross-check the verified token's iss claim against the stored credential's IssuerDid.
            // Feature 093 FR-004: the token's iss and the credential row must agree.
            if (!string.IsNullOrEmpty(verifyOutcome.VerifiedIssuer)
                && !IssuerIdentifiersMatch(verifyOutcome.VerifiedIssuer, credential.IssuerDid))
            {
                errors.Add(new VerificationError
                {
                    RequirementType = request.CredentialType,
                    FailureReason = "IssuerMismatch",
                    Message = $"Token iss '{verifyOutcome.VerifiedIssuer}' does not match credential issuer '{credential.IssuerDid}'"
                });

                _logger.LogWarning(
                    "Presentation issuer mismatch for credential {CredentialId}: token iss='{TokenIss}' credential iss='{CredentialIss}'",
                    credentialId, verifyOutcome.VerifiedIssuer, credential.IssuerDid);

                return new VerificationResult
                {
                    IsValid = false,
                    Errors = errors,
                    CredentialType = credential.Type,
                    IssuerDid = credential.IssuerDid,
                    StatusListCheck = "NotChecked"
                };
            }
        }
        else if (!string.IsNullOrEmpty(request.VpToken))
        {
            // Legacy fallback path — logged so operators can see when production is missing the wiring.
            _logger.LogWarning(
                "Presentation verification running in legacy no-signature mode for credential {CredentialId}. " +
                "ISdJwtService or IServiceScopeFactory not injected — production DI must wire both.",
                credentialId);
        }

        // 3. Type match
        if (!string.Equals(credential.Type, request.CredentialType, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(new VerificationError
            {
                RequirementType = request.CredentialType,
                FailureReason = "TypeMismatch",
                Message = $"Expected '{request.CredentialType}', got '{credential.Type}'"
            });
        }

        // 4. Issuer check
        if (request.AcceptedIssuers is { Length: > 0 } &&
            !request.AcceptedIssuers.Contains(credential.IssuerDid))
        {
            errors.Add(new VerificationError
            {
                RequirementType = request.CredentialType,
                FailureReason = "UntrustedIssuer",
                Message = $"Issuer '{credential.IssuerDid}' not in accepted issuers list"
            });
        }

        // 5. Status check (active)
        if (credential.Status != "Active")
        {
            errors.Add(new VerificationError
            {
                RequirementType = request.CredentialType,
                FailureReason = credential.Status,
                Message = $"Credential has been {credential.Status.ToLowerInvariant()} by issuer"
            });
        }

        // 6. Expiry check
        if (credential.ExpiresAt.HasValue && credential.ExpiresAt.Value < DateTimeOffset.UtcNow)
        {
            errors.Add(new VerificationError
            {
                RequirementType = request.CredentialType,
                FailureReason = "Expired",
                Message = "Credential has expired"
            });
        }

        // 7. Status list check.
        //    Feature 093 US2 FR-011: prefer the credentialStatus claim embedded in the
        //    verified token over the server-side CredentialEntity row. The row is only
        //    consulted as a fallback for pre-fix credentials (FR-010) that have no
        //    embedded pointer in their signed payload.
        var statusListCheck = "NotConfigured";
        var (embeddedStatusUrl, embeddedStatusIndex) =
            TryExtractEmbeddedCredentialStatus(verifiedTokenClaims);

        var statusUrl = embeddedStatusUrl ?? credential.StatusListUrl;
        var statusIndex = embeddedStatusIndex ?? credential.StatusListIndex;

        if (!string.IsNullOrEmpty(statusUrl) && statusIndex.HasValue)
        {
            statusListCheck = await CheckStatusListAsync(statusUrl, statusIndex.Value, ct);

            if (statusListCheck == "Revoked" || statusListCheck == "Suspended")
            {
                errors.Add(new VerificationError
                {
                    RequirementType = request.CredentialType,
                    FailureReason = statusListCheck,
                    Message = $"Credential status list reports: {statusListCheck}"
                });
            }
        }
        else
        {
            statusListCheck = "Active";
        }

        // 8. Required claims verification.
        //    Feature 093 FR-003: claim values come from the verified token when available;
        //    otherwise (legacy fallback path only) they come from the server-side credential row.
        var claimsSource = verifiedTokenClaims ?? ParseClaims(credential.ClaimsJson);
        var verifiedClaims = new Dictionary<string, object>();

        if (request.RequiredClaims is { Length: > 0 })
        {
            if (claimsSource != null)
            {
                foreach (var constraint in request.RequiredClaims)
                {
                    if (!claimsSource.TryGetValue(constraint.ClaimName, out var value))
                    {
                        errors.Add(new VerificationError
                        {
                            RequirementType = request.CredentialType,
                            FailureReason = "MissingClaim",
                            Message = $"Required claim '{constraint.ClaimName}' not present"
                        });
                        continue;
                    }

                    if (constraint.ExpectedValue != null)
                    {
                        var actualStr = value?.ToString();
                        if (!string.Equals(constraint.ExpectedValue, actualStr, StringComparison.Ordinal))
                        {
                            errors.Add(new VerificationError
                            {
                                RequirementType = request.CredentialType,
                                FailureReason = "ClaimValueMismatch",
                                Message = $"Claim '{constraint.ClaimName}' expected '{constraint.ExpectedValue}', got '{actualStr}'"
                            });
                            continue;
                        }
                    }

                    verifiedClaims[constraint.ClaimName] = value!;
                }
            }
        }
        else
        {
            // No required claims — include all claims from the chosen source
            if (claimsSource != null)
            {
                foreach (var kvp in claimsSource)
                    verifiedClaims[kvp.Key] = kvp.Value;
            }
        }

        if (errors.Count > 0)
        {
            return new VerificationResult
            {
                IsValid = false,
                Errors = errors,
                CredentialType = credential.Type,
                IssuerDid = credential.IssuerDid,
                StatusListCheck = statusListCheck
            };
        }

        return new VerificationResult
        {
            IsValid = true,
            VerifiedClaims = verifiedClaims,
            CredentialType = credential.Type,
            IssuerDid = credential.IssuerDid,
            StatusListCheck = statusListCheck
        };
    }

    /// <summary>
    /// Resolves the issuer public key via <see cref="IWalletRepository"/> and calls
    /// <see cref="ISdJwtService.VerifyPresentationAsync"/> on the submitted vpToken.
    /// Feature 093 FR-001 to FR-005.
    /// </summary>
    private async Task<VpTokenVerifyOutcome> VerifyVpTokenSignatureAsync(
        string vpToken,
        Sorcha.Wallet.Core.Domain.Entities.CredentialEntity credential,
        CancellationToken ct)
    {
        // Extract the issuer wallet address from the credential's stored IssuerDid.
        var issuerAddress = ExtractWalletAddress(credential.IssuerDid);
        if (string.IsNullOrEmpty(issuerAddress))
        {
            return VpTokenVerifyOutcome.Fail(
                "IssuerUnresolvable",
                $"Cannot extract a wallet address from issuer identifier '{credential.IssuerDid}'");
        }

        // Resolve IWalletRepository (Scoped) from a temporary scope since this service is Singleton.
        using var scope = _scopeFactory!.CreateScope();
        var walletRepository = scope.ServiceProvider.GetRequiredService<IWalletRepository>();

        var wallet = await walletRepository.GetByAddressAsync(issuerAddress, cancellationToken: ct);
        if (wallet == null || string.IsNullOrEmpty(wallet.PublicKey))
        {
            return VpTokenVerifyOutcome.Fail(
                "IssuerNotFound",
                $"Issuer wallet '{issuerAddress}' not found or has no public key");
        }

        byte[] publicKeyBytes;
        try
        {
            publicKeyBytes = Convert.FromBase64String(wallet.PublicKey);
        }
        catch (FormatException)
        {
            return VpTokenVerifyOutcome.Fail(
                "IssuerKeyFormat",
                "Issuer public key is not in a recognised base64 format");
        }

        SdJwtVerificationResult verification;
        try
        {
            verification = await _sdJwtService!.VerifyPresentationAsync(
                vpToken, publicKeyBytes, wallet.Algorithm, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "SdJwtService.VerifyPresentationAsync threw for credential {CredentialId}",
                credential.Id);
            return VpTokenVerifyOutcome.Fail(
                "SignatureInvalid",
                $"Presentation token verification threw: {ex.Message}");
        }

        if (!verification.IsValid)
        {
            var errorMessage = verification.Errors.Count > 0
                ? string.Join("; ", verification.Errors)
                : "vpToken failed SD-JWT verification";

            var failureReason = errorMessage.Contains("disclosure", StringComparison.OrdinalIgnoreCase)
                ? "DisclosureIntegrityFailure"
                : "SignatureInvalid";

            return VpTokenVerifyOutcome.Fail(failureReason, errorMessage);
        }

        return VpTokenVerifyOutcome.Ok(verification.Claims, verification.Issuer);
    }

    /// <summary>
    /// Normalises a stored IssuerDid value (either a bare wallet address or a
    /// did:sorcha:w|org: URI) to a raw wallet address suitable for repository lookup.
    /// </summary>
    private static string ExtractWalletAddress(string issuerDid)
    {
        if (string.IsNullOrEmpty(issuerDid)) return string.Empty;

        const string walletPrefix = "did:sorcha:w:";
        const string orgPrefix = "did:sorcha:org:";

        if (issuerDid.StartsWith(walletPrefix, StringComparison.OrdinalIgnoreCase))
            return issuerDid[walletPrefix.Length..];
        if (issuerDid.StartsWith(orgPrefix, StringComparison.OrdinalIgnoreCase))
            return issuerDid[orgPrefix.Length..];

        return issuerDid;
    }

    /// <summary>
    /// Compares a verified token iss claim against a stored credential IssuerDid, allowing
    /// for the two common shapes (bare wallet address, did:sorcha:w|org URI) to match.
    /// </summary>
    private static bool IssuerIdentifiersMatch(string tokenIss, string credentialIss)
    {
        if (string.Equals(tokenIss, credentialIss, StringComparison.OrdinalIgnoreCase))
            return true;

        var tokenAddress = ExtractWalletAddress(tokenIss);
        var credentialAddress = ExtractWalletAddress(credentialIss);

        return !string.IsNullOrEmpty(tokenAddress)
            && string.Equals(tokenAddress, credentialAddress, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Attempts to extract a W3C BitstringStatusListEntry credentialStatus claim from a
    /// verified token's claims dictionary. Feature 093 US2 FR-011 — when present, this
    /// pointer takes precedence over the server-side CredentialEntity row.
    /// </summary>
    private static (string? Url, int? Index) TryExtractEmbeddedCredentialStatus(
        Dictionary<string, object>? verifiedClaims)
    {
        if (verifiedClaims == null) return (null, null);
        if (!verifiedClaims.TryGetValue("credentialStatus", out var raw) || raw is null)
            return (null, null);

        string? url = null;
        string? indexStr = null;

        switch (raw)
        {
            case JsonElement je when je.ValueKind == JsonValueKind.Object:
                if (je.TryGetProperty("statusListCredential", out var urlProp))
                    url = urlProp.GetString();
                if (je.TryGetProperty("statusListIndex", out var indexProp))
                    indexStr = indexProp.ValueKind == JsonValueKind.Number
                        ? indexProp.GetInt32().ToString()
                        : indexProp.GetString();
                break;

            case Dictionary<string, object> dict:
                if (dict.TryGetValue("statusListCredential", out var urlObj))
                    url = urlObj?.ToString();
                if (dict.TryGetValue("statusListIndex", out var indexObj))
                    indexStr = indexObj?.ToString();
                break;
        }

        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(indexStr))
            return (null, null);

        return int.TryParse(indexStr, out var index)
            ? (url, index)
            : (null, null);
    }

    /// <summary>
    /// Internal result type for the vpToken signature verification step.
    /// </summary>
    private sealed class VpTokenVerifyOutcome
    {
        public bool Success { get; private init; }
        public string? FailureReason { get; private init; }
        public string? Message { get; private init; }
        public Dictionary<string, object>? VerifiedClaims { get; private init; }
        public string? VerifiedIssuer { get; private init; }

        public static VpTokenVerifyOutcome Ok(
            Dictionary<string, object> claims, string? issuer) =>
            new() { Success = true, VerifiedClaims = claims, VerifiedIssuer = issuer };

        public static VpTokenVerifyOutcome Fail(string reason, string message) =>
            new() { Success = false, FailureReason = reason, Message = message };
    }

    private async Task<string> CheckStatusListAsync(
        string statusListUrl, int index, CancellationToken ct)
    {
        try
        {
            if (_httpClientFactory == null) return "Active";

            var client = _httpClientFactory.CreateClient();
            var response = await client.GetFromJsonAsync<JsonDocument>(statusListUrl, ct);
            if (response == null) return "Unknown";

            // Navigate W3C BitstringStatusListCredential envelope
            var root = response.RootElement;
            if (root.TryGetProperty("credentialSubject", out var subject) &&
                subject.TryGetProperty("encodedList", out var encodedList))
            {
                var encoded = encodedList.GetString();
                if (!string.IsNullOrEmpty(encoded))
                {
                    var compressed = Convert.FromBase64String(encoded);
                    using var ms = new MemoryStream(compressed);
                    using var gzip = new System.IO.Compression.GZipStream(ms, System.IO.Compression.CompressionMode.Decompress);
                    using var output = new MemoryStream();
                    await gzip.CopyToAsync(output, ct);
                    var bytes = output.ToArray();

                    var byteIndex = index / 8;
                    var bitIndex = 7 - (index % 8); // MSB-first

                    if (byteIndex < bytes.Length)
                    {
                        var isSet = (bytes[byteIndex] & (1 << bitIndex)) != 0;
                        return isSet ? "Revoked" : "Active";
                    }
                }
            }

            return "Active";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check status list at {Url}", statusListUrl);
            return "Unknown";
        }
    }

    private static Dictionary<string, object>? ParseClaims(string claimsJson)
    {
        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object>>(claimsJson);
        }
        catch
        {
            return null;
        }
    }
}
