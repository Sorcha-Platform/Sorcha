// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Hubs;

/// <summary>
/// Typed client interface for <see cref="BlueprintHub"/>. Bridge interface added
/// in Feature 118 Phase 3 (US1 — multi-node correctness) so the hub can be
/// registered through <c>services.AddSorchaHub&lt;ActionsHub, IBlueprintHubClient&gt;(...)</c>.
/// </summary>
/// <remarks>
/// <para>
/// The full topology rename to <c>BlueprintHub</c> + <c>IBlueprintHubClient</c>
/// lands in Phase 4 (US2). This interface mirrors the existing untyped
/// <c>SendAsync</c> method names emitted by <c>NotificationService</c> so existing
/// emitters that use <see cref="Microsoft.AspNetCore.SignalR.IHubContext{ActionsHub}"/>
/// continue to work via the untyped client-proxy path during the transition.
/// </para>
/// <para>
/// Subject to the thin-signal contract from Feature 118 spec FR-016 — FR-019 once
/// US4 lands. Today's payload shapes (<c>SignalNotification</c>, <c>EncryptionSignal</c>,
/// <c>CredentialNotification</c>) carry descriptive fields; US4 strips them to opaque IDs.
/// </para>
/// </remarks>
public interface IBlueprintHubClient
{
    /// <summary>A new action is available for the recipient wallet.</summary>
    Task ActionAvailable(SignalNotification notification);

    /// <summary>An action was rejected by the validation pipeline.</summary>
    Task ActionRejected(SignalNotification notification);

    /// <summary>A workflow instance reached a terminal state.</summary>
    Task WorkflowCompleted(SignalNotification notification);

    /// <summary>Encryption operation progress (percentage, status).</summary>
    Task EncryptionProgress(object signal);

    /// <summary>Encryption operation succeeded.</summary>
    Task EncryptionComplete(object signal);

    /// <summary>Encryption operation failed.</summary>
    Task EncryptionFailed(object signal);
}
