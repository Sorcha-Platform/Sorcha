// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Sorcha.McpServer.Infrastructure;
using Sorcha.McpServer.Services;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Tenant;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Wallet.Contracts.Constants;

// The project namespace Sorcha.McpServer shadows the SDK type
// ModelContextProtocol.Server.McpServer, so an unqualified `McpServer` in this file is CS0118
// ("namespace used like a type"). Alias it once rather than fully qualifying every mention.
using SdkMcpServer = ModelContextProtocol.Server.McpServer;

namespace Sorcha.McpServer.Tools.Designer;

/// <summary>
/// Designer tool for creating a register — the two-phase owner-attestation ceremony
/// (initiate → sign → finalize) run as one tool call, gated on a real person's confirmation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Two values the agent never chooses.</b> The signing context is
/// <see cref="SorchaDerivationPaths.RegisterAttestation"/> (slot 100 — the organisation's
/// <em>governance</em> key), and the owning wallet is resolved from the caller's own
/// <c>org_id</c> claim, never from a tool argument. Both failures are silent: a wrong derivation
/// path does not throw, it derives a different but perfectly valid key, and the register's
/// governance roster then records a key the validator will never match — the register is
/// ungovernable from the moment it is created, with nothing downstream reporting it.
/// </para>
/// <para>
/// <b>Org-wallet lookup.</b> The address comes from
/// <see cref="ITenantServiceClient.GetOrganizationAsync"/> (<c>GET /api/organizations/{id}</c>,
/// whose <c>OrganizationResponse</c> carries <c>walletAddress</c>). The purpose-built
/// <c>IOrgInfoClient.ResolveCanonicalWalletAddressAsync</c> was deliberately NOT used: it targets
/// the Tenant Service's <c>/api/internal/*</c> surface, which requires a <c>:service</c>-audience
/// service-principal token. The MCP server holds no <c>ServiceAuth</c> credentials and forwards
/// the caller's own bearer instead, and its container does not attach the forwarding handler to
/// that client's HttpClient — so the call would 401, the client would swallow it into a null, and
/// every organisation would be reported as "has no wallet". A silent-wrong answer, which is
/// exactly the class of defect this tool exists to avoid.
/// </para>
/// <para>
/// <b>Ordering.</b> The person is asked BEFORE <c>POST /api/registers/initiate</c>. The pending
/// registration has a hard 5-minute TTL (<c>RegisterCreationOrchestrator</c>), and a human's
/// thinking time must not be spent against it.
/// </para>
/// </remarks>
[McpServerToolType]
public sealed class RegisterCreateTool
{
    private const string ToolName = "sorcha_register_create";

    /// <summary>Matches <c>InitiateRegisterCreationRequest.Name</c>'s <c>[StringLength(38, MinimumLength = 1)]</c>.</summary>
    private const int MaxNameLength = 38;

    /// <summary>Matches <c>InitiateRegisterCreationRequest.Description</c>'s <c>[StringLength(500)]</c>.</summary>
    private const int MaxDescriptionLength = 500;

    private readonly IMcpAuthorizationService _authService;
    private readonly IServiceAvailabilityTracker _availabilityTracker;
    private readonly ICallerContext _callerContext;
    private readonly ITenantServiceClient _tenantClient;
    private readonly IRegisterServiceClient _registerClient;
    private readonly IWalletServiceClient _walletClient;
    private readonly IHumanApproval _humanApproval;
    private readonly ILogger<RegisterCreateTool> _logger;

    /// <summary>Creates the tool.</summary>
    public RegisterCreateTool(
        IMcpAuthorizationService authService,
        IServiceAvailabilityTracker availabilityTracker,
        ICallerContext callerContext,
        ITenantServiceClient tenantClient,
        IRegisterServiceClient registerClient,
        IWalletServiceClient walletClient,
        IHumanApproval humanApproval,
        ILogger<RegisterCreateTool> logger)
    {
        _authService = authService;
        _availabilityTracker = availabilityTracker;
        _callerContext = callerContext;
        _tenantClient = tenantClient;
        _registerClient = registerClient;
        _walletClient = walletClient;
        _humanApproval = humanApproval;
        _logger = logger;
    }

