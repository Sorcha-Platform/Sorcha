// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.ObjectModel;

namespace Sorcha.CitizenWallet.Abstractions.Models;

/// <summary>
/// Outcome of a single presentation as the wallet recorded it.
/// </summary>
public enum PresentationLogOutcome
{
    /// <summary>Wallet built and delivered the presentation; no verifier callback was observed.</summary>
    Presented = 0,

    /// <summary>Citizen declined to release attributes after seeing the consent screen.</summary>
    DeclinedByCitizen = 1,

    /// <summary>Verifier reported back that it rejected the presentation.</summary>
    VerifierRejected = 2,

    /// <summary>Verifier reported back that it accepted the presentation.</summary>
    Acknowledged = 3
}

/// <summary>
/// One entry in the wallet's local presentation history, also reported back to the
/// platform on the next online sync via <c>POST /api/v1/wallet/presentations/log</c>.
/// </summary>
public sealed record PresentationLogEntry
{
    /// <summary>Wallet-generated identifier; used for server-side dedupe.</summary>
    public Guid Id { get; init; }

    /// <summary>Credential that was presented.</summary>
    public Guid CredentialId { get; init; }

    /// <summary>Verifier's declared DID, if the request carried one.</summary>
    public string? VerifierDid { get; init; }

    /// <summary>Verifier-supplied display label (untrusted).</summary>
    public string? VerifierLabel { get; init; }

    /// <summary>Names of disclosed claims.</summary>
    public IReadOnlyList<string> DisclosedClaims { get; init; } =
        new ReadOnlyCollection<string>([]);

    /// <summary>UTC time at which the wallet completed the presentation.</summary>
    public DateTimeOffset PresentedAt { get; init; }

    /// <summary>Outcome the wallet observed.</summary>
    public PresentationLogOutcome Outcome { get; init; }

    /// <summary>Originating register, if known to the wallet (for lifecycle correlation).</summary>
    public string? RegisterId { get; init; }

    /// <summary>Originating action transaction id, if known.</summary>
    public string? ActionTxId { get; init; }
}
