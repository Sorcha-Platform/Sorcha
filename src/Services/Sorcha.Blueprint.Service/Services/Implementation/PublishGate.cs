// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.OrgInfo;
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
/// the endpoint.
/// </summary>
/// <remarks>
/// <para>
/// A roster subject is a <c>did:sorcha:w:{walletId}</c> DID, so matching is by wallet address.
/// Two addresses can satisfy it, and they are NOT equivalent:
/// </para>
/// <list type="bullet">
///   <item>
///     <see cref="WalletAddress"/> — the caller's OWN linked wallet (JWT <c>wallet_address</c>).
///     An individual attestation: this person was put on the roster.
///   </item>
///   <item>
///     <see cref="OrganizationId"/> — resolved by the gate to the organisation's canonical wallet.
///     An attestation of the ORGANISATION, which is what <c>#1525</c> made the normal case: the org
///     admin creates the org's signing wallet, the register is owned by it, and no user token ever
///     carries that address. Without this the org admin cannot publish to their own org's register
///     and neither can anyone else (#1620).
///   </item>
/// </list>
/// <para>
/// Because the second is authority held by the organisation rather than by the person, it is gated
/// on <see cref="HoldsOrgPublishRole"/>. Matching the org wallet for ANY member would silently grant
/// publish to every user in the org — a wider change than the outage it fixes.
/// </para>
/// </remarks>
/// <param name="PlatformUserId">The caller's platform user id (JWT <c>sub</c>), used to attribute an override.</param>
/// <param name="OrganizationId">The caller's organisation id (JWT <c>org_id</c>). Resolved to the org's canonical wallet.</param>
/// <param name="WalletAddress">The caller's own linked wallet address (JWT <c>wallet_address</c>). May be null — it is absent unless the user has a wallet link.</param>
/// <param name="HoldsOrgPublishRole">Whether the caller holds an org role that may act for the organisation (Administrator or Designer). Gates the org-wallet match ONLY; an individually-attested caller does not need it.</param>
public readonly record struct PublishCaller(
    Guid PlatformUserId,
    string? OrganizationId,
    string? WalletAddress,
    bool HoldsOrgPublishRole = false);

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
    private readonly IOrgInfoClient? _orgInfoClient;
    private readonly ExecutableDefinitionHasher _hasher;
    private readonly ILogger<PublishGate> _logger;

    /// <summary>Initialises a new instance of the <see cref="PublishGate"/> class.</summary>
    /// <param name="orgInfoClient">
    /// Resolves the caller's organisation to its canonical wallet address, so a register owned by
    /// the ORG's signing wallet (#1525, and what the UI creates) can be published to by that org's
    /// administrators. Optional: when absent the gate simply cannot make the organisational match
    /// and falls back to individual attestation only — fail-closed, never fail-open.
    /// </param>
    public PublishGate(
        IBlueprintStore blueprintStore,
        IRegisterServiceClient registerClient,
        IRehearsalPassStore passStore,
        ILogger<PublishGate> logger,
        IOrgInfoClient? orgInfoClient = null,
        ExecutableDefinitionHasher? hasher = null)
    {
        _blueprintStore = blueprintStore ?? throw new ArgumentNullException(nameof(blueprintStore));
        _registerClient = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        _passStore = passStore ?? throw new ArgumentNullException(nameof(passStore));
        _orgInfoClient = orgInfoClient;
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

        // Free matches first — the caller's own linked wallet, or an org-DID subject. Only if none
        // of those hold do we pay for a Tenant round trip to resolve the organisation's wallet.
        // Resolving eagerly put a network call on EVERY publish authorisation, which is both
        // wasteful and, where Tenant is slow or unreachable, a retry cycle on the hot path.
        var authorised = CallerHoldsPublishingRole(roster, caller, orgWalletAddress: null, out var matchedRole, out var matchedVia);

        if (!authorised)
        {
            var orgWallet = await ResolveOrgWalletAsync(caller, cancellationToken);
            if (orgWallet is not null)
            {
                authorised = CallerHoldsPublishingRole(roster, caller, orgWallet, out matchedRole, out matchedVia);
            }
        }

        if (!authorised)
        {
            _logger.LogWarning(
                "Publish refused (governance) — caller (user {UserId}, org {OrgId}, own wallet {Wallet}, org publish role {HasRole}) "
                + "lacks a publish-governance role on register {RegisterId}",
                caller.PlatformUserId, caller.OrganizationId, Short(caller.WalletAddress),
                caller.HoldsOrgPublishRole, registerId);

            return new PublishGateDecision
            {
                Outcome = PublishGateOutcome.Forbidden,
                ExecDefHash = execDefHash,
                Reason = "You do not hold a publish-governance role (Owner, Admin, or Designer) on the target register.",
            };
        }

        _logger.LogInformation(
            "Publish governance satisfied for register {RegisterId} — role {Role}, matched via {MatchedVia}",
            registerId, matchedRole, matchedVia);

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
    /// Resolves the caller's organisation to its canonical wallet address — the address the
    /// platform uses for <c>did:sorcha:org:{address}</c> and for register ownership.
    /// </summary>
    /// <remarks>
    /// Skipped entirely unless the caller holds an org publish role, so a lookup is never performed
    /// for a caller who could not use the result anyway. A failure to resolve returns null and the
    /// caller falls back to individual attestation: this can only ever REFUSE a publish that would
    /// otherwise have been allowed, never allow one that should be refused.
    /// </remarks>
    private async Task<string?> ResolveOrgWalletAsync(PublishCaller caller, CancellationToken ct)
    {
        if (!caller.HoldsOrgPublishRole) return null;
        if (_orgInfoClient is null) return null;
        if (!Guid.TryParse(caller.OrganizationId, out var orgId)) return null;

        try
        {
            return await _orgInfoClient.ResolveCanonicalWalletAddressAsync(orgId, ct);
        }
        catch (Exception ex)
        {
            // Never let a Tenant lookup failure decide authority in the permissive direction.
            _logger.LogWarning(ex, "Could not resolve the canonical wallet for org {OrgId}", caller.OrganizationId);
            return null;
        }
    }

    /// <summary>
    /// Determines whether the caller maps to a roster member holding a publish-governance role.
    /// </summary>
    /// <remarks>
    /// A roster subject is a <c>did:sorcha:w:{walletId}</c> DID, so every match is by wallet
    /// address. Two addresses can satisfy it — the caller's own linked wallet (an individual
    /// attestation) and the organisation's canonical wallet (an attestation of the org). The second
    /// is gated on <see cref="PublishCaller.HoldsOrgPublishRole"/>: it is authority held by the
    /// organisation, and honouring it for any member would grant publish to every user in the org.
    /// </remarks>
    private static bool CallerHoldsPublishingRole(
        GovernanceRosterResponse? roster,
        PublishCaller caller,
        string? orgWalletAddress,
        out string? matchedRole,
        out string matchedVia)
    {
        matchedRole = null;
        matchedVia = "no match";

        if (roster?.Members is null || roster.Members.Count == 0)
        {
            // No governance roster recorded — there is no authority to assert against, so the
            // hard gate cannot be satisfied. Fail closed (FR-027: refuse with a clear reason).
            matchedVia = "no roster";
            return false;
        }

        foreach (var member in roster.Members)
        {
            if (!PublishingRoles.Contains(member.Role))
            {
                continue;
            }

            var via = SubjectMatchesCaller(member.Subject, caller, orgWalletAddress);
            if (via is not null)
            {
                matchedRole = member.Role;
                matchedVia = via;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Matches a roster member subject DID against the caller identity, returning HOW it matched
    /// (for the audit log), or null when it does not.
    /// </summary>
    private static string? SubjectMatchesCaller(string subject, PublishCaller caller, string? orgWalletAddress)
    {
        if (string.IsNullOrWhiteSpace(subject))
        {
            return null;
        }

        // The caller's own linked wallet: they were individually attested on this roster. Needs no
        // org role — being named on the roster IS the authority.
        if (!string.IsNullOrWhiteSpace(caller.WalletAddress)
            && subject.Contains(caller.WalletAddress, StringComparison.OrdinalIgnoreCase))
        {
            return "own wallet";
        }

        // The ORGANISATION's canonical wallet (#1620). Only reachable when the caller holds an org
        // publish role — ResolveOrgWalletAsync returns null otherwise, so this cannot fire for an
        // ordinary member. This is the path that makes a #1525 org-owned register publishable at
        // all; without it the admin who created the org wallet is locked out of their own register.
        if (!string.IsNullOrWhiteSpace(orgWalletAddress)
            && subject.Contains(orgWalletAddress, StringComparison.OrdinalIgnoreCase))
        {
            return "organisation wallet";
        }

        // An ORG DID subject (`did:sorcha:org:{orgId}`) — a real roster shape, not dead code: it
        // attests the organisation directly rather than via its wallet.
        //
        // DELIBERATELY LEFT UNGATED, though it asserts the same organisational authority as the
        // wallet match above and arguably wants the same role check. Adding one here is a
        // behavioural change to a pre-existing, live path — it failed 24 integration tests whose
        // callers publish through this match without an Administrator/Designer role — and #1620 is
        // about making org-WALLET-owned registers publishable, not about re-scoping authority that
        // already worked. Tightening it is a separate decision with its own blast radius; see the
        // note on #1620.
        if (!string.IsNullOrWhiteSpace(caller.OrganizationId)
            && subject.Contains(caller.OrganizationId, StringComparison.OrdinalIgnoreCase))
        {
            return "organisation id";
        }

        return null;
    }

    private static string Short(string? value) =>
        string.IsNullOrEmpty(value) ? "—" : value[..Math.Min(8, value.Length)];
}
