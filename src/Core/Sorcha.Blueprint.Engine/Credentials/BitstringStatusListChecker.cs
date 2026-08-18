// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.IO.Compression;
using System.Text.Json;

namespace Sorcha.Blueprint.Engine.Credentials;

/// <summary>
/// Checks credential revocation/suspension status by fetching and decoding
/// a W3C Bitstring Status List from the issuer's register endpoint.
/// Implements both the legacy <see cref="IRevocationChecker"/> and the unified
/// <see cref="IStatusListChecker"/> seam (feature 135).
/// </summary>
public class BitstringStatusListChecker : IRevocationChecker, IStatusListChecker
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

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Feature 135 — unified status seam. Feature 192 — the mapping now PRESERVES which status was
    /// set. <see cref="CheckBitAsync"/> already resolved the list's own <c>statusPurpose</c> to
    /// "Revoked" or "Suspended"; the old mapping collapsed both to a single "set" one line later,
    /// which is where the distinction was lost for the W3C rail.
    /// </para>
    /// <para>
    /// A word this checker does not recognise maps to <see cref="CredentialStatusValue.Unresolved"/>
    /// rather than to a status — asserting "revoked" off a value we cannot interpret would be a
    /// false accusation, and Unresolved is what the fail-closed policy exists to handle.
    /// </para>
    /// </remarks>
    public async Task<CredentialStatusValue> CheckAsync(StatusReference statusRef, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(statusRef);
        if (string.IsNullOrWhiteSpace(statusRef.Uri))
            return CredentialStatusValue.Unresolved;

        var status = await CheckBitAsync(statusRef.Uri, statusRef.Index, cancellationToken).ConfigureAwait(false);
        return status switch
        {
            "Active" => CredentialStatusValue.Valid,
            "Revoked" => CredentialStatusValue.Invalid,
            "Suspended" => CredentialStatusValue.Suspended,
            _ => CredentialStatusValue.Unresolved
        };
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

            // Decode: (multibase base64url | base64) → GZip decompress → raw bytes.
            // Feature 181 US3 (R7/FR-010) — the W3C Bitstring Status List v1 canonical form is
            // multibase base64url ('u' prefix, unpadded); the pre-v1 draft (and current Sorcha
            // issuance) used plain base64. Accept both so external EUDI status lists decode.
            var compressed = DecodeEncodedList(encodedList);
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
    /// Decode a W3C Bitstring Status List <c>encodedList</c> value. Feature 181 US3 (R7/FR-010):
    /// a leading <c>u</c> is the multibase base64url prefix (v1 canonical form, unpadded) — decode
    /// the remainder as base64url; any other value is plain base64 (the pre-v1 draft form Sorcha
    /// issuance still emits). The returned bytes are the GZip-compressed bitstring.
    /// </summary>
    internal static byte[] DecodeEncodedList(string encodedList)
    {
        ArgumentException.ThrowIfNullOrEmpty(encodedList);
        return encodedList[0] == 'u'
            ? Base64Url.DecodeFromChars(encodedList.AsSpan(1))
            : Convert.FromBase64String(encodedList);
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
