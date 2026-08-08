// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;

namespace Sorcha.Register.Service.Services;

/// <summary>Why a proposal could not be produced.</summary>
public enum ProposalReadOutcome
{
    /// <summary>The proposal was read.</summary>
    Found = 0,

    /// <summary>No transaction with that id on that register.</summary>
    NotFound,

    /// <summary>The transaction exists but is not a governance proposal.</summary>
    NotAProposal,

    /// <summary>The payload would not decode into a governance operation.</summary>
    Unreadable,
}

/// <summary>A proposal read from the ledger.</summary>
/// <param name="Outcome">Whether it was found, and why not if it was not.</param>
/// <param name="Operation">The stored operation. Non-null only when <paramref name="Outcome"/> is <c>Found</c>.</param>
/// <param name="Transaction">The proposal transaction itself, for callers that need its seal state.</param>
public sealed record GovernanceProposalRead(
    ProposalReadOutcome Outcome,
    GovernanceOperation? Operation,
    TransactionModel? Transaction);

/// <summary>Reads a governance proposal from the register's transaction log.</summary>
public interface IGovernanceProposalReader
{
    /// <summary>Reads the proposal a transaction id names.</summary>
    Task<GovernanceProposalRead> ReadAsync(
        string registerId, string proposalId, CancellationToken ct = default);
}

/// <inheritdoc />
/// <remarks>
/// <para>
/// The proposal <b>is</b> a ledger transaction, so its transaction id is the proposal id and there is
/// no proposal table to drift from the ledger (R-009).
/// </para>
/// <para>
/// Extracted so the signing-request and the approve endpoints read a proposal exactly one way. Both
/// need the same decode, and the base64-versus-base64url probe below is the kind of hand-rolled step
/// that quietly diverges when it exists twice: one copy learns about an encoding and the other does
/// not, and the symptom is a proposal that is readable through one endpoint and "unreadable" through
/// the other.
/// </para>
/// </remarks>
public sealed class GovernanceProposalReader : IGovernanceProposalReader
{
    private readonly IReadOnlyRegisterRepository _repository;
    private readonly ILogger<GovernanceProposalReader> _logger;

    /// <summary>Initialises a new instance of the <see cref="GovernanceProposalReader"/> class.</summary>
    public GovernanceProposalReader(
        IReadOnlyRegisterRepository repository,
        ILogger<GovernanceProposalReader> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<GovernanceProposalRead> ReadAsync(
        string registerId, string proposalId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);

        var tx = await _repository.GetTransactionAsync(registerId, proposalId, ct);
        if (tx is null)
        {
            return new GovernanceProposalRead(ProposalReadOutcome.NotFound, null, null);
        }

        var trackingType = tx.MetaData?.TrackingData?.GetValueOrDefault("transactionType");
        if (!string.Equals(trackingType, "GovernanceOperation", StringComparison.Ordinal))
        {
            return new GovernanceProposalRead(ProposalReadOutcome.NotAProposal, null, tx);
        }

        var operation = TryDecodeOperation(tx);

        return operation is null
            // Refused rather than approximated: an approver must see exactly what their signature
            // binds, and a partially-reconstructed operation is the substitution risk in another form.
            ? new GovernanceProposalRead(ProposalReadOutcome.Unreadable, null, tx)
            : new GovernanceProposalRead(ProposalReadOutcome.Found, operation, tx);
    }

    private GovernanceOperation? TryDecodeOperation(TransactionModel tx)
    {
        try
        {
            var payloadData = tx.Payloads.Length > 0 ? tx.Payloads[0].Data : null;
            if (string.IsNullOrWhiteSpace(payloadData))
            {
                return null;
            }

            // Same probe the genesis-attestation reader uses: Data is base64 or base64url depending
            // on which producer wrote it.
            var payloadBytes = payloadData.Contains('+') || payloadData.Contains('/') || payloadData.Contains('=')
                ? Convert.FromBase64String(payloadData)
                : System.Buffers.Text.Base64Url.DecodeFromChars(payloadData);

            return System.Text.Json.JsonSerializer
                .Deserialize<ControlTransactionPayload>(
                    payloadBytes,
                    new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                ?.Operation;
        }
        catch (Exception ex) when (ex is System.Text.Json.JsonException or FormatException)
        {
            _logger.LogWarning(ex,
                "Proposal {TxId} on register {RegisterId} would not decode as a governance operation",
                tx.TxId, tx.RegisterId);
            return null;
        }
    }
}
