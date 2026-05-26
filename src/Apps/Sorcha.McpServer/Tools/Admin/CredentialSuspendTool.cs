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
/// Administrator/issuer tool that temporarily suspends an Active credential (reversible).
/// Routes through the typed <see cref="IBlueprintServiceClient"/>.
/// </summary>
[McpServerToolType]
public sealed class CredentialSuspendTool
{
    private const string ToolName = "sorcha_credential_suspend";
    private const string ServiceName = "Blueprint";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IBlueprintServiceClient _blueprintClient;
    private readonly ILogger<CredentialSuspendTool> _logger;

    public CredentialSuspendTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IBlueprintServiceClient blueprintClient,
        ILogger<CredentialSuspendTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _blueprintClient = blueprintClient;
        _logger = logger;
    }

    /// <summary>
    /// Temporarily suspends an Active credential. The suspension is reversible via reinstate.
    /// </summary>
    /// <param name="credentialId">The credential ID to suspend.</param>
    /// <param name="issuerWallet">Wallet address of the issuing authority.</param>
    /// <param name="reason">Optional human-readable reason for the suspension.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The suspend result JSON, or a failure result.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Temporarily suspends an Active verifiable credential, flipping its status to Suspended and setting the bitstring status-list bit so verifiers stop accepting it for now. Call this when a credential should be paused pending investigation rather than permanently invalidated — for example while a dispute or compliance review is in progress. Suspension is reversible with sorcha_credential_reinstate; use sorcha_credential_revoke instead when the credential must be invalidated permanently. Only the original issuer may suspend, so supply the issuing authority's wallet address; the credential must currently be Active.")]
    public Task<CredentialLifecycleToolResult> SuspendAsync(
        [Description("The credential ID to suspend")] string credentialId,
        [Description("Wallet address of the issuing authority")] string issuerWallet,
        [Description("Optional human-readable reason for the suspension")] string? reason = null,
        CancellationToken cancellationToken = default) =>
        CredentialLifecycleRunner.RunAsync(
            ToolName, ServiceName, "suspend", credentialId, issuerWallet,
            _authService, _availabilityTracker, _logger,
            ct => _blueprintClient.SuspendCredentialAsync(credentialId, issuerWallet, reason, ct),
            cancellationToken);
}
