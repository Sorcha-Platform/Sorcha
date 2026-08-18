// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.IO.Compression;
using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Models.Credentials;

/// <summary>
/// W3C Bitstring Status List v1.0 — a compressed bitstring where each bit
/// represents the status of a credential at that index position.
/// </summary>
public class BitstringStatusList
{
    /// <summary>
    /// W3C minimum list size for privacy (verifiers cannot determine which credential was checked).
    /// </summary>
    public const int MinimumSize = 131072;

    /// <summary>
    /// Unique list identifier: {issuerWallet}-{registerId}-{purpose}-{sequence}.
    /// </summary>
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    /// <summary>
    /// Wallet address of the issuing entity.
    /// </summary>
    [JsonPropertyName("issuerWallet")]
    public required string IssuerWallet { get; set; }

    /// <summary>
    /// Register where the canonical list is stored as a Control TX.
    /// </summary>
    [JsonPropertyName("registerId")]
    public required string RegisterId { get; set; }

    /// <summary>
    /// Status purpose: "revocation" or "suspension".
    /// </summary>
    [JsonPropertyName("statusPurpose")]
    public required string Purpose { get; set; }

    /// <summary>
    /// GZip + Base64 encoded bitstring.
    /// </summary>
    [JsonPropertyName("encodedList")]
    public required string EncodedList { get; set; }

    /// <summary>
    /// Number of entries (bits) in the list. Must be >= 131,072.
    /// </summary>
    [JsonPropertyName("size")]
    public int Size { get; set; } = MinimumSize;

    /// <summary>
    /// Next free position for allocation.
    /// </summary>
    [JsonPropertyName("nextAvailableIndex")]
    public int NextAvailableIndex { get; set; }

    /// <summary>
    /// Incremented on each update.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    /// <summary>
    /// When the bitstring was last modified.
    /// </summary>
    [JsonPropertyName("lastUpdated")]
    public DateTimeOffset LastUpdated { get; set; }

    /// <summary>
    /// Transaction ID of the latest Control TX on the register.
    /// </summary>
    [JsonPropertyName("registerTxId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? RegisterTxId { get; set; }

    /// <summary>
    /// Creates a new empty status list with the specified size.
    /// </summary>
    public static BitstringStatusList Create(
        string issuerWallet, string registerId, string purpose, int size = MinimumSize)
    {
        if (size < MinimumSize)
            throw new ArgumentOutOfRangeException(nameof(size), $"Size must be at least {MinimumSize}");

        if (purpose is not ("revocation" or "suspension"))
            throw new ArgumentException("Purpose must be 'revocation' or 'suspension'", nameof(purpose));

        var bitstring = new byte[size / 8]; // All zeros = all credentials active
        var encoded = CompressBitstring(bitstring);

        return new BitstringStatusList
        {
            Id = $"{issuerWallet}-{registerId}-{purpose}-1",
            IssuerWallet = issuerWallet,
            RegisterId = registerId,
            Purpose = purpose,
            EncodedList = encoded,
            Size = size,
            NextAvailableIndex = 0,
            Version = 1,
            LastUpdated = DateTimeOffset.UtcNow
        };
    }

    /// <summary>
    /// Allocates the next available index. Returns -1 if the list is full.
    /// </summary>
    public int AllocateIndex()
    {
        if (NextAvailableIndex >= Size)
            return -1;

        return NextAvailableIndex++;
    }

    /// <summary>
    /// Consumes a SPECIFIC index, advancing this list past it. Returns false if the index is beyond
    /// capacity.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Issue #1502. A credential carries one entry number across every purpose list, so the number
    /// is chosen once over all of them and then reserved in each — rather than each list allocating
    /// its own and the results being compared afterwards. Independent allocation drifts the moment
    /// one list is created later than its sibling, and the lagging list then believes indexes are
    /// free that credentials already point at.
    /// </para>
    /// <para>
    /// Reserving an index below <see cref="NextAvailableIndex"/> is a no-op rather than an error:
    /// the caller reserves the same shared index in several lists, and one of them is usually the
    /// list that number came from.
    /// </para>
    /// </remarks>
    public bool ReserveIndex(int index)
    {
        if (index < 0 || index >= Size)
            return false;

        if (index >= NextAvailableIndex)
            NextAvailableIndex = index + 1;

        return true;
    }

    /// <summary>
    /// Gets the bit value at the specified index.
    /// </summary>
    public bool GetBit(int index)
    {
        if (index < 0 || index >= Size)
            throw new ArgumentOutOfRangeException(nameof(index));

        var bitstring = DecompressBitstring(EncodedList);
        var byteIndex = index / 8;
        var bitIndex = 7 - (index % 8); // MSB first per W3C spec
        return (bitstring[byteIndex] & (1 << bitIndex)) != 0;
    }

    /// <summary>
    /// Sets the bit at the specified index to the given value.
    /// </summary>
    public void SetBit(int index, bool value)
    {
        if (index < 0 || index >= Size)
            throw new ArgumentOutOfRangeException(nameof(index));

        var bitstring = DecompressBitstring(EncodedList);
        var byteIndex = index / 8;
        var bitIndex = 7 - (index % 8); // MSB first per W3C spec

        if (value)
            bitstring[byteIndex] |= (byte)(1 << bitIndex);
        else
            bitstring[byteIndex] &= (byte)~(1 << bitIndex);

        EncodedList = CompressBitstring(bitstring);
        Version++;
        LastUpdated = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Compresses a raw bitstring using GZip and encodes as Base64.
    /// </summary>
    public static string CompressBitstring(byte[] bitstring)
    {
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
        {
            gzip.Write(bitstring, 0, bitstring.Length);
        }
        return Convert.ToBase64String(output.ToArray());
    }

    /// <summary>
    /// Decodes a Base64 string and decompresses using GZip.
    /// </summary>
    public static byte[] DecompressBitstring(string encoded)
    {
        var compressed = Convert.FromBase64String(encoded);
        using var input = new MemoryStream(compressed);
        using var gzip = new GZipStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        gzip.CopyTo(output);
        return output.ToArray();
    }
}
