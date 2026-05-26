// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Blueprint;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Administrator/issuer tool that reissues an Expired credential with a fresh expiry.
/// Routes through the typed <see cref="IBlueprintServiceClient"/>.
/// </summary>
[McpServerToolType]
public sealed class CredentialRefreshTool
{
    private const string ToolName = "sorcha_credential_refresh";
    private const string ServiceName = "Blueprint";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IBlueprintServiceClient _blueprintClient;
    private readonly ILogger<CredentialRefreshTool> _logger;

    public CredentialRefreshTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IBlueprintServiceClient blueprintClient,
        ILogger<CredentialRefreshTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _blueprintClient = blueprintClient;
        _logger = logger;
    }

    /// <summary>
    /// Reissues an Expired credential with a fresh expiry period, consuming the old one.
    /// </summary>
    /// <param name="credentialId">The expired credential ID to refresh.</param>
    /// <param name="issuerWallet">Wallet address of the issuing authority.</param>
    /// <param name="newExpiryDuration">Optional ISO 8601 duration for the new expiry (default P365D).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The refresh result JSON (original consumed + new credential), or a failure result.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Reissues an Expired verifiable credential as a fresh credential with a new expiry period, marking the old one Consumed and minting a replacement carrying the same claims and subject. Call this when a credential has lapsed and the holder is still entitled to it — it is the renewal path, supplying an optional ISO-8601 duration such as P365D for the new validity window. The credential must currently be in the Expired state (use sorcha_credential_reinstate instead for a merely Suspended credential, and sorcha_credential_offer to issue a brand-new credential to a wallet). Only the original issuer may refresh, so supply the issuing authority's wallet address.")]
    public Task<CredentialLifecycleToolResult> RefreshAsync(
        [Description("The expired credential ID to refresh")] string credentialId,
        [Description("Wallet address of the issuing authority")] string issuerWallet,
        [Description("Optional ISO 8601 duration for the new expiry, e.g. 'P365D' (default P365D)")] string? newExpiryDuration = null,
        CancellationToken cancellationToken = default) =>
        CredentialLifecycleRunner.RunAsync(
            ToolName, ServiceName, "refresh", credentialId, issuerWallet,
            _authService, _availabilityTracker, _logger,
            ct => _blueprintClient.RefreshCredentialAsync(credentialId, issuerWallet, newExpiryDuration, ct),
            cancellationToken);
}
