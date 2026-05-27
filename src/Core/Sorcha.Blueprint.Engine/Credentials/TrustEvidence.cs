// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Blueprint.Models.Credentials;

namespace Sorcha.Blueprint.Engine.Credentials;

/// <summary>
/// Audit record of a trust decision (feature 135). Names which source vouched and the
/// basis consulted, sufficient to reproduce the same decision offline from the pinned
/// evidence alone (FR-014/FR-015). Carried on spec-079 verification receipts.
/// </summary>
public class TrustEvidence
{
    /// <summary>Which trust source established trust.</summary>
    public TrustSourceKind VouchingSource { get; set; }

    /// <summary>The resolved issuer identifier (DID URI or certificate subject).</summary>
    public string IssuerId { get; set; } = string.Empty;

    /// <summary>For a register source — the register height consulted, if applicable.</summary>
    public long? RegisterHeight { get; set; }

    /// <summary>For an X.509 source — the CRL version / thisUpdate consulted, if applicable.</summary>
    public string? CrlVersion { get; set; }

    /// <summary>For a trust-list source — the snapshot identifier consulted.</summary>
    public string? TrustListId { get; set; }

    /// <summary>For a trust-list source — the freshness timestamp of the snapshot consulted.</summary>
    public DateTimeOffset? TrustListFreshness { get; set; }

    /// <summary>The assurance level established by the decision.</summary>
    public AssuranceLevel AssuranceLevel { get; set; } = AssuranceLevel.Low;

    /// <summary>When the decision was made.</summary>
    public DateTimeOffset EvaluatedAt { get; set; }

    /// <summary>
    /// Hash of the trust policy that was evaluated, so an offline re-evaluation uses the
    /// same policy and reaches the same decision.
    /// </summary>
    public string PolicyDigest { get; set; } = string.Empty;
}
