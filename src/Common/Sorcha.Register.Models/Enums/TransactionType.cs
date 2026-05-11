// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Models.Enums;

/// <summary>
/// Represents the type of transaction
/// </summary>
public enum TransactionType
{
    /// <summary>
    /// Control transaction (register governance — genesis + admin operations)
    /// </summary>
    Control = 0,

    /// <summary>
    /// Action transaction (blueprint workflow action)
    /// </summary>
    Action = 1,

    /// <summary>
    /// Docket transaction (block sealing)
    /// </summary>
    Docket = 2,

    /// <summary>
    /// Participant transaction (published participant identity record)
    /// </summary>
    Participant = 3,

    /// <summary>
    /// Revocation transaction (supersedes or revokes a previously sealed transaction)
    /// </summary>
    Revocation = 4,

    /// <summary>
    /// Presentation-initiated transaction (Feature 111 — citizen started a timebound
    /// presentation flow; no credential data yet).
    /// </summary>
    PresentationInitiated = 5,

    /// <summary>
    /// Presentation-outcome transaction (Feature 111 — verifier callback resolved
    /// the attempt; success carries verified claims, decline carries a reason code).
    /// </summary>
    PresentationOutcome = 6,

    /// <summary>
    /// Presentation-abandoned transaction (Feature 111 — validity window expired
    /// with no callback; opt-in per blueprint).
    /// </summary>
    PresentationAbandoned = 7,

    /// <summary>
    /// Credential status change (multi-node audit CRITICAL #2 — issuer revokes /
    /// suspends / reinstates a credential. Recipient is the holder's wallet
    /// address; the transaction propagates the new status to the holder's node
    /// via the existing InboundTransactionRouter pipeline. Status list bitstring
    /// remains the authoritative cross-node check for verifiers — this tx exists
    /// to keep the holder's locally-cached row in sync.)
    /// </summary>
    CredentialStatusChange = 8
}
