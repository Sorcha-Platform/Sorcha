// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Sorcha.Verifier.Engine.Dcql;

/// <summary>
/// DCQL (Digital Credentials Query Language) wire model per OpenID4VP 1.0 final —
/// the request-side vocabulary that replaced Presentation Exchange (Feature 181, D1).
/// Serialized property names are exact spec names; the model is deliberately the
/// v1 subset Sorcha emits and consumes: <c>credentials</c> + <c>credential_sets</c>,
/// claim <c>path</c> selection, no value-matching constraints
/// (<c>claims[].values</c> / <c>trusted_authorities</c> are out of scope — research R2).
/// </summary>
public sealed record DcqlQuery
{
    /// <summary>Credential queries; at least one, ids unique across the request.</summary>
    [JsonPropertyName("credentials")]
    public required IReadOnlyList<DcqlCredentialQuery> Credentials { get; init; }

    /// <summary>
    /// Optional alternative sets — each entry's <c>options</c> lists id-combinations that
    /// satisfy the set. Every referenced id must exist in <see cref="Credentials"/>.
    /// </summary>
    [JsonPropertyName("credential_sets")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DcqlCredentialSetQuery>? CredentialSets { get; init; }

    /// <summary>
    /// Validates structural rules (id syntax + uniqueness, per-format meta, referential
    /// integrity of credential_sets and claim_sets). Returns an empty list when valid.
    /// </summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (Credentials is null || Credentials.Count == 0)
        {
            errors.Add("dcql_query.credentials must contain at least one credential query.");
            return errors;
        }

        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var credential in Credentials)
        {
            credential.Validate(errors);
            if (credential.Id is not null && !seenIds.Add(credential.Id))
            {
                errors.Add($"Duplicate credential query id '{credential.Id}'.");
            }
        }

        if (CredentialSets is not null)
        {
            foreach (var set in CredentialSets)
            {
                if (set.Options is null || set.Options.Count == 0)
                {
                    errors.Add("credential_sets entry must declare at least one option.");
                    continue;
                }

                foreach (var option in set.Options)
                {
                    if (option is null || option.Count == 0)
                    {
                        errors.Add("credential_sets option must reference at least one credential query id.");
                        continue;
                    }

                    foreach (var id in option)
                    {
                        if (!seenIds.Contains(id))
                        {
                            errors.Add($"credential_sets references unknown credential query id '{id}'.");
                        }
                    }
                }
            }
        }

        return errors;
    }
}

/// <summary>One unit of ask inside a <see cref="DcqlQuery"/>.</summary>
public sealed record DcqlCredentialQuery
{
    /// <summary>Spec id charset: alphanumeric, underscore, hyphen.</summary>
    public static readonly Regex IdPattern = new("^[a-zA-Z0-9_-]+$", RegexOptions.Compiled);

    /// <summary>Query identifier — the key the response envelope is keyed by.</summary>
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    /// <summary>Credential format — see <see cref="DcqlFormats"/>.</summary>
    [JsonPropertyName("format")]
    public required string Format { get; init; }

    /// <summary>Format-specific matching metadata (required in v1).</summary>
    [JsonPropertyName("meta")]
    public required DcqlCredentialMeta Meta { get; init; }

