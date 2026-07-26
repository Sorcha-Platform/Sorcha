// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sorcha.Blueprint.Models;

/// <summary>
/// Represents the <c>x-holder-key</c> extension on a <c>format: "sorcha-holder-key"</c> property.
/// Declares whether the citizen's carried delivery keys MUST be captured before the form may be
/// submitted.
/// </summary>
/// <remarks>
/// <para>
/// Issue #1302. The keyword shipped in <c>aias-assured-identity</c> and <c>assured-identity</c> as
/// <c>{ "required": true }</c> and, until now, nothing read it — the F137 contract introduced it as
/// "optional config" and never gave it a consumer. It was not decoration: the declaration states a
/// load-bearing fact.
/// </para>
/// <para>
/// In those blueprints BOTH participants are open (<c>walletAddress: null</c>), so a walk-in citizen
/// has no published participant record and the issuer has nowhere to look their keys up. The citizen
/// must carry their own public keys in the starting action's payload; the issuing action reads them
/// back via <c>holderKeySourceField</c> to bind the credential (SD-JWT <c>cnf</c>, FR-014) and wrap
/// the delivery envelope (FR-012).
/// </para>
/// <para>
/// <b>Why the schema's own <c>required</c> array cannot express this.</b>
/// <c>FormSchemaService.ValidateDataRecursive</c> deliberately skips object-typed entries in a
/// parent <c>required</c> array — it delegates to the child's own <c>properties</c>/<c>required</c>
/// so the errors land on precise leaves. A <c>sorcha-holder-key</c> field declares neither (its
/// value is written by the renderer, not authored), so listing it in <c>required</c> is silently a
/// no-op. This keyword is how the requirement becomes expressible.
/// </para>
/// <para>
/// <b>Client-side gate, not the guarantee.</b> Honouring this blocks submission early with an
/// actionable message. The real guarantee remains server-side: issuance fails closed with
/// <c>VAL_RUNTIME_CRED_004</c>/<c>_005</c> if the keys cannot be resolved. This exists so the
/// citizen finds out at their own form rather than the analyst discovering it when an approval they
/// have already made throws.
/// </para>
/// </remarks>
public sealed class HolderKeySchemaExtension
{
    /// <summary>
    /// When <see langword="true"/>, the carried key leaves must be present before the form may be
    /// submitted. Defaults to <see langword="false"/> — the keyword is opt-in, so a bare
    /// <c>x-holder-key: {}</c> does not retroactively block blueprints that never asked for it.
    /// </summary>
    [JsonPropertyName("required")]
    public bool Required { get; init; }

    /// <summary>
    /// The sibling pointers a <c>sorcha-holder-key</c> renderer writes beneath the field, and which
    /// <c>ActionExecutionService.ResolveCarriedHolderKeys</c> reads back at issuance. Enforcing
    /// fewer than all three would admit a submission that still cannot be issued against.
    /// </summary>
    public static IReadOnlyList<string> CarriedLeafNames { get; } =
        ["holderJwk", "encryptionPublicKey", "algorithm"];

    /// <summary>
    /// Attempts to read the <c>x-holder-key</c> extension from a JSON Schema element. Returns
    /// <see langword="false"/> when absent or malformed — a broken declaration must not block a
    /// citizen's submission, since the server-side fail-closed still protects correctness.
    /// </summary>
    /// <summary>
    /// The keywords this extension is declared under. <c>x-holder-key</c> sits on a
    /// <c>sorcha-holder-key</c> field (the citizen's wallet keys); <c>x-device-key</c> on a
    /// <c>sorcha-device-key</c> field (this device's key, #1195 Phase 2). They are the same idea and
    /// both renderers write the same three leaves, so both are honoured here — which is why neither
    /// shipped blueprint needs editing.
    /// </summary>
    public static IReadOnlyList<string> Keywords { get; } = ["x-holder-key", "x-device-key"];

    public static bool TryParseFromSchema(JsonElement schema, out HolderKeySchemaExtension? extension)
    {
        extension = null;

        if (schema.ValueKind != JsonValueKind.Object) return false;

        var found = false;
        var required = false;

        foreach (var keyword in Keywords)
        {
            if (!schema.TryGetProperty(keyword, out var element)) continue;
            if (element.ValueKind != JsonValueKind.Object) continue;

            try
            {
                var parsed = JsonSerializer.Deserialize<HolderKeySchemaExtension>(element.GetRawText());
                if (parsed is null) continue;

                found = true;
                // A field declaring both is a copy-paste between the two templates. The safe
                // reading of a contradiction is the stricter one.
                required |= parsed.Required;
            }
            catch (JsonException)
            {
                // Malformed declaration — ignore this keyword rather than blocking a citizen's
                // submission on it. The server-side fail-closed still protects correctness.
            }
        }

        if (!found) return false;

        extension = new HolderKeySchemaExtension { Required = required };
        return true;
    }
}
