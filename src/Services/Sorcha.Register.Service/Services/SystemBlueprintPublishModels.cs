// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

namespace Sorcha.Register.Service.Services;

/// <summary>Optional body of the operator publish request.</summary>
/// <param name="DryRun">Decide, but submit nothing.</param>
/// <param name="ExpectedCurrent">Publication id the operator believes is current; refused (409) if not.</param>
public sealed record SystemBlueprintPublishRequest(bool DryRun = false, string? ExpectedCurrent = null);

/// <summary>What a successful publish request did. On the wire the enums are kebab-case strings.</summary>
public enum SystemBlueprintPublishResultOutcome
{
    /// <summary>Everything would have been accepted; nothing submitted.</summary>
    DryRun,

    /// <summary>The register already holds the image's definition as current.</summary>
    Noop,

    /// <summary>Submitted; becomes current once sealed.</summary>
    Submitted
}

/// <summary>Body of a 200/202 operator publish response.</summary>
/// <param name="BlueprintId">The system blueprint.</param>
/// <param name="Outcome">What was done.</param>
/// <param name="State">Drift classification at decision time.</param>
/// <param name="CurrentPublicationTxId">The register's current publication, when known.</param>
/// <param name="CandidatePublicationTxId">The id a publish of the image's definition produces.</param>
/// <param name="TransactionId">Set when <paramref name="Outcome"/> is submitted.</param>
public sealed record SystemBlueprintPublishResult(
    string BlueprintId,
    SystemBlueprintPublishResultOutcome Outcome,
    SystemBlueprintDriftState State,
    string? CurrentPublicationTxId,
    string? CandidatePublicationTxId,
    string? TransactionId);

/// <summary>
/// The validator rejected a system blueprint publication submission. Derives from
/// <see cref="InvalidOperationException"/> so existing callers' handling is unchanged; thrown ONLY at the
/// validator-rejection site, so the operator endpoint can tell it from signing or canonicalisation failures.
/// </summary>
public sealed class ValidatorRejectedSubmissionException : InvalidOperationException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What the validator reported.</param>
    public ValidatorRejectedSubmissionException(string message) : base(message) { }
}
