// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sorcha.Register.Models;
using Sorcha.ServiceClients.Validator;

namespace Sorcha.Register.Service.Services;

/// <summary>Outcome of carrying an approval to the ledger.</summary>
/// <param name="Submitted">Whether the Validator accepted the transaction.</param>
/// <param name="TxId">Deterministic transaction id of the approval. Populated whether or not it was accepted.</param>
/// <param name="CarriedBy">Roster subject whose key signed the transaction envelope.</param>
/// <param name="Error">Validator's refusal message. Null when accepted.</param>
public sealed record GovernanceApprovalActionResult(
    bool Submitted,
    string TxId,
    string? CarriedBy,
    string? Error);

/// <summary>
/// Carries an externally-produced governance approval to the ledger as an action submission of the
/// published governance blueprint (T075).
/// </summary>
public interface IGovernanceApprovalActionSubmitter
{
    /// <summary>
    /// Builds the approval action transaction and submits it through the Validator.
    /// </summary>
    /// <param name="registerId">Register the proposal belongs to.</param>
    /// <param name="proposalId">Transaction id of the proposal being voted on.</param>
    /// <param name="submission">
    /// The detached approval. It MUST already have passed <see cref="IDetachedApprovalVerifier"/> —
    /// this type carries, it does not judge.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<GovernanceApprovalActionResult> SubmitAsync(
        string registerId,
        string proposalId,
        GovernanceApprovalSubmission submission,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
/// <remarks>
/// <para>
/// <b>Through the Validator, never straight to storage.</b> Writing a governance record directly to
/// Mongo was the original User Story 1 defect: it produced a record that had never been validated,
/// never sealed, and never replicated, while looking indistinguishable from one that had. Every
/// governance write on this platform now goes through <see cref="IValidatorServiceClient"/>, and
/// this class holds no repository so it cannot regress to the old shape.
/// </para>
/// <para>
/// <b>Envelope signature versus approval signature.</b> The transaction is signed by whichever
/// roster organisation this node can sign as — the Owner by default. That is the <i>carry</i>, and
/// it is what satisfies the Validator's roster check (<c>VAL_PERM_002</c>). The <i>authority</i> is
/// the approver's own detached signature inside the payload, which the server cannot produce.
/// The two are deliberately not conflated: signing the envelope as the approver whenever this node
/// happened to hold their key would dress a carry up as an approval, reinstating the server-side
/// signing R-014 withdrew — and it would do so only sometimes, which is worse than doing it always.
/// <c>Metadata["carriedBy"]</c> records the distinction on the ledger.
/// </para>
/// <para>
/// A node holding no governance key for the register therefore cannot carry an approval. That is
/// correct rather than a limitation: approvals are submitted to a node that participates in the
/// register's governance.
/// </para>
/// </remarks>
public sealed class GovernanceApprovalActionSubmitter : IGovernanceApprovalActionSubmitter
{
    private readonly IGovernanceSigningService _signingService;
    private readonly IValidatorServiceClient _validatorClient;
    private readonly ILogger<GovernanceApprovalActionSubmitter> _logger;

