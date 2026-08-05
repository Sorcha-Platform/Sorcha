// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Models;

/// <summary>
/// A cryptographic signature over an on-register object — a docket, a transaction, or a consensus
/// vote. This is ledger evidence: it is persisted to the register and replicated to every node.
/// </summary>
/// <remarks>
/// <para>
/// <b>Moved here from <c>Sorcha.Validator.Service.Models.Signature</c> (Feature 187 / #1371).</b>
/// It was never a validator working type — its own documentation already said it is "persisted to
/// the Register Service as part of the blockchain ledger", and every member is immutable evidence
/// (no mempool bookkeeping, no retry counters, nothing transient). Living in the validator assembly
/// meant the ledger's own signature shape was defined by, and could drift with, one consumer.
/// </para>
/// <para>
/// <b>Named <c>RegisterSignature</c>, not <c>Signature</c>.</b> A bare <c>Signature</c> in a
/// broadly-imported namespace is the kind of generic name that let this family of collisions
/// accumulate in the first place. Three signature types now say what they are:
/// <see cref="RegisterSignature"/> (on-register evidence), <see cref="ReceiptSignature"/> (an F079
/// transaction receipt), and <c>CollectedSignature</c> (a signature gathered during consensus
/// collection, validator-local).
/// </para>
/// <para><b>Related requirements:</b> FR-003 (dockets signed before broadcast), FR-004 (consensus
/// votes signed), FR-005 (peer vote signatures verified), SC-002 (100% of dockets carry valid
/// signatures).</para>
/// </remarks>
public class RegisterSignature
{
    /// <summary>
    /// Signer's public key bytes.
    /// </summary>
    /// <remarks>
    /// Verifies the signature. Length depends on algorithm: ED25519 32 bytes, NISTP256 32 bytes,
    /// RSA4096 512 bytes.
    /// </remarks>
    public required byte[] PublicKey { get; init; }

    /// <summary>
    /// The cryptographic signature bytes produced by signing the data hash. Length varies by
    /// algorithm.
    /// </summary>
    public required byte[] SignatureValue { get; init; }

    /// <summary>
    /// Signature algorithm used — matches a <c>WalletAlgorithm</c> enum name (e.g. "ED25519",
    /// "NISTP256", "RSA4096"). Selects the verification method.
    /// </summary>
    public required string Algorithm { get; init; }

    /// <summary>
    /// UTC timestamp when the signature was created. Used for audit logging.
    /// </summary>
    public required DateTimeOffset SignedAt { get; init; }

    /// <summary>
    /// Bech32-encoded wallet address of the signer (optional).
    /// </summary>
    /// <remarks>
    /// Identifies the signer without deriving the address from the public key. The Wallet Service
    /// populates it on the normal signing path; it is absent on some legacy genesis transactions.
    /// Persisting it is load-bearing — see <c>DocketRegisterProjection.ResolveSenderWallet</c>, where
    /// falling back to base64url(PublicKey) produced strings that never matched a bech32 wallet
    /// lookup and emptied every "My Transactions" result (the wave 11 audit bug).
    /// </remarks>
    /// <example>ws11qr4f5ulrxg450l2zunexd7mscapvcx9mzefwq8lp5ntnuj2e9lwkkczg0t6</example>
    public string? SignedBy { get; init; }
}
