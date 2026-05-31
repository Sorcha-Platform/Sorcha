// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;

namespace Sorcha.Blueprint.Service.Services.Implementation;

/// <summary>
/// Deterministic, ledger-anchored workflow instance identity (Feature 145, data-model Entity 4).
/// An instance is born when its starting action seals; its id is derived purely from on-ledger
/// facts so every node computes the same id for the same workflow — with no node-local GUID and
/// no mirror.
/// </summary>
/// <remarks>
/// <c>instanceId = encode( SHA256( registerId || 0x1F || blueprintId || 0x1F || startingActionTxHash ) )</c>.
/// The unit-separator (<c>0x1F</c>) byte between fields removes concatenation ambiguity (so that
/// e.g. <c>("ab","c")</c> and <c>("a","bc")</c> cannot collide). The output is lowercase hex —
/// stable, node-independent, and safe in URLs / store keys. Stability under open-participant
/// late-binding is automatic: the id derives from the sealed starting-action tx hash, regardless
/// of who is late-bound.
/// </remarks>
public static class InstanceIdentity
{
    private const byte FieldSeparator = 0x1F; // ASCII Unit Separator

    /// <summary>
    /// Derives the canonical instance id from the three on-ledger facts.
    /// </summary>
    /// <param name="registerId">The register the starting action sealed on.</param>
    /// <param name="blueprintId">The blueprint the instance executes.</param>
    /// <param name="startingActionTxHash">The hash of the sealed starting-action transaction.</param>
    /// <returns>Lowercase-hex SHA-256 over the separated, UTF-8-encoded inputs.</returns>
    /// <exception cref="ArgumentException">If any input is null or whitespace.</exception>
    public static string Derive(string registerId, string blueprintId, string startingActionTxHash)
    {
        if (string.IsNullOrWhiteSpace(registerId))
            throw new ArgumentException("registerId is required", nameof(registerId));
        if (string.IsNullOrWhiteSpace(blueprintId))
            throw new ArgumentException("blueprintId is required", nameof(blueprintId));
        if (string.IsNullOrWhiteSpace(startingActionTxHash))
            throw new ArgumentException("startingActionTxHash is required", nameof(startingActionTxHash));

        using var buffer = new MemoryStream();
        WriteUtf8(buffer, registerId);
        buffer.WriteByte(FieldSeparator);
        WriteUtf8(buffer, blueprintId);
        buffer.WriteByte(FieldSeparator);
        WriteUtf8(buffer, startingActionTxHash);

        var hash = SHA256.HashData(buffer.ToArray());
        return Convert.ToHexStringLower(hash);
    }

    private static void WriteUtf8(Stream target, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        target.Write(bytes, 0, bytes.Length);
    }
}
