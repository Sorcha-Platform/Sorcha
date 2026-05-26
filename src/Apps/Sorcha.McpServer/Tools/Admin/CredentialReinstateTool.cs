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
/// Administrator/issuer tool that reinstates a Suspended credential back to Active.
/// Routes through the typed <see cref="IBlueprintServiceClient"/>.
/// </summary>
[McpServerToolType]
public sealed class CredentialReinstateTool
{
    private const string ToolName = "sorcha_credential_reinstate";
    private const string ServiceName = "Blueprint";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IBlueprintServiceClient _blueprintClient;
    private readonly ILogger<CredentialReinstateTool> _logger;

    public CredentialReinstateTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IBlueprintServiceClient blueprintClient,
        ILogger<CredentialReinstateTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _blueprintClient = blueprintClient;
        _logger = logger;
    }

    /// <summary>
    /// Reinstates a Suspended credential back to Active.
    /// </summary>
    /// <param name="credentialId">The credential ID to reinstate.</param>
    /// <param name="issuerWallet">Wallet address of the issuing authority.</param>
    /// <param name="reason">Optional human-readable reason for the reinstatement.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The reinstate result JSON, or a failure result.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Reinstates a Suspended verifiable credential back to Active, clearing the bitstring status-list bit so verifiers accept it again. Call this when the reason for a sorcha_credential_suspend has cleared — for example after a dispute or compliance review resolves in the holder's favour — to undo the suspension. This only works on a credential currently in the Suspended state; use sorcha_credential_refresh instead of this tool for an Expired credential, and note a Revoked credential can never be reinstated because revocation is permanent. Only the original issuer may reinstate, so supply the issuing authority's wallet address.")]
    public Task<CredentialLifecycleToolResult> ReinstateAsync(
        [Description("The credential ID to reinstate")] string credentialId,
        [Description("Wallet address of the issuing authority")] string issuerWallet,
        [Description("Optional human-readable reason for the reinstatement")] string? reason = null,
        CancellationToken cancellationToken = default) =>
        CredentialLifecycleRunner.RunAsync(
            ToolName, ServiceName, "reinstate", credentialId, issuerWallet,
            _authService, _availabilityTracker, _logger,
            ct => _blueprintClient.ReinstateCredentialAsync(credentialId, issuerWallet, reason, ct),
            cancellationToken);
}