    /// <summary>Creates a register, after a person confirms it.</summary>
    /// <param name="server">The live MCP server, injected by the SDK; used to reach the caller's client.</param>
    /// <param name="name">Register name (1-38 characters).</param>
    /// <param name="description">What the register is for (max 500 characters).</param>
    /// <param name="devMode">When true, payloads are stored as plaintext with read-time disclosure filtering.</param>
    /// <param name="advertise">When true, the register is advertised to the peer network.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created register's id and genesis transaction id.</returns>
    [McpServerTool(Name = ToolName, Destructive = true, ReadOnly = false, Idempotent = false)]
    [Description("Creates a new Sorcha register — the ledger a workflow's transactions are written to — and returns its registerId plus the id of the genesis transaction that was submitted for it. Call this when you are setting up a workflow from scratch and have no registerId yet: create the register first, then design a blueprint with sorcha_blueprint_create, and only then start work on it with sorcha_instance_create, which needs both ids. Creating a register is irreversible and establishes the governance keys that authorise every later administrative change, so it REQUIRES a person to confirm it interactively; if your MCP client does not support elicitation the call is refused before anything is created. The register is owned by YOUR organisation's signing wallet, which is resolved from your token rather than passed in. Set devMode only for development registers — it stores payloads as plaintext instead of encrypting them.")]
    public async Task<RegisterCreateResult> CreateRegisterAsync(
        SdkMcpServer server,
        [Description("Register name, 1-38 characters")] string name,
        [Description("What this register is for, max 500 characters")] string description,
        [Description("Store payloads as plaintext instead of encrypting them. Development only.")] bool devMode = false,
        [Description("Advertise this register to the peer network")] bool advertise = false,
        CancellationToken cancellationToken = default)
    {
        // 1. Entitlement — cheap and local, so it precedes everything.
        if (!_authService.CanInvokeTool(ToolName))
        {
            return Fail("Unauthorized", "Access denied. This tool requires the sorcha:designer role.");
        }

        // 2. Validate against the real DataAnnotations on InitiateRegisterCreationRequest, so the
        //    agent gets a precise message instead of a 400 it has to decode.
        var validationErrors = Validate(name, description);
        if (validationErrors.Count > 0)
        {
            return new RegisterCreateResult
            {
                Status = "ValidationError",
                Message = "The register name or description is outside the limits the register service enforces.",
                CheckedAt = DateTimeOffset.UtcNow,
                ValidationErrors = validationErrors
            };
        }

        if (!_availabilityTracker.IsServiceAvailable("Register"))
        {
            return Fail("Unavailable", "Register service is currently unavailable. Please try again later.");
        }

        if (!_availabilityTracker.IsServiceAvailable("Tenant"))
        {
            return Fail("Unavailable", "Tenant service is currently unavailable, so the owning organisation's wallet cannot be resolved. Please try again later.");
        }

        // 3. Resolve the owning wallet from the CALLER'S org_id — never from an argument. An agent
        //    naming a wallet cannot forge a signature, but it could produce a working register
        //    whose governance roster records the wrong key.
        var orgId = _callerContext.OrganizationId;
        if (string.IsNullOrWhiteSpace(orgId))
        {
            return Fail(
                "Error",
                "Your token carries no organisation (org_id), so there is no organisation wallet to own "
                + "the register. Sign in with a platform-tier account that belongs to an organisation.");
        }

        string? walletAddress;
        try
        {
            walletAddress = await ResolveOrgWalletAddressAsync(orgId, cancellationToken);
        }
        catch (Exception ex)
        {
            _availabilityTracker.RecordFailure("Tenant", ex);
            _logger.LogWarning(ex, "Could not resolve the owning organisation's wallet address");
            return Fail("Error", $"Could not resolve your organisation's signing wallet: {ex.Message}");
        }

        if (string.IsNullOrWhiteSpace(walletAddress))
        {
            // A legitimate state, not a fault to repair (#1525). The wallet's BIP39 recovery phrase
            // is shown exactly once and is the organisation's secret, so it is created by the org's
            // own administrator — no service, and certainly no agent, may mint it on their behalf.
            return Fail(
                "Error",
                "Your organisation has no signing wallet yet, so it cannot own a register. This is not "
                + "something this tool (or any service) can fix: creating the wallet returns a BIP39 "
                + "recovery phrase that is shown once, never stored, and can never be reissued, so it "
                + "must reach a person. An administrator of your organisation must create the wallet "
                + "and link it (sorcha org wallet create <orgId>, or the Wallet page in the Sorcha UI). "
                + "Retry once they have.");
        }

        // 4. Ask a person. BEFORE /initiate, so their thinking time does not run against the
        //    pending registration's 5-minute TTL.
        var approval = await _humanApproval.RequestAsync(server, new HumanApprovalRequest(
            $"Create a new Sorcha register '{name}'?\n\n" +
            $"Purpose: {description}\n" +
            $"Owning organisation wallet: {walletAddress}\n" +
            (devMode
                ? "Storage: DEVELOPMENT MODE — payloads stored as PLAINTEXT.\n"
                : "Storage: encrypted payloads.\n") +
            (advertise
                ? "Visibility: advertised to the peer network.\n"
                : "Visibility: private.\n") +
            "\nThis is irreversible and establishes the register's governance.",
            "Create the register"), cancellationToken);

        if (approval.Outcome != ApprovalOutcome.Approved)
        {
            // The two refusal paths need different operator responses: a decline is a decision,
            // a missing capability is a client limitation. Nothing has been created either way.
            return new RegisterCreateResult
            {
                Status = approval.Outcome == ApprovalOutcome.NotSupported ? "ApprovalRequired" : "Refused",
                Message = approval.Detail,
                CheckedAt = DateTimeOffset.UtcNow
            };
        }

        // 5. initiate -> sign each attestation -> finalize.
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var initiateResponse = await _registerClient.InitiateRegisterCreationAsync(
                BuildInitiateRequest(name, description, devMode, advertise, orgId, walletAddress),
                cancellationToken);

            var signedAttestations = new List<SignedAttestation>(initiateResponse.AttestationsToSign.Count);
            foreach (var attestation in initiateResponse.AttestationsToSign)
            {
                // SorchaDerivationPaths.RegisterAttestation is slot 100 — the organisation's
                // GOVERNANCE key. The register's roster records whatever key signs here, and the
                // validator authorises later governance transactions by matching it. A wrong
                // value does not throw; it derives a different valid key and the register is
                // silently ungovernable. Never a literal (CLAUDE.md pattern 15).
                var signResult = await _walletClient.SignTransactionAsync(
                    attestation.WalletId,
                    Convert.FromHexString(attestation.DataToSign),
                    SorchaDerivationPaths.RegisterAttestation,
                    isPreHashed: true,
                    cancellationToken);

                signedAttestations.Add(new SignedAttestation
                {
                    AttestationData = attestation.AttestationData,
                    PublicKey = Convert.ToBase64String(signResult.PublicKey),
                    Signature = Convert.ToBase64String(signResult.Signature),
                    Algorithm = ParseAlgorithm(signResult.Algorithm)
                });
            }

            var finalizeResponse = await _registerClient.FinalizeRegisterCreationAsync(
                new FinalizeRegisterCreationRequest
                {
                    RegisterId = initiateResponse.RegisterId,
                    Nonce = initiateResponse.Nonce,
                    SignedAttestations = signedAttestations
                },
                cancellationToken);

            stopwatch.Stop();
            _availabilityTracker.RecordSuccess("Register");

            var registerId = string.IsNullOrWhiteSpace(finalizeResponse.RegisterId)
                ? initiateResponse.RegisterId
                : finalizeResponse.RegisterId;

            _logger.LogInformation(
                "Register '{RegisterId}' created via MCP in {ElapsedMs}ms (devMode={DevMode}, advertise={Advertise})",
                registerId, stopwatch.ElapsedMilliseconds, devMode, advertise);

            return new RegisterCreateResult
            {
                Status = "Success",
                // Submission is asynchronous (Feature 145) — the genesis transaction is submitted
                // and sealed into docket 0 by the validator, not settled by the time this returns.
                Message =
                    $"Register '{name}' was created with id '{registerId}'. Its genesis transaction "
                    + $"('{finalizeResponse.GenesisTransactionId}') has been submitted; sealing is "
                    + "asynchronous, so allow a short delay before querying the register's chain.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds,
                RegisterId = registerId,
                GenesisTransactionId = finalizeResponse.GenesisTransactionId,
                GenesisDocketId = finalizeResponse.GenesisDocketId,
                OwnerWalletAddress = walletAddress,
                DevMode = devMode,
                Advertised = advertise
            };
        }
        catch (TaskCanceledException)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Register");
            _logger.LogWarning("Register creation timed out");

