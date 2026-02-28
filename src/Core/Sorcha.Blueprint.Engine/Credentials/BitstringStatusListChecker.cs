// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IO.Compression;
using System.Text.Json;

namespace Sorcha.Blueprint.Engine.Credentials;

/// <summary>
/// Checks credential revocation/suspension status by fetching and decoding
/// a W3C Bitstring Status List from the issuer's register endpoint.
/// </summary>
public class BitstringStatusListChecker : IRevocationChecker
{
    private readonly HttpClient _httpClient;

    public BitstringStatusListChecker(HttpClient httpClient)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
    }

    /// <inheritdoc />
    public async Task<string?> CheckRevocationStatusAsync(
        string credentialId,
        string issuerWallet,
        CancellationToken cancellationToken = default)
    {
        // The credentialId may contain the status list URL as metadata.
        // For cross-blueprint flows, the caller passes the status list URL
        // as the credentialId parameter (convention: "statusListUrl#index").
        if (!TryParseStatusReference(credentialId, out var statusListUrl, out var index))
        {
            return null; // No status list reference — can't check
        }

        return await CheckBitAsync(statusListUrl, index, cancellationToken);
    }

    /// <summary>
    /// Checks a specific bit in a remote bitstring status list.
    /// </summary>
    public async Task<string?> CheckBitAsync(
        string statusListUrl, int index, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetStringAsync(statusListUrl, cancellationToken);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;

            // Navigate W3C BitstringStatusListCredential envelope
            if (!root.TryGetProperty("credentialSubject", out var subject))
                return null;

            if (!subject.TryGetProperty("encodedList", out var encodedListProp))
                return null;

            var encodedList = encodedListProp.GetString();
            if (string.IsNullOrEmpty(encodedList))
                return null;

            // Decode: Base64 → GZip decompress → raw bytes
            var compressed = Convert.FromBase64String(encodedList);
            using var ms = new MemoryStream(compressed);
            using var gzip = new GZipStream(ms, CompressionMode.Decompress);
            using var output = new MemoryStream();
            await gzip.CopyToAsync(output, cancellationToken);
            var bytes = output.ToArray();

            // Check bit at index (MSB-first within each byte)
            var byteIndex = index / 8;
            var bitIndex = 7 - (index % 8);

            if (byteIndex >= bytes.Length)
                return null;

            var isSet = (bytes[byteIndex] & (1 << bitIndex)) != 0;

            // Default purpose is "revocation" per W3C Bitstring Status List specification
            var purpose = "revocation";
            if (subject.TryGetProperty("statusPurpose", out var purposeProp))
            {
                purpose = purposeProp.GetString() ?? "revocation";
            }

            if (isSet)
            {
                return purpose == "suspension" ? "Suspended" : "Revoked";
            }

            return "Active";
        }
        catch (Exception ex) when (ex is not OutOfMemoryException and not StackOverflowException)
        {
            // Status check is optional — network or parse failures are non-fatal.
            // The caller applies its own policy when null is returned.
            return null;
        }
    }

    /// <summary>
    /// Parses a status list reference in the format "statusListUrl#index".
    /// </summary>
    public static bool TryParseStatusReference(
        string reference, out string statusListUrl, out int index)
    {
        statusListUrl = string.Empty;
        index = 0;

        if (string.IsNullOrWhiteSpace(reference))
            return false;

        var hashIndex = reference.LastIndexOf('#');
        if (hashIndex < 0 || hashIndex >= reference.Length - 1)
            return false;

        statusListUrl = reference[..hashIndex];
        return int.TryParse(reference[(hashIndex + 1)..], out index) && index >= 0;
    }
}
