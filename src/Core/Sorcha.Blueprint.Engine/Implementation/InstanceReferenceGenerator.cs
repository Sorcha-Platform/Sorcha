// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Sorcha.Blueprint.Models;

namespace Sorcha.Blueprint.Engine.Implementation;

/// <summary>
/// Generates human-readable compound references for workflow instances.
/// References are derived from blueprint-defined templates and first-action payload data.
/// Format: {PREFIX}-{COMP1}-{COMP2}-{HASH} (e.g., "CP-RIV-14W-A7K3").
/// </summary>
public static partial class InstanceReferenceGenerator
{
    private const string UnknownFallback = "UNK";
    private const string DefaultPrefix = "BP";
    private const int HashLength = 4;

    /// <summary>
    /// Generate a human-readable instance reference from the template and accumulated data.
    /// </summary>
    /// <param name="template">Blueprint-defined reference template, or null for fallback.</param>
    /// <param name="accumulatedData">Payload field values from completed actions.</param>
    /// <param name="instanceId">Instance ID used for uniqueness hash.</param>
    /// <param name="blueprintTitle">Blueprint title used for fallback prefix.</param>
    /// <returns>Uppercase hyphen-separated reference string.</returns>
    public static string Generate(
        InstanceReferenceTemplate? template,
        Dictionary<string, object> accumulatedData,
        string instanceId,
        string blueprintTitle)
    {
        var hash = GenerateHash(instanceId);

        if (template is null)
        {
            var fallbackPrefix = ExtractFallbackPrefix(blueprintTitle);
            return $"{fallbackPrefix}-{hash}";
        }

        var segments = new List<string> { template.Prefix };

        foreach (var component in template.Components)
        {
            var fieldName = component.Field.TrimStart('/');
            var rawValue = accumulatedData.TryGetValue(fieldName, out var val)
                ? val?.ToString() ?? string.Empty
                : string.Empty;

            var segment = ApplyTransform(rawValue, component.Transform, component.Chars);
            segments.Add(segment);
        }

        segments.Add(hash);

        return string.Join("-", segments);
    }

    private static string ApplyTransform(string value, ReferenceTransform transform, int maxChars)
    {
        if (string.IsNullOrWhiteSpace(value))
            return UnknownFallback;

        var cleaned = StripNonAscii(value);
        if (string.IsNullOrWhiteSpace(cleaned))
            return UnknownFallback;

        var result = transform switch
        {
            ReferenceTransform.FirstWord => ExtractFirstWord(cleaned, maxChars),
            ReferenceTransform.Truncate => Truncate(cleaned, maxChars),
            ReferenceTransform.Uppercase => Truncate(cleaned, maxChars),
            _ => Truncate(cleaned, maxChars)
        };

        return result.ToUpperInvariant();
    }

    private static string ExtractFirstWord(string value, int maxChars)
    {
        var firstWord = value.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? value;
        return firstWord.Length <= maxChars ? firstWord : firstWord[..maxChars];
    }

    private static string Truncate(string value, int maxChars)
    {
        return value.Length <= maxChars ? value : value[..maxChars];
    }

    private static string StripNonAscii(string value)
    {
        return AsciiOnlyRegex().Replace(value, string.Empty);
    }

    private static string ExtractFallbackPrefix(string blueprintTitle)
    {
        if (string.IsNullOrWhiteSpace(blueprintTitle))
            return DefaultPrefix;

        var cleaned = StripNonAscii(blueprintTitle).Replace(" ", string.Empty);
        if (cleaned.Length < 2)
            return DefaultPrefix;

        return cleaned[..2].ToUpperInvariant();
    }

    private static string GenerateHash(string instanceId)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(instanceId));
        // Take first 3 bytes (24 bits) and encode as base36 for 4 characters
        var value = ((long)bytes[0] << 16) | ((long)bytes[1] << 8) | bytes[2];
        return ToBase36(value, HashLength).ToUpperInvariant();
    }

    private static string ToBase36(long value, int length)
    {
        const string chars = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        var result = new char[length];
        for (var i = length - 1; i >= 0; i--)
        {
            result[i] = chars[(int)(value % 36)];
            value /= 36;
        }
        return new string(result);
    }

    [GeneratedRegex(@"[^\x20-\x7E]")]
    private static partial Regex AsciiOnlyRegex();
}
