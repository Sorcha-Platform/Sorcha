// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.ServiceClients.Tenant;
using Sorcha.ServiceClients.Wallet;

namespace Sorcha.McpServer.Tools.Participant;

/// <summary>
/// Publishes a participant record to a register, binding a blueprint role to the caller's wallet
/// on-ledger (#1664). Without one, a later action whose sender is that role cannot be submitted by
/// anyone: the validator refuses it with <c>VAL_BP_002</c> after the submission has already been
/// accepted, and that refusal reaches no audit log.
/// </summary>
/// <remarks>
/// <para>
/// <b>The record is per organisation, and the organisation comes from the caller's token.</b> The MCP
/// server forwards the caller's own bearer and holds no identity of its own, so an agent can publish a
/// record only for the organisation it is signed in to. A two-party exchange therefore needs one MCP
/// session per participating organisation — each publishes its own role.
/// </para>
/// <para>
/// ⚠ The published record's own <c>participantId</c> is a fresh GUID assigned by the Tenant Service, so
/// the validator resolves a role by matching the record's NAME (and organisation, when the blueprint sets
/// one). This tool therefore publishes <paramref name="participantId"/> as the record's name — that is what
/// makes the binding resolvable at all.
/// </para>
/// </remarks>
[McpServerToolType]
public sealed class ParticipantPublishTool
{
    private const string ToolName = "sorcha_participant_publish";
    private const string ServiceName = "Tenant";

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly ITenantServiceClient _tenantClient;
    private readonly IWalletServiceClient _walletClient;
    private readonly ICallerContext _callerContext;
    private readonly ILogger<ParticipantPublishTool> _logger;