    /// <summary>
    /// Claims to disclose. Null means the wallet discloses per its own policy.
    /// </summary>
    [JsonPropertyName("claims")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<DcqlClaimQuery>? Claims { get; init; }

    /// <summary>
    /// Preference-ordered combinations of claim ids that satisfy this query. In v1 this is
    /// only emitted by <see cref="DcqlRequestBuilder"/>'s required/optional mapping (research R2):
    /// first option = required + optional claim ids, second option = required-only.
    /// </summary>
    [JsonPropertyName("claim_sets")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<IReadOnlyList<string>>? ClaimSets { get; init; }

    internal void Validate(List<string> errors)
    {
        if (string.IsNullOrEmpty(Id) || !IdPattern.IsMatch(Id))
        {
            errors.Add($"Credential query id '{Id}' must match {IdPattern}.");
        }

        switch (Format)
        {
            case DcqlFormats.SdJwtVc:
                if (Meta?.VctValues is null || Meta.VctValues.Count == 0)
                {
                    errors.Add($"Credential query '{Id}': format '{DcqlFormats.SdJwtVc}' requires meta.vct_values.");
                }
                break;
            case DcqlFormats.MsoMdoc:
                if (string.IsNullOrEmpty(Meta?.DoctypeValue))
                {
                    errors.Add($"Credential query '{Id}': format '{DcqlFormats.MsoMdoc}' requires meta.doctype_value.");
                }
                break;
            default:
                errors.Add($"Credential query '{Id}': unsupported format '{Format}' (v1 supports '{DcqlFormats.SdJwtVc}', '{DcqlFormats.MsoMdoc}').");
                break;
        }

        var claimIds = new HashSet<string>(StringComparer.Ordinal);
        if (Claims is not null)
        {
            foreach (var claim in Claims)
            {
                if (claim.Path is null || claim.Path.Count == 0)
                {
                    errors.Add($"Credential query '{Id}': claim path must be non-empty.");
                }

                if (claim.Id is not null && !claimIds.Add(claim.Id))
                {
                    errors.Add($"Credential query '{Id}': duplicate claim id '{claim.Id}'.");
                }
            }
        }

        if (ClaimSets is not null)
        {
            foreach (var set in ClaimSets)
            {
                foreach (var claimId in set)
                {
                    if (!claimIds.Contains(claimId))
                    {
                        errors.Add($"Credential query '{Id}': claim_sets references unknown claim id '{claimId}'.");
                    }
                }
            }
        }
    }
}

/// <summary>Format-specific matching metadata.</summary>
public sealed record DcqlCredentialMeta
{
    /// <summary>Accepted <c>vct</c> values (dc+sd-jwt).</summary>
    [JsonPropertyName("vct_values")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? VctValues { get; init; }

    /// <summary>Required mdoc doctype (mso_mdoc).</summary>
    [JsonPropertyName("doctype_value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? DoctypeValue { get; init; }
}

/// <summary>A single claim selection inside a credential query.</summary>
public sealed record DcqlClaimQuery
{
    /// <summary>Claim id — required only when referenced from <c>claim_sets</c>.</summary>
    [JsonPropertyName("id")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Id { get; init; }

    /// <summary>Claim path segments (string segments only in v1 — no array indices).</summary>
    [JsonPropertyName("path")]
    public required IReadOnlyList<string> Path { get; init; }
}

/// <summary>An alternative set — "satisfy with any one of these id-combinations".</summary>
public sealed record DcqlCredentialSetQuery
{
    /// <summary>Preference-ordered options; each option is a list of credential query ids.</summary>
    [JsonPropertyName("options")]
    public required IReadOnlyList<IReadOnlyList<string>> Options { get; init; }

    /// <summary>Whether satisfying this set is required (default true per spec).</summary>
    [JsonPropertyName("required")]
    public bool Required { get; init; } = true;

    /// <summary>Consent-surface display text.</summary>
    [JsonPropertyName("purpose")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Purpose { get; init; }
}

/// <summary>Credential format identifiers (final-profile forms — Feature 181 FR-004).</summary>
public static class DcqlFormats
{
    /// <summary>SD-JWT VC, final media-type form.</summary>
    public const string SdJwtVc = "dc+sd-jwt";

    /// <summary>ISO 18013-5 mdoc.</summary>
    public const string MsoMdoc = "mso_mdoc";
}

/// <summary>
/// The OpenID4VP 1.0 response envelope: a JSON object keyed by credential-query id, each value
/// an array of presentation strings (SD-JWT compact form or base64url mdoc DeviceResponse).
/// Replaces <c>presentation_submission</c> + bare-string <c>vp_token</c> (FR-003).
/// </summary>
public sealed record DcqlVpToken
{
    /// <summary>Presentations keyed by credential-query id.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyList<string>> Presentations { get; init; }

    /// <summary>Serialize to the wire object form.</summary>
    public string ToJson()
        => JsonSerializer.Serialize(Presentations, DcqlJson.Options);

    /// <summary>
    /// Parse a <c>vp_token</c> form value. A bare SD-JWT compact string (the legacy
    /// pre-DCQL shape) is rejected with <see cref="DcqlParseException"/> code
    /// <see cref="DcqlErrorCodes.LegacyDialect"/>; malformed JSON or wrong shapes reject
    /// with <see cref="DcqlErrorCodes.Invalid"/>.
    /// </summary>
    public static DcqlVpToken Parse(string vpToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(vpToken);
        var trimmed = vpToken.TrimStart();
        if (trimmed.Length == 0 || trimmed[0] != '{')
        {
            throw new DcqlParseException(
                DcqlErrorCodes.LegacyDialect,
                "vp_token must be a JSON object keyed by credential query id (OpenID4VP 1.0); " +
                "bare compact-form tokens are the retired pre-DCQL dialect.");
        }

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(vpToken);
        }
        catch (JsonException ex)
        {
            throw new DcqlParseException(DcqlErrorCodes.Invalid, $"vp_token is not valid JSON: {ex.Message}");
        }

        using (doc)
        {
            var presentations = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                switch (property.Value.ValueKind)
                {
                    // The spec value is an array; tolerate a single string for robustness.
                    case JsonValueKind.String:
                        presentations[property.Name] = [property.Value.GetString()!];
                        break;
                    case JsonValueKind.Array:
                        var list = new List<string>();
                        foreach (var item in property.Value.EnumerateArray())
                        {
                            if (item.ValueKind != JsonValueKind.String)
                            {
                                throw new DcqlParseException(
                                    DcqlErrorCodes.Invalid,
                                    $"vp_token entry '{property.Name}' must contain presentation strings.");
                            }
                            list.Add(item.GetString()!);
                        }
                        if (list.Count == 0)
                        {
                            throw new DcqlParseException(
                                DcqlErrorCodes.Invalid,
                                $"vp_token entry '{property.Name}' must contain at least one presentation.");
                        }
                        presentations[property.Name] = list;
                        break;
                    default:
                        throw new DcqlParseException(
                            DcqlErrorCodes.Invalid,
                            $"vp_token entry '{property.Name}' has an unsupported value kind '{property.Value.ValueKind}'.");
                }
            }

            if (presentations.Count == 0)
            {
                throw new DcqlParseException(DcqlErrorCodes.Invalid, "vp_token object carries no presentations.");
            }

            return new DcqlVpToken { Presentations = presentations };
        }
    }
}

/// <summary>Typed error codes for dialect parsing failures (data-model §6).</summary>
public static class DcqlErrorCodes
{
    /// <summary>The retired Presentation Exchange dialect was received (FR-007).</summary>
    public const string LegacyDialect = "LEGACY_DIALECT";

    /// <summary>Structurally invalid DCQL.</summary>
    public const string Invalid = "DCQL_INVALID";

    /// <summary>A vp_token entry is keyed to a query id the request never declared (FR-003).</summary>
    public const string UnknownQueryId = "DCQL_UNKNOWN_QUERY_ID";
}

/// <summary>Thrown by the DCQL parser/envelope reader; carries a typed error code.</summary>
public sealed class DcqlParseException : FormatException
{
    /// <summary>Initialises a new instance.</summary>
    public DcqlParseException(string code, string message) : base(message) => Code = code;

    /// <summary>Typed error code from <see cref="DcqlErrorCodes"/>.</summary>
    public string Code { get; }
}

/// <summary>Shared serializer options for the DCQL wire shapes (hoisted — pattern T10).</summary>
public static class DcqlJson
{
    /// <summary>Strict, camel-free options — property names come from attributes.</summary>
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };
}
