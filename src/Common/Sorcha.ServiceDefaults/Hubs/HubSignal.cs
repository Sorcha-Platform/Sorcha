// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.ServiceDefaults.Hubs;

/// <summary>
/// Conceptual envelope for every server-to-client SignalR event on a Sorcha
/// notification hub. Carries opaque IDs only — no domain payload, no claim
/// values, no descriptive text, no balances.
/// </summary>
/// <remarks>
/// <para>
/// This record is the contract every hub method conforms to. The wire shape
/// itself is the SignalR method-call argument list (typed parameters per
/// hub method); this envelope expresses the rule. See spec FR-016 — FR-019.
/// </para>
/// <para>
/// Hub method signatures take individual parameters that match this shape,
/// e.g. <c>Task ActionAvailable(string instanceId, string actionId,
/// DateTimeOffset occurredAt, string traceId)</c>. Code review enforces
/// that every event method on every <c>I*HubClient</c> interface declares
/// only ID-like parameter types (<c>string</c>, <c>Guid</c>, <c>int</c>,
/// <c>long</c>) plus <c>DateTimeOffset</c> and the trace token.
/// </para>
/// <para>
/// ChatHub is exempt — it streams content payloads by design.
/// </para>
/// </remarks>
public sealed record HubSignal(
    string EventType,
    IReadOnlyList<string> Ids,
    DateTimeOffset OccurredAt,
    string TraceId);
