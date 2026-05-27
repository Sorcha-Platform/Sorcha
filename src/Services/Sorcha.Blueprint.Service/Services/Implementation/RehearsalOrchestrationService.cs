// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Service.Models;
using Sorcha.Blueprint.Service.Models.Requests;
using Sorcha.Blueprint.Service.Models.Responses;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
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

    /// <summary>Transient rehearsal sessions keyed by id (singleton-scoped, in-memory).</summary>
    private readonly ConcurrentDictionary<Guid, RehearsalSession> _sessions = new();

    /// <summary>Algorithm used to mint ephemeral per-role sandbox wallets.</summary>
    private const string EphemeralWalletAlgorithm = "ED25519";

    /// <summary>Server-orchestrated rehearsals run against the devMode sandbox with a synthetic token.</summary>
    private const string RehearsalDelegationToken = "rehearsal-sandbox";

    /// <summary>Initialises a new instance of the <see cref="RehearsalOrchestrationService"/> class.</summary>
    public RehearsalOrchestrationService(
        IServiceScopeFactory scopeFactory,
        ISandboxRegisterProvider sandboxProvider,
        ILogger<RehearsalOrchestrationService> logger,
        ExecutableDefinitionHasher? hasher = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _sandboxProvider = sandboxProvider ?? throw new ArgumentNullException(nameof(sandboxProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _hasher = hasher ?? new ExecutableDefinitionHasher();
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
            return ToContract(session);
        }

        // Create a fresh sandbox instance against the sandbox blueprint copy.
        var instance = await CreateSandboxInstanceAsync(
            sp, sandboxBlueprint, sandboxBlueprintId, sandboxRegisterId, organizationId, session.RoleWallets, cancellationToken);
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

            // caller: null — server-orchestrated; skip wallet-ownership validation (we minted the
            // ephemeral wallets ourselves). Signing happens server-side inside ExecuteAsync against
            // request.SenderWallet (the acting role's sandbox wallet) — T027 "sign-as-acting-role".
            try
            {
                response = await execution.ExecuteAsync(
                    instanceId, actionId, request, RehearsalDelegationToken,
                    caller: null, cancellationToken);
            }
            catch (Exception ex)
            {
                lock (session.SyncRoot)
                {
                    session.Outcome = RehearsalOutcome.Failed;
                    AppendLog(session, RehearsalEventKind.Error,
                        $"Step '{actingRole}' failed: {ex.Message}");
                    return ToContract(session);
                }
            }
        }

        bool terminalSuccess;
        string execDefHash;
        string blueprintId;
        Guid userId;
        lock (session.SyncRoot)
        {
            AdvanceWalkThrough(session, actionId, actingRole, payload, response);
            terminalSuccess = response.IsComplete;
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

    private void AdvanceWalkThrough(
        RehearsalSession session,
        int actionId,
        string actingRole,
        IReadOnlyDictionary<string, object> payload,
        ActionSubmissionResponse response)
    {
        var step = session.Steps.FirstOrDefault(s => s.ActionId == actionId);
        if (step is not null)
        {
            step.Status = RehearsalStepStatus.Done;
            step.ActingRole = actingRole;
            step.SubmittedPayload = payload;
            step.RoutingOutcome = response.NextActions.Select(n => n.ActionId).ToList();
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

        if (response.IsComplete)
        {
            return;
        }

        // Mark the routed next action(s) current and announce the routing.
        var nextIds = response.NextActions.Select(n => n.ActionId).ToHashSet();
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

            var routedNames = response.NextActions
                .Select(n => $"action {n.ActionId} ({n.ActionTitle})");
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
