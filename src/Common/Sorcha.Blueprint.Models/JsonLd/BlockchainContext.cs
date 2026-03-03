// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;

namespace Sorcha.Blueprint.Models.JsonLd;

/// <summary>
/// Provides JSON-LD context definitions for Sorcha blockchain transactions
/// Implements the Blockchain Transaction Format specification
/// </summary>
/// <remarks>
/// See: docs/reference/blockchain-transaction-format.md for complete specification
/// Context URL: https://sorcha.dev/contexts/blockchain/v1.jsonld
/// </remarks>
public static class BlockchainContext
{
    /// <summary>
    /// Canonical JSON-LD context for Sorcha blockchain transactions
    /// Aligns with W3C standards for DIDs, Verifiable Credentials, and blockchain vocabularies
    /// </summary>
    public static readonly JsonNode TransactionContext = JsonNode.Parse(@"
    {
      ""@context"": {
        ""@version"": 1.1,
        ""@vocab"": ""https://sorcha.dev/blockchain/v1#"",
        ""sec"": ""https://w3id.org/security#"",
        ""blockchain"": ""https://w3id.org/blockchain/v1#"",
        ""xsd"": ""http://www.w3.org/2001/XMLSchema#"",
        ""did"": ""https://www.w3.org/ns/did/v1#"",

        ""TransactionReference"": ""blockchain:TransactionReference"",
        ""Transaction"": ""blockchain:Transaction"",

        ""txId"": {
          ""@id"": ""blockchain:transactionHash"",
          ""@type"": ""@id""
        },
        ""previousTxHash"": {
          ""@id"": ""blockchain:previousTransactionHash"",
          ""@type"": ""@id""
        },
        ""registerId"": {
          ""@id"": ""blockchain:registerId"",
          ""@type"": ""@id""
        },
        ""blockNumber"": {
          ""@id"": ""blockchain:blockNumber"",
          ""@type"": ""xsd:unsignedLong""
        },
        ""timestamp"": {
          ""@id"": ""sec:created"",
          ""@type"": ""xsd:dateTime""
        },
        ""senderWallet"": {
          ""@id"": ""blockchain:from"",
          ""@type"": ""@id""
        },
        ""recipients"": {
          ""@id"": ""blockchain:to"",
          ""@type"": ""@id"",
          ""@container"": ""@list""
        },
        ""metadata"": {
          ""@id"": ""blockchain:metadata"",
          ""@type"": ""@json""
        },
        ""signature"": {
          ""@id"": ""sec:signatureValue"",
          ""@type"": ""xsd:base64Binary""
        },
        ""payloadCount"": {
          ""@id"": ""blockchain:payloadCount"",
          ""@type"": ""xsd:unsignedInt""
        },
        ""version"": {
          ""@id"": ""blockchain:version"",
          ""@type"": ""xsd:unsignedInt""
        }
      }
    }")!;

    /// <summary>
    /// Compact context reference for minimal transaction references
    /// </summary>
    public static readonly JsonNode TransactionReferenceContext = JsonNode.Parse(@"
    {
      ""@context"": {
        ""@version"": 1.1,
        ""@vocab"": ""https://sorcha.dev/blockchain/v1#"",
        ""blockchain"": ""https://w3id.org/blockchain/v1#"",
        ""xsd"": ""http://www.w3.org/2001/XMLSchema#"",

        ""TransactionReference"": ""blockchain:TransactionReference"",

        ""registerId"": {
          ""@id"": ""blockchain:registerId"",
          ""@type"": ""@id""
        },
        ""txId"": {
          ""@id"": ""blockchain:transactionHash"",
          ""@type"": ""@id""
        },
        ""timestamp"": {
          ""@id"": ""https://w3id.org/security#created"",
          ""@type"": ""xsd:dateTime""
        }
      }
    }")!;

    /// <summary>
    /// Generates a DID URI for a transaction
    /// Format: did:sorcha:register:{registerId}/tx/{txId}
    /// </summary>
    /// <param name="registerId">Register (ledger) identifier</param>
    /// <param name="txId">Transaction identifier</param>
    /// <returns>W3C DID-compliant URI</returns>
    public static string GenerateDidUri(string registerId, string txId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId, nameof(registerId));
        ArgumentException.ThrowIfNullOrWhiteSpace(txId, nameof(txId));

        return $"did:sorcha:register:{registerId}/tx/{txId}";
    }

    /// <summary>
    /// Parses a DID URI to extract register ID and transaction ID
    /// </summary>
    /// <param name="didUri">DID URI to parse</param>
    /// <returns>Tuple of (registerId, txId) or null if invalid</returns>
    public static (string registerId, string txId)? ParseDidUri(string didUri)
    {
        if (string.IsNullOrWhiteSpace(didUri))
            return null;

        // Expected format: did:sorcha:register:{registerId}/tx/{txId}
        const string prefix = "did:sorcha:register:";
        if (!didUri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var remainder = didUri[prefix.Length..];
        var parts = remainder.Split("/tx/", StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length != 2)
            return null;

        return (parts[0], parts[1]);
    }

    /// <summary>
    /// Validates a DID URI format
    /// </summary>
    /// <param name="didUri">DID URI to validate</param>
    /// <returns>True if valid format</returns>
    public static bool IsValidDidUri(string didUri)
    {
        return ParseDidUri(didUri) != null;
    }

    /// <summary>
    /// Gets the blockchain context URL for external reference
    /// </summary>
    public static string ContextUrl => "https://sorcha.dev/contexts/blockchain/v1.jsonld";

    /// <summary>
    /// Creates a compact transaction reference with JSON-LD context
    /// </summary>
    /// <param name="registerId">Register identifier</param>
    /// <param name="txId">Transaction identifier</param>
    /// <param name="timestamp">Transaction timestamp</param>
    /// <returns>JSON-LD transaction reference object</returns>
    public static JsonObject CreateTransactionReference(string registerId, string txId, DateTime timestamp)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId, nameof(registerId));
        ArgumentException.ThrowIfNullOrWhiteSpace(txId, nameof(txId));

        var didUri = GenerateDidUri(registerId, txId);

        return new JsonObject
        {
            ["@context"] = ContextUrl,
            ["@type"] = "TransactionReference",
            ["@id"] = didUri,
            ["registerId"] = registerId,
            ["txId"] = txId,
            ["timestamp"] = timestamp.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
        };
    }

    /// <summary>
    /// Merges blockchain context with blueprint contexts
    /// </summary>
    /// <param name="blueprintContext">Existing blueprint context</param>
    /// <returns>Merged context array</returns>
    public static JsonArray MergeWithBlueprintContext(JsonNode blueprintContext)
    {
        var merged = new JsonArray
        {
            blueprintContext.DeepClone(),
            ContextUrl
        };

        return merged;
    }

    /// <summary>
    /// Validates if a JSON node contains blockchain transaction context
    /// </summary>
    public static bool HasBlockchainContext(JsonNode? node)
    {
        if (node is not JsonObject obj)
            return false;

        if (!obj.TryGetPropertyValue("@context", out var context))
            return false;

        // Check if context is our URL or contains it
        if (context is JsonValue jsonValue)
        {
            return jsonValue.ToString() == ContextUrl;
        }
        else if (context is JsonArray contextArray)
        {
            return contextArray.Any(item =>
                item is JsonValue val && val.ToString() == ContextUrl);
        }

        return false;
    }
}
