// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Blueprint;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Administrator/issuer tool that revokes a previously-issued credential.
/// Routes through the typed <see cref="IBlueprintServiceClient"/>.
/// </summary>
[McpServerToolType]
public sealed class CredentialRevokeTool
{
    private const string ToolName = "sorcha_credential_revoke";
    private const string ServiceName = "Blueprint";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly IBlueprintServiceClient _blueprintClient;
    private readonly ILogger<CredentialRevokeTool> _logger;

    public CredentialRevokeTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        IBlueprintServiceClient blueprintClient,
        ILogger<CredentialRevokeTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _blueprintClient = blueprintClient;
        _logger = logger;
    }

    /// <summary>
    /// Revokes a previously-issued credential (irreversible).
    /// </summary>
    /// <param name="credentialId">The credential ID to revoke.</param>
    /// <param name="issuerWallet">Wallet address of the issuing authority requesting revocation.</param>
    /// <param name="reason">Optional human-readable reason for revocation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The revocation result JSON, or a failure result.</returns>
    [McpServerTool(Name = ToolName)]
    [Description("Permanently revokes a previously-issued verifiable credential, flipping its status to Revoked, updating the bitstring status list, and propagating a CredentialStatusChange transaction to the holder's node so verifiers stop honouring it. Call this when a credential must be invalidated for good — key compromise, fraud, or the underlying entitlement being withdrawn. Revocation is irreversible (unlike sorcha_credential_suspend, which can be reversed with sorcha_credential_reinstate); only the original issuer may revoke, so supply the issuing authority's wallet address. Use this to stop a credential being accepted by relying parties; use sorcha_transaction_revoke instead when revoking a raw ledger transaction rather than an issued credential.")]
    public async Task<CredentialLifecycleToolResult> RevokeAsync(
        [Description("The credential ID to revoke")] string credentialId,
        [Description("Wallet address of the issuing authority requesting revocation")] string issuerWallet,
        [Description("Optional human-readable reason for revocation")] string? reason = null,
        CancellationToken cancellationToken = default)
    {
        return await CredentialLifecycleRunner.RunAsync(
            ToolName, ServiceName, "revoke", credentialId, issuerWallet,
            _authService, _availabilityTracker, _logger,
            ct => _blueprintClient.RevokeCredentialAsync(credentialId, issuerWallet, reason, ct),
            cancellationToken);
    }
}
