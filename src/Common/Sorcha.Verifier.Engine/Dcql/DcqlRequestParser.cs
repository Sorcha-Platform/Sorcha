// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.Verifier.Engine.Dcql;

/// <summary>
/// THE parser of <c>dcql_query</c> bodies for every Sorcha wallet/verifier surface (wallet
/// PWA presentation engine, direct_post verification, desk verifier). Lives beside
/// <see cref="DcqlRequestBuilder"/> so build and parse sides cannot drift (FR-008).
/// </summary>
public static class DcqlRequestParser
{
    /// <summary>
    /// Parse and validate a <c>dcql_query</c> JSON body. The retired Presentation Exchange
    /// shape (<c>input_descriptors</c>) is refused with code
    /// <see cref="DcqlErrorCodes.LegacyDialect"/> (FR-007); structural failures with
    /// <see cref="DcqlErrorCodes.Invalid"/>.
    /// </summary>
    public static DcqlQuery Parse(string dcqlQueryJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(dcqlQueryJson);

        using var doc = ParseDocument(dcqlQueryJson);
        RejectLegacyShapes(doc.RootElement);

        DcqlQuery? query;
        try
        {
            query = doc.RootElement.Deserialize<DcqlQuery>(DcqlJson.Options);
        }
        catch (JsonException ex)
        {
            throw new DcqlParseException(DcqlErrorCodes.Invalid, $"dcql_query does not deserialize: {ex.Message}");
        }

        if (query is null)
        {
            throw new DcqlParseException(DcqlErrorCodes.Invalid, "dcql_query is null.");
        }

        var errors = query.Validate();
        if (errors.Count > 0)
        {
            throw new DcqlParseException(DcqlErrorCodes.Invalid, string.Join(" ", errors));
        }

        return query;
    }

    /// <summary>
    /// Parse the query out of a full Request Object payload (the decoded JWT claims JSON).
    /// A payload carrying <c>presentation_definition</c> is the retired dialect (FR-007).
    /// </summary>
    public static DcqlQuery ParseFromRequestObjectPayload(JsonElement payload)
    {
        if (payload.TryGetProperty("presentation_definition", out _))
        {
            throw new DcqlParseException(
                DcqlErrorCodes.LegacyDialect,
                "Request object carries presentation_definition — the retired Presentation Exchange dialect. Expected dcql_query (OpenID4VP 1.0).");
        }

        if (!payload.TryGetProperty("dcql_query", out var dcql))
        {
            throw new DcqlParseException(DcqlErrorCodes.Invalid, "Request object carries no dcql_query.");
        }

        return Parse(dcql.GetRawText());
    }

    /// <summary>
    /// Path segments → the platform claim-name convention: single segment is the plain name;
    /// nested segments render as a slash path. Mirror of <see cref="DcqlRequestBuilder.ToPath"/>.
    /// </summary>
    public static string FromPath(IReadOnlyList<string> path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return path.Count switch
        {
            0 => throw new DcqlParseException(DcqlErrorCodes.Invalid, "Claim path must be non-empty."),
            1 => path[0],
            _ => "/" + string.Join('/', path),
        };
    }

    /// <summary>
    /// Recover the required/optional claim-name split the builder encoded (research R2): with
    /// claim_sets, the first option is the full ask and the last is the required-only floor;
    /// without, every listed claim is required.
    /// </summary>
    public static (IReadOnlyList<string> Required, IReadOnlyList<string> Optional) SplitClaims(DcqlCredentialQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (query.Claims is null || query.Claims.Count == 0)
        {
            return ([], []);
        }

        if (query.ClaimSets is null || query.ClaimSets.Count == 0)
        {
            return (query.Claims.Select(c => FromPath(c.Path)).ToList(), []);
        }

        var byId = query.Claims.Where(c => c.Id is not null).ToDictionary(c => c.Id!, StringComparer.Ordinal);
        var floor = new HashSet<string>(query.ClaimSets[^1], StringComparer.Ordinal);

        var required = new List<string>();
        var optional = new List<string>();
        foreach (var claim in query.Claims)
        {
            var name = FromPath(claim.Path);
            if (claim.Id is not null && byId.ContainsKey(claim.Id) && !floor.Contains(claim.Id))
            {
                optional.Add(name);
            }
            else
            {
                required.Add(name);
            }
        }

        return (required, optional);
    }

    private static JsonDocument ParseDocument(string json)
    {
        try
        {
            return JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new DcqlParseException(DcqlErrorCodes.Invalid, $"dcql_query is not valid JSON: {ex.Message}");
        }
    }

    private static void RejectLegacyShapes(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new DcqlParseException(DcqlErrorCodes.Invalid, "dcql_query must be a JSON object.");
        }

        if (root.TryGetProperty("input_descriptors", out _) || root.TryGetProperty("presentation_definition", out _))
        {
            throw new DcqlParseException(
                DcqlErrorCodes.LegacyDialect,
                "Presentation Exchange shape received — the retired pre-DCQL dialect. Expected OpenID4VP 1.0 dcql_query.");
        }
    }
}
