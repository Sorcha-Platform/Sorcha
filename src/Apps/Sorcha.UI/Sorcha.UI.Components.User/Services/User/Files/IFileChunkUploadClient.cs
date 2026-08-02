// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using Sorcha.Serialization;

namespace Sorcha.UI.Core.Services.User.Files;

/// <summary>
/// Issue #1311 — Feature 085 staged file-chunk submission, over a typed client the host wires with
/// <c>AuthenticatedHttpMessageHandler</c>.
///
/// <para><b>Why this type exists.</b> <c>FileReferenceField.razor</c> POSTed
/// <c>/api/file-chunks</c> on the ambient <c>@inject HttpClient</c>, which carries no
/// <c>Authorization</c> header by design. That endpoint group carries
/// <c>.RequireAuthorization()</c> (<c>FileChunkEndpoints.cs:29</c>), so every chunk 401'd and
/// F085 file-attachment upload could not work from the web form renderer at all.</para>
///
/// <para>Unlike the sibling <c>MyDevices</c> case, this one failed LOUDLY —
/// <c>EnsureSuccessStatusCode()</c> throws, and the caller surfaces the message on the upload row.
/// So a user saw an upload fail rather than a convincing empty state. Loud is better, but the
/// feature was still unusable.</para>
/// </summary>
public interface IFileChunkUploadClient
{
    /// <summary>
    /// Submits one encrypted chunk. Throws on a non-success status, preserving the existing caller
    /// contract — <c>FileReferenceField</c> catches and reports per-file, and swallowing the failure
    /// here would turn a failed upload into a silently truncated file.
    /// </summary>
    Task<FileChunkUploadResult> UploadChunkAsync(
        FileChunkUploadRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Wire request for <c>POST /api/file-chunks</c>.</summary>
/// <param name="UploadSessionId">Null on the first chunk; server-assigned thereafter.</param>
public sealed record FileChunkUploadRequest(
    string SenderWallet,
    string RegisterAddress,
    int ChunkIndex,
    int TotalChunks,
    string FileHash,
    string ContentType,
    string ContentBase64,
    string? UploadSessionId);

/// <summary>The subset of the server's FileChunkSubmissionResponse the renderer consumes.</summary>
public sealed record FileChunkUploadResult(
    string? ChunkTransactionId,
    string? UploadSessionId,
    string? SaltBase64);

/// <inheritdoc />
public sealed class FileChunkUploadClient : IFileChunkUploadClient
{
    private const string Endpoint = "/api/file-chunks";

    private readonly HttpClient _httpClient;

    public FileChunkUploadClient(HttpClient httpClient)
        => _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));

    /// <inheritdoc />
    public async Task<FileChunkUploadResult> UploadChunkAsync(
        FileChunkUploadRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        using var response = await _httpClient
            .PostAsJsonAsync(Endpoint, request, SorchaJson.Options, cancellationToken)
            .ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        var result = await response.Content
            .ReadFromJsonAsync<FileChunkUploadResult>(SorchaJson.Options, cancellationToken)
            .ConfigureAwait(false);

        return result ?? new FileChunkUploadResult(null, null, null);
    }
}