            return new RegisterCreateResult
            {
                Status = "Timeout",
                Message =
                    "The register-creation ceremony timed out. The pending registration expires after "
                    + "five minutes; check whether the register exists before retrying.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (HttpRequestException ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Register", ex);
            _logger.LogWarning(ex, "Register creation failed");

            return new RegisterCreateResult
            {
                Status = "Error",
                Message = $"The register-creation ceremony failed: {ex.Message}",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _availabilityTracker.RecordFailure("Register", ex);
            _logger.LogError(ex, "Unexpected error creating a register");

            return new RegisterCreateResult
            {
                Status = "Error",
                Message = "An unexpected error occurred while creating the register.",
                CheckedAt = DateTimeOffset.UtcNow,
                ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds
            };
        }
    }

    private static InitiateRegisterCreationRequest BuildInitiateRequest(
        string name,
        string description,
        bool devMode,
        bool advertise,
        string orgId,
        string walletAddress) => new()
        {
            Name = name.Trim(),
            Description = description.Trim(),
            DevMode = devMode,
            Advertise = advertise,
            Purpose = RegisterPurpose.General,
            Metadata = new Dictionary<string, string>
            {
                // Audit fact about the creation event. Claims nothing about participants.
                // NOTHING in the platform may ever branch on this (CLAUDE.md pattern 23) — it is a
                // self-supplied label, so reading it to grant anything would be the exact defect
                // that pattern exists to prevent. RegisterCreateToolTests asserts no other file
                // under src/ so much as mentions the key.
                ["createdVia"] = "mcp",
                ["mcpToolVersion"] = McpServerVersion.Current
            },
            Owners =
            [
                new OwnerInfo
                {
                    UserId = orgId,
                    WalletId = walletAddress
                }
            ]
        };

    /// <summary>
    /// Reads <c>walletAddress</c> off <c>GET /api/organizations/{id}</c>'s
    /// <c>OrganizationResponse</c>. Null means the organisation is still awaiting its wallet.
    /// </summary>
    private async Task<string?> ResolveOrgWalletAddressAsync(string orgId, CancellationToken cancellationToken)
    {
        var body = await _tenantClient.GetOrganizationAsync(orgId, cancellationToken);
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        _availabilityTracker.RecordSuccess("Tenant");

        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.ValueKind == JsonValueKind.Object
            && doc.RootElement.TryGetProperty("walletAddress", out var addressElement)
            && addressElement.ValueKind == JsonValueKind.String)
        {
            var address = addressElement.GetString();
            return string.IsNullOrWhiteSpace(address) ? null : address;
        }

        return null;
    }

    private static List<string> Validate(string name, string description)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add($"name is required and must be 1-{MaxNameLength} characters");
        }
        else if (name.Trim().Length > MaxNameLength)
        {
            errors.Add($"name must not exceed {MaxNameLength} characters (was {name.Trim().Length})");
        }

        if (description is not null && description.Trim().Length > MaxDescriptionLength)
        {
            errors.Add($"description must not exceed {MaxDescriptionLength} characters (was {description.Trim().Length})");
        }

        return errors;
    }

    /// <summary>
    /// Maps the wallet service's algorithm string onto the register
    /// <see cref="SignatureAlgorithm"/>, defaulting to ED25519 (the org wallet's algorithm) when
    /// unrecognised. Mirrors <c>SandboxRegisterProvider.ParseAlgorithm</c>.
    /// </summary>
    private static SignatureAlgorithm ParseAlgorithm(string algorithm) =>
        Enum.TryParse<SignatureAlgorithm>(algorithm, ignoreCase: true, out var parsed)
            ? parsed
            : SignatureAlgorithm.ED25519;

    private static RegisterCreateResult Fail(string status, string message) => new()
    {
        Status = status,
        Message = message,
        CheckedAt = DateTimeOffset.UtcNow
    };
}

