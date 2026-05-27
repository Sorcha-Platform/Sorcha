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
/// citizen's own cross-device presentation history via the typed
/// <see cref="ICitizenWalletClient"/>.
/// </summary>
[McpServerToolType]
public sealed class MyPresentationsTool
{
    private const string ToolName = "sorcha_my_presentations";
    private const string ServiceName = "Wallet";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly ICitizenWalletClient _walletClient;
    private readonly ILogger<MyPresentationsTool> _logger;

    public MyPresentationsTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        ICitizenWalletClient walletClient,
        ILogger<MyPresentationsTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _walletClient = walletClient;
        _logger = logger;
    }

    /// <summary>
    /// Lists the calling citizen's own cross-device presentation history.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The citizen's presentation history, newest-first.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("List the calling citizen's own cross-device presentation history — each entry records which credential was presented, the verifier (DID and untrusted display label), the claims disclosed, the time, and the outcome (presented / declined / verifier-rejected / acknowledged). The history is server-authoritative and scoped to the caller by the platform from the forwarded token. Call this when an agent needs to answer 'where have I used my credentials and what did I disclose'; use sorcha_my_credentials instead to list the credentials the citizen currently holds rather than past presentation events.")]
    public async Task<MyPresentationsResult> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return new MyPresentationsResult
            {
                Status = "Unauthorized",
                Message = "Access denied. This tool requires a consumer-tier (citizen) token.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return new MyPresentationsResult
            {
                Status = "Unavailable",
                Message = "Wallet service is currently unavailable. Please try again later.",
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        _logger.LogInformation("Listing the calling citizen's presentation history");

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var entries = await _walletClient.ListPresentationsAsync(cancellationToken);
            stopwatch.Stop();
            _availabilityTracker.RecordSuccess(ServiceName);

            var presentations = entries
                .Select(e => new MyPresentationSummary
                {
                    Id = e.Id,
                    CredentialId = e.CredentialId,
                    VerifierDid = e.VerifierDid,
                    VerifierLabel = e.VerifierLabel,
                    DisclosedClaims = e.DisclosedClaims,
                    PresentedAt = e.PresentedAt,
                    Outcome = e.Outcome.ToString()
                })
                .ToList();

            return new MyPresentationsResult
            {
                Status = "Success",
                Message = $"Retrieved {presentations.Count} presentation(s).",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Presentations = presentations
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            return new MyPresentationsResult
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
            _logger.LogError(ex, "Failed to list the citizen's presentation history");
            return new MyPresentationsResult
            {
                Status = "Error",
                Message = $"Failed to list presentations: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }
}

/// <summary>Result of listing the calling citizen's presentation history.</summary>
public sealed record MyPresentationsResult
{
    /// <summary>Status: Success, Error, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the result.</summary>
    public required string Message { get; init; }

    /// <summary>When the request was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The citizen's presentation history (on success), newest-first.</summary>
    public IReadOnlyList<MyPresentationSummary> Presentations { get; init; } = [];
}

/// <summary>One entry in the citizen's presentation history.</summary>
public sealed record MyPresentationSummary
{
    /// <summary>Wallet-generated identifier for the presentation.</summary>
    public Guid Id { get; init; }

    /// <summary>Credential that was presented.</summary>
    public Guid CredentialId { get; init; }

    /// <summary>Verifier's declared DID, if known.</summary>
    public string? VerifierDid { get; init; }

    /// <summary>Verifier-supplied display label (untrusted).</summary>
    public string? VerifierLabel { get; init; }

    /// <summary>Names of disclosed claims.</summary>
    public IReadOnlyList<string> DisclosedClaims { get; init; } = [];

    /// <summary>UTC time the presentation completed.</summary>
    public DateTimeOffset PresentedAt { get; init; }

    /// <summary>Outcome the wallet observed.</summary>
    public required string Outcome { get; init; }
}
