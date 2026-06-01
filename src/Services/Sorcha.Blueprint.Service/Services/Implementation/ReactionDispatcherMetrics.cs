// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Feature 145 US2 — OpenTelemetry instruments for the <see cref="ReactionDispatcher"/> (meter
/// <c>Sorcha.Blueprint.Reactions</c>, on the ServiceDefaults export allowlist). Tracks idempotent,
/// role-gated side effects (notification + durable inbox writes): how many fired, how many were
/// idempotently skipped (already done — replay/restart), and how many were skipped because this node
/// is not entitled (does not host the target wallet). Each instrument is tagged by reaction
/// <c>kind</c>.
/// </summary>
public sealed class ReactionDispatcherMetrics
{
    public const string MeterName = "Sorcha.Blueprint.Reactions";

    private readonly Counter<long> _dispatched;
    private readonly Counter<long> _idempotentSkip;
    private readonly Counter<long> _entitlementSkip;

    /// <summary>Initialises the reaction-dispatcher metrics on the <see cref="MeterName"/> meter.</summary>
    public ReactionDispatcherMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);

        _dispatched = meter.CreateCounter<long>(
            "reaction_dispatched_total",
            description: "Side-effect reactions performed (notification / inbox) on the entitled node");

        _idempotentSkip = meter.CreateCounter<long>(
            "reaction_idempotent_skip_total",
            description: "Reactions skipped because the (sealedTxId, kind, target) claim was already taken");

        _entitlementSkip = meter.CreateCounter<long>(
            "reaction_entitlement_skip_total",
            description: "Reactions skipped because this node does not host the target wallet");
    }

    /// <summary>Records a reaction that fired its side effect.</summary>
    public void RecordDispatched(string kind) => _dispatched.Add(1, new KeyValuePair<string, object?>("kind", kind));

    /// <summary>Records a reaction skipped because the idempotency claim was already taken.</summary>
    public void RecordIdempotentSkip(string kind) => _idempotentSkip.Add(1, new KeyValuePair<string, object?>("kind", kind));

    /// <summary>Records a reaction skipped because this node is not entitled (wallet not hosted here).</summary>
    public void RecordEntitlementSkip(string kind) => _entitlementSkip.Add(1, new KeyValuePair<string, object?>("kind", kind));
}
