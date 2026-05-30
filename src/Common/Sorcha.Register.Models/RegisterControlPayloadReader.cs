// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.Register.Models;

/// <summary>
/// Shared reader for a serialised register control payload.
/// </summary>
/// <remarks>
/// <para>
/// PR #868 changed the writer side (<c>RegisterCreationOrchestrator.CreateGenesisTransaction</c>
/// and the non-genesis governance-commit path) to wrap <see cref="RegisterControlRecord"/> in
/// a <see cref="ControlTransactionPayload"/> envelope <c>{ version, roster, operation }</c>.
/// PR #870 (relationship reader) and PR #871 (roster-projection reader) added dual-shape
/// support on two consumers. This helper centralises the same rule for every remaining reader
/// so future writer drift (or pre-#868 bytes still on disk after an upgrade-in-place) can't
/// silently produce a default-empty record.
/// </para>
/// <para>
/// The rule: probe the wrapper shape first (returning <c>wrapper.Roster</c> when the embedded
/// record carries at least one attestation or a non-null validator roster). Fall back to the
/// bare shape and return it under the same emptiness guard. A genuinely empty record — no
/// attestations, no validator roster — is indistinguishable from a default-constructed
/// deserialisation; treat it as "not a real control payload" and return null so the caller
/// keeps scanning rather than acting on phantom roster data.
/// </para>
/// </remarks>
public static class RegisterControlPayloadReader
{
    /// <summary>
    /// Attempts to read a populated <see cref="RegisterControlRecord"/> from a serialised
    /// payload that may be in either the wrapper or bare shape.
    /// </summary>
    /// <param name="payloadJson">The UTF-8 JSON bytes of the payload.</param>
    /// <returns>
    /// The decoded record when the payload carries at least one attestation or a non-null
    /// validator roster; <c>null</c> when the payload is not a real control payload (either
    /// JSON is malformed or the deserialised record is default-empty).
    /// </returns>
    public static RegisterControlRecord? TryRead(ReadOnlySpan<byte> payloadJson)
    {
        if (payloadJson.IsEmpty) return null;

        try
        {
            // Wrapper shape ({ version, roster, operation }) first — the post-#868 default.
            var wrapper = JsonSerializer.Deserialize<ControlTransactionPayload>(payloadJson);
            if (IsPopulated(wrapper?.Roster)) return wrapper!.Roster;
        }
        catch (JsonException)
        {
            // fall through to bare-shape probe
        }

        try
        {
            var bare = JsonSerializer.Deserialize<RegisterControlRecord>(payloadJson);
            if (IsPopulated(bare)) return bare;
        }
        catch (JsonException)
        {
            // fall through to null
        }

        return null;
    }

    /// <summary>
    /// Convenience overload accepting the UTF-8 JSON as a string.
    /// </summary>
    public static RegisterControlRecord? TryRead(string payloadJson) =>
        TryRead(System.Text.Encoding.UTF8.GetBytes(payloadJson));

    private static bool IsPopulated(RegisterControlRecord? record)
    {
        if (record is null) return false;
        // A real control record carries at least one of: an attestation, a validator roster
        // entry, a CryptoPolicy, or a Name. The fourth (Name) catches genuine genesis records
        // that pre-date the orchestrator wrapping but still carry meaningful content; the
        // earlier three catch every wrapper-shape governance commit. A truly-default
        // deserialisation (no name, no attestations, no validators, no policy) is treated as
        // "not a real control payload" — that's the silent-failure mode this helper exists
        // to prevent.
        if (!string.IsNullOrWhiteSpace(record.Name)) return true;
        if (record.Attestations is { Count: > 0 }) return true;
        if (record.Validators?.Validators is { Count: > 0 }) return true;
        if (record.CryptoPolicy is not null) return true;
        return false;
    }
}
