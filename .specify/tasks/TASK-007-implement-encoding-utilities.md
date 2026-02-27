# Task: Implement Encoding Utilities

**ID:** TASK-007
**Status:** Not Started
**Priority:** High
**Estimate:** 6 hours
**Assignee:** Unassigned
**Created:** 2025-11-12
**Updated:** 2025-11-12

## Context

Implement encoding and decoding utilities for Base58, Bech32, hexadecimal, and variable-length integer encoding. These utilities are essential for wallet address formatting, key encoding, and binary data serialization.

**Related Specifications:**
- [Sorcha.Cryptography Rewrite Spec - FR-7, FR-8](../specs/sorcha-cryptography-rewrite.md#fr-7-wallet-address-encoding)
- [Current WalletUtils Encoding](../../src/Common/Sorcha.Cryptography/WalletUtils.cs)

**Dependencies:**
- TASK-001 (Project setup)
- TASK-002 (Enums and models)

## Objective

Implement IEncodingProvider interface with support for Base58, Bech32, hexadecimal, and variable-length integer encoding used throughout the cryptography library.

## Implementation Details

### Files to Create

1. **Interfaces/IEncodingProvider.cs** - Interface definition
2. **Utilities/EncodingUtilities.cs** - Main implementation
3. **Utilities/Base58Utilities.cs** - Base58 encoding/decoding
4. **Utilities/Bech32Utilities.cs** - Bech32 encoding/decoding
5. **Utilities/VarIntUtilities.cs** - Variable-length integer encoding

### Technical Approach

**Interface: Interfaces/IEncodingProvider.cs**
```csharp
namespace Sorcha.Cryptography.Interfaces;

/// <summary>
/// Provides encoding and decoding utilities.
/// </summary>
public interface IEncodingProvider
{
    // Base58 Encoding
    string? EncodeBase58(byte[] data);
    byte[]? DecodeBase58(string encoded);
    string? EncodeBase58Check(byte[] data);
    byte[]? DecodeBase58Check(string encoded);

    // Bech32 Encoding
    string? EncodeBech32(string hrp, byte[] data);
    (string? Hrp, byte[]? Data) DecodeBech32(string encoded);

    // Hexadecimal
    string EncodeHex(byte[] data);
    byte[]? DecodeHex(string hex);

    // Variable-Length Integer
    byte[] EncodeVarInt(ulong value);
    (ulong Value, int BytesRead) DecodeVarInt(ReadOnlySpan<byte> data);
}
```

### Base58 Implementation

**Base58 Alphabet (Bitcoin-style):**
```
123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz
(Excludes: 0, O, I, l to avoid confusion)
```

**Utilities/Base58Utilities.cs**
```csharp
namespace Sorcha.Cryptography.Utilities;

/// <summary>
/// Base58 encoding/decoding (Bitcoin-style).
/// </summary>
public static class Base58Utilities
{
    private const string Alphabet = "123456789ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
    private static readonly int[] DecodeMap = CreateDecodeMap();

    /// <summary>
    /// Encodes bytes to Base58 string.
    /// </summary>
    public static string? Encode(byte[] data)
    {
        if (data == null || data.Length == 0)
            return null;

        // Count leading zeros
        int leadingZeros = 0;
        while (leadingZeros < data.Length && data[leadingZeros] == 0)
            leadingZeros++;

        // Convert to base58
        var result = new List<char>();
        var temp = new BigInteger(data.Reverse().Concat(new byte[] { 0 }).ToArray());

        while (temp > 0)
        {
            var remainder = (int)(temp % 58);
            temp /= 58;
            result.Add(Alphabet[remainder]);
        }

        // Add leading '1's for leading zeros
        for (int i = 0; i < leadingZeros; i++)
            result.Add('1');

        result.Reverse();
        return new string(result.ToArray());
    }

    /// <summary>
    /// Decodes Base58 string to bytes.
    /// </summary>
    public static byte[]? Decode(string encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return null;

        // Count leading '1's
        int leadingOnes = 0;
        while (leadingOnes < encoded.Length && encoded[leadingOnes] == '1')
            leadingOnes++;

        // Decode base58
        BigInteger value = 0;
        for (int i = leadingOnes; i < encoded.Length; i++)
        {
            int digit = DecodeMap[encoded[i]];
            if (digit < 0)
                return null; // Invalid character

            value = value * 58 + digit;
        }

        // Convert to bytes
        var bytes = value.ToByteArray().Reverse().ToArray();

        // Remove extra zero byte if present
        if (bytes.Length > 1 && bytes[0] == 0)
            bytes = bytes.Skip(1).ToArray();

        // Add leading zeros
        return Enumerable.Repeat((byte)0, leadingOnes)
            .Concat(bytes)
            .ToArray();
    }

    /// <summary>
    /// Encodes with checksum (Base58Check).
    /// </summary>
    public static string? EncodeCheck(byte[] data)
    {
        var hash = SHA256.HashData(SHA256.HashData(data));
        var checksum = hash.Take(4).ToArray();
        var dataWithChecksum = data.Concat(checksum).ToArray();
        return Encode(dataWithChecksum);
    }

    /// <summary>
    /// Decodes and validates checksum.
    /// </summary>
    public static byte[]? DecodeCheck(string encoded)
    {
        var decoded = Decode(encoded);
        if (decoded == null || decoded.Length < 4)
            return null;

        var data = decoded.Take(decoded.Length - 4).ToArray();
        var checksum = decoded.Skip(decoded.Length - 4).ToArray();

        var hash = SHA256.HashData(SHA256.HashData(data));
        var expectedChecksum = hash.Take(4).ToArray();

        return checksum.SequenceEqual(expectedChecksum) ? data : null;
    }

    private static int[] CreateDecodeMap()
    {
        var map = new int[128];
        Array.Fill(map, -1);
        for (int i = 0; i < Alphabet.Length; i++)
            map[Alphabet[i]] = i;
        return map;
    }
}
```

### Bech32 Implementation

**Bech32 Format:**
- Human-readable part (HRP): "ws1" for Sorcha wallets
- Separator: "1"
- Data part: Base32 encoded data
- Checksum: 6 characters

**Utilities/Bech32Utilities.cs**
```csharp
namespace Sorcha.Cryptography.Utilities;

/// <summary>
/// Bech32 encoding/decoding (BIP 173).
/// </summary>
public static class Bech32Utilities
{
    private const string Charset = "qpzry9x8gf2tvdw0s3jn54khce6mua7l";
    private const int ChecksumLength = 6;

    /// <summary>
    /// Encodes data in Bech32 format.
    /// </summary>
    /// <param name="hrp">Human-readable part (e.g., "ws1").</param>
    /// <param name="data">Data to encode.</param>
    public static string? Encode(string hrp, byte[] data)
    {
        if (string.IsNullOrEmpty(hrp) || data == null)
            return null;

        // Convert to 5-bit groups
        var fiveBitData = ConvertBits(data, 8, 5, true);
        if (fiveBitData == null)
            return null;

        // Create checksum
        var checksum = CreateChecksum(hrp, fiveBitData);
        var combined = fiveBitData.Concat(checksum).ToArray();

        // Encode to Bech32 string
        return hrp + "1" + string.Concat(combined.Select(b => Charset[b]));
    }

    /// <summary>
    /// Decodes Bech32 string.
    /// </summary>
    public static (string? Hrp, byte[]? Data) Decode(string encoded)
    {
        if (string.IsNullOrEmpty(encoded))
            return (null, null);

        // Find separator
        int separatorIndex = encoded.LastIndexOf('1');
        if (separatorIndex < 1 || separatorIndex + 7 > encoded.Length)
            return (null, null);

        string hrp = encoded.Substring(0, separatorIndex);
        string data = encoded.Substring(separatorIndex + 1);

        // Decode data part
        var decoded = new List<byte>();
        foreach (char c in data)
        {
            int index = Charset.IndexOf(c);
            if (index < 0)
                return (null, null);
            decoded.Add((byte)index);
        }

        // Verify checksum
        if (!VerifyChecksum(hrp, decoded.ToArray()))
            return (null, null);

        // Remove checksum
        var dataWithoutChecksum = decoded.Take(decoded.Count - ChecksumLength).ToArray();

        // Convert from 5-bit to 8-bit
        var result = ConvertBits(dataWithoutChecksum, 5, 8, false);
        return (hrp, result);
    }

    private static byte[]? ConvertBits(
        byte[] data,
        int fromBits,
        int toBits,
        bool pad)
    {
        int acc = 0;
        int bits = 0;
        int maxv = (1 << toBits) - 1;
        var result = new List<byte>();

        foreach (byte b in data)
        {
            if (b >> fromBits != 0)
                return null;

            acc = (acc << fromBits) | b;
            bits += fromBits;

            while (bits >= toBits)
            {
                bits -= toBits;
                result.Add((byte)((acc >> bits) & maxv));
            }
        }

        if (pad && bits > 0)
            result.Add((byte)((acc << (toBits - bits)) & maxv));
        else if (bits >= fromBits || ((acc << (toBits - bits)) & maxv) != 0)
            return null;

        return result.ToArray();
    }

    private static byte[] CreateChecksum(string hrp, byte[] data)
    {
        var values = ExpandHRP(hrp).Concat(data).Concat(new byte[ChecksumLength]).ToArray();
        uint polymod = PolyMod(values) ^ 1;

        var checksum = new byte[ChecksumLength];
        for (int i = 0; i < ChecksumLength; i++)
            checksum[i] = (byte)((polymod >> (5 * (5 - i))) & 31);

        return checksum;
    }

    private static bool VerifyChecksum(string hrp, byte[] data)
    {
        var values = ExpandHRP(hrp).Concat(data).ToArray();
        return PolyMod(values) == 1;
    }

    private static byte[] ExpandHRP(string hrp)
    {
        return hrp.Select(c => (byte)(c >> 5))
            .Concat(new byte[] { 0 })
            .Concat(hrp.Select(c => (byte)(c & 31)))
            .ToArray();
    }

    private static uint PolyMod(byte[] values)
    {
        uint[] generator = { 0x3b6a57b2, 0x26508e6d, 0x1ea119fa, 0x3d4233dd, 0x2a1462b3 };
        uint chk = 1;

        foreach (byte b in values)
        {
            byte top = (byte)(chk >> 25);
            chk = ((chk & 0x1ffffff) << 5) ^ b;

            for (int i = 0; i < 5; i++)
            {
                if (((top >> i) & 1) == 1)
                    chk ^= generator[i];
            }
        }

        return chk;
    }
}
```

### Variable-Length Integer Encoding

**VarInt Format (Bitcoin-style):**
```
Value           | Encoding
----------------|----------
0-252           | 1 byte (value)
253-65535       | 3 bytes (0xFD + 2-byte LE)
65536-2^32-1    | 5 bytes (0xFE + 4-byte LE)
2^32-2^64-1     | 9 bytes (0xFF + 8-byte LE)
```

**Utilities/VarIntUtilities.cs**
```csharp
namespace Sorcha.Cryptography.Utilities;

/// <summary>
/// Variable-length integer encoding (Bitcoin-style).
/// </summary>
public static class VarIntUtilities
{
    public static byte[] Encode(ulong value)
    {
        if (value < 0xFD)
        {
            return new[] { (byte)value };
        }
        else if (value <= 0xFFFF)
        {
            return new byte[] { 0xFD }
                .Concat(BitConverter.GetBytes((ushort)value))
                .ToArray();
        }
        else if (value <= 0xFFFFFFFF)
        {
            return new byte[] { 0xFE }
                .Concat(BitConverter.GetBytes((uint)value))
                .ToArray();
        }
        else
        {
            return new byte[] { 0xFF }
                .Concat(BitConverter.GetBytes(value))
                .ToArray();
        }
    }

    public static (ulong Value, int BytesRead) Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length == 0)
            throw new ArgumentException("Data is empty");

        byte first = data[0];

        if (first < 0xFD)
        {
            return (first, 1);
        }
        else if (first == 0xFD && data.Length >= 3)
        {
            ushort value = BitConverter.ToUInt16(data.Slice(1, 2));
            return (value, 3);
        }
        else if (first == 0xFE && data.Length >= 5)
        {
            uint value = BitConverter.ToUInt32(data.Slice(1, 4));
            return (value, 5);
        }
        else if (first == 0xFF && data.Length >= 9)
        {
            ulong value = BitConverter.ToUInt64(data.Slice(1, 8));
            return (value, 9);
        }

        throw new ArgumentException("Insufficient data for VarInt");
    }
}
```

### Constitutional Compliance

- ✅ Standard encoding schemes (Base58, Bech32)
- ✅ Bitcoin-compatible implementations
- ✅ Checksum validation
- ✅ Complete XML documentation
- ✅ Efficient implementations

## Testing Requirements

### Unit Tests (Unit/EncodingUtilitiesTests.cs)

**Base58 Tests:**
- [ ] Encode/decode round trip
- [ ] Base58Check with checksum validation
- [ ] Leading zeros handling
- [ ] Invalid character detection
- [ ] Checksum validation failure

**Bech32 Tests:**
- [ ] Encode/decode round trip with "ws1" HRP
- [ ] Checksum validation
- [ ] Invalid character detection
- [ ] Invalid checksum detection
- [ ] Case insensitivity

**Hexadecimal Tests:**
- [ ] Encode/decode round trip
- [ ] Lowercase/uppercase handling
- [ ] Invalid hex character detection
- [ ] Empty input handling

**VarInt Tests:**
- [ ] Encode/decode for 1-byte values (0-252)
- [ ] Encode/decode for 2-byte values (253-65535)
- [ ] Encode/decode for 4-byte values
- [ ] Encode/decode for 8-byte values
- [ ] Boundary values (252, 253, 65535, 65536)
- [ ] Insufficient data handling

### Test Vectors (TestVectors/EncodingTestVectors.cs)

- [ ] Base58 test vectors from Bitcoin
- [ ] Bech32 test vectors from BIP 173
- [ ] VarInt test vectors from Bitcoin
- [ ] Known wallet addresses

## Acceptance Criteria

- [ ] IEncodingProvider interface fully defined
- [ ] Base58 encoding/decoding working
- [ ] Base58Check with checksum working
- [ ] Bech32 encoding/decoding working
- [ ] Hexadecimal encoding/decoding working
- [ ] VarInt encoding/decoding working
- [ ] All unit tests passing (>95% coverage)
- [ ] Test vectors validated
- [ ] Bitcoin-compatible

## Review Checklist

- [ ] Code follows constitutional principles
- [ ] All encodings are standard-compliant
- [ ] Checksums properly validated
- [ ] Test vectors passing
- [ ] Bitcoin compatibility verified

---

**Task Control**
- **Created By:** Claude Code
- **Reviewed By:** (Pending)
- **Approved By:** (Pending)
