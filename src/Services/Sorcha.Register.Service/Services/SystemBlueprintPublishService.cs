// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using Sorcha.Register.Core.Services;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Constants;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.Wallet.Contracts.Constants;

namespace Sorcha.Register.Service.Services;

/// <summary>What an operator publish of a system blueprint decided (Feature 197, #1466).</summary>
public enum SystemBlueprintPublishOutcome
{
    /// <summary>The blueprint is not a catalogued system blueprint, or the image ships no template for it.</summary>
    NotFound,

    /// <summary>This node's blueprint-publish key is not an Active entry on the system register's validator roster.</summary>
    NoPublishingKey,

    /// <summary>The register already holds the image's definition as current; nothing submitted.</summary>
    Noop,

    /// <summary>The image holds an older definition than the register; publishing would roll the register back.</summary>
    RefusedRollback,

    /// <summary>The register's state could not be read, so a publish would be blind.</summary>
    StateUnknown,

    /// <summary>The caller's expected current publication is no longer the register's current one.</summary>
    RefusedConcurrency,

    /// <summary>Everything would have been accepted; nothing submitted.</summary>
    DryRun,

    /// <summary>The image's definition was submitted to the register.</summary>
    Submitted
}

/// <summary>Outcome tag values of the publish counter.</summary>
public static class PublishOutcomeNames
{
    /// <summary>Submitted.</summary>
    public const string Published = "published";

    /// <summary>No-op.</summary>
    public const string Noop = "noop";

    /// <summary>Dry run.</summary>
    public const string DryRun = "dry_run";

    /// <summary>Rollback refused.</summary>
    public const string RefusedRollback = "refused_rollback";

    /// <summary>Concurrency refused.</summary>
    public const string RefusedConcurrency = "refused_concurrency";

    /// <summary>No publishing key.</summary>
    public const string RefusedNoKey = "refused_no_key";

    /// <summary>State unknown.</summary>
    public const string RefusedUnknown = "refused_unknown";

    /// <summary>Not found.</summary>
    public const string NotFound = "not_found";
}

/// <summary>The decision of a publish request.</summary>
/// <param name="Outcome">What was decided.</param>
/// <param name="DriftState">Drift classification at decision time; null when not reached.</param>
/// <param name="CurrentPublicationTxId">The register's current publication, when known.</param>
/// <param name="CandidatePublicationTxId">The id a publish of the image's definition produces, when known.</param>
/// <param name="TransactionId">The submitted publication's transaction id; set only for <see cref="SystemBlueprintPublishOutcome.Submitted"/>.</param>
/// <param name="Reason">Human-readable reason, for problem details and the refusal audit.</param>
public sealed record PublishDecision(
    SystemBlueprintPublishOutcome Outcome,
    SystemBlueprintDriftState? DriftState,
    string? CurrentPublicationTxId,
    string? CandidatePublicationTxId,
    string? TransactionId,
    string Reason);

/// <summary>Operator-initiated publish of a shipped system blueprint (Feature 197, #1466).</summary>
public interface ISystemBlueprintPublishService
{
    /// <summary>
    /// Publishes the image's definition of <paramref name="blueprintId"/> unless a guard refuses it.
    /// Every refusal and a dry run submit nothing.
    /// </summary>
    /// <param name="blueprintId">System blueprint id.</param>
    /// <param name="dryRun">Decide, but submit nothing.</param>
    /// <param name="expectedCurrent">When set, the publication the caller believes is current.</param>
    /// <param name="operatorId">Identity recorded as the publisher.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<PublishDecision> PublishAsync(
        string blueprintId,
        bool dryRun,
        string? expectedCurrent,
        string operatorId,
        CancellationToken cancellationToken = default);
}

/// <summary>Default <see cref="ISystemBlueprintPublishService"/>. Scoped.</summary>
public sealed class SystemBlueprintPublishService : ISystemBlueprintPublishService
{
    private static readonly Meter Meter = new(SystemBlueprintMetrics.MeterName);
    private static readonly Counter<long> PublishCounter = Meter.CreateCounter<long>(
        SystemBlueprintMetrics.PublishCounterName,
        description: "Operator system blueprint publish attempts by outcome");

    private const string ZeroHash = "0000000000000000000000000000000000000000000000000000000000000000";

    private readonly ISystemBlueprintCatalogSource _catalog;
    private readonly SystemRegisterService _systemRegister;
    private readonly ISystemWalletSigningService _signingService;
    private readonly IGovernanceRosterService _rosterService;
    private readonly TimeProvider _time;
    private readonly ILogger<SystemBlueprintPublishService> _logger;

