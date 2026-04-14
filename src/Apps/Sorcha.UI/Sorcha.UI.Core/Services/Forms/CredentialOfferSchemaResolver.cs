// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sorcha.UI.Core.Services.Forms;

/// <summary>
/// Detects the <c>x-credential-offer</c> schema extension on a blueprint action's
/// data schema and extracts the seeded credential offer payload. Feature 104 wave 14b.
/// </summary>
/// <remarks>
/// The claim-action pattern in Feature 104 is:
/// <list type="number">
///   <item>A previous action's <c>Route.OutputMapping</c> seeds the claim action's
///     prepopulated payload with a <c>credentialOffer</c> object.</item>
///   <item>The pending-actions listing surfaces that object on
///     <see cref="Sorcha.UI.Core.Models.Workflows.PendingActionViewModel.PrepopulatedPayload"/>.</item>
///   <item>This resolver inspects the action's schema for a property marked
///     <c>x-credential-offer: true</c> and, if found, extracts the corresponding
///     object from the prepopulated payload so the UI can render the claim card.</item>
/// </list>
/// Returns <c>null</c> when the action is not a claim action, is not seeded, or
/// the seeded payload is missing the offer URI — the caller falls back to the
/// default form renderer in all three cases.
/// </remarks>
public static class CredentialOfferSchemaResolver
{
    /// <summary>
    /// Inspects the action's data schema and returns a <see cref="CredentialOfferInfo"/>
    /// when the action carries an <c>x-credential-offer: true</c> object field that
    /// is populated in the prepopulated payload. Returns <c>null</c> otherwise.
    /// </summary>
    public static CredentialOfferInfo? TryResolve(JsonElement? dataSchema, JsonObject? prepopulatedPayload)
    {
        if (dataSchema is null || !dataSchema.HasValue) return null;
        if (prepopulatedPayload is null || prepopulatedPayload.Count == 0) return null;

        var schemaRoot = dataSchema.Value;
        if (schemaRoot.ValueKind != JsonValueKind.Object) return null;
        if (!schemaRoot.TryGetProperty("properties", out var props)) return null;
        if (props.ValueKind != JsonValueKind.Object) return null;

        foreach (var prop in props.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.Object) continue;

            if (!prop.Value.TryGetProperty("x-credential-offer", out var marker)) continue;
            if (marker.ValueKind != JsonValueKind.True) continue;

            // Found a claim field. Must be object-typed per VAL_BP_012.
            var isObject = prop.Value.TryGetProperty("type", out var typeEl) &&
                           typeEl.ValueKind == JsonValueKind.String &&
                           typeEl.GetString() == "object";
            if (!isObject) continue;

            // Look up the seeded value in the prepopulated payload.
            if (!prepopulatedPayload.TryGetPropertyValue(prop.Name, out var seededNode)) continue;
            if (seededNode is not JsonObject seededObj) continue;

            var offerUri = seededObj["credential_offer_uri"]?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(offerUri))
            {
                // FR-022 prerequisite — no point rendering the card without a URI.
                // Fall through and let the caller degrade to the default renderer,
                // which will fail validation at submit time per VAL_BP_011 rules.
                continue;
            }

            var credentialType = seededObj["credential_type"]?.GetValue<string>();
            var offerId = seededObj["offer_id"]?.GetValue<string>();
            DateTimeOffset? expiresAt = null;
            var expiresAtRaw = seededObj["expires_at"]?.GetValue<string>();
            if (!string.IsNullOrWhiteSpace(expiresAtRaw) &&
                DateTimeOffset.TryParse(expiresAtRaw, out var parsed))
            {
                expiresAt = parsed;
            }

            return new CredentialOfferInfo(
                FieldName: prop.Name,
                CredentialOfferUri: offerUri!,
                CredentialType: credentialType,
                OfferId: offerId,
                ExpiresAt: expiresAt,
                RawCredentialOffer: seededObj);
        }

        return null;
    }
}

/// <summary>
/// Parsed credential offer extracted from a claim action's prepopulated payload.
/// Feature 104 wave 14b.
/// </summary>
/// <param name="FieldName">
/// Name of the schema property that carries <c>x-credential-offer</c>. The claim
/// card uses this when assembling the confirmation payload on submit so it sits
/// under the same key the engine validated.
/// </param>
/// <param name="CredentialOfferUri">OpenID4VCI offer URI (required).</param>
/// <param name="CredentialType">Credential type advertised by the issuer, when known.</param>
/// <param name="OfferId">HAIP offer identifier for status polling, when known.</param>
/// <param name="ExpiresAt">Parsed expiry timestamp, when the payload carried one.</param>
/// <param name="RawCredentialOffer">
/// The full credentialOffer object from the seed. The claim card echoes this
/// back in the submission payload so the engine-side schema validation passes
/// and the audit trail records the offer as claimed.
/// </param>
public record CredentialOfferInfo(
    string FieldName,
    string CredentialOfferUri,
    string? CredentialType,
    string? OfferId,
    DateTimeOffset? ExpiresAt,
    JsonObject RawCredentialOffer);
