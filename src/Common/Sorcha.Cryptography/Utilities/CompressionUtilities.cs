// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Interfaces;

namespace Sorcha.Cryptography.Utilities;

/// <summary>
/// Implements data compression and decompression utilities.
/// </summary>
public class CompressionUtilities : ICompressionUtilities
{
    private const byte CompressionMarker = 0xC0; // Marker to indicate compressed data
    private const int MinSizeForCompression = 128; // Don't compress data smaller than this

    // File signatures for common compressed formats
    private static readonly Dictionary<string, byte[]> FileSignatures = new()
    {
        { "ZIP", new byte[] { 0x50, 0x4B, 0x03, 0x04 } },
        { "GZIP", new byte[] { 0x1F, 0x8B } },
        { "PNG", new byte[] { 0x89, 0x50, 0x4E, 0x47 } },
        { "JPEG", new byte[] { 0xFF, 0xD8, 0xFF } },
        { "PDF", new byte[] { 0x25, 0x50, 0x44, 0x46 } },
        { "MP3", new byte[] { 0xFF, 0xFB } },
        { "MP4", new byte[] { 0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70 } },
        { "7Z", new byte[] { 0x37, 0x7A, 0xBC, 0xAF, 0x27, 0x1C } }
    };

    /// <summary>
    /// Compresses data if beneficial.
    /// </summary>
    public (byte[]? Data, bool WasCompressed) Compress(
        byte[] data,
        CompressionType type = CompressionType.Balanced)
    {
        try
        {
            if (data == null || data.Length == 0)
                return (data, false);

            // Don't compress if type is None
            if (type == CompressionType.None)
                return (data, false);

            // Don't compress if data is too small
            if (data.Length < MinSizeForCompression)
                return (data, false);

            // Don't compress if already compressed
            if (IsAlreadyCompressedFormat(data))
                return (data, false);

            // Attempt compression
            using var outputStream = new MemoryStream();

            // Write compression marker
            outputStream.WriteByte(CompressionMarker);

            // Write original size (4 bytes)
            byte[] sizeBytes = BitConverter.GetBytes(data.Length);
            outputStream.Write(sizeBytes, 0, 4);

            // Compress data
            var compressionLevel = GetCompressionLevel(type);
            using (var deflateStream = new DeflateStream(outputStream, compressionLevel, leaveOpen: true))
            {
                deflateStream.Write(data, 0, data.Length);
            }

            byte[] compressed = outputStream.ToArray();

            // Only use compressed data if it's actually smaller
            if (compressed.Length < data.Length)
            {
                return (compressed, true);
            }
            else
            {
                return (data, false);
            }
        }
        catch
        {
            return (data, false);
        }
    }

    /// <summary>
    /// Decompresses data if it was compressed.
    /// </summary>
    public byte[]? Decompress(byte[] data)
    {
        try
        {
            if (data == null || data.Length == 0)
                return data;

            // Check for compression marker
            if (data.Length < 5 || data[0] != CompressionMarker)
                return data; // Not compressed by us

            // Read original size
            int originalSize = BitConverter.ToInt32(data, 1);

            // Validate size
            if (originalSize <= 0 || originalSize > 100_000_000) // 100MB max
                return null;

            // Decompress
            using var inputStream = new MemoryStream(data, 5, data.Length - 5);
            using var deflateStream = new DeflateStream(inputStream, CompressionMode.Decompress);
            using var outputStream = new MemoryStream(originalSize);

            deflateStream.CopyTo(outputStream);

            byte[] decompressed = outputStream.ToArray();

            // Verify size matches
            if (decompressed.Length != originalSize)
                return null;

            return decompressed;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Checks if data appears to already be compressed.
    /// </summary>
    public bool IsCompressed(byte[] data)
    {
        if (data == null || data.Length == 0)
            return false;

        // Check if compressed by us
        if (data.Length >= 5 && data[0] == CompressionMarker)
            return true;

        // Check for known compressed file formats
        return IsAlreadyCompressedFormat(data);
    }

    /// <summary>
    /// Detects the file type from the data signature.
    /// </summary>
    public string? DetectFileType(byte[] data)
    {
        if (data == null || data.Length < 4)
            return null;

        foreach (var signature in FileSignatures)
        {
            if (MatchesSignature(data, signature.Value))
                return signature.Key;
        }

        return null;
    }

    private bool IsAlreadyCompressedFormat(byte[] data)
    {
        if (data == null || data.Length < 4)
            return false;

        string? fileType = DetectFileType(data);
        if (fileType == null)
            return false;

        // List of formats that are already compressed
        var compressedFormats = new[] { "ZIP", "GZIP", "PNG", "JPEG", "MP3", "MP4", "7Z", "PDF" };
        return compressedFormats.Contains(fileType);
    }

    private bool MatchesSignature(byte[] data, byte[] signature)
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

    private CompressionLevel GetCompressionLevel(CompressionType type)
    {
        return type switch
        {
            CompressionType.Fast => CompressionLevel.Fastest,
            CompressionType.Balanced => CompressionLevel.Optimal,
            CompressionType.Maximum => CompressionLevel.SmallestSize,
            _ => CompressionLevel.Optimal
        };
    }

}
