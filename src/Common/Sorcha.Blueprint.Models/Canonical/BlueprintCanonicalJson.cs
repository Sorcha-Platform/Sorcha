// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Sorcha.Blueprint.Models.Canonical;

/// <summary>
/// The canonical JSON form of a blueprint definition — the bytes its publication id is computed over
/// (Feature 195). One home, one rule set.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a new type rather than reusing what exists.</b> Two near-misses were already in the tree
/// and neither is a content address. <c>RegisterSerializationOptions.Canonical</c> fixes whitespace,
/// naming policy, null handling and the encoder but <b>does not sort keys</b>. The former
/// <c>BlueprintContentHash</c> re-serialized a parsed <see cref="JsonDocument"/>, which also
/// preserves input key order. Both therefore address the <i>serializer's output</i> rather than the
/// content: the same definition emitted with its keys in a different order hashes differently.
/// </para>
/// <para>
/// <b>The organising rule: only what survives a parse can vary.</b> Insignificant whitespace and
/// string escaping do not — a literal ampersand and its <c>&</c> escape parse to the same
/// string — so this type needs no rule for either, and the <i>producer's</i> encoder cannot affect
/// the identity. What does survive a parse, and is therefore pinned here, is object key order, number
/// representation and how duplicate keys are resolved.
/// </para>
/// <para>
/// <b>What is NOT pinned here, and where it lives instead.</b> Property names and null-omission are
/// pinned by <c>[JsonPropertyName]</c> and <c>JsonIgnoreCondition</c> attributes on the blueprint
/// object graph. That reads like safety and is not: once a definition is content-addressed, every one
/// of those attributes is part of the ledger contract, and renaming one is a refactor with no
/// compile-time consequence that silently re-identifies every definition on every register. The
/// golden-vector test is the only guard that catches it.
/// </para>
/// <para>
/// <b>Changing any rule in this type re-identifies every definition on every register.</b> Such a
/// change takes the domain tag in <see cref="BlueprintPublicationId"/> to <c>v2</c> and is a
/// deliberate migration, never a tidy-up.
/// </para>
/// </remarks>
public static class BlueprintCanonicalJson
{
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = false,
        // Minimal escaping (RFC 8785's intent): write '&', '<', '>', '+' and non-ASCII literally
        // rather than as \uXXXX. The choice is arbitrary but must be FIXED — it is only reached after
        // a parse, so it governs the output form, never the input's.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        SkipValidation = false
    };

    /// <summary>
    /// Produces the canonical form of a blueprint definition document.
    /// </summary>
    /// <param name="json">The definition as JSON.</param>
    /// <returns>Canonical JSON: object keys recursively sorted, no insignificant whitespace.</returns>
    /// <exception cref="ArgumentException"><paramref name="json"/> is null, empty or whitespace.</exception>
    /// <exception cref="JsonException"><paramref name="json"/> is not valid JSON.</exception>
    /// <exception cref="InvalidOperationException">An object carries a duplicate key.</exception>
    public static string Canonicalise(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);

        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
        {
            Write(document.RootElement, writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }

    private static void Write(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                WriteObject(element, writer);
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    Write(item, writer);
                }
                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                // Raw text, deliberately. System.Text.Json writes a parsed number from the text it
                // was given, so `1`, `1.0` and `1e0` are three forms that survive a parse. Blueprint
                // numbers originate from a typed model, so a producer emits one form consistently and
                // normalising would add a rule with no failure mode to prevent. Pinned by test rather
                // than by transformation — see BlueprintCanonicalJsonTests.
                writer.WriteRawValue(element.GetRawText());
                break;

            case JsonValueKind.True:
            case JsonValueKind.False:
                writer.WriteBooleanValue(element.GetBoolean());
                break;

            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;

            default:
                throw new InvalidOperationException(
                    $"Unsupported JSON value kind '{element.ValueKind}' in a blueprint definition.");
        }
    }

    private static void WriteObject(JsonElement element, Utf8JsonWriter writer)
    {
        // Collect first so duplicates are detected before anything is written — a partially-written
        // document would otherwise be discarded with a less useful error.
        var properties = new List<JsonProperty>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var property in element.EnumerateObject())
        {
            if (!seen.Add(property.Name))
            {
                // Refuse rather than resolve. System.Text.Json's lookup takes the last occurrence,
                // which is a silent choice about WHICH of two definitions was published — and under
                // content addressing that choice is baked into an immutable ledger record.
                throw new InvalidOperationException(
                    $"Blueprint definition contains a duplicate object key '{property.Name}'. " +
                    "A document that cannot be read unambiguously is refused, not resolved.");
            }

            properties.Add(property);
        }

        // Ordinal — RFC 8785 orders by UTF-16 code unit, which is what StringComparer.Ordinal does.
        // Keys are unique by the check above, so the sort's instability cannot matter.
        properties.Sort(static (a, b) => string.CompareOrdinal(a.Name, b.Name));

        writer.WriteStartObject();
        foreach (var property in properties)
        {
            writer.WritePropertyName(property.Name);
            Write(property.Value, writer);
        }
        writer.WriteEndObject();
    }
}
