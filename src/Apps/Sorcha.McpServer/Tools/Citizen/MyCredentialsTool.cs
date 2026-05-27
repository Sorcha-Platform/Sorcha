// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.CitizenWallet;

namespace Sorcha.McpServer.Tools.Citizen;

/// <summary>
/// Consumer-tier citizen self-service tool (Feature 140 Wave 3). Lists the calling
/// citizen's own verifiable credentials via the typed <see cref="ICitizenWalletClient"/>
/// (the caller's bearer is forwarded, so the listing is scoped to the caller by the platform).
/// </summary>
[McpServerToolType]
public sealed class MyCredentialsTool
{
    private const string ToolName = "sorcha_my_credentials";
    private const string ServiceName = "Wallet";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly ICitizenWalletClient _walletClient;
    private readonly ILogger<MyCredentialsTool> _logger;

    public MyCredentialsTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        ICitizenWalletClient walletClient,
        ILogger<MyCredentialsTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _walletClient = walletClient;
        _logger = logger;
    }

    /// <summary>
    /// Lists the calling citizen's own verifiable credentials.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The citizen's credential snapshot.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("List the verifiable credentials held by the calling citizen's own wallet — credential id, type (VCT), issuer DID, issuance and expiry times, and status-list pointer for each. The set is always scoped to the caller by the platform from the forwarded token; there is no parameter to read another person's wallet. Call this when an agent needs to know what credentials the citizen already holds (for example before deciding whether to start an application or present an existing credential); prefer this over sorcha_my_presentations, which returns past presentation events rather than the credentials themselves.")]
    public async Task<MyCredentialsResult> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new MyCredentialsResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires a consumer-tier (citizen) token.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new MyCredentialsResult
            {
                Status = "Unavailable",
                Message = "Wallet service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Listing the calling citizen's credentials");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var response = await _walletClient.ListCredentialsAsync(cancellationToken);
            stopwatch.Stop();
            _availabilityTracker.RecordSuccess(ServiceName);

            var credentials = response.Credentials
                .Select(c => new MyCredentialSummary
                {
                    Id = c.Id,
                    Vct = c.Vct,
                    IssuerDid = c.IssuerDid,
                    IssuedAt = c.IssuedAt,
                    ExpiresAt = c.ExpiresAt,
                    StatusListUri = c.StatusListUri
                })
                .ToList();

            return new MyCredentialsResult
            {
                Status = "Success",
                Message = $"Retrieved {credentials.Count} credential(s).",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Credentials = credentials
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            return new MyCredentialsResult
            {
                Status = "Timeout",
                Message = "Request to wallet service timed out.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName, ex);
            _logger.LogError(ex, "Failed to list the citizen's credentials");
            return new MyCredentialsResult
            {
                Status = "Error",
                Message = $"Failed to list credentials: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>Result of listing the calling citizen's credentials.</summary>
public sealed record MyCredentialsResult
{
    /// <summary>Status: Success, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the request was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The citizen's credentials (on success).</summary>
    public IReadOnlyList<MyCredentialSummary> Credentials { get; init; } = [];
}

/// <summary>Public summary of one of the citizen's credentials. No JWT / claim material is exposed.</summary>
public sealed record MyCredentialSummary
{
    /// <summary>Server-assigned credential identifier.</summary>
    public required string Id { get; init; }

    /// <summary>Verifiable Credential Type URI.</summary>
    public required string Vct { get; init; }

    /// <summary>Issuer DID.</summary>
    public required string IssuerDid { get; init; }

    /// <summary>UTC issuance time.</summary>
    public DateTimeOffset IssuedAt { get; init; }

    /// <summary>UTC expiry, if any.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }

    /// <summary>Status list URI for revocation, if revocable.</summary>
    public string? StatusListUri { get; init; }
}
