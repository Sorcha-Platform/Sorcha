// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Blueprint.Engine.Credentials;

/// <summary>
/// The bit value read from a credential status list (feature 135).
/// </summary>
public enum StatusListBit
{
    /// <summary>Bit is 0 — the credential is active.</summary>
    NotSet,

    /// <summary>Bit is 1 — the credential is revoked or suspended.</summary>
    Set,

    /// <summary>The status could not be resolved (endpoint unreachable / unparseable).</summary>
    Unknown
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
    /// <summary>Reads the status bit for the referenced credential entry.</summary>
    Task<StatusListBit> CheckAsync(StatusReference statusRef, CancellationToken cancellationToken = default);
}
