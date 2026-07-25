// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.Blueprint.Engine.Schemas;

/// <summary>
/// One <c>x-claim-source</c> binding discovered on an action's form schema: a payload property whose
/// value is supplied from a named identity claim rather than typed by the user.
/// </summary>
/// <param name="PropertyName">Top-level payload property to write.</param>
/// <param name="ClaimName">Identity claim name to resolve, in the JWT claim vocabulary.</param>
/// <param name="DeclaredType">The property's declared JSON Schema <c>type</c>, if any.</param>
public readonly record struct ClaimSourceBinding(string PropertyName, string ClaimName, string? DeclaredType);

/// <summary>
/// Discovers and coerces schema-declared <c>x-claim-source</c> bindings (Feature 183 US1).
/// </summary>
/// <remarks>
/// <para>
/// <b>These values are resolved server-side, at submission.</b> The original implementation seeded
/// them client-side from the browser's JWT, which made the value only ever as fresh as the token the
/// client happened to hold. Issue #1264 is what that cost: a citizen's token was minted at signup
/// carrying <c>email_verified: false</c>, they verified nine minutes later, and the application they
/// submitted five minutes after that was auto-rejected on the stale value. Verifying updates server
/// state but cannot rewrite an issued token, and nothing re-mints it.
/// </para>
/// <para>
/// Resolving at submission kills the whole staleness class rather than one instance of it, and — since
/// the server overwrites whatever the client sent — it simultaneously removes the client's ability to
/// assert its own value for a field the platform is supposed to vouch for. Both properties matter:
/// the field gates an identity decision.
/// </para>
/// <para>
/// This type is deliberately pure (no I/O) so the discovery and coercion rules are unit-testable and
/// live in exactly one place. Live claim values come from
/// <c>Sorcha.ServiceClients.PlatformUserClaims.IPlatformUserClaimsClient</c>.
/// </para>
/// </remarks>
public static class ClaimSourceBindings
{
    /// <summary>The JSON Schema property extension keyword that declares a binding.</summary>
    public const string Keyword = "x-claim-source";

    /// <summary>
    /// Walks every supplied schema's top-level <c>properties</c> for <see cref="Keyword"/> bindings.
    /// </summary>
    /// <remarks>
    /// An action carries one schema per form page, so all of them are walked. A property is reported
    /// once even if it appears on several pages (first declaration wins) — a duplicate would otherwise
    /// resolve twice and the last write would silently decide the value.
    /// <para>
    /// Top-level properties only, matching the Feature 183 contract (nested bindings are a documented
    /// YAGNI). Malformed declarations are skipped rather than throwing: a blueprint author's typo must
    /// not take down submission, and a skipped binding fails closed downstream.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<ClaimSourceBinding> Discover(IEnumerable<JsonDocument>? schemas)
    {
        if (schemas is null)
        {
            return [];
        }

        var bindings = new List<ClaimSourceBinding>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var schema in schemas)
        {
            if (schema is null)
            {
                continue;
            }

            var root = schema.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("properties", out var properties) ||
                properties.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var property in properties.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.Object ||
                    !property.Value.TryGetProperty(Keyword, out var claimNameElement) ||
                    claimNameElement.ValueKind != JsonValueKind.String)
                {
                    continue;
                }

                var claimName = claimNameElement.GetString();
                if (string.IsNullOrEmpty(claimName) || !seen.Add(property.Name))
                {
                    continue;
                }

                var declaredType = property.Value.TryGetProperty("type", out var typeElement)
                                   && typeElement.ValueKind == JsonValueKind.String
                    ? typeElement.GetString()
                    : null;

                bindings.Add(new ClaimSourceBinding(property.Name, claimName, declaredType));
            }
        }

        return bindings;
    }

    /// <summary>The distinct claim names a binding set needs resolved — the batch-read request.</summary>
    public static IReadOnlyCollection<string> ClaimNames(IReadOnlyList<ClaimSourceBinding> bindings)
        => bindings.Select(b => b.ClaimName).Distinct(StringComparer.Ordinal).ToArray();

    /// <summary>
    /// Coerces a resolved claim value to the binding's declared JSON type.
    /// </summary>
    /// <param name="binding">The binding being satisfied.</param>
    /// <param name="claimValue">The live claim value, or <see langword="null"/> if unresolved.</param>
    /// <returns>
    /// The value to write into the payload, or <see langword="null"/> to indicate the property should
    /// be <b>removed</b> from the payload.
    /// </returns>
    /// <remarks>
    /// <b>Boolean bindings fail closed:</b> only a literal "true" is <see langword="true"/>; absent,
    /// unparseable, or anything else is <see langword="false"/>. A boolean binding therefore never
    /// removes the property — an identity gate must resolve to a definite deny, not to an absent field
    /// whose meaning depends on how the consumer treats missing data.
    /// <para>
    /// Non-boolean bindings with no resolved value are removed, so a stale or client-asserted value
    /// cannot survive in place of one the server declined to vouch for.
    /// </para>
    /// </remarks>
    public static object? Coerce(ClaimSourceBinding binding, string? claimValue)
    {
        if (string.Equals(binding.DeclaredType, "boolean", StringComparison.Ordinal))
        {
            return string.Equals(claimValue, "true", StringComparison.OrdinalIgnoreCase);
        }

        return claimValue;
    }
}
