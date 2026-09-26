// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using Sorcha.Register.Models;

namespace Sorcha.Register.Service.Services.Implementation;

/// <summary>
/// Reads the transactions out of a recovered docket's <c>SyncDocketEntry.DocketData</c> (#1653).
/// </summary>
/// <remarks>
/// <para>
/// Recovery deserialised the bytes as <c>List&lt;TransactionModel&gt;</c>, but every producer that
/// fills the peer's docket cache writes a <b>docket object</b>: <c>RelayMessageHandler</c> and
/// <c>RegisterSyncGrpcService</c> both serialise a <c>DocketModel</c> with default options, so the
/// transactions sit under a PascalCase <c>Transactions</c> property. The mismatch failed at byte 1
/// on every docket, recovery logged a warning and reported "processed N dockets", and every
/// recovered Action transaction went unrouted — masked on tiny only because live replication
/// delivered the same dockets at the same moment.
/// </para>
/// <para>
/// The docket object is the contract. A bare array is still accepted for any producer that wrote
/// the old shape, and property names are matched case-insensitively.
/// </para>
/// </remarks>
internal static class RecoveredDocketData
{
    private static readonly JsonSerializerOptions TransactionOptions = new() { PropertyNameCaseInsensitive = true };

    /// <summary>
    /// Extracts the docket's transactions. Returns false when the bytes are not a readable docket,
    /// so the caller can report that rather than count the docket as processed.
    /// </summary>
    public static bool TryReadTransactions(ReadOnlySpan<byte> docketData, out List<TransactionModel> transactions)
    {
        transactions = [];
        try
        {
            using var document = JsonDocument.Parse(docketData.ToArray());
            var root = document.RootElement;

            JsonElement array;
            if (root.ValueKind == JsonValueKind.Array)
            {
                array = root;
            }
            else if (root.ValueKind == JsonValueKind.Object && TryGetPropertyIgnoreCase(root, "transactions", out var found))
            {
                if (found.ValueKind != JsonValueKind.Array)
                {
                    // A "transactions" that is not a list is a malformed docket, not an empty one.
                    return false;
                }

                array = found;
            }
            else if (root.ValueKind == JsonValueKind.Object)
            {
                // A docket object with no transactions property at all: readable, and empty.
                return true;
            }
            else
            {
                return false;
            }

            transactions = array.Deserialize<List<TransactionModel>>(TransactionOptions) ?? [];
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }
}
