// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace Sorcha.Wallet.Pwa.Services.Drafts;

/// <summary>
/// Feature 152 (US5) — uploads a captured file to the Blueprint Service via the consumer-tier
/// <c>/api/file-chunks</c> staging pipeline (4 MB chunks, server-side XChaCha20) and returns the
/// file-reference object to embed as a form-field value. The execute endpoint resolves the staged
/// chunks into a file transaction (Feature 085), so no inline bytes travel in the action payload.
/// Mirrors the agent's <c>FileUploadHandler</c> contract.
/// </summary>
public interface IFileChunkUploader
{
    /// <summary>
    /// Chunks + uploads <paramref name="content"/> and returns the file reference
    /// (<c>fileName, contentType, size, hash, salt, chunkTransactionIds, uploadSessionId,
    /// masterKeyId</c>) for the payload field, or <c>null</c> on failure.
    /// </summary>
    Task<Dictionary<string, object?>?> UploadAsync(
        byte[] content, string fileName, string contentType,
        string senderWallet, string registerId, CancellationToken ct = default);

    /// <summary>
    /// Uploads every item in <paramref name="media"/> and sets each one's reference into
    /// <paramref name="payload"/> at its <see cref="Sorcha.Wallet.Pwa.Services.Drafts.Models.DraftMedia.Scope"/>.
    /// Returns <c>false</c> if any upload fails (so the caller can retry the whole submission).
    /// </summary>
    Task<bool> AttachAllAsync(
        IReadOnlyList<Sorcha.Wallet.Pwa.Services.Drafts.Models.DraftMedia> media,
        IDictionary<string, object?> payload,
        string senderWallet, string registerId, CancellationToken ct = default);
}

/// <summary>Default <see cref="IFileChunkUploader"/> over the PWA's bearer-authed HttpClient.</summary>
public sealed class FileChunkUploader : IFileChunkUploader
{
    private const int ChunkSize = 4 * 1024 * 1024; // 4 MB — matches the server chunk ceiling

    private readonly HttpClient _http;

    /// <summary>Initialises a new instance.</summary>
    public FileChunkUploader(HttpClient http) => _http = http ?? throw new ArgumentNullException(nameof(http));

    /// <inheritdoc />
    public async Task<Dictionary<string, object?>?> UploadAsync(
        byte[] content, string fileName, string contentType,
        string senderWallet, string registerId, CancellationToken ct = default)
    {
        var hash = $"sha256:{Convert.ToHexStringLower(SHA256.HashData(content))}";
        var totalChunks = Math.Max(1, (int)Math.Ceiling((double)content.Length / ChunkSize));

        var chunkTxIds = new List<string>();
        string? uploadSessionId = null;
        string? salt = null;

        for (var i = 0; i < totalChunks; i++)
        {
            var offset = i * ChunkSize;
            var length = Math.Min(ChunkSize, content.Length - offset);
            var chunk = new byte[length];
            Array.Copy(content, offset, chunk, 0, length);

            var body = new Dictionary<string, object?>
            {
                ["senderWallet"] = senderWallet,
                ["registerAddress"] = registerId,
                ["chunkIndex"] = i,
                ["totalChunks"] = totalChunks,
                ["fileHash"] = hash,
                ["contentType"] = contentType,
                ["contentBase64"] = Convert.ToBase64String(chunk),
            };
            if (uploadSessionId is not null) body["uploadSessionId"] = uploadSessionId;

            using var resp = await _http.PostAsJsonAsync("api/file-chunks", body, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;

            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var root = doc.RootElement;
            if (!root.TryGetProperty("chunkTransactionId", out var txEl)) return null;
            chunkTxIds.Add(txEl.GetString()!);
            if (uploadSessionId is null && root.TryGetProperty("uploadSessionId", out var sid)) uploadSessionId = sid.GetString();
            if (salt is null && root.TryGetProperty("saltBase64", out var s)) salt = s.GetString();
        }

        return new Dictionary<string, object?>
        {
            ["fileName"] = fileName,
            ["contentType"] = contentType,
            ["size"] = content.Length,
            ["hash"] = hash,
            ["salt"] = salt ?? "",
            ["chunkTransactionIds"] = chunkTxIds,
            ["uploadSessionId"] = uploadSessionId ?? "",
            ["masterKeyId"] = "server-managed",
        };
    }

    /// <inheritdoc />
    public async Task<bool> AttachAllAsync(
        IReadOnlyList<Models.DraftMedia> media,
        IDictionary<string, object?> payload,
        string senderWallet, string registerId, CancellationToken ct = default)
    {
        foreach (var m in media)
        {
            byte[] bytes;
            try { bytes = Convert.FromBase64String(m.ContentBase64); }
            catch (FormatException) { return false; }

            var reference = await UploadAsync(bytes, m.FileName, m.ContentType, senderWallet, registerId, ct)
                .ConfigureAwait(false);
            if (reference is null) return false;

            if (!string.IsNullOrEmpty(m.Scope))
            {
                payload[m.Scope] = reference;
            }
        }
        return true;
    }
}
