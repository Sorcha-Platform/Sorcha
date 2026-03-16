// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sorcha.Register.Service.Services;

/// <summary>
/// Computes structural hashes of blueprints for version classification.
/// Strips all instruction fields before hashing so that documentation-only
/// changes produce the same structural hash as the previous version.
/// </summary>
public class StructuralDiffService
{
    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        WriteIndented = false,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    /// <summary>
    /// Computes the structural hash of a blueprint JSON element.
    /// Strips instructions at blueprint, action, control, and participant levels,
    /// then serializes to canonical JSON (sorted keys) and returns the SHA-256 hex digest.
    /// </summary>
    /// <param name="blueprintJson">The full blueprint JSON</param>
    /// <returns>64-character lowercase hex SHA-256 hash</returns>
    public string ComputeStructuralHash(JsonElement blueprintJson)
    {
        var node = JsonNode.Parse(blueprintJson.GetRawText());
        if (node is not JsonObject root)
            throw new ArgumentException("Blueprint JSON must be an object.");

        StripInstructions(root);
        var canonical = SerializeCanonical(root);
        return ComputeSha256Hex(canonical);
    }

    /// <summary>
    /// Computes the structural hash of a blueprint JSON string.
    /// </summary>
    /// <param name="blueprintJson">The full blueprint JSON string</param>
    /// <returns>64-character lowercase hex SHA-256 hash</returns>
    public string ComputeStructuralHash(string blueprintJson)
    {
        using var doc = JsonDocument.Parse(blueprintJson);
        return ComputeStructuralHash(doc.RootElement);
    }

    /// <summary>
    /// Classifies a change between two blueprint versions by comparing their structural hashes.
    /// </summary>
    /// <param name="currentHash">Structural hash of the current published version</param>
    /// <param name="newHash">Structural hash of the new version</param>
    /// <returns>"documentation" if hashes match, "structural" if they differ</returns>
    public static string ClassifyChange(string currentHash, string newHash)
    {
        return string.Equals(currentHash, newHash, StringComparison.OrdinalIgnoreCase)
            ? "documentation"
            : "structural";
    }

    /// <summary>
    /// Strips all instruction-related fields from a blueprint JsonObject.
    /// Removes: instructions (blueprint-level), action.instructions, control.instructions,
    /// participant.instructions, versionMajor, versionMinor.
    /// </summary>
    private static void StripInstructions(JsonObject root)
    {
        // Strip blueprint-level instruction fields
        root.Remove("instructions");
        root.Remove("versionMajor");
        root.Remove("versionMinor");

        // Strip action-level instructions
        if (root.TryGetPropertyValue("actions", out var actionsNode) && actionsNode is JsonArray actions)
        {
            foreach (var actionNode in actions)
            {
                if (actionNode is JsonObject action)
                {
                    action.Remove("instructions");
                    StripControlInstructions(action);
                }
            }
        }

        // Strip participant-level instructions
        if (root.TryGetPropertyValue("participants", out var participantsNode) && participantsNode is JsonArray participants)
        {
            foreach (var participantNode in participants)
            {
                if (participantNode is JsonObject participant)
                {
                    participant.Remove("instructions");
                }
            }
        }
    }

    /// <summary>
    /// Recursively strips instructions from control elements within an action.
    /// </summary>
    private static void StripControlInstructions(JsonObject action)
    {
        if (action.TryGetPropertyValue("form", out var formNode) && formNode is JsonObject form)
        {
            StripControlInstructionsRecursive(form);
        }
    }

    private static void StripControlInstructionsRecursive(JsonObject control)
    {
        control.Remove("instructions");

        if (control.TryGetPropertyValue("elements", out var elementsNode) && elementsNode is JsonArray elements)
        {
            foreach (var elementNode in elements)
            {
                if (elementNode is JsonObject element)
                {
                    StripControlInstructionsRecursive(element);
                }
            }
        }
    }

    /// <summary>
    /// Serializes a JsonNode to canonical JSON with sorted keys for deterministic hashing.
    /// </summary>
    private static string SerializeCanonical(JsonNode node)
    {
        var sorted = SortKeys(node);
        return sorted?.ToJsonString(CanonicalOptions) ?? "null";
    }

    /// <summary>
    /// Recursively sorts all object keys in alphabetical order.
    /// </summary>
    private static JsonNode? SortKeys(JsonNode? node)
    {
        if (node is JsonObject obj)
        {
            var sorted = new JsonObject();
            foreach (var kvp in obj.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                sorted[kvp.Key] = SortKeys(kvp.Value?.DeepClone());
            }
            return sorted;
        }

        if (node is JsonArray arr)
        {
            var sortedArr = new JsonArray();
            foreach (var item in arr)
            {
                sortedArr.Add(SortKeys(item?.DeepClone()));
            }
            return sortedArr;
        }

        return node?.DeepClone();
    }

    private static string ComputeSha256Hex(string input)
    {
        var bytes = Encoding.UTF8.GetBytes(input);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
