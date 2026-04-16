// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Extensions.Logging;

using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Models;
using Sorcha.ServiceClients.Register;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Services.Implementation;
using Sorcha.Wallet.Service.Credentials;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Feature 106 — default implementation of <see cref="IInboundCredentialDetector"/>.
/// Fetches the inbound transaction, finds the recipient-addressed encrypted disclosure
/// group that matches the local wallet, unwraps the symmetric key, decrypts the
/// disclosure payload, and looks for a <c>/credential</c> object shape produced by
/// the Blueprint Service's <c>ActionExecutionService</c> Wave A step 9b-bis.
/// </summary>
/// <remarks>
/// Invariants:
/// <list type="bullet">
///   <item>MUST NOT throw. All failure modes are logged and counted in
///   <see cref="InboundCredentialDetectorMetrics"/>. A thrown dependency is caught
///   by the outer try/catch and surfaced as <c>Errored</c>.</item>
///   <item>Dedup by credential id: duplicate arrivals are a no-op and increment the
///   <c>SkippedDuplicate</c> counter (data-model invariant INV-1).</item>
///   <item>Persisted rows start in <see cref="CredentialStatus.PendingAcceptance"/>.
///   The holder accept/decline flow transitions the row via the dedicated PATCH
///   endpoint (Wave C).</item>
/// </list>
/// Contract: <c>specs/106-register-native-credentials/contracts/inbound-credential-detection.md</c>.
/// </remarks>
public sealed class InboundCredentialDetector : IInboundCredentialDetector
{
    private readonly IRegisterServiceClient _registerClient;
    private readonly WalletManager _walletManager;
    private readonly ISymmetricCrypto _symmetricCrypto;
    private readonly ICredentialStore _credentialStore;
    private readonly InboundCredentialDetectorMetrics _metrics;
    private readonly ILogger<InboundCredentialDetector> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public InboundCredentialDetector(
        IRegisterServiceClient registerClient,
        WalletManager walletManager,
        ISymmetricCrypto symmetricCrypto,
        ICredentialStore credentialStore,
        InboundCredentialDetectorMetrics metrics,
        ILogger<InboundCredentialDetector> logger)
    {
        _registerClient = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        _walletManager = walletManager ?? throw new ArgumentNullException(nameof(walletManager));
        _symmetricCrypto = symmetricCrypto ?? throw new ArgumentNullException(nameof(symmetricCrypto));
        _credentialStore = credentialStore ?? throw new ArgumentNullException(nameof(credentialStore));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<InboundCredentialExtract?> TryExtractAsync(
        string walletAddress,
        string transactionId,
        string registerId,
        CancellationToken cancellationToken)
    {
        _metrics.RecordInspected();
        var sw = Stopwatch.StartNew();

        try
        {
            // 1. Fetch the transaction envelope from the register.
            var tx = await _registerClient.GetTransactionAsync(registerId, transactionId, cancellationToken);
            if (tx is null)
            {
                _logger.LogDebug(
                    "InboundCredentialDetector: transaction {TxId} not found in register {RegisterId}",
                    transactionId, registerId);
                _metrics.RecordSkippedNoRecipientDisclosure();
                return null;
            }

            // 2. The encrypted disclosure groups are stored inside the canonical JSON payload
            //    at Payloads[0].Data. Decode exactly as FileReassemblyService does to stay
            //    symmetric with the write path in ActionExecutionService + ITransactionBuilderService.
            var rawPayload = tx.Payloads?.FirstOrDefault()?.Data;
            if (string.IsNullOrWhiteSpace(rawPayload))
            {
                _metrics.RecordSkippedNoRecipientDisclosure();
                return null;
            }

            byte[] canonicalBytes;
            try
            {
                canonicalBytes = DecodePayloadData(rawPayload);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "InboundCredentialDetector: failed to decode payload data for tx {TxId}",
                    transactionId);
                _metrics.RecordSkippedNoRecipientDisclosure();
                return null;
            }

            JsonElement txJson;
            try
            {
                txJson = JsonSerializer.Deserialize<JsonElement>(canonicalBytes, JsonOptions);
            }
            catch (JsonException)
            {
                _metrics.RecordSkippedNoRecipientDisclosure();
                return null;
            }

            // 3. Only encrypted transactions carry recipient-addressed disclosure groups.
            //    Dev-mode/plaintext transactions never seal Feature 106 credentials.
            var contentEncoding = txJson.TryGetProperty("contentEncoding", out var ceEl)
                ? ceEl.GetString()
                : null;
            if (!string.Equals(contentEncoding, "encrypted", StringComparison.OrdinalIgnoreCase))
            {
                _metrics.RecordSkippedNoRecipientDisclosure();
                return null;
            }

            // 4. Walk the encrypted payload groups looking for one whose wrappedKeys contains
            //    our wallet address. This matches the FileReassemblyService pattern exactly.
            if (!txJson.TryGetProperty("encryptedPayloads", out var groupsEl)
                || groupsEl.ValueKind != JsonValueKind.Array)
            {
                _metrics.RecordSkippedNoRecipientDisclosure();
                return null;
            }

            JsonElement? decryptedPayload = null;
            foreach (var group in groupsEl.EnumerateArray())
            {
                var decrypted = await TryDecryptGroupForWalletAsync(
                    walletAddress, group, transactionId, cancellationToken);
                if (decrypted is not null)
                {
                    decryptedPayload = decrypted;
                    break;
                }
            }

            if (decryptedPayload is null)
            {
                // Either none of the groups target us, OR every group we tried failed to decrypt.
                // The distinction matters for metrics — TryDecryptGroupForWalletAsync counts
                // decrypt failures internally; a bare null here means no group targeted us.
                _metrics.RecordSkippedNoRecipientDisclosure();
                return null;
            }

            // 5. Look for the Feature 106 `/credential` shape in the decrypted payload.
            //    The writer at ActionExecutionService step 9b-bis puts the credential under
            //    a literal "/credential" field. camelCase fallback isn't required because
            //    the JSON is serialised with the literal pointer key.
            var credentialField = FindCredentialField(decryptedPayload.Value);
            if (credentialField is null)
            {
                _metrics.RecordSkippedNoRecipientDisclosure();
                return null;
            }

            // 6. Parse the extract shape.
            var extract = TryParseExtract(credentialField.Value, transactionId);
            if (extract is null)
            {
                _logger.LogWarning(
                    "InboundCredentialDetector: tx {TxId} has /credential shape but parsing failed",
                    transactionId);
                _metrics.RecordSkippedNoRecipientDisclosure();
                return null;
            }

            // 7. Dedup by credential id (INV-1) — persisting is idempotent on replay.
            var existing = await _credentialStore.GetByIdAsync(extract.CredentialId, cancellationToken);
            if (existing is not null)
            {
                _logger.LogDebug(
                    "InboundCredentialDetector: credential {CredentialId} already stored — skipping duplicate",
                    extract.CredentialId);
                _metrics.RecordSkippedDuplicate();
                return null;
            }

            // 8. Persist as PendingAcceptance. Wave C's PATCH endpoint handles accept/decline.
            var entity = new CredentialEntity
            {
                Id = extract.CredentialId,
                Type = extract.CredentialType,
                IssuerDid = extract.IssuerDid,
                SubjectDid = walletAddress,
                ClaimsJson = extract.ClaimsJson,
                IssuedAt = extract.IssuedAt,
                ExpiresAt = extract.ExpiresAt,
                RawToken = extract.RawToken,
                Status = CredentialStatus.PendingAcceptance,
                IssuanceTxId = transactionId,
                IssuanceBlueprintId = extract.BlueprintId,
                IssuanceInstanceId = extract.InstanceId,
                IssuanceActionId = extract.ActionId,
                ClaimActionId = extract.ClaimActionId,
                RegisterId = extract.RegisterId,
                WalletAddress = walletAddress,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            await _credentialStore.StoreAsync(entity, cancellationToken);

            _metrics.RecordExtracted();
            _logger.LogInformation(
                "InboundCredentialDetector: persisted pending credential {CredentialId} (type {Type}) for wallet {Wallet} from tx {TxId}",
                extract.CredentialId, extract.CredentialType, walletAddress, transactionId);

            return extract;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "InboundCredentialDetector: unexpected error inspecting tx {TxId} for wallet {Wallet}",
                transactionId, walletAddress);
            _metrics.RecordErrored();
            return null;
        }
        finally
        {
            sw.Stop();
            _metrics.RecordExtractionLatency(sw.Elapsed.TotalMilliseconds);
        }
    }

