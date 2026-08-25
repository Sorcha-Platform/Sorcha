// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;

namespace Sorcha.Blueprint.Models.Canonical;

/// <summary>
/// The identity of a published blueprint definition (Feature 195) — the id of the transaction that
/// published it.
/// </summary>
/// <remarks>
/// <para>
/// <c>publicationTxId = hex(SHA-256("sorcha:blueprint-publication:v1" ␟ registerId ␟ blueprintId ␟
/// canonicalDefinitionJson))</c>, where ␟ is the ASCII unit separator <c>0x1F</c>.
/// </para>
/// <para>
/// <b>ONE PRODUCER.</b> Only <c>Sorcha.Register.Service</c> may call <see cref="Compute"/> to mint an
/// id. Everything else <i>reads</i> the value: the Blueprint Service records what the publish call
/// returns, recovery reads real transaction ids, instance creation reads the published store, and a
/// starting action reads the instance's pin. This type lives in a shared leaf only so the golden
/// vector can reach it; the restriction is enforced by <c>scripts/check-publication-id-owner.ps1</c>.
/// Recovery is the one other legitimate caller, and it <i>verifies</i> rather than mints — it
/// recomputes the id from the bytes it received and compares to the transaction's own id.
/// </para>
/// <para>
/// <b>Why this replaced a formula with four homes.</b> The previous publish id was
/// <c>SHA-256("blueprint-publish-{registerId}-{blueprintId}")</c>, computed in the Register Service,
/// twice in the Blueprint Service, and hand-rewritten a fifth time in a test that existed to guard
/// it. All four existed only because the published-blueprint store never recorded the transaction id
/// it was published as. It was also version-blind, so every republish deduped to one transaction and
/// was silently dropped (issue #1563).
/// </para>
/// <para>
/// <b>Each field of the preimage is load-bearing:</b>
/// </para>
/// <list type="bullet">
/// <item><description><b>Domain tag</b> — <c>InstanceIdentity.Derive</c> is already
/// <c>SHA-256(registerId ␟ blueprintId ␟ startingActionTxHash)</c>. Untagged, a publication id would
/// be the same preimage construction sharing its first two fields: two kinds of identity that are
/// indistinguishable by shape.</description></item>
/// <item><description><b>Register</b> — a definition published to two registers is byte-identical by
/// construction (same template, same model, same serializer). Without it one id would name two ledger
/// facts, and every <c>(registerId, txId)</c> lookup, receipt and inclusion proof would be
/// ambiguous.</description></item>
/// <item><description><b>Blueprint</b> — binds the identity to the blueprint even if a future
/// canonical form were to normalise or omit the <c>id</c> property.</description></item>
/// <item><description><b>Canonical content</b> — makes the id a content address, so verification is
/// self-anchoring: a tampered payload cannot match its own transaction id.</description></item>
/// </list>
/// <para>
/// <b>The <c>v1</c> in the tag is deliberate.</b> Any change to this construction, or to
/// <see cref="BlueprintCanonicalJson"/>'s rules, re-identifies every definition on every register.
/// Such a change takes the tag to <c>v2</c> and is a migration, never a tidy-up.
/// </para>
/// </remarks>
public static class BlueprintPublicationId
{
    /// <summary>
    /// The domain-separation tag. Bump the version suffix only in a change that deliberately
    /// re-identifies every published definition.
    /// </summary>
    public const string DomainTag = "sorcha:blueprint-publication:v1";

    private const byte FieldSeparator = 0x1F; // ASCII Unit Separator

    /// <summary>
    /// Computes a definition's publication id from its <b>already canonical</b> JSON.
    /// </summary>
    /// <param name="registerId">The register the definition is published to.</param>
    /// <param name="blueprintId">The blueprint this is a definition of.</param>
    /// <param name="canonicalDefinitionJson">
    /// The definition in canonical form — see <see cref="BlueprintCanonicalJson.Canonicalise"/>.
    /// Passing non-canonical JSON yields a stable but meaningless id, which is why
    /// <see cref="ComputeFromDefinition"/> exists and should be preferred at call sites holding raw
    /// JSON.
    /// </param>
    /// <returns>Lowercase-hex SHA-256 of the domain-separated preimage.</returns>
    /// <exception cref="ArgumentException">Any input is null, empty or whitespace.</exception>
    public static string Compute(string registerId, string blueprintId, string canonicalDefinitionJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(registerId);
        ArgumentException.ThrowIfNullOrWhiteSpace(blueprintId);
        ArgumentException.ThrowIfNullOrWhiteSpace(canonicalDefinitionJson);

        using var buffer = new MemoryStream();
        WriteUtf8(buffer, DomainTag);
        buffer.WriteByte(FieldSeparator);
        WriteUtf8(buffer, registerId);
        buffer.WriteByte(FieldSeparator);
        WriteUtf8(buffer, blueprintId);
        buffer.WriteByte(FieldSeparator);
        WriteUtf8(buffer, canonicalDefinitionJson);

        return Convert.ToHexStringLower(SHA256.HashData(buffer.ToArray()));
    }

    /// <summary>
    /// Canonicalises <paramref name="definitionJson"/> and computes its publication id. The preferred
    /// entry point wherever raw definition JSON is held.
    /// </summary>
    /// <exception cref="System.Text.Json.JsonException">The definition is not valid JSON.</exception>
    /// <exception cref="InvalidOperationException">The definition carries a duplicate object key.</exception>
    public static string ComputeFromDefinition(string registerId, string blueprintId, string definitionJson)
        => Compute(registerId, blueprintId, BlueprintCanonicalJson.Canonicalise(definitionJson));

    private static void WriteUtf8(Stream target, string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        target.Write(bytes, 0, bytes.Length);
    }
}
