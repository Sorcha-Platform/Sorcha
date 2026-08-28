// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Validator.Service.Models;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// Decides whether a transaction is entitled to the administrative validation exemptions.
/// </summary>
/// <remarks>
/// <para>
/// <b>Feature 196 / issue #1591.</b> The <b>single producer</b> of <see cref="ExemptionDecision"/>.
/// Before this existed, the grant was computed independently in <c>TransactionTypeClassifier</c>
/// while the compensating roster check lived in <c>RightsEnforcementService</c>, and the system was
/// correct only where those two happened to agree — for two of the three claimable values there was
/// no compensating check at all.
/// </para>
/// <para>
/// The rule: <b>grant iff a claim is present AND the signer is proved entitled to it</b>. A claim
/// alone never grants. Authority is derived only from material a submitter cannot change without
/// invalidating a signature — the signer's own key — so no ledger byte moves and no ceremony changes.
/// </para>
/// </remarks>
public interface IExemptionAuthorityResolver
{
    /// <summary>
    /// Resolves the exemption decision for a transaction.
    /// </summary>
    /// <param name="transaction">The transaction under validation.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>
    /// The decision. Never throws for an unresolvable authority — that is reported as
    /// <see cref="ExemptionRefusalReason.AuthorityUnresolvable"/> and withholds the exemption
    /// (FR-007, fail closed in every environment).
    /// </returns>
    Task<ExemptionDecision> ResolveAsync(Transaction transaction, CancellationToken ct = default);
}