    /// <summary>
    /// Walks a single <c>encryptedPayloads</c> group, finds the wrapped key entry for
    /// <paramref name="walletAddress"/>, unwraps the symmetric key via the wallet's
    /// private key, and decrypts the group ciphertext. Returns the decrypted payload
    /// JSON or null if the group doesn't target this wallet. Counts decrypt failures
    /// internally.
    /// </summary>
    private async Task<JsonElement?> TryDecryptGroupForWalletAsync(
        string walletAddress,
        JsonElement group,
        string txId,
        CancellationToken ct)
    {
        if (!group.TryGetProperty("wrappedKeys", out var wrappedKeysEl)
            || wrappedKeysEl.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var wk in wrappedKeysEl.EnumerateArray())
        {
            var wkAddress = wk.TryGetProperty("walletAddress", out var waEl) ? waEl.GetString() : null;
            if (!string.Equals(wkAddress, walletAddress, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!wk.TryGetProperty("encryptedKey", out var ekEl)
                || !group.TryGetProperty("ciphertext", out var ctEl)
                || !group.TryGetProperty("nonce", out var nonceEl))
            {
                _metrics.RecordSkippedDecryptFailed();
                return null;
            }

            byte[] encryptedKey;
            byte[] ciphertext;
            byte[] nonce;
            try
            {
                encryptedKey = Convert.FromBase64String(ekEl.GetString()!);
                ciphertext = Convert.FromBase64String(ctEl.GetString()!);
                nonce = Convert.FromBase64String(nonceEl.GetString()!);
            }
            catch
            {
                _metrics.RecordSkippedDecryptFailed();
                return null;
            }

            byte[] symmetricKey;
            try
            {
                symmetricKey = await _walletManager.DecryptPayloadAsync(walletAddress, encryptedKey, ct);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "InboundCredentialDetector: symmetric key unwrap failed for wallet {Wallet} on tx {TxId}",
                    walletAddress, txId);
                _metrics.RecordSkippedDecryptFailed();
                return null;
            }

            using var cipherModel = new SymmetricCiphertext
            {
                Data = ciphertext,
                Key = symmetricKey,
                IV = nonce,
                Type = EncryptionType.XCHACHA20_POLY1305,
            };

            var decrypt = await _symmetricCrypto.DecryptAsync(cipherModel, ct);
            if (!decrypt.IsSuccess || decrypt.Value is null)
            {
                _logger.LogDebug(
                    "InboundCredentialDetector: symmetric decryption failed for wallet {Wallet} on tx {TxId}: {Error}",
                    walletAddress, txId, decrypt.ErrorMessage);
                _metrics.RecordSkippedDecryptFailed();
                return null;
            }

            try
            {
                return JsonSerializer.Deserialize<JsonElement>(decrypt.Value, JsonOptions);
            }
            catch (JsonException)
            {
                _metrics.RecordSkippedDecryptFailed();
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// The writer at Blueprint Service step 9b-bis places the credential under a
    /// literal "/credential" key. Some serialisers may emit it as "credential"
    /// after RFC 6901 unescape round-tripping; accept both.
    /// </summary>
    internal static JsonElement? FindCredentialField(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
            return null;

        if (payload.TryGetProperty("/credential", out var literal)
            && literal.ValueKind == JsonValueKind.Object)
        {
            return literal;
        }

        if (payload.TryGetProperty("credential", out var bare)
            && bare.ValueKind == JsonValueKind.Object)
        {
            return bare;
        }

        return null;
    }

    internal static InboundCredentialExtract? TryParseExtract(JsonElement credential, string txId)
    {
        var id = credential.TryGetProperty("credentialId", out var idEl) ? idEl.GetString() : null;
        var type = credential.TryGetProperty("credentialType", out var typeEl) ? typeEl.GetString() : null;
        var issuerDid = credential.TryGetProperty("issuerDid", out var issuerEl) ? issuerEl.GetString() : null;
        var rawToken = credential.TryGetProperty("rawToken", out var rawEl) ? rawEl.GetString() : null;

        if (string.IsNullOrWhiteSpace(id)
            || string.IsNullOrWhiteSpace(type)
            || string.IsNullOrWhiteSpace(issuerDid)
            || string.IsNullOrWhiteSpace(rawToken))
        {
            return null;
        }

        DateTimeOffset issuedAt = credential.TryGetProperty("issuedAt", out var issuedEl)
            && issuedEl.TryGetDateTimeOffset(out var parsedIssued)
                ? parsedIssued
                : DateTimeOffset.UtcNow;

        DateTimeOffset? expiresAt = null;
        if (credential.TryGetProperty("expiresAt", out var expiresEl)
            && expiresEl.ValueKind == JsonValueKind.String
            && expiresEl.TryGetDateTimeOffset(out var parsedExpires))
        {
            expiresAt = parsedExpires;
        }

        var blueprintId = credential.TryGetProperty("issuanceBlueprintId", out var bpEl) ? bpEl.GetString() : null;
        var instanceId = credential.TryGetProperty("issuanceInstanceId", out var instEl) ? instEl.GetString() : null;
        var actionId = credential.TryGetProperty("issuanceActionId", out var actEl) ? actEl.GetString() : null;
        var claimActionId = credential.TryGetProperty("claimActionId", out var claimEl) ? claimEl.GetString() : null;
        var credRegisterId = credential.TryGetProperty("registerId", out var regEl) ? regEl.GetString() : null;

        // Persist the full credential JSON as ClaimsJson so the holder UI can render it.
        // The writer doesn't currently seal a pre-extracted claims bag — rawToken is the
        // authoritative claims source for SD-JWT VC.
        var claimsJson = credential.GetRawText();

        return new InboundCredentialExtract
        {
            CredentialId = id!,
            CredentialType = type!,
            IssuerDid = issuerDid!,
            RawToken = rawToken!,
            ClaimsJson = claimsJson,
            IssuedAt = issuedAt,
            ExpiresAt = expiresAt,
            TransactionId = txId,
            BlueprintId = blueprintId,
            InstanceId = instanceId,
            ActionId = actionId,
            ClaimActionId = claimActionId,
            RegisterId = credRegisterId,
        };
    }

    /// <summary>
    /// Mirrors <c>FileReassemblyService.DecodePayloadData</c> — the write path uses
    /// base64url by default, older payloads use raw base64. Supports both.
    /// </summary>
    private static byte[] DecodePayloadData(string raw)
    {
        // Try base64url first (RFC 4648 §5) — the canonical Feature 085+ write encoding.
        try
        {
            var padded = raw
                .Replace('-', '+')
                .Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2: padded += "=="; break;
                case 3: padded += "="; break;
            }
            return Convert.FromBase64String(padded);
        }
        catch
        {
            return Convert.FromBase64String(raw);
        }
    }
}
