// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;

namespace Sorcha.Verifier.Engine.Dcql;

/// <summary>
/// Caller-facing description of one credential ask, in the required/optional vocabulary the
/// platform's blueprint <c>credentialRequirements</c> and verifier APIs already speak. The
/// builder owns the mapping onto DCQL <c>claims</c> + <c>claim_sets</c> (research R2) so no
/// caller hand-rolls the wire shape (FR-008).
/// </summary>
/// <param name="Id">Credential query id (alphanumeric/underscore/hyphen; unique per request).</param>
/// <param name="Format">A <see cref="DcqlFormats"/> value.</param>
/// <param name="VctValues">Accepted vct URIs (required for <see cref="DcqlFormats.SdJwtVc"/>).</param>
/// <param name="DoctypeValue">Required doctype (required for <see cref="DcqlFormats.MsoMdoc"/>).</param>
/// <param name="RequiredClaims">Claim names — plain name ("givenName") or slash path ("/address/street").</param>
/// <param name="OptionalClaims">Claims the holder may withhold.</param>
public sealed record DcqlCredentialAsk(
    string Id,
    string Format,
    IReadOnlyList<string>? VctValues,
    string? DoctypeValue,
    IReadOnlyList<string> RequiredClaims,
    IReadOnlyList<string>? OptionalClaims = null)
{
    /// <summary>Convenience factory for the common single-vct SD-JWT ask.</summary>
    public static DcqlCredentialAsk SdJwt(string id, string vct, IReadOnlyList<string> requiredClaims, IReadOnlyList<string>? optionalClaims = null)
        => new(id, DcqlFormats.SdJwtVc, [vct], null, requiredClaims, optionalClaims);
}

/// <summary>An alternative group — "any one of these id-combinations satisfies the request".</summary>
/// <param name="Options">Preference-ordered id-combinations.</param>
/// <param name="Required">Whether the set must be satisfied (default true).</param>
/// <param name="Purpose">Consent-surface display text.</param>
public sealed record DcqlAlternativeGroup(
    IReadOnlyList<IReadOnlyList<string>> Options,
    bool Required = true,
    string? Purpose = null);

/// <summary>
/// THE producer of <c>dcql_query</c> bodies for every Sorcha verifier surface (HAIP verifier
/// API, Blueprint presentation consumer, desk/open verifier). Lives beside
/// <see cref="DcqlRequestParser"/> so build and parse sides cannot drift (FR-008).
/// </summary>
public static class DcqlRequestBuilder
{
    /// <summary>
    /// Build a <see cref="DcqlQuery"/> from asks + optional alternative groups. When
    /// <paramref name="purpose"/> is supplied and no explicit alternatives exist, a single
    /// all-asks credential_set carries it (the consent-display idiom from contract §1).
    /// Throws <see cref="DcqlParseException"/> (code <see cref="DcqlErrorCodes.Invalid"/>)
    /// when the resulting query fails structural validation.
    /// </summary>
    public static DcqlQuery Build(
        IReadOnlyList<DcqlCredentialAsk> asks,
        IReadOnlyList<DcqlAlternativeGroup>? alternatives = null,
        string? purpose = null)
    {
        ArgumentNullException.ThrowIfNull(asks);
        if (asks.Count == 0)
        {
            throw new DcqlParseException(DcqlErrorCodes.Invalid, "At least one credential ask is required.");
        }

        var credentials = asks.Select(BuildCredentialQuery).ToList();

        IReadOnlyList<DcqlCredentialSetQuery>? sets = null;
        if (alternatives is { Count: > 0 })
        {
            sets = alternatives
                .Select(g => new DcqlCredentialSetQuery { Options = g.Options, Required = g.Required, Purpose = g.Purpose })
                .ToList();
        }
        else if (purpose is not null)
        {
            sets = [new DcqlCredentialSetQuery
            {
                Options = [credentials.Select(c => c.Id).ToList()],
                Required = true,
                Purpose = purpose,
            }];
        }

        var query = new DcqlQuery { Credentials = credentials, CredentialSets = sets };
        var errors = query.Validate();
        if (errors.Count > 0)
        {
            throw new DcqlParseException(DcqlErrorCodes.Invalid, string.Join(" ", errors));
        }

        return query;
    }

    /// <summary>Serialize a query to its wire JSON (for embedding as <c>dcql_query</c>).</summary>
    public static string ToJson(DcqlQuery query)
        => JsonSerializer.Serialize(query, DcqlJson.Options);

    private static DcqlCredentialQuery BuildCredentialQuery(DcqlCredentialAsk ask)
    {
        var required = ask.RequiredClaims ?? [];
        var optional = ask.OptionalClaims ?? [];

        List<DcqlClaimQuery>? claims = null;
        List<IReadOnlyList<string>>? claimSets = null;

        if (required.Count > 0 || optional.Count > 0)
        {
            claims = [];
            var requiredIds = new List<string>();
            var optionalIds = new List<string>();
            var index = 0;

            foreach (var name in required)
            {
                var id = $"c{index++}";
                claims.Add(new DcqlClaimQuery { Id = optional.Count > 0 ? id : null, Path = ToPath(name) });
                requiredIds.Add(id);
            }

            foreach (var name in optional)
            {
                var id = $"c{index++}";
                claims.Add(new DcqlClaimQuery { Id = id, Path = ToPath(name) });
                optionalIds.Add(id);
            }

            // R2 mapping: optionality rides claim_sets — first (preferred) option asks for
            // everything, second accepts required-only. No optional claims ⇒ plain claims list.
            if (optional.Count > 0)
            {
                claimSets =
                [
                    [.. requiredIds, .. optionalIds],
                    [.. requiredIds],
                ];
            }
        }

        return new DcqlCredentialQuery
        {
            Id = ask.Id,
            Format = ask.Format,
            Meta = new DcqlCredentialMeta { VctValues = ask.VctValues, DoctypeValue = ask.DoctypeValue },
            Claims = claims,
            ClaimSets = claimSets,
        };
    }

    /// <summary>
    /// Claim name → path segments: plain name is a single segment; a slash path
    /// ("/address/street") splits into nested segments. Mirror of
    /// <see cref="DcqlRequestParser.FromPath"/>.
    /// </summary>
    internal static IReadOnlyList<string> ToPath(string claimName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(claimName);
        return claimName.StartsWith('/')
            ? claimName.Split('/', StringSplitOptions.RemoveEmptyEntries)
            : [claimName];
    }
}
