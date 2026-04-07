// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Sorcha.Agent.Auth;
using Sorcha.Agent.Configuration;

namespace Sorcha.Agent.Execution;

/// <summary>
/// Handles file generation/reading, chunking, upload, and file reference building.
/// </summary>
public class FileUploadHandler
{
    private const int ChunkSize = 4 * 1024 * 1024; // 4MB

    private readonly HttpClient _httpClient;
    private readonly AgentAuthService _authService;
    private readonly string _walletAddress;
    private readonly string _registerId;
    private readonly ILogger<FileUploadHandler> _logger;

    public FileUploadHandler(
        HttpClient httpClient,
        AgentAuthService authService,
        string walletAddress,
        string registerId,
        ILogger<FileUploadHandler> logger)
    {
        _httpClient = httpClient;
        _authService = authService;
        _walletAddress = walletAddress;
        _registerId = registerId;
        _logger = logger;
    }

    /// <summary>
    /// Executes a file-upload pre-action: generate/read file, chunk, upload, return file reference.
    /// </summary>
    public async Task<Dictionary<string, object>> ExecuteAsync(
        FileUploadConfig config, CancellationToken cancellationToken = default)
    {
        // Get file bytes
        byte[] fileBytes;
        string fileName;
        string contentType;

        if (!string.IsNullOrEmpty(config.FilePath))
        {
            // Read real file
            if (!File.Exists(config.FilePath))
                throw new FileNotFoundException($"File not found: {config.FilePath}");

            fileBytes = await File.ReadAllBytesAsync(config.FilePath, cancellationToken);
            fileName = config.FileName ?? Path.GetFileName(config.FilePath);
            contentType = config.ContentType ?? DetectContentType(config.FilePath);

            _logger.LogInformation("Read file {FilePath} ({Size} bytes)", config.FilePath, fileBytes.Length);
        }
        else
        {
            // Generate deterministic test file
            var size = config.SizeBytes ?? 1024;
            var seed = config.Seed ?? 85;
            fileBytes = GenerateTestFile(size, seed);
            fileName = config.FileName ?? $"test-file-{size}B.bin";
            contentType = config.ContentType ?? "application/octet-stream";

            _logger.LogInformation("Generated test file ({Size} bytes, seed {Seed})", size, seed);
        }

        // Compute SHA-256 hash
        var hash = ComputeSha256(fileBytes);
        var hashString = $"sha256:{hash}";

        // Chunk and upload
        var totalChunks = (int)Math.Ceiling((double)fileBytes.Length / ChunkSize);
        if (totalChunks == 0) totalChunks = 1;

        var chunkTransactionIds = new List<string>();
        string? uploadSessionId = null;
        string? salt = null;

        var token = await _authService.GetTokenAsync(cancellationToken);

        for (var i = 0; i < totalChunks; i++)
        {
            var offset = i * ChunkSize;
            var length = Math.Min(ChunkSize, fileBytes.Length - offset);
            var chunkBytes = new byte[length];
            Array.Copy(fileBytes, offset, chunkBytes, 0, length);

            var chunkBody = new Dictionary<string, object>
            {
                ["senderWallet"] = _walletAddress,
                ["registerAddress"] = _registerId,
                ["chunkIndex"] = i,
                ["totalChunks"] = totalChunks,
                ["fileHash"] = hashString,
                ["contentType"] = contentType,
                ["contentBase64"] = Convert.ToBase64String(chunkBytes)
            };

            if (uploadSessionId is not null)
                chunkBody["uploadSessionId"] = uploadSessionId;

            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/file-chunks/")
            {
                Content = JsonContent.Create(chunkBody)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var doc = JsonDocument.Parse(responseJson);

            var chunkTxId = doc.RootElement.GetProperty("chunkTransactionId").GetString()!;
            chunkTransactionIds.Add(chunkTxId);

            if (uploadSessionId is null && doc.RootElement.TryGetProperty("uploadSessionId", out var sid))
                uploadSessionId = sid.GetString();

            if (salt is null && doc.RootElement.TryGetProperty("saltBase64", out var s))
                salt = s.GetString();

            _logger.LogDebug("Uploaded chunk {Index}/{Total} → {TxId}", i + 1, totalChunks, chunkTxId);
        }

        _logger.LogInformation("File upload complete: {Chunks} chunks, hash {Hash}", totalChunks, hashString);

        // Build file reference
        return new Dictionary<string, object>
        {
            ["fileName"] = fileName,
            ["contentType"] = contentType,
            ["size"] = fileBytes.Length,
            ["hash"] = hashString,
            ["salt"] = salt ?? "",
            ["chunkTransactionIds"] = chunkTransactionIds,
            ["uploadSessionId"] = uploadSessionId ?? "",
            ["masterKeyId"] = "server-managed"
        };
    }

    /// <summary>
    /// Generates a deterministic test file using a seeded RNG.
    /// </summary>
    public static byte[] GenerateTestFile(int sizeBytes, int seed)
    {
        var rng = new Random(seed);
        var buffer = new byte[sizeBytes];
        rng.NextBytes(buffer);
        return buffer;
    }

    /// <summary>
    /// Computes SHA-256 hash of file bytes, returns lowercase hex string.
    /// </summary>
    public static string ComputeSha256(byte[] data)
    {
        var hashBytes = SHA256.HashData(data);
        return Convert.ToHexStringLower(hashBytes);
    }

    private static string DetectContentType(string filePath)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        return ext switch
        {
            ".jpg" or ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".gif" => "image/gif",
            ".pdf" => "application/pdf",
            ".doc" => "application/msword",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xls" => "application/vnd.ms-excel",
            ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".csv" => "text/csv",
            ".txt" => "text/plain",
            ".json" => "application/json",
            ".xml" => "application/xml",
            ".zip" => "application/zip",
            _ => "application/octet-stream"
        };
    }
}
