// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Models.Enums;

/// <summary>
/// Typed predicates over <see cref="TransactionType"/>.
/// </summary>
public static class TransactionTypeExtensions
{
    /// <summary>
    /// True when the transaction is an <b>intra-action-lifecycle terminal</b> —
    /// <see cref="TransactionType.PresentationOutcome"/> or
    /// <see cref="TransactionType.PresentationAbandoned"/> (Feature 111). These chain off the
    /// same action's <see cref="TransactionType.PresentationInitiated"/> and carry the SAME
    /// <c>ActionId</c>, so they describe how that single action's presentation attempt resolved —
    /// they do not advance from one action to the next on their own. Excludes
    /// <see cref="TransactionType.PresentationInitiated"/>, which genuinely advances from action
    /// N-1 to action N and gets the full route-reachability check.
    /// </summary>
    /// <remarks>
    /// Feature 145 (US6): the <c>InstanceProjector</c> folds one of these <b>only</b> when it
    /// carries a signed <c>RoutingDecision</c> (a successful outcome that routes onward); a
    /// decline / abandonment carries no decision and is skipped so the action stays current. The
    /// validator's <c>TransactionTypeClassifier.IsIntraActionLifecycleTerminal</c> is the
    /// submission-side (string-metadata) counterpart of this seal-side typed predicate.
    /// </remarks>
    public static bool IsIntraActionLifecycleTerminal(this TransactionType type)
        => type is TransactionType.PresentationOutcome or TransactionType.PresentationAbandoned;

    /// <summary>
    /// True when the transaction is part of a Feature 111 presentation lifecycle —
    /// <see cref="TransactionType.PresentationInitiated"/>,
    /// <see cref="TransactionType.PresentationOutcome"/>, or
    /// <see cref="TransactionType.PresentationAbandoned"/>. These all chain off (and carry the
    /// ActionId of) a single presentation-gated action; none of them advances instance control
    /// state on its own EXCEPT a successful outcome, which carries a signed <c>RoutingDecision</c>.
    /// </summary>
    /// <remarks>
    /// Feature 145 (US6): the instance projection must NOT fold a presentation-lifecycle tx that
    /// carries no decision — the action arrived as current via the <i>previous</i> action's routing
    /// fold, and an empty next-action set would wrongly retire it (premature completion). The
    /// presentation action stays current until a successful outcome (carrying a decision) routes
    /// onward. Distinct from <see cref="IsIntraActionLifecycleTerminal(TransactionType)"/>, which
    /// excludes <c>PresentationInitiated</c> for the validator's route-reachability carve-out.
    /// </remarks>
    public static bool IsPresentationLifecycle(this TransactionType type)
        => type is TransactionType.PresentationInitiated
                or TransactionType.PresentationOutcome
                or TransactionType.PresentationAbandoned;
}
