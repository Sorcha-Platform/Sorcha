// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sorcha.Register.Models;

/// <summary>
/// Canonical <see cref="JsonSerializerOptions"/> for on-the-wire register payloads.
/// </summary>
/// <remarks>
/// <para>
/// Any service that serialises a register-scoped model (register record, transaction,
/// docket, control record) for checksumming, gossip, or peer replication MUST use these
/// options. They define the exact byte shape of the canonical form — if two services
/// diverge on naming policy, encoder, or null-handling, they will produce different
/// bytes for the same logical object and every downstream integrity check will fail.
/// </para>
/// <para>
/// Specifically pinned:
/// <list type="bullet">
///   <item><description><see cref="JsonNamingPolicy.CamelCase"/> — matches the HTTP
///   wire format Register Service has always exposed; a subscriber can
///   byte-compare payloads from any source.</description></item>
///   <item><description><see cref="JavaScriptEncoder.UnsafeRelaxedJsonEscaping"/> —
///   keeps '+' in base64/DateTimeOffset literals un-escaped, so SHA-256 hashes
///   over the UTF-8 bytes are deterministic across runtimes.</description></item>
///   <item><description><see cref="JsonIgnoreCondition.WhenWritingNull"/> — omits
///   null fields so an older serialiser and a newer one agree on the shape of a
///   record that gained optional nullable properties.</description></item>
///   <item><description><c>WriteIndented = false</c> — no whitespace, hash is
///   deterministic to the byte.</description></item>
/// </list>
/// </para>
/// </remarks>
public static class RegisterSerializationOptions
{
    /// <summary>
    /// Canonical JSON options for register-scoped payloads. Immutable after first
    /// access; do not mutate — construct a fresh <see cref="JsonSerializerOptions"/>
    /// with <c>new(Canonical)</c> if you need a variant.
    /// </summary>
    public static readonly JsonSerializerOptions Canonical = BuildCanonical();

    private static JsonSerializerOptions BuildCanonical()
    {
        // Intentionally not calling MakeReadOnly() — doing so requires a
        // TypeInfoResolver to be set first, and forcing one here would leak
        // source-generation concerns into the options class. Callers MUST NOT
        // mutate this instance; clone with `new(Canonical)` if you need a variant.
        return new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }
}
