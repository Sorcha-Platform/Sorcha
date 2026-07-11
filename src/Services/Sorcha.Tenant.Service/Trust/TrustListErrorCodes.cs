// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Tenant.Service.Trust;

/// <summary>Feature 181 US3 — typed trusted-list error codes (data-model §6).</summary>
public static class TrustListErrorCodes
{
    /// <summary>Document is not well-formed / not a parseable TS 119 612 trusted list (FR-011).</summary>
    public const string Malformed = "TRUSTLIST_MALFORMED";

    /// <summary>Enveloped XMLDSig signature is absent or does not verify (FR-011).</summary>
    public const string SignatureInvalid = "TRUSTLIST_SIGNATURE_INVALID";

    /// <summary>Sequence number is not newer than the current Active snapshot (FR-013).</summary>
    public const string SequenceRegression = "TRUSTLIST_SEQUENCE_REGRESSION";

    /// <summary>Trust evaluation requested but no snapshot is present for the list identity (FR-014).</summary>
    public const string Unavailable = "TRUSTLIST_UNAVAILABLE";

    /// <summary>Strict-freshness mode: the authoritative snapshot is past its next-update (FR-016).</summary>
    public const string Stale = "TRUSTLIST_STALE";
}