/// <summary>
/// Result of a register create operation.
/// </summary>
public sealed record RegisterCreateResult
{
    /// <summary>
    /// Operation status: Success, ValidationError, Refused, ApprovalRequired, Error,
    /// Unavailable, Timeout, or Unauthorized.
    /// </summary>
    public required string Status { get; init; }

    /// <summary>Human-readable message about the operation result.</summary>
    public required string Message { get; init; }

    /// <summary>When the operation was performed.</summary>
    public required DateTimeOffset CheckedAt { get; init; }

    /// <summary>Response time in milliseconds.</summary>
    public int ResponseTimeMs { get; init; }

    /// <summary>Validation errors (if Status is ValidationError).</summary>
    public IReadOnlyList<string> ValidationErrors { get; init; } = [];

    /// <summary>The created register's identifier (if successful).</summary>
    public string? RegisterId { get; init; }

    /// <summary>The genesis transaction's identifier (if successful).</summary>
    public string? GenesisTransactionId { get; init; }

    /// <summary>The genesis docket identifier — always "0" (if successful).</summary>
    public string? GenesisDocketId { get; init; }

    /// <summary>The organisation wallet that owns the register and signed its attestation.</summary>
    public string? OwnerWalletAddress { get; init; }

    /// <summary>True when payloads are stored as plaintext with read-time disclosure filtering.</summary>
    public bool DevMode { get; init; }

    /// <summary>True when the register is advertised to the peer network.</summary>
    public bool Advertised { get; init; }
}
