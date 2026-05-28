// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Register;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Feature 142 (T037/T038 / D4 + D5 / FR-027 + FR-032) — the server-side publish gate.
/// Evaluates the two safety checks that guard <c>POST /api/blueprints/{id}/publish</c>
/// before any publish proceeds:
/// <list type="number">
/// <item><b>Governance hard gate (FR-027/D5):</b> the caller MUST hold a publish-governance
/// role (Owner / Admin / Designer) on the target register's governance roster. If not, the
/// publish is refused — no record is written.</item>
/// <item><b>Rehearsal soft gate (FR-032/D4):</b> the publishing version's executable-definition
/// hash MUST match a recorded <see cref="RehearsalPass"/>. If it does not, the publish is
/// blocked unless the (already governance-authorised) caller explicitly confirms an override,
/// which is then audited via a <see cref="PublishOverride"/> record.</item>
/// </list>
/// </summary>
public interface IPublishGate
{
    /// <summary>
    /// Evaluates the governance + rehearsal gates for a publish attempt. Does NOT publish or
    /// write any record — it returns the decision the endpoint must act on.
    /// </summary>
    /// <param name="caller">The authenticated caller's resolved identity.</param>
    /// <param name="blueprintId">The draft/service identity of the blueprint to publish.</param>
    /// <param name="registerId">The target live register.</param>
    /// <param name="overrideConfirmed">Whether the caller explicitly confirmed a soft-gate override.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The gate decision.</returns>
    Task<PublishGateDecision> EvaluateAsync(
        PublishCaller caller,
        string blueprintId,
        string registerId,
        bool overrideConfirmed,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// The authenticated caller identity used by the publish gate. Carried from the JWT claims by
/// the endpoint. The <see cref="WalletAddress"/> is the primary key used to match a member of
/// the register governance roster (whose subject is a <c>did:sorcha:w:{walletId}</c> DID);
/// <see cref="OrganizationId"/> is the fallback key.
/// </summary>
/// <param name="PlatformUserId">The caller's platform user id (JWT <c>sub</c>), used to attribute an override.</param>
/// <param name="OrganizationId">The caller's organisation id (JWT <c>org_id</c>), fallback roster match key.</param>
/// <param name="WalletAddress">The caller's wallet address (JWT <c>wallet_address</c>), primary roster match key. May be null.</param>
public readonly record struct PublishCaller(
    Guid PlatformUserId,
    string? OrganizationId,
    string? WalletAddress);

/// <summary>The kind of outcome the publish gate produced.</summary>
public enum PublishGateOutcome
{
    /// <summary>Caller lacks register governance publish rights — refuse (HTTP 403). No record written.</summary>
    Forbidden,

    /// <summary>No matching rehearsal pass and no confirmed override — block (HTTP 409 REHEARSAL_REQUIRED).</summary>
    RehearsalRequired,

    /// <summary>Cleared to publish normally (a matching rehearsal pass exists).</summary>
    Proceed,

    /// <summary>Cleared to publish via an authorised, audited override (no matching pass).</summary>
    ProceedWithOverride,
}

/// <summary>
/// The decision produced by <see cref="IPublishGate.EvaluateAsync"/>. The endpoint acts on
/// <see cref="Outcome"/>; <see cref="ExecDefHash"/> is always populated (the computed hash of
/// the publishing version) so the endpoint can surface it on a 409 and record it on an override.
/// </summary>
public sealed class PublishGateDecision
{
    /// <summary>The gate outcome.</summary>
    public required PublishGateOutcome Outcome { get; init; }

    /// <summary>The computed executable-definition hash of the publishing blueprint version.</summary>
    public string ExecDefHash { get; init; } = string.Empty;

    /// <summary>A human-readable reason, populated for <see cref="PublishGateOutcome.Forbidden"/>.</summary>
    public string? Reason { get; init; }
}

/// <summary>
/// Default <see cref="IPublishGate"/> implementation backed by the existing governance roster
/// (<see cref="IRegisterServiceClient.GetGovernanceRosterAsync"/>), the executable-definition
/// hasher (<see cref="ExecutableDefinitionHasher"/>), and the rehearsal-pass store
/// (<see cref="IRehearsalPassStore"/>).
/// </summary>
public sealed class PublishGate : IPublishGate
{
    /// <summary>Roster roles that confer publish-governance authority (D5).</summary>
    private static readonly HashSet<string> PublishingRoles =
        new(StringComparer.OrdinalIgnoreCase) { "Owner", "Admin", "Designer" };

    private readonly IBlueprintStore _blueprintStore;
    private readonly IRegisterServiceClient _registerClient;
    private readonly IRehearsalPassStore _passStore;
    private readonly ExecutableDefinitionHasher _hasher;
    private readonly ILogger<PublishGate> _logger;

    /// <summary>Initialises a new instance of the <see cref="PublishGate"/> class.</summary>
    public PublishGate(
        IBlueprintStore blueprintStore,
        IRegisterServiceClient registerClient,
        IRehearsalPassStore passStore,
        ILogger<PublishGate> logger,
        ExecutableDefinitionHasher? hasher = null)
    {
        _blueprintStore = blueprintStore ?? throw new ArgumentNullException(nameof(blueprintStore));
        _registerClient = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        _passStore = passStore ?? throw new ArgumentNullException(nameof(passStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _hasher = hasher ?? new ExecutableDefinitionHasher();
    }

    /// <inheritdoc/>
    public async Task<PublishGateDecision> EvaluateAsync(
        PublishCaller caller,
        string blueprintId,
        string registerId,
        bool overrideConfirmed,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);

        var blueprint = await _blueprintStore.GetAsync(blueprintId)
            ?? throw new KeyNotFoundException($"Blueprint {blueprintId} not found");

        var execDefHash = _hasher.ComputeHash(blueprint);

        // ---- 1) Governance HARD gate (FR-027/D5) ----------------------------------------
        var roster = await _registerClient.GetGovernanceRosterAsync(registerId, cancellationToken);
        if (!CallerHoldsPublishingRole(roster, caller, out var matchedRole))
        {
            _logger.LogWarning(
                "Publish refused (governance) — caller (user {UserId}, org {OrgId}, wallet {Wallet}) lacks a publish-governance role on register {RegisterId}",
                caller.PlatformUserId, caller.OrganizationId, Short(caller.WalletAddress), registerId);

            return new PublishGateDecision
            {
                Outcome = PublishGateOutcome.Forbidden,
                ExecDefHash = execDefHash,
                Reason = "You do not hold a publish-governance role (Owner, Admin, or Designer) on the target register.",
            };
        }

        // ---- 2) Rehearsal SOFT gate (FR-032/D4) -----------------------------------------
        var pass = await _passStore.GetLatestAsync(blueprintId, execDefHash, cancellationToken);
        if (pass is not null)
        {
            _logger.LogInformation(
                "Publish gate passed for blueprint {BlueprintId} on register {RegisterId} (role {Role}, rehearsal pass {PassId})",
                blueprintId, registerId, matchedRole, pass.Id);

            return new PublishGateDecision
            {
                Outcome = PublishGateOutcome.Proceed,
                ExecDefHash = execDefHash,
            };
        }

        if (!overrideConfirmed)
        {
            _logger.LogInformation(
                "Publish blocked (rehearsal soft gate) for blueprint {BlueprintId} hash {Hash} — no matching rehearsal pass and no override confirmed",
                blueprintId, execDefHash);

            return new PublishGateDecision
            {
                Outcome = PublishGateOutcome.RehearsalRequired,
                ExecDefHash = execDefHash,
            };
        }

        // Caller already passed the HARD governance check, so they hold the publish-governance
        // authority required to override the soft gate.
        _logger.LogInformation(
            "Publish soft gate overridden for blueprint {BlueprintId} hash {Hash} by user {UserId} (role {Role})",
            blueprintId, execDefHash, caller.PlatformUserId, matchedRole);

        return new PublishGateDecision
        {
            Outcome = PublishGateOutcome.ProceedWithOverride,
            ExecDefHash = execDefHash,
        };
    }

    /// <summary>
    /// Determines whether the caller maps to a roster member holding a publish-governance role.
    /// The roster member subject is a <c>did:sorcha:w:{walletId}</c> DID, so the caller's wallet
    /// address is the primary match key (substring within the DID), with the organisation id as a
    /// fallback. Mirrors the convenience UI check in <c>PublishBlueprintWizard</c>.
    /// </summary>
    private static bool CallerHoldsPublishingRole(
        GovernanceRosterResponse? roster,
        PublishCaller caller,
        out string? matchedRole)
    {
        matchedRole = null;
        if (roster?.Members is null || roster.Members.Count == 0)
        {
            // No governance roster recorded — there is no authority to assert against, so the
            // hard gate cannot be satisfied. Fail closed (FR-027: refuse with a clear reason).
            return false;
        }

        foreach (var member in roster.Members)
        {
            if (!PublishingRoles.Contains(member.Role))
            {
                continue;
            }

            if (SubjectMatchesCaller(member.Subject, caller))
            {
                matchedRole = member.Role;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Matches a roster member subject DID against the caller identity. Prefers the wallet
    /// address (the roster subject is wallet-DID shaped), falls back to the organisation id.
    /// </summary>
    private static bool SubjectMatchesCaller(string subject, PublishCaller caller)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(caller.WalletAddress)
            && subject.Contains(caller.WalletAddress, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (!string.IsNullOrWhiteSpace(caller.OrganizationId)
            && subject.Contains(caller.OrganizationId, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private static string Short(string? value) =>
        string.IsNullOrEmpty(value) ? "—" : value[..Math.Min(8, value.Length)];
}