    /// <summary>Creates the service.</summary>
    public SystemBlueprintPublishService(
        ISystemBlueprintCatalogSource catalog,
        SystemRegisterService systemRegister,
        ISystemWalletSigningService signingService,
        IGovernanceRosterService rosterService,
        TimeProvider time,
        ILogger<SystemBlueprintPublishService> logger)
    {
        _catalog = catalog ?? throw new ArgumentNullException(nameof(catalog));
        _systemRegister = systemRegister ?? throw new ArgumentNullException(nameof(systemRegister));
        _signingService = signingService ?? throw new ArgumentNullException(nameof(signingService));
        _rosterService = rosterService ?? throw new ArgumentNullException(nameof(rosterService));
        _time = time ?? throw new ArgumentNullException(nameof(time));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<PublishDecision> PublishAsync(
        string blueprintId,
        bool dryRun,
        string? expectedCurrent,
        string operatorId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);
        ArgumentException.ThrowIfNullOrWhiteSpace(operatorId);

        if (!SystemBlueprintCatalog.Ids.Contains(blueprintId, StringComparer.Ordinal)
            || _catalog.TryLoad(blueprintId) is not { } definition)
        {
            return Decide(PublishOutcomeNames.NotFound,
                new PublishDecision(SystemBlueprintPublishOutcome.NotFound, null, null, null, null,
                    "Not a system blueprint shipped with this node's image."), blueprintId);
        }

        if (!await HoldsActivePublishingKeyAsync(cancellationToken))
        {
            return Decide(PublishOutcomeNames.RefusedNoKey,
                new PublishDecision(SystemBlueprintPublishOutcome.NoPublishingKey, null, null, null, null,
                    "This node's blueprint-publish key is not an active entry on the system register validator roster."),
                blueprintId);
        }

        var candidate = _catalog.TryComputePublicationId(blueprintId);
        SystemBlueprintDriftEntry drift;
        try
        {
            var publications = await _systemRegister.GetPublicationsAsync(blueprintId, cancellationToken);
            drift = SystemBlueprintDriftReporter.Classify(blueprintId, candidate, publications, _time.GetUtcNow());
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "System blueprint {BlueprintId} state could not be read for publish", blueprintId);
            return Decide(PublishOutcomeNames.RefusedUnknown,
                new PublishDecision(SystemBlueprintPublishOutcome.StateUnknown, SystemBlueprintDriftState.Unknown,
                    null, candidate, null, "The system register's state for this blueprint could not be read."),
                blueprintId);
        }

        PublishDecision Result(SystemBlueprintPublishOutcome outcome, string reason, string? txId = null)
            => new(outcome, drift.State, drift.CurrentPublicationTxId, candidate, txId, reason);

        switch (drift.State)
        {
            case SystemBlueprintDriftState.InSync:
                return Decide(PublishOutcomeNames.Noop,
                    Result(SystemBlueprintPublishOutcome.Noop, "The register already holds this definition as current."),
                    blueprintId);
            case SystemBlueprintDriftState.ImageBehind:
                return Decide(PublishOutcomeNames.RefusedRollback,
                    Result(SystemBlueprintPublishOutcome.RefusedRollback,
                        "The image holds an older definition than the register; publishing would roll it back."),
                    blueprintId);
            case SystemBlueprintDriftState.Unknown:
                return Decide(PublishOutcomeNames.RefusedUnknown,
                    Result(SystemBlueprintPublishOutcome.StateUnknown,
                        "The state of this blueprint is unknown; publishing blind is unsafe."),
                    blueprintId);
        }

        if (expectedCurrent is not null
            && !string.Equals(expectedCurrent, drift.CurrentPublicationTxId, StringComparison.OrdinalIgnoreCase))
        {
            return Decide(PublishOutcomeNames.RefusedConcurrency,
                Result(SystemBlueprintPublishOutcome.RefusedConcurrency,
                    "The register's current publication is not the one the caller expected."),
                blueprintId);
        }

        if (dryRun)
        {
            return Decide(PublishOutcomeNames.DryRun,
                Result(SystemBlueprintPublishOutcome.DryRun, "Dry run; nothing was submitted."), blueprintId);
        }

        var entry = await _systemRegister.PublishBlueprintAsync(
            blueprintId,
            definition,
            operatorId,
            new Dictionary<string, string> { ["seedReason"] = "operator" },
            cancellationToken);

        return Decide(PublishOutcomeNames.Published,
            Result(SystemBlueprintPublishOutcome.Submitted, "Submitted.", entry.PublicationTransactionId),
            blueprintId);
    }

    private PublishDecision Decide(string outcomeTag, PublishDecision decision, string blueprintId)
    {
        PublishCounter.Add(1, new KeyValuePair<string, object?>("outcome", outcomeTag));
        _logger.LogInformation(
            "System blueprint {BlueprintId} publish decided {Outcome}: {Reason}",
            blueprintId, decision.Outcome, decision.Reason);
        return decision;
    }

    /// <summary>Fail closed: any error deriving the key or reading the roster means no key.</summary>
    private async Task<bool> HoldsActivePublishingKeyAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Same probe-sign precedent the register creation orchestrator uses to learn a node's purpose key.
            var probe = await _signingService.SignAsync(
                registerId: SystemRegisterConstants.SystemRegisterId,
                txId: "publisher-roster-key-derivation",
                payloadHash: ZeroHash,
                derivationPath: SorchaDerivationPaths.BlueprintPublish,
                transactionType: "ValidatorKeyDerivation",
                cancellationToken);
            var nodeKey = Convert.ToBase64String(probe.PublicKey);

            var roster = await _rosterService.GetCurrentRosterAsync(
                SystemRegisterConstants.SystemRegisterId, cancellationToken);

            return roster?.ControlRecord.Validators?.Validators.Any(v =>
                v.Status == ValidatorKeyStatus.Active
                && string.Equals(v.DerivationContext, SorchaDerivationPaths.BlueprintPublish, StringComparison.Ordinal)
                && GovernanceKeyMatcher.Matches(v.PublicKey, nodeKey)) ?? false;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not establish whether this node holds an active blueprint-publish roster key");
            return false;
        }
    }
}
