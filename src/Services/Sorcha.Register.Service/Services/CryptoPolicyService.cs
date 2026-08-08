// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.Register.Core.Managers;
using Sorcha.ServiceClients.SystemWallet;
using Sorcha.ServiceClients.Validator;
using Sorcha.Wallet.Contracts.Constants;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Extracts the active crypto policy from a register's control transaction chain,
/// and submits policy updates as control transactions.
/// The latest version wins — policy updates are delivered via control transactions.
/// </summary>
public class CryptoPolicyService
{
    /// <summary>
    /// The control action id the Validator's <c>ControlDocketProcessor</c> maps to
    /// <c>ControlActionType.CryptoPolicyUpdate</c>. A submission carrying any other action id is
    /// not recognised as a control transaction and its policy is never applied.
    /// </summary>
    public const string CryptoPolicyUpdateActionId = "control.crypto.update";

    /// <summary>
    /// Value of the <c>transactionType</c> tracking-metadata key that Register Service's docket-write
    /// path keys off when projecting a sealed policy update's DevMode onto the register record.
    /// </summary>
    public const string CryptoPolicyUpdateTrackingType = "CryptoPolicyUpdate";

    /// <summary>
    /// Blueprint id carried by a crypto-policy control transaction — the governance control
    /// blueprint (the same value <c>RegisterPolicy.GovernancePolicy.BlueprintVersion</c> defaults to
    /// for every register).
    /// </summary>
    /// <remarks>
    /// Two constraints pin this value, and both were found the hard way:
    /// <list type="bullet">
    /// <item><b>It must be non-empty.</b> <c>TransactionValidator</c> rejects a blank blueprint id
    /// with <c>TX_003 "Blueprint ID is required"</c> before any control-specific handling runs.</item>
    /// <item><b>It must NOT be <c>GenesisConstants.BlueprintId</c> ("genesis").</b>
    /// <c>TransactionTypeClassifier.IsGenesisTransaction</c> keys off exactly that value, so using it
    /// would classify a routine policy update as the network's pre-signed genesis transaction and
    /// subject it to the short <c>GenesisMaxAge</c> freshness window.</item>
    /// </list>
    /// The <c>Metadata["Type"]="Control"</c> entry is what earns the control-transaction exemptions
    /// (<c>IsGenesisOrControlTransaction</c>), so this id does not need to be "genesis" to skip
    /// blueprint/routing validation.
    /// </remarks>
    public const string GovernanceBlueprintId = GovernanceBlueprint.BlueprintId;

    // Must match the Validator's ValidationEngine.CanonicalJsonOptions — it re-canonicalises the
    // payload and recomputes the hash, so a divergence here is a silent VAL_HASH_001 rejection.
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private readonly TransactionManager _transactionManager;
    private readonly IGovernanceSigningService _signingService;
    private readonly IValidatorServiceClient _validatorClient;
    private readonly ILogger<CryptoPolicyService> _logger;

    public CryptoPolicyService(
        TransactionManager transactionManager,
        IGovernanceSigningService signingService,
        IValidatorServiceClient validatorClient,
        ILogger<CryptoPolicyService> logger)
    {
        _transactionManager = transactionManager;
        _signingService = signingService;
        _validatorClient = validatorClient;
        _logger = logger;
    }

    /// <summary>
    /// Gets the active crypto policy for a register by scanning its control transaction chain.
    /// Returns the default policy if no explicit policy has been set.
    /// </summary>
    public async Task<CryptoPolicy> GetActivePolicyAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Get genesis transaction to check for embedded policy
            var genesisPolicy = await ExtractGenesisPolicyAsync(registerId, cancellationToken);

            // Scan control transactions for policy updates (latest version wins)
            var latestUpdate = await FindLatestPolicyUpdateAsync(registerId, cancellationToken);

            if (latestUpdate != null)
                return latestUpdate;

            if (genesisPolicy != null)
                return genesisPolicy;

