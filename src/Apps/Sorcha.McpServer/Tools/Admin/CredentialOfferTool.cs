// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Haip;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Administrator/issuer tool that creates an OID4VCI credential offer for an external HAIP
/// wallet, or reads back the status of an existing offer. Routes through the typed
/// <see cref="IHaipServiceClient"/> (<see cref="IHaipServiceClient.CreateCredentialOfferAsync"/>
/// + <see cref="IHaipServiceClient.GetOfferStatusAsync"/>).
/// </summary>
[McpServerToolType]
public sealed class CredentialOfferTool
{
    private const string ToolName = "sorcha_credential_offer";
    private const string ServiceName = "Blueprint";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHaipServiceClient _haipClient;
    private readonly ILogger<CredentialOfferTool> _logger;

    public CredentialOfferTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IHaipServiceClient haipClient,
        ILogger<CredentialOfferTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _haipClient = haipClient;
        _logger = logger;
    }

    /// <summary>
    /// Creates an OID4VCI credential offer, or — when <paramref name="offerId"/> is supplied —
    /// reads the status of an existing offer.
    /// </summary>
    /// <param name="issuerWalletAddress">Wallet address of the issuing authority (required to create).</param>
    /// <param name="tenantId">Tenant under which the offer is created (required to create).</param>
    /// <param name="credentialType">The credential type to issue (required to create).</param>
    /// <param name="claimsJson">JSON object of claim name/value pairs to embed in the credential.</param>
    /// <param name="disclosablePathsJson">Optional JSON array of selectively-disclosable claim paths.</param>
    /// <param name="offerId">When supplied, returns the status of this existing offer instead of creating one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created offer (id + QR URI + pre-authorized code + expiry) or the polled offer status.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Creates an OpenID4VCI (OID4VCI) credential offer so an external HAIP wallet can collect a verifiable credential by scanning the returned openid-credential-offer:// URI, or — when offerId is supplied — polls the status of an existing offer (pending / claimed / expired). Call this without offerId to push a credential to a citizen's standards-compliant wallet (it returns the offer id, the QR URI, a pre-authorized code, and an expiry), and call it again with that offerId to check whether the wallet has collected it. Use this for HAIP/OID4VCI interoperable issuance; use the credential-lifecycle tools (sorcha_credential_revoke / _suspend / _reinstate / _refresh) instead when managing a credential that has already been issued, and use sorcha_presentation_request when verifying rather than issuing.")]
    public async Task<CredentialOfferToolResult> CreateOrStatusAsync(
        [Description("Wallet address of the issuing authority (required when creating an offer)")] string? issuerWalletAddress = null,
        [Description("Tenant ID under which the offer is created (required when creating an offer)")] string? tenantId = null,
        [Description("The credential type to issue, e.g. 'AssuredIdentityCredential' (required when creating an offer)")] string? credentialType = null,
        [Description("JSON object of claim name/value pairs to embed in the credential")] string? claimsJson = null,
        [Description("Optional JSON array of selectively-disclosable claim paths")] string? disclosablePathsJson = null,
        [Description("When supplied, returns the status of this existing offer instead of creating a new one")] string? offerId = null,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return Denied();
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return Unavailable();
        }

        return string.IsNullOrWhiteSpace(offerId)
            ? await CreateOfferAsync(issuerWalletAddress, tenantId, credentialType, claimsJson, disclosablePathsJson, cancellationToken)
            : await GetStatusAsync(offerId, cancellationToken);
    }

    private async Task<CredentialOfferToolResult> CreateOfferAsync(
        string? issuerWalletAddress,
        string? tenantId,
        string? credentialType,
        string? claimsJson,
        string? disclosablePathsJson,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(issuerWalletAddress) ||
            string.IsNullOrWhiteSpace(tenantId) ||
            string.IsNullOrWhiteSpace(credentialType))
        {
            return new CredentialOfferToolResult
            {
                Status = "Error",
                Message = "issuerWalletAddress, tenantId, and credentialType are required to create an offer.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        Dictionary<string, object> claims;
        List<string>? disclosablePaths;
        try
        {
            claims = JsonSerializer.Deserialize<Dictionary<string, object>>(
                string.IsNullOrWhiteSpace(claimsJson) ? "{}" : claimsJson) ?? new();
            disclosablePaths = string.IsNullOrWhiteSpace(disclosablePathsJson)
                ? null
                : JsonSerializer.Deserialize<List<string>>(disclosablePathsJson);
        }
        catch (JsonException ex)
        {
            return new CredentialOfferToolResult
            {
                Status = "Error",
                Message = $"Invalid JSON for claims or disclosablePaths: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Creating credential offer ({CredentialType}) for issuer {Issuer}", credentialType, issuerWalletAddress);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var offer = await _haipClient.CreateCredentialOfferAsync(
                issuerWalletAddress, tenantId, credentialType, claims, disclosablePaths, cancellationToken);
            stopwatch.Stop();
            _availabilityTracker.RecordSuccess(ServiceName);

            return new CredentialOfferToolResult
            {
                Status = "Success",
                Message = $"Credential offer '{offer.OfferId}' created (expires {offer.ExpiresAt:u}).",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                OfferId = offer.OfferId,
                CredentialOfferUri = offer.CredentialOfferUri,
                PreAuthorizedCode = offer.PreAuthorizedCode,
                ExpiresAt = offer.ExpiresAt
            };
        }
        catch (TaskCanceledException)
        {
            return Timeout(stopwatch);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName, ex);
            _logger.LogError(ex, "Failed to create credential offer for issuer {Issuer}", issuerWalletAddress);
            return new CredentialOfferToolResult
            {
                Status = "Error",
                Message = $"Failed to create credential offer: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    private async Task<CredentialOfferToolResult> GetStatusAsync(string offerId, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(offerId, out var offerGuid))
        {
            return new CredentialOfferToolResult
            {
                Status = "Error",
                Message = "offerId must be a valid GUID.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var status = await _haipClient.GetOfferStatusAsync(offerGuid, cancellationToken);
            stopwatch.Stop();
            _availabilityTracker.RecordSuccess(ServiceName);

            if (status is null)
            {
                return new CredentialOfferToolResult
                {
                    Status = "NotFound",
                    Message = $"Credential offer '{offerId}' was not found.",
                    CheckedAt = DateTimeOffset.UtcNow,
                    ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
                };
            }

            return new CredentialOfferToolResult
            {
                Status = "Success",
                Message = $"Credential offer '{offerId}' is {status.Status}.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                OfferId = status.OfferId,
                OfferStatus = status.Status,
                ExpiresAt = status.ExpiresAt
            };
        }
        catch (TaskCanceledException)
        {
            return Timeout(stopwatch);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName, ex);
            _logger.LogError(ex, "Failed to get credential offer status for {OfferId}", offerId);
            return new CredentialOfferToolResult
            {
                Status = "Error",
                Message = $"Failed to get credential offer status: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    private static CredentialOfferToolResult Denied() => new()
    {
        Status = "Unauthorized",
        Message = "Access denied. This tool requires the sorcha:admin role.",
        CheckedAt = DateTimeOffset.UtcNow
    };

    private static CredentialOfferToolResult Unavailable() => new()
    {
        Status = "Unavailable",
        Message = "HAIP/credential service is currently unavailable. Please try again later.",
        CheckedAt = DateTimeOffset.UtcNow
    };

    private CredentialOfferToolResult Timeout(Stopwatch stopwatch)
    {
        stopwatch.Stop();
        _availabilityTracker.RecordFailure(ServiceName);
        return new CredentialOfferToolResult
        {
            Status = "Timeout",
            Message = "Request to the HAIP/credential service timed out.",
            CheckedAt = DateTimeOffset.UtcNow,
            ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
        };
    }
}

/// <summary>
/// Result of a credential-offer create or status query.
/// </summary>
public sealed record CredentialOfferToolResult
{
    /// <summary>Status: Success, NotFound, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the request was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The credential offer ID (on success).</summary>
    public Guid? OfferId { get; init; }

    /// <summary>The openid-credential-offer:// URI for QR rendering (on create).</summary>
    public string? CredentialOfferUri { get; init; }

    /// <summary>The pre-authorized code for the OID4VCI flow (on create).</summary>
    public string? PreAuthorizedCode { get; init; }

    /// <summary>The offer's current status string (on status query).</summary>
    public string? OfferStatus { get; init; }

    /// <summary>When the offer expires.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}
