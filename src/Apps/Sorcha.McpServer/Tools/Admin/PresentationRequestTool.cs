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
/// Administrator/verifier tool that creates an OID4VP presentation request for verification by
/// an external HAIP wallet. Routes through the typed <see cref="IHaipServiceClient"/>.
/// </summary>
[McpServerToolType]
public sealed class PresentationRequestTool
{
    private const string ToolName = "sorcha_presentation_request";
    private const string ServiceName = "Blueprint";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IHaipServiceClient _haipClient;
    private readonly ILogger<PresentationRequestTool> _logger;

    public PresentationRequestTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IHaipServiceClient haipClient,
        ILogger<PresentationRequestTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _haipClient = haipClient;
        _logger = logger;
    }

    /// <summary>
    /// Creates an OID4VP presentation request for verification from an external HAIP wallet.
    /// </summary>
    /// <param name="credentialType">The credential type to request a presentation of.</param>
    /// <param name="requiredClaimsJson">Optional JSON array of claim names the wallet must disclose.</param>
    /// <param name="acceptedIssuersJson">Optional JSON array of issuer DIDs whose credentials are accepted.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The request ID, openid4vp:// authorization URI for QR rendering, nonce, and expiry.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Creates an OpenID4VP (OID4VP) presentation request so an external HAIP wallet can present a verifiable credential by scanning the returned openid4vp:// authorization URI, optionally constraining which claims must be disclosed and which issuers are accepted. Call this when an operator or relying party needs to verify a citizen's credential (proof of identity, eligibility, etc.) — it returns the request id, the QR URI, a nonce, and an expiry. Use this to start a verification (presentation) exchange; use sorcha_presentation_status afterwards to poll the request to its terminal outcome, and use sorcha_credential_offer instead when issuing a credential rather than verifying one.")]
    public async Task<PresentationRequestToolResult> CreateRequestAsync(
        [Description("The credential type to request a presentation of (e.g. 'AssuredIdentityCredential')")] string credentialType,
        [Description("Optional JSON array of claim names the wallet must disclose")] string? requiredClaimsJson = null,
        [Description("Optional JSON array of issuer DIDs whose credentials are accepted")] string? acceptedIssuersJson = null,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new PresentationRequestToolResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires the sorcha:admin role.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (string.IsNullOrWhiteSpace(credentialType))
        {
            return new PresentationRequestToolResult
            {
                Status = "Error",
                Message = "credentialType is required.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        List<string>? requiredClaims;
        List<string>? acceptedIssuers;
        try
        {
            requiredClaims = string.IsNullOrWhiteSpace(requiredClaimsJson)
                ? null
                : JsonSerializer.Deserialize<List<string>>(requiredClaimsJson);
            acceptedIssuers = string.IsNullOrWhiteSpace(acceptedIssuersJson)
                ? null
                : JsonSerializer.Deserialize<List<string>>(acceptedIssuersJson);
        }
        catch (JsonException ex)
        {
            return new PresentationRequestToolResult
            {
                Status = "Error",
                Message = $"Invalid JSON for requiredClaims or acceptedIssuers: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new PresentationRequestToolResult
            {
                Status = "Unavailable",
                Message = "HAIP/credential service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Creating presentation request for credential type {CredentialType}", credentialType);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var request = await _haipClient.CreatePresentationRequestAsync(
                credentialType, requiredClaims, acceptedIssuers, ct: cancellationToken);
            stopwatch.Stop();
            _availabilityTracker.RecordSuccess(ServiceName);

            return new PresentationRequestToolResult
            {
                Status = "Success",
                Message = $"Presentation request '{request.RequestId}' created (expires {request.ExpiresAt:u}).",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                RequestId = request.RequestId,
                AuthorizationRequestUri = request.AuthorizationRequestUri,
                RequestUri = request.RequestUri,
                Nonce = request.Nonce,
                ExpiresAt = request.ExpiresAt
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            _logger.LogWarning("Presentation-request creation timed out for credential type {CredentialType}", credentialType);
            return new PresentationRequestToolResult
            {
                Status = "Timeout",
                Message = "Request to the HAIP/credential service timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName, ex);
            _logger.LogError(ex, "Failed to create presentation request for credential type {CredentialType}", credentialType);
            return new PresentationRequestToolResult
            {
                Status = "Error",
                Message = $"Failed to create presentation request: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>
/// Result of a presentation-request creation.
/// </summary>
public sealed record PresentationRequestToolResult
{
    /// <summary>Status: Success, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the request was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The presentation request ID (on success).</summary>
    public Guid? RequestId { get; init; }

    /// <summary>The openid4vp:// authorization request URI for QR rendering (on success).</summary>
    public string? AuthorizationRequestUri { get; init; }

    /// <summary>The request URI (on success).</summary>
    public string? RequestUri { get; init; }

    /// <summary>The nonce binding the presentation to this request (on success).</summary>
    public string? Nonce { get; init; }

    /// <summary>When the request expires.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
}