            // No explicit policy — return default (permissive, all algorithms accepted)
            _logger.LogDebug("No crypto policy found for register {RegisterId}, using default", registerId);
            return CryptoPolicy.CreateDefault();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to extract crypto policy for register {RegisterId}, using default", registerId);
            return CryptoPolicy.CreateDefault();
        }
    }

    /// <summary>
    /// Submits a crypto policy update as a control transaction, via the Validator, so it seals into
    /// a docket and replicates to every node holding the register.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>This MUST go through the Validator.</b> An earlier implementation wrote the control
    /// transaction straight to the register store via <c>TransactionManager.StoreTransactionAsync</c>.
    /// That path looks like it works — the row appears in Mongo and the endpoint returns 200 — but
    /// <c>DocketBuilder</c> builds dockets solely from <c>IVerifiedTransactionQueue</c>, which only
    /// validator submission populates. So the transaction was never sealed into a docket, the
    /// docket-write DevMode projection never fired, the register flag never flipped, and the
    /// Validator's one-way <c>DevMode</c> guard never ran. Silent, and green in tests, because
    /// nothing asserted the join. Every other control-transaction producer in this service
    /// (genesis ingestion, register creation, system-register publish) submits via the Validator;
    /// this is now consistent with them.
    /// </para>
    /// <para>
    /// The transaction id is derived deterministically from the register and policy version, so a
    /// retry of the same logical update is idempotent rather than forking the chain.
    /// </para>
    /// </remarks>
    /// <param name="registerId">Target register.</param>
    /// <param name="policy">The new policy. Its <see cref="CryptoPolicy.Version"/> must already be set.</param>
    /// <param name="updatedBy">Optional identity recorded on the payload for audit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The submitted transaction id and policy version.</returns>
    /// <exception cref="InvalidOperationException">
    /// The policy re-enables DevMode, or the Validator rejected the submission.
    /// </exception>
    public async Task<CryptoPolicySubmissionResult> SubmitPolicyUpdateAsync(
        string registerId,
        CryptoPolicy policy,
        string? updatedBy = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentNullException.ThrowIfNull(policy);

        // Defence in depth. The Validator refuses a policy update carrying DevMode=true
        // (ControlDocketProcessor.ValidateCryptoPolicyUpdate), which is the consensus-level
        // guarantee. Refusing here too means a caller gets a clear error instead of a
        // submission that is accepted locally and then silently dropped at seal time.
        if (policy.DevMode)
        {
            throw new InvalidOperationException(
                "DevMode cannot be enabled via a crypto-policy update — it is a one-way, " +
                "genesis-only posture (a register may be promoted DevMode→Normal, never the reverse).");
        }

        // Canonical JSON is what the Validator re-serialises and hashes. CryptoPolicy carries
        // [JsonPropertyName] camelCase attributes matching CryptoPolicyUpdatePayload, so this
        // deserialises cleanly on the Validator side.
        var canonicalJson = JsonSerializer.Serialize(policy, CanonicalJsonOptions);
        var payloadBytes = Encoding.UTF8.GetBytes(canonicalJson);
        var payloadHash = Convert.ToHexString(SHA256.HashData(payloadBytes)).ToLowerInvariant();

        var txIdSource = $"crypto-policy-update-{registerId}-v{policy.Version}";
        var txId = Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(txIdSource))).ToLowerInvariant();

        var chainHead = await _transactionManager.GetLatestTransactionAsync(registerId, cancellationToken);

        // Feature 189 (R-001): signed by an ORGANISATION on the register's roster, at slot 100 —
        // not by the node's system wallet at slot 101, which is on no roster and whose signature
        // the Validator therefore refuses on any register whose genesis has sealed.
        var signResult = await _signingService.SignAsync(
            registerId: registerId,
            txId: txId,
            payloadHash: payloadHash,
            preferredSubject: null,   // Owner signs; consortium selection is Feature 189 US2
            cancellationToken: cancellationToken);

        var submission = new TransactionSubmission
        {
            TransactionId = txId,
            RegisterId = registerId,
            // Non-empty (TX_003) and deliberately not "genesis" — see GovernanceBlueprintId.
            BlueprintId = GovernanceBlueprintId,
            // ActionId is load-bearing: ControlDocketProcessor.GetControlActionType matches on it to
            // recognise this as a crypto-policy update. Anything else and the policy is never applied.
            ActionId = CryptoPolicyUpdateActionId,
            Payload = JsonDocument.Parse(canonicalJson).RootElement,
            PayloadHash = payloadHash,
            PreviousTransactionId = chainHead?.TxId,
            // Base64Url, matching every other control-transaction producer. The Validator decodes
            // signatures as base64url; plain base64 silently fails signature verification.
            Signatures =
            [
                new SignatureInfo
                {
                    PublicKey = Base64Url.EncodeToString(signResult.PublicKey),
                    SignatureValue = Base64Url.EncodeToString(signResult.Signature),
                    Algorithm = signResult.Algorithm
                }
            ],
            CreatedAt = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, string>
            {
                // "Type" resolves the persisted MetaData.TransactionType (DocketRegisterProjection),
                // and the whole dictionary is copied onto TrackingData. Register Service's docket-write
                // path requires BOTH TransactionType==Control and TrackingData["transactionType"]
                // =="CryptoPolicyUpdate" before it projects DevMode onto the register record.
                ["Type"] = "Control",
                ["transactionType"] = CryptoPolicyUpdateTrackingType,
                ["policyVersion"] = policy.Version.ToString(),
                ["SystemWalletAddress"] = signResult.WalletAddress
            }
        };

        var result = await _validatorClient.SubmitTransactionAsync(submission, cancellationToken);

        if (!result.Success)
        {
            _logger.LogError(
                "Crypto policy update v{Version} for register {RegisterId} rejected by Validator: {Error}",
                policy.Version, registerId, result.ErrorMessage);
            throw new InvalidOperationException(
                $"Crypto policy update submission failed for register {registerId}: {result.ErrorMessage}");
        }

        _logger.LogInformation(
            "Crypto policy update v{Version} (devMode={DevMode}) submitted for register {RegisterId} as {TxId} — " +
            "takes effect on every node when the control transaction seals",
            policy.Version, policy.DevMode, registerId, txId);

        return new CryptoPolicySubmissionResult
        {
            TransactionId = txId,
            PolicyVersion = policy.Version
        };
    }

    /// <summary>
    /// Gets all policy versions for a register, ordered by version number.
    /// </summary>
    public async Task<List<CryptoPolicy>> GetPolicyHistoryAsync(
        string registerId,
        CancellationToken cancellationToken = default)
    {
        var policies = new List<CryptoPolicy>();

        var genesisPolicy = await ExtractGenesisPolicyAsync(registerId, cancellationToken);
        if (genesisPolicy != null)
            policies.Add(genesisPolicy);

        var updates = await FindAllPolicyUpdatesAsync(registerId, cancellationToken);
        policies.AddRange(updates);

        return policies.OrderBy(p => p.Version).ToList();
    }

    private async Task<CryptoPolicy?> ExtractGenesisPolicyAsync(
        string registerId, CancellationToken ct)
    {
        // Genesis is the first control transaction on the register — pushed down to the store
        // (earliest Control by TimeStamp) instead of materialising the whole ledger.
        var earliestControl = await _transactionManager.GetTransactionsByTypeAsync(
            registerId, TransactionType.Control, TransactionSort.TimeStampAscending, skip: 0, take: 1, cancellationToken: ct);
        var genesis = earliestControl.FirstOrDefault();

        if (genesis?.Payloads == null || genesis.Payloads.Length == 0)
            return null;

        return ExtractPolicyFromPayload(genesis.Payloads[0].Data);
    }

    private async Task<CryptoPolicy?> FindLatestPolicyUpdateAsync(
        string registerId, CancellationToken ct)
    {
        var updates = await FindAllPolicyUpdatesAsync(registerId, ct);
        return updates.OrderByDescending(p => p.Version).FirstOrDefault();
    }

    private async Task<List<CryptoPolicy>> FindAllPolicyUpdatesAsync(
        string registerId, CancellationToken ct)
    {
        var policies = new List<CryptoPolicy>();

        // Pushed down to the store: all Control txs, earliest first (index-backed), then the
        // policy-update filter in memory over that small subset.
        var allControl = await _transactionManager.GetTransactionsByTypeAsync(
            registerId, TransactionType.Control, TransactionSort.TimeStampAscending, cancellationToken: ct);
        var controlTxs = allControl.Where(IsCryptoPolicyUpdate);

        foreach (var tx in controlTxs)
        {
            if (tx.Payloads == null || tx.Payloads.Length == 0)
                continue;

            var policy = ExtractPolicyFromPayload(tx.Payloads[0].Data);
            if (policy != null)
                policies.Add(policy);
        }

        return policies;
    }

    private static bool IsCryptoPolicyUpdate(TransactionModel tx)
    {
        // Check metadata for crypto policy update marker
        if (tx.MetaData?.TrackingData != null &&
            tx.MetaData.TrackingData.TryGetValue("transactionType", out var txType) &&
            txType == "CryptoPolicyUpdate")
        {
            return true;
        }

        return false;
    }

    private static CryptoPolicy? ExtractPolicyFromPayload(string? base64Data)
    {
        if (string.IsNullOrEmpty(base64Data))
            return null;

        try
        {
            // Payload data is base64url-encoded JSON
            byte[] bytes;
            try
            {
                bytes = Convert.FromBase64String(base64Data);
            }
            catch (FormatException)
            {
                // Try base64url decoding
                var padded = base64Data.Replace('-', '+').Replace('_', '/');
                switch (padded.Length % 4)
                {
                    case 2: padded += "=="; break;
                    case 3: padded += "="; break;
                }
                bytes = Convert.FromBase64String(padded);
            }

            var json = System.Text.Encoding.UTF8.GetString(bytes);

            // Try to deserialize as RegisterControlRecord (genesis) or CryptoPolicy (update)
            // First, try as a CryptoPolicy directly
            var policy = JsonSerializer.Deserialize<CryptoPolicy>(json);
            if (policy != null && policy.Version > 0 && policy.AcceptedSignatureAlgorithms.Length > 0)
                return policy;

            // Try as RegisterControlRecord (either bare-shape pre-#868 or wrapped post-#868
            // — RegisterControlPayloadReader handles both) and extract its CryptoPolicy.
            var controlRecord = RegisterControlPayloadReader.TryRead(bytes);
            return controlRecord?.CryptoPolicy;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// Outcome of submitting a crypto policy update control transaction.
/// </summary>
/// <remarks>
/// Submission is not application. The policy takes effect — and, for a DevMode→Normal promotion,
/// the register flag flips on every node — only once this transaction seals into a docket.
/// </remarks>
public record CryptoPolicySubmissionResult
{
    /// <summary>Identifier of the submitted control transaction.</summary>
    public required string TransactionId { get; init; }

    /// <summary>Version number of the submitted policy.</summary>
    public required uint PolicyVersion { get; init; }
}
