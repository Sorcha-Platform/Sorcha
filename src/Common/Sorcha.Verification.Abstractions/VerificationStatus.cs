// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Verification.Abstractions;

/// <summary>
/// The outcome of a single verification check, shared by every Sorcha verification engine.
/// </summary>
/// <remarks>
/// <para>
/// <b><see cref="Unverified"/> is a first-class result, not a soft failure.</b> It means the check
/// could not run — the evidence needed to answer the question was not available. It is deliberately
/// distinct from <see cref="Failed"/> (the check ran and did not pass) because the two must be
/// treated differently: an <see cref="Unverified"/> result never vetoes an otherwise-passing verdict,
/// whereas a <see cref="Failed"/> one does.
/// </para>
/// <para>
/// Conflating them is the single most damaging misreading available here. A node holding only part
/// of a register, a deployment with one validator and therefore no quorum evidence, and a record
/// predating the evidence a check needs are all ordinary, healthy states — reporting them as
/// <see cref="Failed"/> would tell an operator their register had been tampered with. Absence of
/// evidence is not evidence of tampering.
/// </para>
/// <para>
/// The converse is worse still: a check that did not run MUST NOT report <see cref="Verified"/>.
/// A verification surface that manufactures confidence is worse than none.
/// </para>
/// <para>
/// <b>Hoisted here from <c>Sorcha.Verifier.Engine.Models.LayerStatus</c> (Feature 188 / D2)</b>,
/// where its members were named <c>Pass</c> / <c>Fail</c> / <c>Unverified</c>. The tri-state is a
/// genuinely shared concept; each engine's *layer* enum is not, and stays local
/// (<c>ValidationLayer</c> is credential-specific, <c>ProvenanceLayer</c> register-specific).
/// Declaring a second near-identical status enum instead is the defect Feature 187 spent its length
/// removing — <c>VoteDecision</c> existed twice with <i>incompatible</i> numeric values
/// (<c>Reject</c> = 2 versus 0), in two assemblies that referenced each other, told apart only by
/// namespace qualification, with nothing in the type system objecting.
/// </para>
/// <para>
/// <b>Member order is load-bearing.</b> These values serialise as integers under the default
/// <c>System.Text.Json</c> options (no <c>JsonStringEnumConverter</c> is applied), so reordering
/// members silently changes the wire value of every persisted or transmitted result. The order below
/// preserves the original <c>LayerStatus</c> ordinals exactly: 0, 1, 2.
/// </para>
/// </remarks>
public enum VerificationStatus
{
    /// <summary>The check ran and passed.</summary>
    Verified,

    /// <summary>The check ran and did not pass (e.g. revoked, signature mismatch, contents altered).</summary>
    Failed,

    /// <summary>
    /// The check could not run — the evidence it needs was unavailable (e.g. issuer key unresolved,
    /// predecessor not held on this node, no quorum evidence recorded). Carries a reason wherever the
    /// consuming result type has somewhere to put one. Never a failure, and never a veto.
    /// </summary>
    Unverified,
}
