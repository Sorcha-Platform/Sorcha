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

            // 3. Fork on payload shape:
            //    - `contentEncoding == "encrypted"`: walk encryptedPayloads → decrypt our group → find /credential
            //    - otherwise: only parse when the register is DevMode=true, then walk
            //      payloads[walletAddress] → find /credential. Plaintext on a non-DevMode
            //      register is a security signal (encryption skipped because recipient keys
            //      couldn't be resolved) and we must NOT parse such transactions.
            var contentEncoding = txJson.TryGetProperty("contentEncoding", out var ceEl)
                ? ceEl.GetString()
                : null;
            var isEncrypted = string.Equals(contentEncoding, "encrypted", StringComparison.OrdinalIgnoreCase);

            JsonElement? credentialField;
            if (isEncrypted)
            {
                credentialField = await TryFindEncryptedCredentialAsync(
                    walletAddress, txJson, transactionId, cancellationToken);
            }
            else
            {
                credentialField = await TryFindDevModePlaintextCredentialAsync(
                    walletAddress, registerId, txJson, transactionId, cancellationToken);
            }

            if (credentialField is null)
            {
                // All skip counters are recorded inside the Try* helpers; a null return here
                // is already accounted for. Nothing more to do.
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

            // 7. Dedup by (credentialId, walletAddress) (INV-1) — persisting is idempotent on
            //    replay. MUST be wallet-scoped: on single-node deployments the issuer audit
            //    row lives under the issuer wallet with the same credential id, and the
            //    prior Id-only dedup would skip the recipient-side insert entirely (the
            //    n1 Feature 106 bug surfaced at deploy time).
            var existing = await _credentialStore.GetByIdForWalletAsync(
                extract.CredentialId, walletAddress, cancellationToken);
            if (existing is not null)
            {
                _logger.LogDebug(
                    "InboundCredentialDetector: credential {CredentialId} already stored for wallet {Wallet} — skipping duplicate",
                    extract.CredentialId, walletAddress);
                _metrics.RecordSkippedDuplicate();
                return null;
            }

            // 8. Persist as PendingAcceptance. Wave C's PATCH endpoint handles accept/decline.
            var entity = new CredentialEntity
            {
                Id = extract.CredentialId,
                Type = extract.CredentialType,
                IssuerDid = extract.IssuerDid,
                IssuerOrgName = extract.IssuerOrgName,
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
    /// Encrypted path: walks the transaction's <c>encryptedPayloads</c> array, finds
    /// the first group that targets <paramref name="walletAddress"/> (via
    /// <see cref="TryDecryptGroupForWalletAsync"/>), and extracts the <c>/credential</c>
    /// field from the decrypted payload. Records the appropriate skip counters on miss.
    /// </summary>
    private async Task<JsonElement?> TryFindEncryptedCredentialAsync(
        string walletAddress,
        JsonElement txJson,
        string transactionId,
        CancellationToken cancellationToken)
    {
        if (!txJson.TryGetProperty("encryptedPayloads", out var groupsEl)
            || groupsEl.ValueKind != JsonValueKind.Array)
        {
            _metrics.RecordSkippedNoRecipientDisclosure();
            return null;
        }

        foreach (var group in groupsEl.EnumerateArray())
        {
            var decrypted = await TryDecryptGroupForWalletAsync(
                walletAddress, group, transactionId, cancellationToken);
            if (decrypted is null)
            {
                continue;
            }

            var field = FindCredentialField(decrypted.Value);
            if (field is not null)
            {
                return field;
            }
        }

        // Either no group targeted us, every targeted group failed to decrypt (those
        // increment SkippedDecryptFailed internally), or a group decrypted but carried
        // no /credential shape. All three collapse to "no recipient disclosure" for us.
        _metrics.RecordSkippedNoRecipientDisclosure();
        return null;
    }

    /// <summary>
    /// Plaintext path (Feature 106 DevMode): when the register is in DevMode the
    /// writer emits payloads as a flat <c>{ "payloads": { walletAddress: { ... } } }</c>
    /// dictionary — see the plaintext branch of
    /// <c>ITransactionBuilderService.BuildActionTransactionAsync</c>. This helper looks
    /// up our wallet's entry and extracts the <c>/credential</c> shape.
    /// <para>
    /// SECURITY: plaintext transactions on non-DevMode registers are dropped. Such
    /// transactions only occur when the encryption pipeline could not resolve recipient
    /// keys (Feature 083 publishing gap or similar) — treating them as credentials would
    /// silently downgrade the DAD guarantee. Returning null on that path is intentional.
    /// </para>
    /// </summary>
    private async Task<JsonElement?> TryFindDevModePlaintextCredentialAsync(
        string walletAddress,
        string registerId,
        JsonElement txJson,
        string transactionId,
        CancellationToken cancellationToken)
    {
        bool registerDevMode;
        try
        {
            var register = await _registerClient.GetRegisterAsync(registerId, cancellationToken);
            registerDevMode = register?.DevMode ?? false;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex,
                "InboundCredentialDetector: failed to resolve register {RegisterId} DevMode flag for plaintext tx {TxId} — skipping",
                registerId, transactionId);
            _metrics.RecordSkippedNoRecipientDisclosure();
            return null;
        }

        if (!registerDevMode)
        {
            _metrics.RecordSkippedNoRecipientDisclosure();
            return null;
        }

        var field = FindPlaintextCredentialForWallet(txJson, walletAddress);
        if (field is null)
        {
            _metrics.RecordSkippedNoRecipientDisclosure();
            return null;
        }

        return field;
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
    /// DevMode plaintext shape: the writer emits a top-level
    /// <c>"payloads": { walletAddress: { "/credential": { ... } } }</c> dictionary
    /// (see <c>ITransactionBuilderService.BuildActionTransactionAsync</c> non-encrypted
    /// branch). Look up our wallet's entry, case-insensitively, and delegate to
    /// <see cref="FindCredentialField"/> for the actual shape match.
    /// </summary>
    internal static JsonElement? FindPlaintextCredentialForWallet(JsonElement txJson, string walletAddress)
    {
        if (!txJson.TryGetProperty("payloads", out var payloadsEl)
            || payloadsEl.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var prop in payloadsEl.EnumerateObject())
        {
            if (!string.Equals(prop.Name, walletAddress, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (prop.Value.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            return FindCredentialField(prop.Value);
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

        var issuerOrgName = credential.TryGetProperty("issuerOrgName", out var orgEl) ? orgEl.GetString() : null;
        var blueprintId = credential.TryGetProperty("issuanceBlueprintId", out var bpEl) ? bpEl.GetString() : null;
        var instanceId = credential.TryGetProperty("issuanceInstanceId", out var instEl) ? instEl.GetString() : null;
        var actionId = credential.TryGetProperty("issuanceActionId", out var actEl) ? actEl.GetString() : null;
        var claimActionId = credential.TryGetProperty("claimActionId", out var claimEl) ? claimEl.GetString() : null;
        var credRegisterId = credential.TryGetProperty("registerId", out var regEl) ? regEl.GetString() : null;

        // Decode the SD-JWT body + disclosures into a flat claim dictionary so
        // the Pending tab can show the holder what they're being offered before
        // they Accept or Decline. Display-only — signature verification kicks in
        // when the holder accepts (we deliberately don't trust the issuer key
        // before that point).
        //
        // The previous behaviour stored the full credential envelope here
        // (rawToken + issuerDid + subjectDid + …), which surfaced as garbage
        // claims like "rawToken: eyJh…" on the Pending card. Those envelope
        // fields are already top-level columns on CredentialEntity.
        var claimsJson = ExtractDisclosedClaimsJson(rawToken!) ?? "{}";

        return new InboundCredentialExtract
        {
            CredentialId = id!,
            CredentialType = type!,
            IssuerDid = issuerDid!,
            RawToken = rawToken!,
            IssuerOrgName = issuerOrgName,
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

    /// <summary>
    /// Decode an SD-JWT VC into the flat claim dictionary the holder UI needs.
    /// Reads always-disclosed claims from the JWT body and merges in
    /// selectively-disclosed claims from each <c>~salt~</c> disclosure segment.
    /// Skips signature verification — this projection is for *display* on the
    /// Pending tab, before the holder has chosen to trust the issuer. Verification
    /// runs on accept.
    /// </summary>
    /// <returns>JSON object string of flat claims, or null if the token is malformed.</returns>
    internal static string? ExtractDisclosedClaimsJson(string rawToken)
    {
        if (string.IsNullOrWhiteSpace(rawToken)) return null;

        try
        {
            // SD-JWT format: <header>.<body>.<signature>~<disclosure1>~<disclosure2>~...~[<kb-jwt>]
            var segments = rawToken.Split('~');
            var jwtParts = segments[0].Split('.');
            if (jwtParts.Length < 2) return null;

            var bodyBytes = DecodePayloadData(jwtParts[1]);
            using var bodyDoc = JsonDocument.Parse(bodyBytes);

            var claims = new Dictionary<string, object?>();

            // Always-disclosed claims live in the JWT body. Skip SD-JWT/JWT
            // protocol fields — they're not credential claims.
            var skip = new HashSet<string>(StringComparer.Ordinal)
            {
                "iss", "sub", "iat", "exp", "nbf", "jti", "aud", "vct",
                "_sd", "_sd_alg", "cnf", "credentialStatus", "type"
            };
            foreach (var prop in bodyDoc.RootElement.EnumerateObject())
            {
                if (skip.Contains(prop.Name)) continue;
                claims[prop.Name] = JsonElementToValue(prop.Value);
            }

            // Selectively-disclosed claims: each disclosure is a base64url-encoded
            // JSON array [salt, claimName, claimValue] (RFC 9901 §4.2.1).
            for (var i = 1; i < segments.Length; i++)
            {
                var seg = segments[i];
                if (string.IsNullOrEmpty(seg)) continue;
                // The optional KB-JWT at the tail is a JWT (header.body.sig), not a
                // disclosure — it has dots, real disclosures don't.
                if (seg.Contains('.')) continue;

                try
                {
                    var disclosureBytes = DecodePayloadData(seg);
                    using var disclosureDoc = JsonDocument.Parse(disclosureBytes);
                    if (disclosureDoc.RootElement.ValueKind != JsonValueKind.Array) continue;
                    var arr = disclosureDoc.RootElement;
                    if (arr.GetArrayLength() < 3) continue;
                    var name = arr[1].GetString();
                    if (string.IsNullOrEmpty(name)) continue;
                    claims[name] = JsonElementToValue(arr[2]);
                }
                catch
                {
                    // Single bad disclosure shouldn't drop the whole credential —
                    // skip it and continue with the rest.
                }
            }

            return JsonSerializer.Serialize(claims, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    private static object? JsonElementToValue(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString(),
        JsonValueKind.Number => el.TryGetInt64(out var i) ? i : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => JsonSerializer.Deserialize<object?>(el.GetRawText(), JsonOptions)
    };
}
