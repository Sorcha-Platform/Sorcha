// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Engine.Credentials;

/// <summary>
/// The status a credential's list entry reports (feature 135, widened by feature 192).
/// </summary>
/// <remarks>
/// <para>
/// Replaces the original <c>StatusListBit</c> tri-state. That type could express "set", "not set"
/// and "could not tell" — but not WHICH status was set, so both rails had to discard information
/// they already had before the evaluator ever saw it. The W3C checker knew the list's
/// <c>statusPurpose</c>; the IETF checker had read a two-bit entry value. Both flattened to "set",
/// and every refusal was then reported as a revocation.
/// </para>
/// <para>
/// The names deliberately follow the IETF Token Status List vocabulary
/// (<c>0x00</c> VALID / <c>0x01</c> INVALID / <c>0x02</c> SUSPENDED) rather than inventing a third
/// spelling, and <see cref="Unresolved"/> is a first-class answer: "I could not tell" is a
/// different thing from either status and must never be folded into one.
/// </para>
/// </remarks>
public enum CredentialStatusValue
{
    /// <summary>The credential is in good standing — no status entry is set.</summary>
    Valid,

    /// <summary>
    /// The credential is revoked. Terminal in both specifications: W3C says the revocation status
    /// "is not reversible", IETF that INVALID means "revoked, annulled, taken back, recalled or
    /// cancelled". Nothing may clear it.
    /// </summary>
    Invalid,

    /// <summary>
    /// The credential is suspended — "temporarily prevent the acceptance" (W3C), "temporarily
    /// invalid… usually temporary" (IETF). Refused like a revocation, but REVERSIBLE, which is the
    /// whole reason the two are worth telling apart.
    /// </summary>
    Suspended,

    /// <summary>
    /// The status could not be resolved: the endpoint was unreachable or unparseable, or the entry
    /// carried a value this verifier does not recognise. The caller applies its fail-closed /
    /// fail-open policy — this is NOT a claim that the credential is bad.
    /// </summary>
    Unresolved
}

/// <summary>
/// A reference to a status-list entry carried by a credential (feature 135). Abstracts the
/// W3C bitstring-status-list and IETF token-status-list shapes behind one shape.
/// </summary>
public class StatusReference
{
    /// <summary>The status-list resource URI.</summary>
    public string Uri { get; set; } = string.Empty;

    /// <summary>The index of this credential's entry within the list.</summary>
    public int Index { get; set; }

    /// <summary>Status purpose where the list distinguishes it ("revocation" / "suspension").</summary>
    public string? Purpose { get; set; }
}

/// <summary>
/// Unified status-list checker (feature 135). One seam over both the W3C bitstring status
/// list and the IETF token status list so every verification path checks revocation
/// identically, fail-closed by policy.
/// </summary>
public interface IStatusListChecker
{
    /// <summary>Reads the status of the referenced credential entry.</summary>
    Task<CredentialStatusValue> CheckAsync(StatusReference statusRef, CancellationToken cancellationToken = default);
}
