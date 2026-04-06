// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

using Sorcha.Blueprint.Models;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;
using Sorcha.Cryptography.Models;
using Sorcha.ServiceClients.Register;
using Sorcha.TransactionHandler.Encryption;
using Sorcha.Wallet.Core.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Mediates decryption and reassembly of multi-chunk encrypted file attachments.
/// </summary>
/// <remarks>
/// <para>
/// <b>Download flow:</b>
/// <list type="number">
///   <item>Fetch the action transaction from the Register Service.</item>
///   <item>Decode <c>Payloads[0].Data</c> (base64url/base64) to obtain the canonical JSON.</item>
///   <item>Locate the <c>encryptedPayloads</c> group whose <c>wrappedKeys</c> contains the requester's wallet address.</item>
///   <item>Unwrap the group's symmetric key via <see cref="WalletManager.DecryptPayloadAsync"/> (wallet private key decryption).</item>
///   <item>Decrypt the group ciphertext to get the plaintext action payload (JSON).</item>
///   <item>Navigate to <c>payload[fieldName][fileIndex]</c> and deserialise as <see cref="FileReference"/>.</item>
///   <item>For each chunk transaction ID in <see cref="FileReference.ChunkTransactionIds"/>:
///     <list type="bullet">
///       <item>Fetch the chunk transaction from the Register Service.</item>
///       <item>Decode <c>Payloads[0].Data</c> to get the chunk envelope JSON (<c>{ ciphertext, nonce }</c>).</item>
///       <item>Derive the per-chunk key via <c>HKDF-SHA256(masterKey, salt, chunkIndex)</c>.</item>
///       <item>Decrypt the chunk with XChaCha20-Poly1305.</item>
///     </list>
///   </item>
///   <item>After streaming all chunks, verify the SHA-256 hash matches <see cref="FileReference.Hash"/>.</item>
/// </list>
/// </para>
/// <para>
/// <b>Chunk transaction payload format (as stored by Blueprint Service upload flow):</b>
/// <code>{ "ciphertext": "&lt;base64&gt;", "nonce": "&lt;base64&gt;" }</code>
/// </para>
/// </remarks>
public sealed class FileReassemblyService : IFileReassemblyService
{
    private readonly IRegisterServiceClient _registerClient;
    private readonly WalletManager _walletManager;
    private readonly ISymmetricCrypto _symmetricCrypto;
    private readonly ILogger<FileReassemblyService> _logger;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString
    };

    /// <summary>
    /// Initialises a new instance of <see cref="FileReassemblyService"/>.
    /// </summary>
    /// <param name="registerClient">Client for fetching transactions from the Register Service.</param>
    /// <param name="walletManager">Wallet manager for private-key decryption (key unwrapping).</param>
    /// <param name="symmetricCrypto">Symmetric decryption provider for XChaCha20-Poly1305.</param>
    /// <param name="logger">Logger instance.</param>
    public FileReassemblyService(
        IRegisterServiceClient registerClient,
        WalletManager walletManager,
        ISymmetricCrypto symmetricCrypto,
        ILogger<FileReassemblyService> logger)
    {
        _registerClient = registerClient ?? throw new ArgumentNullException(nameof(registerClient));
        _walletManager = walletManager ?? throw new ArgumentNullException(nameof(walletManager));
        _symmetricCrypto = symmetricCrypto ?? throw new ArgumentNullException(nameof(symmetricCrypto));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<FileDownloadResult?> PrepareDownloadAsync(
        string walletAddress,
        string registerId,
        string txId,
        string fieldName,
        int fileIndex,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletAddress);
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(txId);
        ArgumentException.ThrowIfNullOrWhiteSpace(fieldName);

        if (fileIndex < 0)
            throw new ArgumentOutOfRangeException(nameof(fileIndex), "File index must be non-negative.");

        _logger.LogInformation(
            "Preparing download for wallet {WalletAddress} from register {RegisterId}, action {ActionTxId}, field {FieldName}[{FileIndex}]",
            walletAddress, registerId, txId, fieldName, fileIndex);

        // Step 1: Fetch the action transaction from the Register Service
        var actionTx = await _registerClient.GetTransactionAsync(registerId, txId, cancellationToken);
        if (actionTx is null)
        {
            _logger.LogWarning(
                "Action transaction {ActionTxId} not found in register {RegisterId}",
                txId, registerId);
            return null;
        }

        // Step 2: Decode the raw canonical JSON bytes from Payloads[0].Data
        var rawPayload = actionTx.Payloads?.FirstOrDefault()?.Data;
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            _logger.LogWarning(
                "Action transaction {ActionTxId} has no payload data",
                txId);
            return null;
        }

        byte[] canonicalBytes = DecodePayloadData(rawPayload);

        // Step 3: Parse the transaction JSON to find the encrypted payload group for this wallet
        JsonElement txJson;
        try
        {
            txJson = JsonSerializer.Deserialize<JsonElement>(canonicalBytes, _jsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse canonical JSON for transaction {ActionTxId}", txId);
            return null;
        }

        // Step 4: Try encrypted path first, then fall back to plaintext (dev mode)
        byte[] masterFileKey;
        JsonElement payloadElement;

        var contentEncoding = txJson.TryGetProperty("contentEncoding", out var ceElement)
            ? ceElement.GetString()
            : null;

        if (string.Equals(contentEncoding, "encrypted", StringComparison.OrdinalIgnoreCase))
        {
            var decryptResult = await DecryptActionPayloadAsync(
                walletAddress, txJson, cancellationToken);
            if (decryptResult is null)
            {
                _logger.LogWarning(
                    "Wallet {WalletAddress} is not an authorised recipient or decryption failed for transaction {ActionTxId}",
                    walletAddress, txId);
                return null;
            }

            (masterFileKey, payloadElement) = decryptResult.Value;
        }
        else
        {
            // Plaintext / dev-mode transaction — no master file key wrapping
            _logger.LogDebug(
                "Transaction {ActionTxId} is plaintext (dev mode). Master file key must be stored in-band.",
                txId);

            // For plaintext transactions the symmetric key is not separately wrapped;
            // retrieve the payload JSON directly.
            if (!txJson.TryGetProperty("payloads", out var plaintextPayloads) &&
                !txJson.TryGetProperty("payload", out plaintextPayloads))
            {
                _logger.LogWarning("Cannot locate payload section in plaintext transaction {ActionTxId}", txId);
                return null;
            }

            // In dev mode there is no master file key — set to empty placeholder.
            // Chunk decryption below will fall back to reading plaintext chunk data.
            masterFileKey = [];
            payloadElement = plaintextPayloads;
        }

        // Step 5: Locate the FileReference in the decrypted payload
        var fileRef = ExtractFileReference(payloadElement, fieldName, fileIndex, walletAddress);
        if (fileRef is null)
        {
            _logger.LogWarning(
                "No FileReference found at field {FieldName}[{FileIndex}] for wallet {WalletAddress}",
                fieldName, fileIndex, walletAddress);
            return null;
        }

        if (fileRef.ChunkTransactionIds.Length == 0)
        {
            _logger.LogWarning(
                "FileReference for {FileName} has no chunk transaction IDs",
                fileRef.FileName);
            return null;
        }

        var salt = Convert.FromBase64String(fileRef.Salt);
        var expectedHash = fileRef.Hash; // "sha256:<hex>" format
        var chunkTxIds = fileRef.ChunkTransactionIds;
        var capturedMasterKey = masterFileKey;

        _logger.LogInformation(
            "File {FileName} ({ContentType}, {Size} bytes) has {ChunkCount} chunk(s) to reassemble",
            fileRef.FileName, fileRef.ContentType, fileRef.Size, chunkTxIds.Length);

        // Step 6: Build the streaming callback — fetches, decrypts, and writes chunks lazily
        async Task WriteToStreamAsync(Stream destination, CancellationToken ct)
        {
            using var sha256 = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

            for (int i = 0; i < chunkTxIds.Length; i++)
            {
                ct.ThrowIfCancellationRequested();

                var chunkTxId = chunkTxIds[i];

                _logger.LogDebug(
                    "Fetching chunk {ChunkIndex}/{Total}: transaction {ChunkTxId} from register {RegisterId}",
                    i, chunkTxIds.Length, chunkTxId, registerId);

                var chunkTx = await _registerClient.GetTransactionAsync(registerId, chunkTxId, ct);
                if (chunkTx is null)
                {
                    throw new InvalidOperationException(
                        $"Chunk transaction {chunkTxId} (index {i}) not found in register {registerId}.");
                }

                var chunkRaw = chunkTx.Payloads?.FirstOrDefault()?.Data;
                if (string.IsNullOrWhiteSpace(chunkRaw))
                {
                    throw new InvalidOperationException(
                        $"Chunk transaction {chunkTxId} (index {i}) has no payload data.");
                }

                var chunkBytes = DecodePayloadData(chunkRaw);
                byte[] plaintextChunk;

                if (capturedMasterKey.Length == HkdfKeyDerivation.KeySizeBytes)
                {
                    // Encrypted chunk path
                    plaintextChunk = await DecryptChunkAsync(
                        chunkBytes, capturedMasterKey, salt, i, ct);
                }
                else
                {
                    // Dev-mode / plaintext fallback — chunk bytes ARE the plaintext
                    plaintextChunk = chunkBytes;
                }

                sha256.AppendData(plaintextChunk);
                await destination.WriteAsync(plaintextChunk, ct);

                _logger.LogDebug(
                    "Written chunk {ChunkIndex} ({Bytes} bytes) to output stream",
                    i, plaintextChunk.Length);
            }

            // Step 7: Verify SHA-256 integrity hash
            var actualHashBytes = sha256.GetHashAndReset();
            var actualHex = Convert.ToHexString(actualHashBytes).ToLowerInvariant();
            var actualHashStr = $"sha256:{actualHex}";

            if (!string.Equals(actualHashStr, expectedHash, StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogError(
                    "Integrity check FAILED for file {FileName}: expected {ExpectedHash}, actual {ActualHash}",
                    fileRef.FileName, expectedHash, actualHashStr);
                throw new InvalidOperationException(
                    $"File integrity check failed: SHA-256 hash mismatch for '{fileRef.FileName}'. " +
                    $"The file may have been tampered with.");
            }

            _logger.LogInformation(
                "File {FileName} reassembled and integrity verified (hash {Hash})",
                fileRef.FileName, actualHashStr);
        }

        return new FileDownloadResult(
            fileRef.FileName,
            fileRef.ContentType,
            fileRef.Size,
            WriteToStreamAsync);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Decodes a payload data string that may be base64 or base64url encoded.
    /// </summary>
    private static byte[] DecodePayloadData(string data)
    {
        // Base64url (RFC 4648 §5) — no padding, '-' and '_' instead of '+' and '/'
        if (data.Contains('-') || data.Contains('_'))
        {
            return System.Buffers.Text.Base64Url.DecodeFromChars(data.AsSpan());
        }

        // Standard base64
        if (data.Contains('+') || data.Contains('/') || data.Contains('='))
        {
            return Convert.FromBase64String(data);
        }

        // Hex string (fallback)
        if (data.Length % 2 == 0 && data.All(c => char.IsAsciiHexDigit(c)))
        {
            return Convert.FromHexString(data);
        }

        // Try base64url without padding (no '-' or '_')
        return System.Buffers.Text.Base64Url.DecodeFromChars(data.AsSpan());
    }

    /// <summary>
    /// Locates the encrypted payload group for the requesting wallet, unwraps the symmetric key,
    /// decrypts the group ciphertext, and returns the symmetric key alongside the plaintext payload.
    /// </summary>
    private async Task<(byte[] MasterFileKey, JsonElement Payload)?> DecryptActionPayloadAsync(
        string walletAddress,
        JsonElement txJson,
        CancellationToken ct)
    {
        if (!txJson.TryGetProperty("encryptedPayloads", out var encryptedPayloadsElement) ||
            encryptedPayloadsElement.ValueKind != JsonValueKind.Array)
        {
            _logger.LogWarning("Transaction has contentEncoding=encrypted but no encryptedPayloads array");
            return null;
        }

        // Find the group that has a wrappedKey for our wallet
        foreach (var groupElement in encryptedPayloadsElement.EnumerateArray())
        {
            if (!groupElement.TryGetProperty("wrappedKeys", out var wrappedKeysElement) ||
                wrappedKeysElement.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var wkElement in wrappedKeysElement.EnumerateArray())
            {
                var wkAddress = wkElement.TryGetProperty("walletAddress", out var waEl)
                    ? waEl.GetString() : null;

                if (!string.Equals(wkAddress, walletAddress, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Found our wrapped key — decode the encrypted key bytes
                if (!wkElement.TryGetProperty("encryptedKey", out var ekEl))
                    continue;

                byte[] encryptedSymKey;
                try
                {
                    encryptedSymKey = Convert.FromBase64String(ekEl.GetString()!);
                }
                catch
                {
                    _logger.LogError("Could not decode encryptedKey for wallet {WalletAddress}", walletAddress);
                    return null;
                }

                // Unwrap the symmetric key using the wallet's private key
                byte[] symmetricKey;
                try
                {
                    symmetricKey = await _walletManager.DecryptPayloadAsync(
                        walletAddress, encryptedSymKey, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to unwrap symmetric key for wallet {WalletAddress}", walletAddress);
                    return null;
                }

                // Decrypt the group ciphertext
                if (!groupElement.TryGetProperty("ciphertext", out var ctEl) ||
                    !groupElement.TryGetProperty("nonce", out var nonceEl))
                {
                    _logger.LogError("Group for wallet {WalletAddress} missing ciphertext or nonce", walletAddress);
                    return null;
                }

                byte[] ciphertextBytes;
                byte[] nonce;
                try
                {
                    ciphertextBytes = Convert.FromBase64String(ctEl.GetString()!);
                    nonce = Convert.FromBase64String(nonceEl.GetString()!);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not decode ciphertext/nonce for wallet {WalletAddress}", walletAddress);
                    return null;
                }

                using var ciphertextModel = new SymmetricCiphertext
                {
                    Data = ciphertextBytes,
                    Key = symmetricKey,
                    IV = nonce,
                    Type = EncryptionType.XCHACHA20_POLY1305
                };

                var decryptResult = await _symmetricCrypto.DecryptAsync(ciphertextModel, ct);
                if (!decryptResult.IsSuccess || decryptResult.Value is null)
                {
                    _logger.LogError(
                        "Symmetric decryption failed for group belonging to wallet {WalletAddress}: {Error}",
                        walletAddress, decryptResult.ErrorMessage);
                    return null;
                }

                JsonElement payloadJson;
                try
                {
                    payloadJson = JsonSerializer.Deserialize<JsonElement>(decryptResult.Value, _jsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex,
                        "Failed to parse decrypted action payload for wallet {WalletAddress}",
                        walletAddress);
                    return null;
                }

                // The symmetric key IS the master file key for this file attachment group
                return (symmetricKey, payloadJson);
            }
        }

        return null;
    }

    /// <summary>
    /// Navigates the payload JSON to find the <see cref="FileReference"/> at
    /// <c>payload[fieldName][fileIndex]</c>.
    /// </summary>
    private FileReference? ExtractFileReference(
        JsonElement payload, string fieldName, int fileIndex, string walletAddress)
    {
        // The decrypted payload is a Dictionary<string, object> — find our field
        // Payload may be the full disclosed dict or just the fields for this wallet
        if (!payload.TryGetProperty(fieldName, out var fieldElement))
        {
            // Also try camelCase / exact match fallback by iterating
            foreach (var prop in payload.EnumerateObject())
            {
                if (string.Equals(prop.Name, fieldName, StringComparison.OrdinalIgnoreCase))
                {
                    fieldElement = prop.Value;
                    break;
                }
            }

            if (fieldElement.ValueKind == JsonValueKind.Undefined)
            {
                _logger.LogDebug(
                    "Field '{FieldName}' not found in payload for wallet {WalletAddress}",
                    fieldName, walletAddress);
                return null;
            }
        }

        // Field can be a single FileReference object or an array
        JsonElement fileRefElement;
        if (fieldElement.ValueKind == JsonValueKind.Array)
        {
            var count = fieldElement.GetArrayLength();
            if (fileIndex >= count)
            {
                _logger.LogWarning(
                    "File index {FileIndex} out of range (array length {Count}) for field {FieldName}",
                    fileIndex, count, fieldName);
                return null;
            }

            fileRefElement = fieldElement[fileIndex];
        }
        else if (fieldElement.ValueKind == JsonValueKind.Object)
        {
            if (fileIndex != 0)
            {
                _logger.LogWarning(
                    "File index {FileIndex} requested but field {FieldName} is a single object",
                    fileIndex, fieldName);
                return null;
            }

            fileRefElement = fieldElement;
        }
        else
        {
            _logger.LogWarning(
                "Field {FieldName} is not a FileReference object or array (kind: {Kind})",
                fieldName, fieldElement.ValueKind);
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<FileReference>(fileRefElement.GetRawText(), _jsonOptions);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to deserialise FileReference from field {FieldName}", fieldName);
            return null;
        }
    }

    /// <summary>
    /// Decodes a chunk transaction's payload bytes (JSON envelope) and decrypts the chunk
    /// using an HKDF-derived per-chunk key.
    /// </summary>
    /// <remarks>
    /// Expected chunk envelope JSON:
    /// <code>{ "ciphertext": "&lt;base64&gt;", "nonce": "&lt;base64&gt;" }</code>
    /// </remarks>
    private async Task<byte[]> DecryptChunkAsync(
        byte[] chunkEnvelopeBytes,
        byte[] masterFileKey,
        byte[] salt,
        int chunkIndex,
        CancellationToken ct)
    {
        // Parse the chunk envelope JSON
        JsonElement chunkJson;
        try
        {
            chunkJson = JsonSerializer.Deserialize<JsonElement>(chunkEnvelopeBytes, _jsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Failed to parse chunk envelope JSON for chunk {chunkIndex}.", ex);
        }

        byte[] ciphertext;
        byte[] nonce;

        if (chunkJson.TryGetProperty("ciphertext", out var ctEl) &&
            chunkJson.TryGetProperty("nonce", out var nonceEl))
        {
            // Standard encrypted chunk format
            ciphertext = Convert.FromBase64String(ctEl.GetString()!);
            nonce = Convert.FromBase64String(nonceEl.GetString()!);
        }
        else
        {
            // Legacy / raw format — treat the entire payload as ciphertext + prepended nonce
            // XChaCha20-Poly1305: first 24 bytes are the nonce
            if (chunkEnvelopeBytes.Length <= 24)
            {
                throw new InvalidOperationException(
                    $"Chunk {chunkIndex} payload is too short to contain an XChaCha20-Poly1305 nonce (24 bytes).");
            }

            nonce = chunkEnvelopeBytes[..24];
            ciphertext = chunkEnvelopeBytes[24..];
        }

        // Derive per-chunk key from master file key + salt + index
        var chunkKey = HkdfKeyDerivation.DeriveChunkKey(masterFileKey, salt, chunkIndex);

        using var ciphertextModel = new SymmetricCiphertext
        {
            Data = ciphertext,
            Key = chunkKey,
            IV = nonce,
            Type = EncryptionType.XCHACHA20_POLY1305
        };

        var decryptResult = await _symmetricCrypto.DecryptAsync(ciphertextModel, ct);
        if (!decryptResult.IsSuccess || decryptResult.Value is null)
        {
            throw new InvalidOperationException(
                $"Failed to decrypt chunk {chunkIndex}: {decryptResult.ErrorMessage}");
        }

        return decryptResult.Value;
    }
}
