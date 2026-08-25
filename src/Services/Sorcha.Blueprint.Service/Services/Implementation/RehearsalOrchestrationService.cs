// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Models.Requests;
using Sorcha.Blueprint.Service.Models.Responses;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Wallet;
using ContractRehearsal = Sorcha.ServiceClients.Blueprint.Models.Rehearsal;
using Sorcha.ServiceClients.Blueprint.Models;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Feature 142 (T028 / US2) — orchestrates full rehearsals. Holds transient rehearsal state in a
/// process-wide <see cref="ConcurrentDictionary{TKey,TValue}"/> (Redis is a future swap, not a
/// ledger record), so it is registered as a singleton and resolves scoped collaborators
/// (execution pipeline, stores, service clients) per operation via <see cref="IServiceScopeFactory"/>.
/// </summary>
public sealed class RehearsalOrchestrationService : IRehearsalOrchestrationService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ISandboxRegisterProvider _sandboxProvider;
    private readonly ExecutableDefinitionHasher _hasher;
    private readonly ILogger<RehearsalOrchestrationService> _logger;
    private readonly BlueprintDesignerMetrics _metrics;

    /// <summary>Transient rehearsal sessions keyed by id (singleton-scoped, in-memory).</summary>
    private readonly ConcurrentDictionary<Guid, RehearsalSession> _sessions = new();

    /// <summary>Algorithm used to mint ephemeral per-role sandbox wallets.</summary>
    private const string EphemeralWalletAlgorithm = "ED25519";

    /// <summary>Server-orchestrated rehearsals run against the devMode sandbox with a synthetic token.</summary>
    private const string RehearsalDelegationToken = "rehearsal-sandbox";

    /// <summary>
    /// How long a step waits for the InstanceProjector to fold its sealed docket. Default mirrors the
    /// proven walkthrough helper (<c>Wait-SorchaActorReady</c>: 90s timeout, 1s interval) — the
    /// validator's docket-build threshold is 5-10s, so this is generous rather than tight.
    /// </summary>
    private readonly TimeSpan _projectionTimeout;

    /// <summary>Interval between projection polls.</summary>
    private readonly TimeSpan _projectionPollInterval;

    /// <summary>Initialises a new instance of the <see cref="RehearsalOrchestrationService"/> class.</summary>
    public RehearsalOrchestrationService(
        IServiceScopeFactory scopeFactory,
        ISandboxRegisterProvider sandboxProvider,
        BlueprintDesignerMetrics metrics,
        ILogger<RehearsalOrchestrationService> logger,
        ExecutableDefinitionHasher? hasher = null,
        TimeSpan? projectionTimeout = null,
        TimeSpan? projectionPollInterval = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _sandboxProvider = sandboxProvider ?? throw new ArgumentNullException(nameof(sandboxProvider));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _hasher = hasher ?? new ExecutableDefinitionHasher();
        _projectionTimeout = projectionTimeout ?? TimeSpan.FromSeconds(90);
        _projectionPollInterval = projectionPollInterval ?? TimeSpan.FromSeconds(1);
    }

    /// <inheritdoc/>
    public async Task<ContractRehearsal> StartFullAsync(
        string blueprintId,
        string organizationId,
        Guid platformUserId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);
        ArgumentException.ThrowIfNullOrWhiteSpace(organizationId);

        using var scope = _scopeFactory.CreateScope();
        var sp = scope.ServiceProvider;
        var blueprintStore = sp.GetRequiredService<IBlueprintStore>();
        var publishService = sp.GetRequiredService<IPublishService>();
        var walletClient = sp.GetRequiredService<IWalletServiceClient>();

        var blueprint = await blueprintStore.GetAsync(blueprintId)
            ?? throw new KeyNotFoundException($"Blueprint {blueprintId} not found");

        // Rehearse is unavailable while the blueprint has blocking validation errors (Edge Cases).
        var validation = await publishService.ValidateAsync(blueprintId);
        if (!validation.IsValid)
        {
            var errors = validation.ValidationResults
                .Where(v => string.Equals(v.Severity, "error", StringComparison.OrdinalIgnoreCase))
                .Select(v => v.Message)
                .ToList();
            // ValidateAsync sets IsValid=false only when there are errors; guard against empty.
            if (errors.Count == 0)
            {
                errors.Add("Blueprint has blocking validation errors.");
            }

            _logger.LogInformation(
                "Rehearsal start refused for blueprint {BlueprintId}: {ErrorCount} blocking error(s)",
                blueprintId, errors.Count);
            throw new RehearsalValidationException(errors);
        }

        var execDefHash = _hasher.ComputeHash(blueprint);
        var rehearsalId = Guid.NewGuid();

        var session = new RehearsalSession
        {
            RehearsalId = rehearsalId,
            BlueprintId = blueprintId,
            ExecDefHash = execDefHash,
            Mode = RehearsalMode.Full,
            OrganizationId = organizationId,
            StartedByPlatformUserId = platformUserId,
        };

        // T058 — count the start (mode=full, outcome=InProgress); terminal events fire below.
        _metrics.RecordRehearsalStart(session.Mode);

        // T026 — provision/reuse the per-org devMode sandbox register (D1).
        var sandboxRegisterId = await _sandboxProvider.GetOrCreateSandboxRegisterAsync(
            organizationId, cancellationToken);
        session.SandboxRegisterId = sandboxRegisterId;
        AppendLog(session, RehearsalEventKind.Info,
            $"Sandbox provisioned — running against register {Short(sandboxRegisterId)} in dev mode (no real data leaves the sandbox).");

        // T027 — mint one ephemeral sandbox wallet per participant role (D2).
        foreach (var participant in blueprint.Participants)
        {
            var wallet = await walletClient.CreateWalletAsync(
                name: $"sandbox-{rehearsalId}-{participant.Id}",
                algorithm: EphemeralWalletAlgorithm,
                owner: $"sandbox:{rehearsalId}:{participant.Id}",
                tenant: organizationId,
                cancellationToken);
            session.RoleWallets[participant.Id] = wallet.Address;
        }
        AppendLog(session, RehearsalEventKind.Info,
            $"Created {session.RoleWallets.Count} stand-in participant{(session.RoleWallets.Count == 1 ? "" : "s")} so you can act as each role.");

        // Publish a sandbox-specific COPY of the blueprint with each NON-starting participant's
        // ephemeral wallet baked into its walletAddress. The validator resolves sender authorization
        // from the PUBLISHED blueprint on the register (not our in-memory instance state), so a
        // null-wallet reviewer/officer would be rejected with VAL_BP_002. Starting-action senders
        // stay null (open → late-bind on submission; VAL_BP_010 forbids a baked wallet there). The
        // RehearsalPass keeps the ORIGINAL exec-def hash computed above, so the sandbox copy's wallet
        // differences never reach the Go-live gate.
        var sandboxBlueprint = BuildSandboxBlueprint(blueprint, session.RoleWallets, rehearsalId);
        var savedSandboxBlueprint = await blueprintStore.AddAsync(sandboxBlueprint);
        var sandboxBlueprintId = savedSandboxBlueprint.Id;

        var publishResult = await publishService.PublishAsync(sandboxBlueprintId, sandboxRegisterId);
        if (!publishResult.IsSuccess)
        {
            session.Outcome = RehearsalOutcome.Failed;
            var reason = publishResult.Errors is { Length: > 0 }
                ? string.Join("; ", publishResult.Errors)
                : "publish to sandbox failed";
            AppendLog(session, RehearsalEventKind.Error, $"Could not stage the blueprint for rehearsal: {reason}.");
            _sessions[rehearsalId] = session;
            _metrics.RecordRehearsalTerminal(session.Mode, RehearsalOutcome.Failed,
                (DateTimeOffset.UtcNow - session.StartedAt).TotalSeconds);
            return ToContract(session);
        }

        // Create a fresh sandbox instance against the sandbox blueprint copy, PINNED to the
        // definition the publish above just assigned (Feature 195). A rehearsal instance is an
        // instance: without a pin its starting action has nothing to chain from, and its actions
        // could not be resolved against any definition.
        var instance = await CreateSandboxInstanceAsync(
            sp, sandboxBlueprint, sandboxBlueprintId, sandboxRegisterId, organizationId,
            session.RoleWallets, publishResult.PublishedBlueprint!.PublicationTxId, cancellationToken);
        session.InstanceId = instance.Id;

        // Build the ordered walk-through steps from the blueprint actions in flow order.
        BuildSteps(session, blueprint);

        _sessions[rehearsalId] = session;

        _logger.LogInformation(
            "Started full rehearsal {RehearsalId} for blueprint {BlueprintId} on sandbox {RegisterId} (instance {InstanceId})",
            rehearsalId, blueprintId, sandboxRegisterId, instance.Id);

        return ToContract(session);
    }

    /// <inheritdoc/>
    public Task<ContractRehearsal?> GetAsync(Guid rehearsalId, CancellationToken cancellationToken = default)
    {
        if (_sessions.TryGetValue(rehearsalId, out var session))
        {
            lock (session.SyncRoot)
            {
                return Task.FromResult<ContractRehearsal?>(ToContract(session));
            }
        }
        return Task.FromResult<ContractRehearsal?>(null);
    }

    /// <inheritdoc/>
    public Task<ContractRehearsal?> SwitchRoleAsync(
        Guid rehearsalId,
        string role,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(role);

        if (!_sessions.TryGetValue(rehearsalId, out var session))
        {
            return Task.FromResult<ContractRehearsal?>(null);
        }

        lock (session.SyncRoot)
        {
            if (!session.RoleWallets.ContainsKey(role))
            {
                throw new InvalidOperationException(
                    $"Role '{role}' is not a participant in this rehearsal.");
            }

            session.CurrentActingRole = role;
            AppendLog(session, RehearsalEventKind.Info, $"Now acting as '{role}'.");
            return Task.FromResult<ContractRehearsal?>(ToContract(session));
        }
    }

    /// <inheritdoc/>
    public async Task<ContractRehearsal?> SubmitStepAsync(
        Guid rehearsalId,
        int actionId,
        string payloadJson,
        CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(rehearsalId, out var session))
        {
            return null;
        }

        // Snapshot the state we need under the lock, then run the (async) pipeline outside it.
        string actingRole;
        string senderWallet;
        string sandboxRegisterId;
        string instanceId;
        lock (session.SyncRoot)
        {
            if (session.Outcome is RehearsalOutcome.Abandoned)
            {
                throw new InvalidOperationException("Rehearsal has been reset.");
            }
            if (session.Outcome is RehearsalOutcome.Passed)
            {
                // Already terminal — nothing further to submit.
                return ToContract(session);
            }

            var current = session.Steps.FirstOrDefault(s => s.Status == RehearsalStepStatus.Current);
            if (current is null || current.ActionId != actionId)
            {
                throw new InvalidOperationException(
                    $"Action {actionId} is not the current rehearsal step.");
            }

            actingRole = string.IsNullOrWhiteSpace(session.CurrentActingRole)
                ? current.ActingRole
                : session.CurrentActingRole;

            if (!session.RoleWallets.TryGetValue(actingRole, out senderWallet!))
            {
                throw new InvalidOperationException(
                    $"No sandbox wallet for acting role '{actingRole}'.");
            }

            sandboxRegisterId = session.SandboxRegisterId
                ?? throw new InvalidOperationException("Rehearsal has no sandbox register.");
            instanceId = session.InstanceId
                ?? throw new InvalidOperationException("Rehearsal has no sandbox instance.");
            session.CurrentActingRole = actingRole;
        }

        var payload = ParsePayload(payloadJson);

        var request = new ActionSubmissionRequest
        {
            BlueprintId = session.BlueprintId,
            ActionId = actionId.ToString(),
            InstanceId = instanceId,
            SenderWallet = senderWallet,
            RegisterAddress = sandboxRegisterId,
            PayloadData = payload,
        };

        ActionSubmissionResponse response;
        using (var scope = _scopeFactory.CreateScope())
        {
            var execution = scope.ServiceProvider
                .GetRequiredService<IActionExecutionService>();

            // Issue #1284 (H-REHEARSAL) — rehearsal means "walk this workflow as me": build a
            // synthetic caller principal from the rehearsal's own initiator (StartedByPlatformUserId,
            // captured at StartFullAsync) rather than passing null. This used to be hardcoded null,
            // which made ActionExecutionService throw InvalidOperationException for ANY action that
            // declares an x-claim-source binding (it has no caller to resolve live values for) — Full
            // Rehearsal could never pass for such a blueprint, including the AIAS assured-identity
            // template. The fix is semantically right, not a workaround: the claim-source bindings
            // should resolve the REAL live values of the person running the rehearsal, which also
            // makes the rehearsal a truthful dry run of what go-live will stamp.
            //
            // Two claims, deliberately:
            //  - platform_user_id: what ActionExecutionService's x-claim-source resolution reads
            //    (via IPlatformUserClaimsClient.ResolveAsync) to look up live values for THIS user.
            //  - NameIdentifier ("sub"), same value: passing a non-null caller now also activates
            //    ValidateWalletOwnershipAsync (SEC-006), which the null-caller path always skipped
            //    outright. Without a parseable "sub" claim that check throws
            //    UnauthorizedAccessException — trading the #1284 exception for a new one. org_id is
            //    deliberately OMITTED so that check short-circuits to "no participant-based
            //    validation" instead of querying the Participant Service for a sandbox wallet that
            //    has no real participant profile (Feature 103 open-participant walk-in path).
            var rehearsalCaller = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(TokenClaimConstants.PlatformUserId, session.StartedByPlatformUserId.ToString()),
                    new Claim(ClaimTypes.NameIdentifier, session.StartedByPlatformUserId.ToString()),
                ],
                authenticationType: "rehearsal"));

            // Wallet-ownership validation still passes through (see above) but skips the
            // participant-based check (no org_id claim), so it is a no-op for sandbox wallets we
            // minted ourselves — matching the intent of the old "caller: null" comment. Signing
            // happens server-side inside ExecuteAsync against request.SenderWallet (the acting
            // role's sandbox wallet) — T027 "sign-as-acting-role".
            try
            {
                response = await execution.ExecuteAsync(
                    instanceId, actionId, request, RehearsalDelegationToken,
                    caller: rehearsalCaller, cancellationToken);
            }
            catch (Exception ex)
            {
                ContractRehearsal contract;
                lock (session.SyncRoot)
                {
                    session.Outcome = RehearsalOutcome.Failed;
                    AppendLog(session, RehearsalEventKind.Error,
                        $"Step '{actingRole}' failed: {ex.Message}");
                    contract = ToContract(session);
                }
                _metrics.RecordRehearsalTerminal(session.Mode, RehearsalOutcome.Failed,
                    (DateTimeOffset.UtcNow - session.StartedAt).TotalSeconds);
                return contract;
            }
        }

        // ── F145 reconciliation ────────────────────────────────────────────────────────────────
        // ExecuteAsync returns 202. Post-Feature-145 it NEVER reports completion synchronously:
        // `IsComplete` is hard-coded false on every return path (the only `IsComplete = true` in the
        // repo was a TEST fixture), and `NextActions` is hard-coded `[]` — the real routing goes only
        // onto the on-chain RoutingDecision for the InstanceProjector to fold. Reading either from
        // the response meant the rehearsal could never reach a terminal state NOR advance past step 1
        // — for ANY blueprint — so `RehearsalPass` was never written and F142's Go-live gate was
        // unearnable, leaving publish override-only.
        //
        // The authoritative answer lives in the instance projection the InstanceProjector writes when
        // the sandbox docket seals. Sandbox registers DO seal automatically: register creation embeds
        // the local validator on the roster and fires `register:relationship-changed`, which
        // RegisterMonitoringBootstrap consumes to enrol the register for monitoring — no separate
        // step. So we wait for OUR transaction to be folded, then read the projection.
        if (response.AwaitingPresentation)
        {
            // Genuinely mid-flight: the action is blocked on an external credential presentation, so
            // there is no seal to wait for. Leave the step current and report progress unchanged.
            lock (session.SyncRoot)
            {
                AppendLog(session, RehearsalEventKind.Gate,
                    $"Action {actionId} is awaiting a credential presentation.");
                return ToContract(session);
            }
        }

        if (string.IsNullOrEmpty(response.TransactionId))
        {
            return FailStepNamed(session, actingRole,
                $"Step '{actingRole}' could not be confirmed: the submission returned no transaction "
                + "id, so its sealing could not be tracked. Nothing was recorded — retry the step.");
        }

        var projected = await WaitForProjectionAsync(instanceId, response.TransactionId, cancellationToken);

        if (projected is null)
        {
            // NAMED failure, deliberately distinct from "your blueprint is broken": the submission was
            // accepted and may still seal later, but this rehearsal cannot vouch for it. No
            // RehearsalPass is written — an unverified pass would be worse than no pass.
            return FailStepNamed(session, actingRole,
                $"Step '{actingRole}' was submitted (tx {Short(response.TransactionId)}) but the "
                + $"sandbox register did not seal it within {_projectionTimeout.TotalSeconds:0}s, so "
                + "the rehearsal could not confirm it. This is a sealing delay, not a blueprint "
                + "problem — no pass was recorded. Retry the rehearsal.");
        }

        if (projected.State == InstanceState.Rejected)
        {
            return FailStepNamed(session, actingRole,
                $"Step '{actingRole}' was rejected on the sandbox register — the workflow reached a "
                + "terminal rejection, so this version is not cleared for Go live.");
        }

        bool terminalSuccess;
        string execDefHash;
        string blueprintId;
        Guid userId;
        lock (session.SyncRoot)
        {
            // Terminal and routing both come from the projection now. InstanceProjection sets
            // State = Completed exactly when CurrentActionIds is empty, i.e. every branch reached a
            // route-graph terminal — which is precisely what the F142 gate needs to attest.
            terminalSuccess = projected.State == InstanceState.Completed;

            AdvanceWalkThrough(
                session, actionId, actingRole, payload, response,
                nextActionIds: projected.CurrentActionIds, isComplete: terminalSuccess);

            execDefHash = session.ExecDefHash;
            blueprintId = session.BlueprintId;
            userId = session.StartedByPlatformUserId;
            if (terminalSuccess)
            {
                session.Outcome = RehearsalOutcome.Passed;
            }
        }

        if (terminalSuccess)
        {
            // D4/FR-032 — record exactly one RehearsalPass for this exec-def hash.
            using var scope = _scopeFactory.CreateScope();
            var passStore = scope.ServiceProvider.GetRequiredService<IRehearsalPassStore>();
            await passStore.RecordAsync(new RehearsalPass
            {
                BlueprintId = blueprintId,
                ExecDefHash = execDefHash,
                RehearsedAt = DateTimeOffset.UtcNow,
                RehearsedByPlatformUserId = userId,
                SandboxRegisterId = sandboxRegisterId,
            }, cancellationToken);

            lock (session.SyncRoot)
            {
                AppendLog(session, RehearsalEventKind.Gate,
                    "Rehearsal passed — this version is cleared for Go live.");
            }

            _logger.LogInformation(
                "Rehearsal {RehearsalId} passed; recorded RehearsalPass for blueprint {BlueprintId} hash {Hash}",
                rehearsalId, blueprintId, execDefHash);

            _metrics.RecordRehearsalTerminal(session.Mode, RehearsalOutcome.Passed,
                (DateTimeOffset.UtcNow - session.StartedAt).TotalSeconds);
        }

        lock (session.SyncRoot)
        {
            return ToContract(session);
        }
    }

    /// <inheritdoc/>
    public Task<bool> ResetAsync(Guid rehearsalId, CancellationToken cancellationToken = default)
    {
        if (!_sessions.TryGetValue(rehearsalId, out var session))
        {
            // Idempotent: nothing to reset.
            return Task.FromResult(false);
        }

        var wasInProgress = session.Outcome == RehearsalOutcome.InProgress;
        var sandboxRegisterId = session.SandboxRegisterId;

        lock (session.SyncRoot)
        {
            // Discard the rehearsal instance state + ephemeral wallet map (D1). The wallets are
            // abandoned (never reused); the sandbox register persists.
            session.RoleWallets.Clear();
            session.InstanceId = null;
            session.CurrentActingRole = string.Empty;
            session.Outcome = RehearsalOutcome.Abandoned;
            foreach (var step in session.Steps)
            {
                step.Status = RehearsalStepStatus.Pending;
                step.SubmittedPayload = null;
                step.RoutingOutcome = null;
            }
            AppendLog(session, RehearsalEventKind.Info,
                "Rehearsal reset — stand-in participants discarded; the sandbox register is kept for next time.");
        }

        if (wasInProgress)
        {
            _metrics.RecordRehearsalTerminal(session.Mode, RehearsalOutcome.Abandoned,
                (DateTimeOffset.UtcNow - session.StartedAt).TotalSeconds);
        }

        // T058 — operator-visible audit line for sandbox discard.
        _logger.LogInformation(
            "Sandbox rehearsal discarded: rehearsal={RehearsalId} register={RegisterId}",
            rehearsalId, sandboxRegisterId);

        return Task.FromResult(true);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void BuildSteps(RehearsalSession session, BlueprintModel blueprint)
    {
        // Flow order: starting actions first, then the remaining actions by id. This matches the
        // walk-through order the rehearsal presents (the engine's routing decides the live next
        // action; the step list is the authoring-order view of the journey).
        var ordered = blueprint.Actions
            .OrderByDescending(a => a.IsStartingAction)
            .ThenBy(a => a.Id)
            .ToList();

        for (var i = 0; i < ordered.Count; i++)
        {
            var action = ordered[i];
            session.Steps.Add(new RehearsalSessionStep
            {
                ActionId = action.Id,
                ActingRole = action.Sender,
                Status = i == 0 ? RehearsalStepStatus.Current : RehearsalStepStatus.Pending,
            });
        }

        if (session.Steps.Count > 0)
        {
            session.CurrentActingRole = session.Steps[0].ActingRole;
        }
    }

    /// <summary>
    /// Polls the instance projection until the InstanceProjector has folded
    /// <paramref name="transactionId"/>, or the timeout elapses. Returns the projected instance, or
    /// <c>null</c> on timeout.
    /// </summary>
    /// <remarks>
    /// Matching on <c>LastAppliedTxId</c> — the projector's idempotency watermark — rather than on
    /// state alone, so we observe OUR submission's effect and never mistake a previous step's
    /// projection for this one's.
    /// </remarks>
    private async Task<Instance?> WaitForProjectionAsync(
        string instanceId, string transactionId, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + _projectionTimeout;

        while (true)
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var instanceStore = scope.ServiceProvider.GetRequiredService<IInstanceStore>();
                var instance = await instanceStore.GetAsync(instanceId, cancellationToken);

                if (instance is not null
                    && string.Equals(instance.LastAppliedTxId, transactionId, StringComparison.Ordinal))
                {
                    return instance;
                }
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                _logger.LogWarning(
                    "Rehearsal step timed out waiting for the projection of tx {TxId} on instance "
                    + "{InstanceId} after {Timeout}s",
                    transactionId, instanceId, _projectionTimeout.TotalSeconds);
                return null;
            }

            await Task.Delay(_projectionPollInterval, cancellationToken);
        }
    }

    /// <summary>
    /// Marks the rehearsal failed with a specific, actionable reason and records the terminal metric.
    /// Deliberately never writes a <c>RehearsalPass</c>.
    /// </summary>
    private ContractRehearsal FailStepNamed(RehearsalSession session, string actingRole, string reason)
    {
        ContractRehearsal contract;
        lock (session.SyncRoot)
        {
            session.Outcome = RehearsalOutcome.Failed;
            AppendLog(session, RehearsalEventKind.Error, reason);
            contract = ToContract(session);
        }

        _metrics.RecordRehearsalTerminal(session.Mode, RehearsalOutcome.Failed,
            (DateTimeOffset.UtcNow - session.StartedAt).TotalSeconds);

        _logger.LogWarning("Rehearsal step failed for role {ActingRole}: {Reason}", actingRole, reason);
        return contract;
    }

    private void AdvanceWalkThrough(
        RehearsalSession session,
        int actionId,
        string actingRole,
        IReadOnlyDictionary<string, object> payload,
        ActionSubmissionResponse response,
        IReadOnlyList<int> nextActionIds,
        bool isComplete)
    {
        var step = session.Steps.FirstOrDefault(s => s.ActionId == actionId);
        if (step is not null)
        {
            step.Status = RehearsalStepStatus.Done;
            step.ActingRole = actingRole;
            step.SubmittedPayload = payload;
            // From the projection, not response.NextActions — the latter is always empty post-F145.
            step.RoutingOutcome = nextActionIds.ToList();
        }

        AppendLog(session, RehearsalEventKind.Gate,
            $"'{actingRole}' submitted action {actionId} — validation passed.");
        AppendLog(session, RehearsalEventKind.Sealed,
            $"Transaction sealed on the sandbox register (tx {Short(response.TransactionId)}).");

        if (response.IssuedCredential is not null || !string.IsNullOrEmpty(response.IssuedCredentialId))
        {
            var credType = response.IssuedCredential?.CredentialType ?? "credential";
            AppendLog(session, RehearsalEventKind.Delivered,
                $"A {credType} was issued and delivered to the recipient.");
        }

        if (isComplete)
        {
            return;
        }

        // Mark the routed next action(s) current and announce the routing. The ids come from the
        // instance projection (the ledger's answer), so names are resolved from the session's own
        // step list rather than from the response, which no longer carries them.
        var nextIds = nextActionIds.ToHashSet();
        if (nextIds.Count > 0)
        {
            foreach (var next in session.Steps.Where(s => nextIds.Contains(s.ActionId)
                && s.Status == RehearsalStepStatus.Pending))
            {
                next.Status = RehearsalStepStatus.Current;
            }

            var nextStep = session.Steps.FirstOrDefault(s => s.Status == RehearsalStepStatus.Current);
            if (nextStep is not null)
            {
                session.CurrentActingRole = nextStep.ActingRole;
            }

            var routedNames = nextIds
                .OrderBy(id => id)
                .Select(id =>
                {
                    var role = session.Steps.FirstOrDefault(s => s.ActionId == id)?.ActingRole;
                    return string.IsNullOrEmpty(role) ? $"action {id}" : $"action {id} ({role})";
                });
            AppendLog(session, RehearsalEventKind.Routed,
                $"Routed to {string.Join(", ", routedNames)}.");
        }
    }

    /// <summary>
    /// Builds a sandbox-specific copy of the blueprint (fresh id, deep-cloned via JSON round-trip so
    /// the caller's draft is never mutated) with each NON-starting participant's ephemeral wallet
    /// baked into its <c>WalletAddress</c>. Starting-action senders are left null so open-participant
    /// late-binding still fires (and VAL_BP_010 is not tripped). This copy is what gets published to
    /// the sandbox register; the original blueprint and its exec-def hash are untouched.
    /// </summary>
    private static BlueprintModel BuildSandboxBlueprint(
        BlueprintModel original,
        IReadOnlyDictionary<string, string> roleWallets,
        Guid rehearsalId)
    {
        var json = System.Text.Json.JsonSerializer.Serialize(original);
        var clone = System.Text.Json.JsonSerializer.Deserialize<BlueprintModel>(json)
            ?? throw new InvalidOperationException("Failed to clone blueprint for sandbox rehearsal.");

        clone.Id = $"rehearsal-{rehearsalId:N}";

        var startingSenders = original.Actions
            .Where(a => a.IsStartingAction)
            .Select(a => a.Sender)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var participant in clone.Participants)
        {
            if (startingSenders.Contains(participant.Id))
            {
                participant.WalletAddress = null;
                continue;
            }

            if (roleWallets.TryGetValue(participant.Id, out var wallet))
            {
                participant.WalletAddress = wallet;
            }
        }

        clone.Metadata ??= new Dictionary<string, string>();
        clone.Metadata["rehearsalSandboxClone"] = "true";
        return clone;
    }

    private async Task<Instance> CreateSandboxInstanceAsync(
        IServiceProvider sp,
        BlueprintModel blueprint,
        string blueprintId,
        string sandboxRegisterId,
        string organizationId,
        IReadOnlyDictionary<string, string> roleWallets,
        string definitionTxId,
        CancellationToken cancellationToken)
    {
        var instanceStore = sp.GetRequiredService<IInstanceStore>();

        var startingActions = blueprint.Actions
            .Where(a => a.IsStartingAction)
            .Select(a => a.Id)
            .ToList();
        if (startingActions.Count == 0 && blueprint.Actions.Count > 0)
        {
            startingActions = [blueprint.Actions.First().Id];
        }

        // Pre-bind the ephemeral wallet of every NON-starting participant into the instance so the
        // validator can resolve their sender authorization (VAL_BP_002). Starting-action senders are
        // left UNBOUND so the engine's open-participant late-binding still fires on their first
        // submission (binding them to the same ephemeral wallet we sign with). This is runtime
        // instance state only — it does not affect the executable-definition hash / RehearsalPass.
        var startingSenders = blueprint.Actions
            .Where(a => a.IsStartingAction)
            .Select(a => a.Sender)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var preBoundWallets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (role, wallet) in roleWallets)
        {
            if (!startingSenders.Contains(role))
            {
                preBoundWallets[role] = wallet;
            }
        }

        var instance = new Instance
        {
            Id = Guid.NewGuid().ToString(),
            BlueprintId = blueprintId,
            BlueprintVersion = 1,
            BlueprintDefinitionTxId = definitionTxId,
            RegisterId = sandboxRegisterId,
            CurrentActionIds = startingActions,
            ParticipantWallets = preBoundWallets,
            State = InstanceState.Active,
            TenantId = organizationId,
            Metadata = new Dictionary<string, string>
            {
                ["BlueprintTitle"] = blueprint.Title,
                ["rehearsal"] = "true",
            },
        };

        return await instanceStore.CreateAsync(instance, cancellationToken);
    }

    private static Dictionary<string, object> ParsePayload(string payloadJson)
    {
        if (string.IsNullOrWhiteSpace(payloadJson))
        {
            return new Dictionary<string, object>();
        }

        try
        {
            using var doc = JsonDocument.Parse(payloadJson);
            var result = new Dictionary<string, object>();
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    result[prop.Name] = prop.Value.Clone();
                }
            }
            return result;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Invalid step payload JSON: {ex.Message}", ex);
        }
    }

    private static void AppendLog(RehearsalSession session, RehearsalEventKind kind, string message)
    {
        session.Log.Add(new RehearsalEvent
        {
            At = DateTimeOffset.UtcNow,
            Kind = kind,
            Message = message,
        });
    }

    private static ContractRehearsal ToContract(RehearsalSession session) => new()
    {
        RehearsalId = session.RehearsalId,
        BlueprintId = session.BlueprintId,
        ExecDefHash = session.ExecDefHash,
        Mode = session.Mode,
        SandboxRegisterId = session.SandboxRegisterId,
        CurrentActingRole = session.CurrentActingRole,
        Outcome = session.Outcome,
        Steps = session.Steps.Select(s => new RehearsalStep
        {
            ActionId = s.ActionId,
            ActingRole = s.ActingRole,
            Status = s.Status,
        }).ToList(),
        Log = session.Log.ToList(),
    };

    private static string Short(string? value) =>
        string.IsNullOrEmpty(value) ? "—" : value[..Math.Min(8, value.Length)];
}
