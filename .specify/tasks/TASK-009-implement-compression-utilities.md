# Task: Implement Compression Utilities

**ID:** TASK-009
**Status:** Not Started
**Priority:** Medium
**Estimate:** 4 hours
**Assignee:** Unassigned
**Created:** 2025-11-12
**Updated:** 2025-11-12

## Context

Implement data compression utilities with file type detection to avoid compressing already-compressed data. Used by payload management to reduce transaction size.

**Related Specifications:**
- [Sorcha.Cryptography Rewrite Spec - FR-10](../specs/sorcha-cryptography-rewrite.md#fr-10-compression)
- [Current WalletUtils Compression](../../src/Common/Sorcha.Cryptography/WalletUtils.cs)

**Dependencies:**
- TASK-001 (Project setup)
- TASK-002 (Enums and models)

## Objective

Implement ICompressionUtilities interface with Deflate compression, file type detection, and configurable compression levels.

## Implementation Details

### Files to Create

1. **Interfaces/ICompressionUtilities.cs** - Interface definition
2. **Utilities/CompressionUtilities.cs** - Implementation
3. **Utilities/FileTypeDetector.cs** - Magic byte detection

### Technical Approach

**Interface: Interfaces/ICompressionUtilities.cs**
```csharp
namespace Sorcha.Cryptography.Interfaces;

/// <summary>
/// Provides data compression and decompression utilities.
/// </summary>
public interface ICompressionUtilities
{
    /// <summary>
    /// Compresses data if beneficial (auto-detects file types).
    /// </summary>
    /// <param name="data">Data to compress.</param>
    /// <param name="level">Compression level.</param>
    /// <returns>Tuple of (compressed data or original, was compressed).</returns>
    (byte[] Data, bool WasCompressed) Compress(
        byte[] data,
        CompressionType level = CompressionType.Balanced);

    /// <summary>
    /// Decompresses data if it was compressed.
    /// </summary>
    /// <param name="data">Data to decompress.</param>
    byte[]? Decompress(byte[] data);

    /// <summary>
    /// Detects if data appears to be already compressed.
    /// </summary>
    bool IsAlreadyCompressed(byte[] data);

    /// <summary>
    /// Gets estimated compression ratio without actually compressing.
    /// </summary>
    double EstimateCompressionRatio(byte[] data);
}
```

**Compression Levels (CompressionType enum):**
```csharp
public enum CompressionType : byte
{
    None = 0,        // No compression
    Fast = 1,        // CompressionLevel.Fastest
    Balanced = 2,    // CompressionLevel.Optimal
    Max = 3          // CompressionLevel.SmallestSize
}
```

### File Type Detection

**Magic Bytes for Common Formats:**
```csharp
private static readonly Dictionary<byte[], string> MagicBytes = new()
{
    // Already compressed formats
    { new byte[] { 0x1F, 0x8B }, "GZIP" },
    { new byte[] { 0x50, 0x4B, 0x03, 0x04 }, "ZIP" },
    { new byte[] { 0x50, 0x4B, 0x05, 0x06 }, "ZIP" },
    { new byte[] { 0x50, 0x4B, 0x07, 0x08 }, "ZIP" },
    { new byte[] { 0x52, 0x61, 0x72, 0x21 }, "RAR" },
    { new byte[] { 0x37, 0x7A, 0xBC, 0xAF }, "7Z" },
    { new byte[] { 0x42, 0x5A, 0x68 }, "BZIP2" },

    // Image formats (often compressed)
    { new byte[] { 0xFF, 0xD8, 0xFF }, "JPEG" },
    { new byte[] { 0x89, 0x50, 0x4E, 0x47 }, "PNG" },
    { new byte[] { 0x47, 0x49, 0x46 }, "GIF" },
    { new byte[] { 0x52, 0x49, 0x46, 0x46 }, "WEBP/AVI" },

    // Video formats
    { new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 }, "MP4" },
    { new byte[] { 0x00, 0x00, 0x00, 0x1C, 0x66, 0x74, 0x79, 0x70 }, "MP4" },

    // Audio formats
    { new byte[] { 0x49, 0x44, 0x33 }, "MP3" },
    { new byte[] { 0xFF, 0xFB }, "MP3" },

    // Document formats
    { new byte[] { 0x25, 0x50, 0x44, 0x46 }, "PDF" },

    // Uncompressed formats (good candidates)
    { new byte[] { 0x42, 0x4D }, "BMP" },
    { new byte[] { 0x49, 0x49, 0x2A, 0x00 }, "TIFF" },
    { new byte[] { 0x4D, 0x4D, 0x00, 0x2A }, "TIFF" }
};
```

### Implementation

**Utilities/CompressionUtilities.cs**
```csharp
namespace Sorcha.Cryptography.Utilities;

public sealed class CompressionUtilities : ICompressionUtilities
{
    private const uint CompressionMagic = 0x534B4D50; // "SKMP" (Sorcha Kompressed)

    public (byte[] Data, bool WasCompressed) Compress(
        byte[] data,
        CompressionType level = CompressionType.Balanced)
    {
        if (data == null || data.Length == 0)
            return (data, false);

        // Skip compression for small data
        if (data.Length < 128)
            return (data, false);

        // Skip if already compressed
        if (IsAlreadyCompressed(data))
            return (data, false);

        // Map compression level
        var compressionLevel = level switch
        {
            CompressionType.Fast => System.IO.Compression.CompressionLevel.Fastest,
            CompressionType.Balanced => System.IO.Compression.CompressionLevel.Optimal,
            CompressionType.Max => System.IO.Compression.CompressionLevel.SmallestSize,
            _ => System.IO.Compression.CompressionLevel.Optimal
        };

        // Compress
        using var outputStream = new MemoryStream();
        using (var deflateStream = new DeflateStream(outputStream, compressionLevel))
        {
            deflateStream.Write(data, 0, data.Length);
        }

        byte[] compressed = outputStream.ToArray();

        // Only use compressed if it's actually smaller
        if (compressed.Length >= data.Length * 0.95) // At least 5% reduction
            return (data, false);

        // Add magic header + original size
        var header = new byte[8];
        BitConverter.GetBytes(CompressionMagic).CopyTo(header, 0);
        BitConverter.GetBytes(data.Length).CopyTo(header, 4);

        return (header.Concat(compressed).ToArray(), true);
    }

    public byte[]? Decompress(byte[] data)
    {
        if (data == null || data.Length < 8)
            return data;

        // Check for compression magic
        uint magic = BitConverter.ToUInt32(data, 0);
        if (magic != CompressionMagic)
            return data; // Not compressed

        // Read original size
        int originalSize = BitConverter.ToInt32(data, 4);
        if (originalSize <= 0 || originalSize > 100_000_000) // 100 MB limit
            return null;

        // Decompress
        try
        {
            using var inputStream = new MemoryStream(data, 8, data.Length - 8);
            using var deflateStream = new DeflateStream(inputStream, CompressionMode.Decompress);
            using var outputStream = new MemoryStream(originalSize);

            deflateStream.CopyTo(outputStream);
            return outputStream.ToArray();
        }
        catch
        {
            return null; // Decompression failed
        }
    }

    public bool IsAlreadyCompressed(byte[] data)
    {
        if (data == null || data.Length < 4)
            return false;

        return FileTypeDetector.IsCompressedFormat(data);
    }

    public double EstimateCompressionRatio(byte[] data)
    {
        if (data == null || data.Length == 0)
            return 1.0;

        // Calculate entropy (Shannon entropy)
        var frequencies = new int[256];
        foreach (byte b in data)
            frequencies[b]++;

        double entropy = 0;
        foreach (int freq in frequencies)
        {
            if (freq > 0)
            {
                double probability = (double)freq / data.Length;
                entropy -= probability * Math.Log2(probability);
            }
        }

        // Entropy is 0-8 bits per byte
        // High entropy (near 8) = already compressed/random
        // Low entropy (near 0) = highly compressible
        return Math.Min(entropy / 8.0, 1.0);
    }
}
```

**Utilities/FileTypeDetector.cs**
```csharp
namespace Sorcha.Cryptography.Utilities;

internal static class FileTypeDetector
{
    private static readonly (byte[] Signature, bool IsCompressed)[] KnownFormats =
    {
        // Compressed archives
        (new byte[] { 0x1F, 0x8B }, true),              // GZIP
        (new byte[] { 0x50, 0x4B, 0x03, 0x04 }, true),  // ZIP
        (new byte[] { 0x52, 0x61, 0x72, 0x21 }, true),  // RAR
        (new byte[] { 0x37, 0x7A, 0xBC, 0xAF }, true),  // 7Z
        (new byte[] { 0x42, 0x5A, 0x68 }, true),        // BZIP2

        // Compressed images
        (new byte[] { 0xFF, 0xD8, 0xFF }, true),        // JPEG
        (new byte[] { 0x89, 0x50, 0x4E, 0x47 }, true),  // PNG
        (new byte[] { 0x47, 0x49, 0x46 }, true),        // GIF

        // Compressed video/audio
        (new byte[] { 0x49, 0x44, 0x33 }, true),        // MP3
        (new byte[] { 0x25, 0x50, 0x44, 0x46 }, true),  // PDF

        // Uncompressed (good compression candidates)
        (new byte[] { 0x42, 0x4D }, false),             // BMP
        (new byte[] { 0x49, 0x49, 0x2A, 0x00 }, false), // TIFF LE
        (new byte[] { 0x4D, 0x4D, 0x00, 0x2A }, false)  // TIFF BE
    };

    public static bool IsCompressedFormat(byte[] data)
    {
        foreach (var (signature, isCompressed) in KnownFormats)
        {
            if (StartsWith(data, signature))
                return isCompressed;
        }

        // Unknown format, assume not compressed
        return false;
    }

    private static bool StartsWith(byte[] data, byte[] signature)
    {
        if (data.Length < signature.Length)
            return false;

        for (int i = 0; i < signature.Length; i++)
        {
            if (data[i] != signature[i])
                return false;
        }

        return true;
    }
}
```

### Constitutional Compliance

- ✅ Avoids compressing already-compressed data
- ✅ Only uses compression when beneficial
- ✅ Configurable compression levels
- ✅ Complete XML documentation
- ✅ Efficient implementation

## Testing Requirements

### Unit Tests (Unit/CompressionUtilitiesTests.cs)

**Compression Tests:**
- [ ] Compress text data (high compression ratio)
- [ ] Compress binary data (low compression ratio)
- [ ] Compress/decompress round trip
- [ ] Small data (< 128 bytes) not compressed
- [ ] Already compressed data not re-compressed (JPEG, PNG, ZIP)
- [ ] Compression only used if >5% size reduction
- [ ] Compression levels (Fast, Balanced, Max)

**Decompression Tests:**
- [ ] Decompress compressed data
- [ ] Uncompressed data returned as-is
- [ ] Invalid compressed data handling
- [ ] Size limit enforcement (> 100 MB)
- [ ] Corrupted data handling

**File Type Detection Tests:**
- [ ] IsAlreadyCompressed detects ZIP
- [ ] IsAlreadyCompressed detects JPEG
- [ ] IsAlreadyCompressed detects PNG
- [ ] IsAlreadyCompressed detects PDF
- [ ] IsAlreadyCompressed returns false for BMP
- [ ] IsAlreadyCompressed returns false for text

**Estimation Tests:**
- [ ] EstimateCompressionRatio for text (low entropy)
- [ ] EstimateCompressionRatio for random (high entropy)
- [ ] EstimateCompressionRatio for zeros (very low entropy)

## Acceptance Criteria

- [ ] ICompressionUtilities interface fully defined
- [ ] CompressionUtilities implementation complete
- [ ] Deflate compression working
- [ ] Decompression working
- [ ] File type detection working
- [ ] Compression only applied when beneficial
- [ ] All compression levels working
- [ ] All unit tests passing (>90% coverage)
- [ ] No crashes on invalid data

## Implementation Notes

**Performance Considerations:**
- Compression is CPU-intensive
- Only compress when network transfer time > compression time
- Typical: Compression beneficial for data > 1 KB

**Security Considerations:**
- Decompression bombs: Limit decompressed size
- Invalid data: Handle gracefully, return null
- No exceptions on invalid data

## Review Checklist

- [ ] Code follows constitutional principles
- [ ] File type detection comprehensive
- [ ] Compression only when beneficial
- [ ] Decompression size limits enforced
- [ ] No security vulnerabilities

---

**Task Control**
- **Created By:** Claude Code
- **Reviewed By:** (Pending)
- **Approved By:** (Pending)
