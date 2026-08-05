// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using Sorcha.ServiceClients.Register;
using Sorcha.Validator.Service.Models;
using Transaction = Sorcha.Validator.Service.Models.Transaction;

namespace Sorcha.Validator.Service.Services;

/// <summary>
/// THE projection from the validator's consensus working model
/// (<see cref="Sorcha.Validator.Service.Models.Docket"/>) onto the canonical ledger model
/// (<see cref="DocketModel"/>) that the Register Service persists.
/// </summary>
/// <remarks>
/// <para>
/// <b>This is the single source of truth for what gets persisted to MongoDB.</b> Every path that
/// writes a docket — <c>DocketBuildTriggerService</c> (the normal seal), <c>ValidatorOrchestrator</c>
/// (admin endpoints) and <c>DocketDistributor</c> (gRPC) — projects through here. Blueprint Service's
/// <c>BuiltTransaction</c> has no equivalent <c>ToTransactionModel()</c> any more: three Feature 119
/// attempts modified that dead method and confirmed it was never on the write path. ALL
/// persisted-metadata changes go here.
/// </para>
/// <para>
/// <b>Why it is one method (Feature 187 / issue #1370).</b> This projection used to exist twice — the
/// inline block in <c>DocketBuildTriggerService</c> and <c>DocketSerializer.ToRegisterModel</c> — and
/// the copies had drifted. The second dropped <c>InstanceId</c> and <c>RoutingDecision</c> entirely
/// and collapsed five <c>TransactionType</c> members onto <c>Action</c>, so which entry point drove a
/// seal silently changed what landed on the ledger: F145 instances would never advance, and F111
/// lifecycle transactions would lose their schema-validation exemption and become unsealable
/// (<c>VAL_SCHEMA_004</c>). Do not reintroduce a second copy.
/// </para>
/// <para>
/// <b>Guard:</b> <c>DocketProjectionCompletenessTests</c> reflects over every
/// <see cref="Sorcha.Register.Models.TransactionMetaData"/> property and fails if any is not
/// populated here. That guard exists because this seam has produced three separate silent defects
/// (<c>InstanceId</c>, <c>TrackingData</c>, and the duplicate-projection drift above), each a field
/// missing from a hand-maintained mapping with no error anywhere. Adding a property to
/// <c>TransactionMetaData</c> means adding it here too — the test will say so.
/// </para>
/// </remarks>
internal static class DocketRegisterProjection
{
    /// <summary>
    /// Projects a validated, consensus-complete docket onto the ledger model written to the
    /// Register Service.
    /// </summary>
    public static DocketModel ToDocketModel(Docket docket)
    {
        ArgumentNullException.ThrowIfNull(docket);

        return new DocketModel
        {
            DocketId = docket.DocketId,
            RegisterId = docket.RegisterId,
            DocketNumber = docket.DocketNumber,
            PreviousHash = docket.PreviousHash,
            DocketHash = docket.DocketHash,
            CreatedAt = docket.CreatedAt,
            Transactions = docket.Transactions.Select(ToTransactionModel).ToList(),
            ProposerValidatorId = docket.ProposerValidatorId,
            MerkleRoot = docket.MerkleRoot
        };
    }

