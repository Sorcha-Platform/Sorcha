// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Feature 111 US3 — thrown when a presentation-required action already has a
/// successful <c>PresentationOutcome</c> on the register for the same instance
/// and action id. The HTTP boundary maps this to 409 Conflict. Prior decline /
/// abandoned outcomes do NOT trigger this — retry after decline is permitted.
/// </summary>
public sealed class PresentationAlreadyCompleteException : Exception
{
    public PresentationAlreadyCompleteException(int actionId, string priorOutcomeTxId)
        : base($"Action {actionId} already has a successful presentation outcome (tx {priorOutcomeTxId}). Further attempts are not permitted.")
    {
        ActionId = actionId;
        PriorOutcomeTransactionId = priorOutcomeTxId;
    }

    public int ActionId { get; }
    public string PriorOutcomeTransactionId { get; }
}
