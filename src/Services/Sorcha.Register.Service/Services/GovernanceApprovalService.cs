// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Models;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Produces a cryptographically signed governance approval on behalf of a roster organisation.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why this exists.</b> Feature 189 US2-A made the Validator verify
/// <see cref="ApprovalSignature.Signature"/> against the approving organisation's roster key.
/// That closed a hole where quorum could be satisfied by simply <i>asserting</i> approvals — but it
/// also meant nothing in the platform could produce a <i>valid</i> one, because approvals had only
/// ever arrived as caller-supplied JSON. Without this service, every quorum-requiring operation
/// would be permanently unsatisfiable: correct, but useless.
/// </para>
/// <para>
/// The approval commits to <see cref="GovernanceApprovalStatement"/>, which binds the register, the
/// exact operation (including target role), the proposal's identity, the approver, and whether this
/// is an approval or a rejection — so a signature cannot be lifted onto a different proposal, nor a
/// rejection be recounted as an approval.
/// </para>
/// </remarks>
public interface IGovernanceApprovalService
{
    /// <summary>
    /// Signs an approval (or rejection) of a governance operation as the given roster organisation.
    /// </summary>
    /// <param name="registerId">Register the operation applies to.</param>
    /// <param name="operation">The proposal being voted on.</param>
    /// <param name="approverSubject">
    /// Roster subject casting the vote (<c>did:sorcha:w:{address}</c>). Must hold a governance role.
    /// </param>
    /// <param name="isApproval"><c>true</c> to approve, <c>false</c> to reject.</param>
    /// <param name="authMethod">
    /// How the person casting the vote authenticated — <c>passkey</c>, <c>totp</c>, <c>password</c>,
    /// <c>re-oauth</c>. Recorded on the ledger as the method only, never the proof.
    /// </param>
    /// <param name="comment">Optional justification, recorded with the vote.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<ApprovalSignature> CreateApprovalAsync(
        string registerId,
        GovernanceOperation operation,
        string approverSubject,
        bool isApproval,
        string? authMethod = null,
        string? comment = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public class GovernanceApprovalService : IGovernanceApprovalService
{
    private readonly IGovernanceSigningService _signingService;
    private readonly ILogger<GovernanceApprovalService> _logger;

    public GovernanceApprovalService(
        IGovernanceSigningService signingService,
        ILogger<GovernanceApprovalService> logger)
    {
        _signingService = signingService ?? throw new ArgumentNullException(nameof(signingService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ApprovalSignature> CreateApprovalAsync(
        string registerId,
        GovernanceOperation operation,
        string approverSubject,
        bool isApproval,
        string? authMethod = null,
        string? comment = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(approverSubject);

        var digest = GovernanceApprovalStatement.ComputeDigest(
            registerId, operation, approverSubject, isApproval);

        // preferredSubject pins WHO signs. Without it the Owner would sign every approval, so a
        // three-organisation consortium would produce three identical Owner signatures and quorum
        // would be satisfied by one party voting three times.
        var signed = await _signingService.SignDigestAsync(
            registerId, digest, preferredSubject: approverSubject, cancellationToken);

        _logger.LogInformation(
            "Governance {Vote} recorded for {OperationType} on register {RegisterId} by {Subject} (authenticated via {AuthMethod})",
            isApproval ? "approval" : "rejection", operation.OperationType, registerId,
            approverSubject, authMethod ?? "unspecified");

        return new ApprovalSignature
        {
            ApproverDid = approverSubject,
            Signature = Convert.ToBase64String(signed.Signature),
            IsApproval = isApproval,
            VotedAt = DateTimeOffset.UtcNow,
            AuthMethod = authMethod,
            Comment = comment
        };
    }
}