    public ParticipantPublishTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        ITenantServiceClient tenantClient,
        IWalletServiceClient walletClient,
        ICallerContext callerContext,
        ILogger<ParticipantPublishTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _tenantClient = tenantClient;
        _walletClient = walletClient;
        _callerContext = callerContext;
        _logger = logger;
    }

    /// <summary>
    /// Publishes a participant record binding a blueprint role to a wallet on a register.
    /// </summary>
    /// <param name="registerId">The register the workflow runs on.</param>
    /// <param name="participantId">The blueprint participant (role) id this record binds.</param>
    /// <param name="organisationName">
    /// The organisation name to publish under. Must match the blueprint participant's
    /// <c>organisation</c> when it sets one, or the validator will not resolve this record for that role.
    /// </param>
    /// <param name="walletAddress">The wallet to bind; defaults to the caller's own when they hold one.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [McpServerTool(Name = ToolName)]
    [Description("Publish a participant record to a register, binding a blueprint participant (role) to a wallet on-ledger, for YOUR organisation. This is what authorises that role to send actions, and what its disclosures are encrypted to, so it must exist before the role's first action: without it a submission is accepted and then refused by the validator with no readable reason. Requires the registerId, the blueprint participant id the record binds, and the organisation name to publish under — which must match the blueprint participant's own organisation when it sets one, or the record will not resolve for that role. The wallet defaults to yours. The organisation always comes from your token and cannot be passed: to bind a role belonging to another organisation, that organisation publishes its own record from its own session. Publishing writes a transaction to the register and takes a few seconds to seal; call this when a role needs binding, then poll sorcha_participant_list until the record appears before submitting that role's action, rather than submitting and hoping.")]
    public async Task<ParticipantPublishResult> PublishParticipantAsync(
        [Description("The register the workflow runs on")] string registerId,
        [Description("The blueprint participant (role) id this record binds, e.g. 'provider'")] string participantId,
        [Description("The organisation name to publish under; must match the blueprint participant's organisation when it sets one")] string organisationName,
        [Description("Optional: the wallet to bind. Defaults to your own wallet.")] string? walletAddress = null,
        CancellationToken cancellationToken = default)
    {
        if (!_authService.CanInvokeTool(ToolName))
        {
            return Fail("Unauthorized", "Access denied. Publishing a participant record requires an organisation Administrator.");
        }

        if (string.IsNullOrWhiteSpace(registerId))
        {
            return Fail("Error", "A registerId is required.");
        }

        if (string.IsNullOrWhiteSpace(participantId))
        {
            return Fail("Error", "A participantId is required: the blueprint participant (role) id this record binds.");
        }

        if (string.IsNullOrWhiteSpace(organisationName))
        {
            return Fail("Error",
                "An organisationName is required. It must match the blueprint participant's organisation when it "
                + "sets one, because that is what the validator matches the record against.");
        }

        // The organisation is the caller's own, from the token — never an argument (#1664: a record binds a
        // role to an organisation's wallet, so only that organisation may publish it).
        var organizationId = _callerContext.OrganizationId;
        if (string.IsNullOrWhiteSpace(organizationId))
        {
            return Fail("Error",
                "Your token carries no organisation (org_id), so there is no organisation to publish this record for. "
                + "Sign in with a platform-tier account that belongs to the organisation acting as this participant.");
        }

        if (!_availabilityTracker.IsServiceAvailable(ServiceName))
        {
            return Fail("Unavailable", "Tenant service is currently unavailable. Please try again later.");
        }

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var wallet = await ResolveWalletAsync(walletAddress, cancellationToken);
            if (wallet.Error is not null)
            {
                return Timed(Fail("Error", wallet.Error), stopwatch);
            }

            var request = JsonSerializer.Serialize(new
            {
                registerId,
                // The record's NAME is what the validator matches a blueprint role against: its own
                // participantId is a GUID the Tenant Service assigns.
                participantName = participantId,
                organizationName = organisationName,
                addresses = new[]
                {
                    new
                    {
                        walletAddress = wallet.Address,
                        publicKey = wallet.PublicKey,
                        algorithm = wallet.Algorithm,
                        primary = true,
                    },
                },
                signerWalletAddress = wallet.Address,
            });

            var (status, body) = await _tenantClient.PublishParticipantRecordAsync(
                organizationId, request, cancellationToken);
            _availabilityTracker.RecordSuccess(ServiceName);

            if (status == HttpStatusCode.Conflict)
            {
                return Timed(Fail("Refused",
                    $"That wallet is already claimed by another participant on register {registerId}"
                    + (string.IsNullOrWhiteSpace(body) ? "." : $": {Reason(body)}")
                    + " A wallet binds to one role per register; use a different wallet for this role."), stopwatch);
            }

            if (!IsSuccess(status))
            {
                return Timed(Fail(status is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized ? "Refused" : "Error",
                    $"Publishing the participant record failed (HTTP {(int)status}): {Reason(body)}"), stopwatch);
            }

            var transactionId = ReadString(body, "transactionId");

            _logger.LogInformation(
                "Published participant record for role {ParticipantId} on register {RegisterId} (tx {TransactionId})",
                participantId, registerId, transactionId ?? "unknown");

            return Timed(new ParticipantPublishResult
            {
                Status = "Success",
                Message =
                    $"Participant record for '{participantId}' submitted for register {registerId}, binding wallet "
                    + $"{wallet.Address}. It is a transaction on the register and takes a few seconds to seal: it does "
                    + "NOT authorise that role until it has. Poll sorcha_participant_list for this register until the "
                    + "record appears, then submit the role's action.",
                CheckedAt = DateTimeOffset.UtcNow,
                RegisterId = registerId,
                ParticipantId = participantId,
                WalletAddress = wallet.Address,
                TransactionId = transactionId,
            }, stopwatch);
        }
        catch (TaskCanceledException)
        {
            _availabilityTracker.RecordFailure(ServiceName);
            return Timed(Fail("Timeout", "Request to the tenant service timed out."), stopwatch);
        }
        catch (HttpRequestException ex)
        {
            _availabilityTracker.RecordFailure(ServiceName, ex);
            return Timed(Fail("Error", $"Failed to connect to the tenant service: {ex.Message}"), stopwatch);
        }
        catch (Exception ex)
        {
            _availabilityTracker.RecordFailure(ServiceName, ex);
            _logger.LogError(ex, "Unexpected error publishing a participant record");
            return Timed(Fail("Error", "An unexpected error occurred while publishing the participant record."), stopwatch);
        }
    }

    /// <summary>
    /// Resolves the wallet to bind: the one named, else the caller's own when they hold exactly one.
    /// The public key comes from the Wallet Service, because the published record carries it as the key
    /// this participant's disclosures are encrypted to.
    /// </summary>
    private async Task<(string Address, string PublicKey, string Algorithm, string? Error)> ResolveWalletAsync(
        string? walletAddress, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(walletAddress))
        {
            var named = await _walletClient.GetWalletAsync(walletAddress, cancellationToken);
            return named is null
                ? ("", "", "", $"Wallet {walletAddress} could not be read, so its public key is unknown.")
                : (named.Address, named.PublicKey, named.Algorithm, null);
        }

        var owner = _callerContext.PlatformUserId;
        if (string.IsNullOrWhiteSpace(owner))
        {
            return ("", "", "", "No walletAddress was given and your token carries no user id to resolve one from.");
        }

        var owned = await _walletClient.GetWalletsByOwnerAsync(owner, cancellationToken);
        return owned.Count switch
        {
            0 => ("", "", "", "You hold no wallet to bind. Create one first, then publish the record."),
            1 => (owned[0].Address, owned[0].PublicKey, owned[0].Algorithm, null),
            _ => ("", "", "", "You hold several wallets, so name the one to bind in walletAddress: "
                + string.Join(", ", owned.Select(w => w.Address))),
        };
    }

    private static bool IsSuccess(HttpStatusCode status) => (int)status is >= 200 and < 300;

    private static ParticipantPublishResult Fail(string status, string message) => new()
    {
        Status = status,
        Message = message,
        CheckedAt = DateTimeOffset.UtcNow,
    };

    private static ParticipantPublishResult Timed(ParticipantPublishResult result, Stopwatch stopwatch) =>
        result with { ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds };

    /// <summary>Reads a reason from a Tenant error body, falling back to the body itself.</summary>
    internal static string Reason(string? body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return "the service gave no reason";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.String)
            {
                return document.RootElement.GetString() ?? body.Trim();
            }

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return Truncate(body);
            }

            if (document.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                var fields = errors.EnumerateObject()
                    .Select(f => $"{f.Name}: {string.Join("; ", f.Value.EnumerateArray().Select(v => v.ToString()))}");
                return string.Join(" ", fields);
            }

            foreach (var name in (string[])["detail", "title", "error", "message"])
            {
                if (document.RootElement.TryGetProperty(name, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(value.GetString()))
                {
                    return value.GetString()!;
                }
            }

            return Truncate(body);
        }
        catch (JsonException)
        {
            return Truncate(body);
        }

        static string Truncate(string text) => text.Length <= 400 ? text.Trim() : text[..400].Trim() + "…";
    }

    /// <summary>Reads one string property from a JSON object body, or null.</summary>
    internal static string? ReadString(string? body, string name)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            return document.RootElement.ValueKind == JsonValueKind.Object
                   && document.RootElement.TryGetProperty(name, out var value)
                   && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>Result of publishing a participant record.</summary>
public sealed record ParticipantPublishResult
{
    /// <summary>Operation status: Success, Error, Refused, Unavailable, Timeout, or Unauthorized.</summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the operation result.</summary>
    public required string Message { get; init; }

    /// <summary>When the operation was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>The register the record was published to.</summary>
    public string? RegisterId { get; init; }

    /// <summary>The blueprint participant (role) id the record binds.</summary>
    public string? ParticipantId { get; init; }

    /// <summary>The wallet address bound to that role.</summary>
    public string? WalletAddress { get; init; }

    /// <summary>
    /// The register transaction carrying the record. The binding is not effective until it seals.
    /// </summary>
    public string? TransactionId { get; init; }
}