    /// <summary>Initialises a new instance of the <see cref="GovernanceApprovalActionSubmitter"/> class.</summary>
    public GovernanceApprovalActionSubmitter(
        IGovernanceSigningService signingService,
        IValidatorServiceClient validatorClient,
        ILogger<GovernanceApprovalActionSubmitter> logger)
    {
        _signingService = signingService ?? throw new ArgumentNullException(nameof(signingService));
        _validatorClient = validatorClient ?? throw new ArgumentNullException(nameof(validatorClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<GovernanceApprovalActionResult> SubmitAsync(
        string registerId,
        string proposalId,
        GovernanceApprovalSubmission submission,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        ArgumentNullException.ThrowIfNull(submission);
        ArgumentException.ThrowIfNullOrWhiteSpace(submission.ApproverDid);

        var payload = GovernanceApprovalActionPayload.FromSubmission(proposalId, submission);
        var canonicalJson = JsonSerializer.Serialize(
            payload, GovernanceApprovalActionPayload.CanonicalJsonOptions);
        var payloadBytes = Encoding.UTF8.GetBytes(canonicalJson);
        var payloadHash = Convert.ToHexString(SHA256.HashData(payloadBytes)).ToLowerInvariant();

        var txId = ComputeTransactionId(registerId, proposalId, submission.ApproverDid);

        var signResult = await _signingService.SignAsync(
            registerId: registerId,
            txId: txId,
            payloadHash: payloadHash,
            preferredSubject: null,
            cancellationToken: cancellationToken);

        var transaction = new TransactionSubmission
        {
            TransactionId = txId,
            RegisterId = registerId,
            BlueprintId = GovernanceBlueprint.BlueprintId,

            // FR-018: the recorded action is the one the published definition declares for collecting
            // approvals, so the ledger sequence can be diffed against the blueprint (T057).
            ActionId = GovernanceBlueprint.CollectQuorumActionId.ToString(),

            Payload = JsonDocument.Parse(canonicalJson).RootElement,
            PayloadHash = payloadHash,

            // The approval chains off the proposal it answers, mirroring the blueprint's action
            // 1 → action 2 route. Sibling approvals are expected and permitted: the Validator's fork
            // check exempts predecessors that are Control transactions, which a proposal is.
            PreviousTransactionId = proposalId,

            // Base64Url, matching every other control-transaction producer. The Validator decodes
            // signatures as base64url; plain base64 silently fails verification.
            Signatures =
            [
                new SignatureInfo
                {
                    PublicKey = Base64Url.EncodeToString(signResult.PublicKey),
                    SignatureValue = Base64Url.EncodeToString(signResult.Signature),
                    Algorithm = signResult.Algorithm,
                }
            ],
            CreatedAt = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, string>
            {
                // "Control" is what makes the Validator treat this as a governance transaction and
                // apply the roster check. It also exempts it from action-schema validation, which is
                // correct for now: register-governance-v1 is not published to ordinary registers, so
                // resolving it would fail outright. Full blueprint conformance is US3 (T054-T057).
                ["Type"] = "Control",
                ["transactionType"] = GovernanceApprovalTrackingType,
                ["proposalId"] = proposalId,
                ["approverDid"] = submission.ApproverDid,
                ["isApproval"] = submission.IsApproval ? "true" : "false",

                // Who carried it, as distinct from who approved it. Metadata is unsigned, so this is
                // an operator aid and never an input to an authorisation decision — the signed facts
                // are all in the payload.
                ["carriedBy"] = signResult.Subject,
                ["SystemWalletAddress"] = signResult.WalletAddress,
            },
        };

        var result = await _validatorClient.SubmitTransactionAsync(transaction, cancellationToken);

        if (!result.Success)
        {
            _logger.LogWarning(
                "Governance approval {TxId} from {ApproverDid} on register {RegisterId} rejected by the Validator: {Error}",
                txId, submission.ApproverDid, registerId, result.ErrorMessage);

            return new GovernanceApprovalActionResult(
                false, txId, signResult.Subject, result.ErrorMessage ?? "The Validator rejected the approval.");
        }

        _logger.LogInformation(
            "Governance {Vote} from {ApproverDid} on proposal {ProposalId} (register {RegisterId}) submitted as {TxId}, carried by {CarriedBy} — it counts once it seals",
            submission.IsApproval ? "approval" : "rejection", submission.ApproverDid, proposalId,
            registerId, txId, signResult.Subject);

        return new GovernanceApprovalActionResult(true, txId, signResult.Subject, null);
    }

    /// <summary>Tracking discriminator, alongside <c>GovernanceOperation</c> and <c>CryptoPolicyUpdate</c>.</summary>
    public const string GovernanceApprovalTrackingType = "GovernanceApproval";

    /// <summary>
    /// One transaction id per (register, proposal, approver).
    /// </summary>
    /// <remarks>
    /// <b><see cref="GovernanceApprovalSubmission.IsApproval"/> is deliberately not an input.</b>
    /// Including it would give an approval and a rejection from the same organisation distinct ids,
    /// so both could sit on the ledger and the recount would have to decide which vote counts —
    /// ambiguity introduced for no gain. Excluding it makes a resubmission idempotent whatever the
    /// flag says: a replay with the vote flipped collides with the original id and is deduped as a
    /// resubmission rather than added as a contradictory second vote. (It would also fail
    /// verification, since the digest binds the flag; this is the second line, not the first.)
    /// </remarks>
    internal static string ComputeTransactionId(string registerId, string proposalId, string approverDid) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"governance-approval-{registerId}-{proposalId}-{approverDid}"))).ToLowerInvariant();
}
