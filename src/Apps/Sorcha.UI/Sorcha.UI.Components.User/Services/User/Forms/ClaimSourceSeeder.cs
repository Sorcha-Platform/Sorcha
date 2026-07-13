// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;
using System.Text.Json;

namespace Sorcha.UI.Core.Services.Forms;

/// <summary>
/// Resolves schema-declared claim-source bindings (Feature 183). A property
/// carrying an <c>x-claim-source</c> extension is seeded, at form init, from
/// the named JWT claim on the authenticated principal — so a read-only,
/// page-less field (e.g. the AIAS <c>emailVerified</c> signal) is still carried
/// onto the wallet-signed submission.
/// </summary>
public interface IClaimSourceSeeder
{
    /// <summary>
    /// Walks the top-level properties of <paramref name="mergedSchema"/> for the
    /// <c>x-claim-source</c> extension, reads the named claim from
    /// <paramref name="user"/>, coerces it to the property's declared JSON type,
    /// and returns leading-slash JSON-Pointer → value entries to seed into the
    /// form data bag. Boolean bindings FAIL CLOSED (absent / unparseable → false).
    /// </summary>
    IReadOnlyDictionary<string, object?> Resolve(JsonDocument? mergedSchema, ClaimsPrincipal? user);
}

/// <inheritdoc />
public sealed class ClaimSourceSeeder : IClaimSourceSeeder
{
    private const string ClaimSourceKeyword = "x-claim-source";

    /// <inheritdoc />
    public IReadOnlyDictionary<string, object?> Resolve(JsonDocument? mergedSchema, ClaimsPrincipal? user)
    {
        var result = new Dictionary<string, object?>();
        if (mergedSchema is null || user is null)
            return result;

        var root = mergedSchema.RootElement;
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("properties", out var properties) ||
            properties.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        foreach (var property in properties.EnumerateObject())
        {
            var propertySchema = property.Value;
            if (propertySchema.ValueKind != JsonValueKind.Object)
                continue;

            if (!propertySchema.TryGetProperty(ClaimSourceKeyword, out var claimNameElement) ||
                claimNameElement.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var claimName = claimNameElement.GetString();
            if (string.IsNullOrEmpty(claimName))
                continue;

            var declaredType = propertySchema.TryGetProperty("type", out var typeElement) &&
                               typeElement.ValueKind == JsonValueKind.String
                ? typeElement.GetString()
                : null;

            var pointer = "/" + property.Name;
            var claimValue = user.FindFirst(claimName)?.Value;

            if (declaredType == "boolean")
            {
                // Fail closed: only an explicit "true" (any case) is verified.
                // Absent / unparseable / anything-else → false.
                result[pointer] = string.Equals(claimValue, "true", StringComparison.OrdinalIgnoreCase);
            }
            else if (claimValue is not null)
            {
                // Non-boolean bindings seed the raw claim string, and only when present.
                result[pointer] = claimValue;
            }
        }

        return result;
    }
}