    private static Sorcha.Register.Models.TransactionModel ToTransactionModel(Transaction t)
    {
        var firstSig = t.Signatures.FirstOrDefault();
        var rawPayload = t.Payload.ValueKind != System.Text.Json.JsonValueKind.Undefined
            ? t.Payload.GetRawText()
            : string.Empty;
        var payloadData = rawPayload.Length > 0
            ? Base64Url.EncodeToString(System.Text.Encoding.UTF8.GetBytes(rawPayload))
            : string.Empty;

        return new Sorcha.Register.Models.TransactionModel
        {
            TxId = t.TransactionId,
            RegisterId = t.RegisterId,
            PrevTxId = t.PreviousTransactionId ?? string.Empty,
            TimeStamp = t.CreatedAt.UtcDateTime,
            SenderWallet = ResolveSenderWallet(t),
            Signature = firstSig != null
                ? Base64Url.EncodeToString(firstSig.SignatureValue)
                : string.Empty,
            PayloadCount = 1,
            Payloads =
            [
                new Sorcha.Register.Models.PayloadModel
                {
                    Data = payloadData,
                    Hash = t.PayloadHash,
                    PayloadSize = (ulong)System.Text.Encoding.UTF8.GetByteCount(rawPayload),
                    ContentEncoding = "base64url"
                }
            ],
            RecipientsWallets = t.RecipientsWallets ?? [],
            MetaData = new Sorcha.Register.Models.TransactionMetaData
            {
                RegisterId = t.RegisterId,
                BlueprintId = t.BlueprintId,
                ActionId = uint.TryParse(t.ActionId, out var actionId) ? actionId : null,
                TransactionType = ResolveTransactionType(t),
                // Carry InstanceId through from the in-memory submission so the Validator's Tier 3
                // chain-derived participant binding can walk in-instance transactions on the
                // register. Prior to this, sealed txs were persisted with InstanceId=null and every
                // Tier 3 lookup returned an empty list.
                InstanceId = t.Metadata.TryGetValue("instanceId", out var iid)
                             && !string.IsNullOrWhiteSpace(iid)
                    ? iid
                    : null,
                // Feature 145 (T024): carry the VALIDATED routing decision through the seal as the
                // typed field. The validator (VAL_ROUTING_*) has already confirmed the carried
                // decision is a structural successor set and that its attestation verifies, so every
                // node folds RoutingDecision.nextActions (full set → parallel branches preserved)
                // without re-running routing or decrypting the payload.
                RoutingDecision = DocketBuildTriggerService.ResolveRoutingDecision(t.Metadata),
                // Carry the submission metadata through to the persisted tx. Omitting it left
                // TrackingData null on EVERY sealed transaction — dropping the F138 US4 blueprint
                // `contentHash` (publish-time sealed digest), `publishedBy`, and the legacy
                // `transactionType` discriminator. A node recovering a replicated register then
                // rejected every blueprint with `no_provenance`.
                TrackingData = t.Metadata is { Count: > 0 }
                    ? new Dictionary<string, string>(t.Metadata)
                    : null,
            }
        };
    }

    /// <summary>
    /// Resolves the persisted transaction type from the submission metadata.
    /// </summary>
    /// <remarks>
    /// "Genesis" is written by <c>RegisterCreationOrchestrator</c> but is not an enum member — it maps
    /// to <c>Control</c>, because genesis is the control record that bootstraps the register. Every
    /// other member is parsed by name, which is what keeps presentation-lifecycle types
    /// (<c>PresentationInitiated</c>/<c>Outcome</c>/<c>Abandoned</c>), <c>Revocation</c> and
    /// <c>CredentialStatusChange</c> intact through the seal. The retired second projection used a
    /// hardcoded three-way switch here and silently collapsed all of those onto <c>Action</c>.
    /// </remarks>
    private static Sorcha.Register.Models.Enums.TransactionType ResolveTransactionType(Transaction t)
    {
        if (!t.Metadata.TryGetValue("Type", out var typeStr))
        {
            return Sorcha.Register.Models.Enums.TransactionType.Action;
        }

        if (typeStr.Equals("Genesis", StringComparison.OrdinalIgnoreCase))
        {
            return Sorcha.Register.Models.Enums.TransactionType.Control;
        }

        return Enum.TryParse<Sorcha.Register.Models.Enums.TransactionType>(
            typeStr, ignoreCase: true, out var parsed)
            ? parsed
            : Sorcha.Register.Models.Enums.TransactionType.Action;
    }

    /// <summary>
    /// Extracts the sender wallet address (bech32) from a transaction.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The Wallet Service populates <c>Signature.SignedBy</c> with the canonical bech32 wallet address
    /// (e.g. <c>ws11qpgd645h...</c>) when it signs a transaction. Blueprint Service plumbs that field
    /// through the gRPC submission contract, and <c>ValidationEndpoints</c> copies it onto the
    /// validator's <c>Signature</c> model. Use it directly so the persisted
    /// <c>TransactionModel.SenderWallet</c> stays in the same address format that
    /// <c>GET /api/register/query/wallets/{address}/transactions</c> queries against.
    /// </para>
    /// <para>
    /// Falling back to <c>Base64Url.EncodeToString(PublicKey)</c> here was the wave 11 audit bug: the
    /// resulting string is the raw public key bytes in base64url, which never matches a bech32 wallet
    /// address, so every "My Transactions" lookup returned empty. The raw-key fallback is kept as a
    /// last resort for edge cases where <c>SignedBy</c> is missing (e.g. legacy genesis transactions)
    /// but it should never be hit on a healthy submission path. <c>IsNullOrWhiteSpace</c> (not
    /// <c>Length &gt; 0</c>) is deliberate — a whitespace <c>SignedBy</c> must take the fallback.
    /// </para>
    /// </remarks>
    private static string ResolveSenderWallet(Transaction t)
    {
        var firstSig = t.Signatures.FirstOrDefault();
        if (firstSig == null)
        {
            return "system";
        }

        return !string.IsNullOrWhiteSpace(firstSig.SignedBy)
            ? firstSig.SignedBy
            : Base64Url.EncodeToString(firstSig.PublicKey);
    }
}
