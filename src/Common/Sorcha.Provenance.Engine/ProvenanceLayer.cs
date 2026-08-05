// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Provenance.Engine;

/// <summary>
/// The question a single provenance check answers about a sealed docket.
/// </summary>
/// <remarks>
/// <para>
/// Register-specific by design. <c>Sorcha.Verifier.Engine.Models.ValidationLayer</c> keeps its own
/// credential-specific members (LivePresentation / IssuerSignature / Revocation / RegisterAnchor);
/// only the tri-state <see cref="Sorcha.Verification.Abstractions.VerificationStatus"/> is shared
/// between the two engines (Feature 188, plan D2). A layer enum is not a shared concept — merging
/// them would put credential vocabulary in front of an administrator reading a ledger.
/// </para>
/// <para>
/// Declaration order is presentation order: broadest first. See
/// <see cref="DocketProvenanceVerifier"/>, which emits exactly one check per member, always in this
/// order, so a reader moves from "does this register trace to its anchor" down to "was the proposer
/// entitled to propose".
/// </para>
/// <para>
/// Adding a member here is not free: <c>ProvenanceLayerCoverageTests</c> reflects over this enum and
/// fails if any member is not exercised by a test, so a new layer cannot be silently left unchecked.
/// </para>
/// </remarks>
public enum ProvenanceLayer
{
    /// <summary>Does this register's origin trace to the trust anchor this node holds?</summary>
    Anchor,

    /// <summary>Does this docket link to its predecessor?</summary>
    Chain,

    /// <summary>Do the docket's recorded contents still match the commitment sealed over them?</summary>
    Seal,

    /// <summary>
    /// Did the validators who signed hold authority <i>at that point in history</i> — not merely
    /// today?
    /// </summary>
    Signers,

    /// <summary>Was the proposing validator a member of the set applying when this docket sealed?</summary>
    Proposer,
}
