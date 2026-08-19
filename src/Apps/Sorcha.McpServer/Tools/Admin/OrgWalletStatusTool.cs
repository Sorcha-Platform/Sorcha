// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Tenant;

namespace Sorcha.McpServer.Tools.Admin;

/// <summary>
/// Reports whether an organisation has created its signing wallet yet (#1525).
/// </summary>
/// <remarks>
/// <para>
/// <b>Read-only by design, and creation is deliberately NOT exposed over MCP.</b> Creating an
/// organisation's wallet returns a BIP39 recovery phrase that is shown once, never stored, and
/// cannot be reissued by anyone — including Sorcha. Returning that through a tool call would place
/// an organisation's master secret into an assistant's context and, in practice, into a transcript
/// or log. The entire point of #1525 is that this secret reaches a human who records it, rather
/// than a machine that retains it.
/// </para>
/// <para>
/// So this tool answers the question an operator actually needs from an assistant — "is this
/// organisation set up, and if not what is outstanding?" — and points at
/// <c>sorcha org wallet create &lt;orgId&gt;</c>, which the org's own administrator runs.
/// </para>
/// </remarks>
[McpServerToolType]
public sealed class OrgWalletStatusTool
{
    private const string ToolName = "sorcha_org_wallet_status";
    private const string ServiceName = "Tenant";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly ITenantServiceClient _tenantClient;
    private readonly ILogger<OrgWalletStatusTool> _logger;

    /// <summary>Creates the tool.</summary>
    public OrgWalletStatusTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        ITenantServiceClient tenantClient,
        ILogger<OrgWalletStatusTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _tenantClient = tenantClient;
        _logger = logger;
    }

    /// <summary>Reports whether organisations have their signing wallet.</summary>
    /// <param name="orgId">Optional organisation ID; omit to report on every organisation.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [McpServerTool(Name = ToolName)]
    [Description("Reports whether organisations have created their canonical signing wallet yet, and names the ones that have not. Call this when an organisation cannot issue credentials, when its issuer DID does not resolve (GET /orgs/{id}/did.json returns 404), or when checking that a newly-created organisation is fully set up — a missing organisation wallet is the usual cause, because nothing creates it automatically. This tool CANNOT create the wallet: doing so returns a BIP39 recovery phrase that is shown once and can never be reissued, so it must go to a human, not into a tool result. Tell the operator to run 'sorcha org wallet create <orgId>' as an administrator of that organisation.")]
    public async Task<OrgWalletStatusResult> InvokeAsync(
        [Description("Organisation ID to check; omit to report on all organisations")] string? orgId = null,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return Error("Unauthorized", "Access denied. This tool requires the sorcha:admin role.");
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return Error("Unavailable", "Tenant service is currently unavailable. Please try again later.");
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var body = await _tenantClient.ListOrganizationsAsync("page=1&pageSize=200", cancellationToken);
            stopwatch.Stop();

            if (string.IsNullOrWhiteSpace(body))
            {
                _availabilityTracker.RecordFailure(ServiceName);
                return Error("Error", "Could not list organisations.");
            }

            _availabilityTracker.RecordSuccess(ServiceName);

            var orgs = new List<OrgWalletState>();
            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            var items = root.ValueKind == JsonValueKind.Array ? root
                : root.TryGetProperty("items", out var it) ? it
                : default;

            if (items.ValueKind == JsonValueKind.Array)
            {
                foreach (var o in items.EnumerateArray())
                {
                    var id = o.TryGetProperty("id", out var idEl) ? idEl.GetString() : null;
                    if (id is null) continue;
                    if (orgId is not null && !string.Equals(id, orgId, StringComparison.OrdinalIgnoreCase)) continue;

                    var address = o.TryGetProperty("walletAddress", out var w) && w.ValueKind == JsonValueKind.String
                        ? w.GetString()
                        : null;

                    orgs.Add(new OrgWalletState
                    {
                        OrganizationId = id,
                        Name = o.TryGetProperty("name", out var n) ? n.GetString() : null,
                        HasWallet = !string.IsNullOrWhiteSpace(address),
                        WalletAddress = address
                    });
                }
            }

            if (orgId is not null && orgs.Count == 0)
            {
                return Error("Error", $"Organisation '{orgId}' was not found.");
            }

            var missing = orgs.Where(o => !o.HasWallet).ToList();
            _logger.LogInformation(
                "Org wallet status: {Total} organisation(s), {Missing} without a wallet", orgs.Count, missing.Count);

            return new OrgWalletStatusResult
            {
                Status = "Success",
                Message = missing.Count == 0
                    ? $"All {orgs.Count} organisation(s) have a signing wallet."
                    : $"{missing.Count} of {orgs.Count} organisation(s) have no signing wallet: "
                      + string.Join(", ", missing.Select(m => m.Name ?? m.OrganizationId))
                      + ". An administrator of each must run 'sorcha org wallet create <orgId>' — the "
                      + "recovery phrase is shown once and cannot be reissued, so it is not created "
                      + "automatically and cannot be created through this tool.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                Organizations = orgs
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure(ServiceName);
            _logger.LogError(ex, "Org wallet status check failed");
            return Error("Error", $"Org wallet status check failed: {ex.Message}");
        }
    }

    private static OrgWalletStatusResult Error(string status, string message) => new()
    {
        Status = status,
        Message = message,
        CheckedAt = DateTimeOffset.UtcNow
    };
}

/// <summary>Result of <c>sorcha_org_wallet_status</c>.</summary>
public sealed class OrgWalletStatusResult
{
    /// <summary>Success, Error, Unauthorized or Unavailable.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Human-readable summary, naming any organisation still missing its wallet.</summary>
    public string Message { get; set; } = string.Empty;

    /// <summary>When the check ran.</summary>
    public DateTimeOffset CheckedAt { get; set; }

    /// <summary>Round-trip time in milliseconds.</summary>
    public int ResponseTimeMs { get; set; }

    /// <summary>Per-organisation state.</summary>
    public List<OrgWalletState> Organizations { get; set; } = [];
}

/// <summary>One organisation's signing-wallet state.</summary>
public sealed class OrgWalletState
{
    /// <summary>Organisation ID.</summary>
    public string OrganizationId { get; set; } = string.Empty;

    /// <summary>Organisation display name.</summary>
    public string? Name { get; set; }

    /// <summary>Whether the organisation has created its canonical signing wallet.</summary>
    public bool HasWallet { get; set; }

    /// <summary>The wallet address, when it has one. Public — never the recovery phrase.</summary>
    public string? WalletAddress { get; set; }
}
