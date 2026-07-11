// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Collections.Specialized;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Web;
using Microsoft.Extensions.Logging;
using Sorcha.UI.Core.Models.Presentation;
using Sorcha.Verifier.Engine.Dcql;

namespace Sorcha.Wallet.Pwa.Services.Presentation;

/// <summary>
/// Default <see cref="IPresentationEngine"/>. Feature 181 — understands the
/// <c>request_uri</c> form of <c>openid4vp://</c> every Sorcha producer emits: fetches
/// the Request Object (via the caller-supplied delegate), decodes its payload, and
/// parses the <c>dcql_query</c>. The retired inline-<c>presentation_definition</c> form
/// is refused with <c>LEGACY_DIALECT</c>. Request-object signature verification is
/// US6 (verifier authentication).
/// </summary>
public sealed class PresentationEngine : IPresentationEngine
{
    private readonly TimeProvider _clock;
    private readonly ILogger<PresentationEngine> _logger;

    /// <summary>Initialises a new instance.</summary>
    public PresentationEngine(TimeProvider clock, ILogger<PresentationEngine> logger)
    {
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ParsedPresentationRequest> ParseAsync(
        string openid4vpDeepLink,
        Func<string, CancellationToken, Task<string>> requestObjectFetcher,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(openid4vpDeepLink);
        ArgumentNullException.ThrowIfNull(requestObjectFetcher);
        if (!openid4vpDeepLink.StartsWith("openid4vp://", StringComparison.OrdinalIgnoreCase))
        {
            throw new FormatException("Deep link must start with openid4vp://");
        }

        var queryStart = openid4vpDeepLink.IndexOf('?');
        if (queryStart < 0)
        {
            throw new FormatException("Deep link is missing the query component.");
        }

        NameValueCollection parsed = HttpUtility.ParseQueryString(openid4vpDeepLink[(queryStart + 1)..]);

        // Feature 181 — the inline presentation_definition form is the retired dialect.
        if (parsed["presentation_definition"] is not null)
        {
            throw new DcqlParseException(
                DcqlErrorCodes.LegacyDialect,
                "This QR uses the retired inline presentation_definition form. Ask the verifier to upgrade.");
        }

        var requestUri = parsed["request_uri"]
            ?? throw new FormatException("Deep link is missing request_uri.");

        // Fetch and decode the Request Object. US1: the payload is decoded WITHOUT
        // signature verification — US6 (verifier authentication) adds JWS validation
        // against the verifier certificate + trusted-list anchors.
        var requestObjectJwt = await requestObjectFetcher(requestUri, ct);
        var payload = DecodeJwtPayload(requestObjectJwt);

        using (payload)
        {
            var root = payload.RootElement;
            var query = DcqlRequestParser.ParseFromRequestObjectPayload(root);

            // US1 consumes the first credential query (multi-query consent is US2).
            var credential = query.Credentials[0];
            var (required, optional) = DcqlRequestParser.SplitClaims(credential);
            var purpose = query.CredentialSets is { Count: > 0 } ? query.CredentialSets[0].Purpose : null;

            return new ParsedPresentationRequest
            {
                ClientId = GetRequiredString(root, "client_id"),
                ResponseUri = GetRequiredString(root, "response_uri"),
                Nonce = GetRequiredString(root, "nonce"),
                Query = query,
                RequiredVct = credential.Meta.VctValues is { Count: > 0 }
                    ? credential.Meta.VctValues[0]
                    : throw new FormatException("Request object's credential query carries no vct_values."),
                RequiredClaims = required,
                OptionalClaims = optional,
                Purpose = purpose,
                ResponseMode = root.TryGetProperty("response_mode", out var rm) && rm.ValueKind == JsonValueKind.String
                    ? rm.GetString()!
                    : "direct_post",
            };
        }
    }

    /// <summary>Base64url-decode the payload segment of a (signed or unsigned) JWT.</summary>
    internal static JsonDocument DecodeJwtPayload(string jwt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jwt);
        var segments = jwt.Split('.');
        if (segments.Length is not (2 or 3) || segments[1].Length == 0)
        {
            throw new FormatException("Request object is not a JWT.");
        }

        try
        {
            return JsonDocument.Parse(Base64Url.DecodeFromChars(segments[1]));
        }
        catch (Exception ex) when (ex is JsonException or global::System.FormatException)
        {
            throw new FormatException($"Request object payload does not decode: {ex.Message}");
        }
    }

    private static string GetRequiredString(JsonElement root, string name)
        => root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw new FormatException($"Request object is missing {name}.");

    /// <inheritdoc />
    public IReadOnlyList<CredentialMatch> Match(
        ParsedPresentationRequest request,
        IReadOnlyList<CachedCredential> credentials)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(credentials);
        return MatchCandidates(request.RequiredVct, request.RequiredClaims, request.OptionalClaims, credentials);
    }

    /// <inheritdoc />
    public DcqlMatchResult MatchQuery(
        ParsedPresentationRequest request,
        IReadOnlyList<CachedCredential> credentials)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(credentials);

        // 1. Per-credential-query candidates.
        var perQuery = new List<DcqlQueryMatch>(request.Query.Credentials.Count);
        var unsatisfiable = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cq in request.Query.Credentials)
        {
            var vct = cq.Meta.VctValues is { Count: > 0 } ? cq.Meta.VctValues[0] : string.Empty;
            var (required, optional) = DcqlRequestParser.SplitClaims(cq);
            var candidates = MatchCandidates(vct, required, optional, credentials);
            perQuery.Add(new DcqlQueryMatch
            {
                QueryId = cq.Id,
                Vct = vct,
                RequiredClaims = required,
                OptionalClaims = optional,
                Candidates = candidates,
            });
            if (candidates.Count == 0)
            {
                unsatisfiable.Add(cq.Id);
            }
        }

        // 2. Solve credential_sets. Absent ⇒ every credential query is required (AND).
        var setChoices = new List<DcqlSetChoice>();
        var unsatisfiedRequired = new List<string>();
        bool satisfiable;

        if (request.Query.CredentialSets is { Count: > 0 } sets)
        {
            satisfiable = true;
            foreach (var set in sets)
            {
                var satisfiableOptions = set.Options
                    .Where(opt => opt.Count > 0 && opt.All(id => !unsatisfiable.Contains(id)))
                    .Select(opt => (IReadOnlyList<string>)opt.ToArray())
                    .ToList();

                setChoices.Add(new DcqlSetChoice
                {
                    Options = set.Options.Select(o => (IReadOnlyList<string>)o.ToArray()).ToList(),
                    SatisfiableOptions = satisfiableOptions,
                    Required = set.Required,
                    Purpose = set.Purpose,
                });

                if (set.Required && satisfiableOptions.Count == 0)
                {
                    satisfiable = false;
                    foreach (var id in set.Options.SelectMany(o => o).Distinct(StringComparer.Ordinal))
                    {
                        if (unsatisfiable.Contains(id) && !unsatisfiedRequired.Contains(id))
                        {
                            unsatisfiedRequired.Add(id);
                        }
                    }
                }
            }
        }
        else
        {
            foreach (var cq in request.Query.Credentials)
            {
                if (unsatisfiable.Contains(cq.Id))
                {
                    unsatisfiedRequired.Add(cq.Id);
                }
            }
            satisfiable = unsatisfiedRequired.Count == 0;
        }

        return new DcqlMatchResult
        {
            Satisfiable = satisfiable,
            PerQuery = perQuery,
            UnsatisfiedRequiredQueryIds = unsatisfiedRequired,
            SetChoices = setChoices,
        };
    }

    private static IReadOnlyList<CredentialMatch> MatchCandidates(
        string vct,
        IReadOnlyList<string> requiredClaims,
        IReadOnlyList<string> optionalClaims,
        IReadOnlyList<CachedCredential> credentials)
    {
        var matches = new List<CredentialMatch>();
        foreach (var credential in credentials)
        {
            if (!string.Equals(credential.Vct, vct, StringComparison.Ordinal)) continue;

            var availableNames = new HashSet<string>(credential.AvailableClaimNames, StringComparer.Ordinal);
            var satisfied = requiredClaims.Where(availableNames.Contains).ToList();
            if (satisfied.Count != requiredClaims.Count)
            {
                continue;
            }

            matches.Add(new CredentialMatch
            {
                Credential = credential,
                SatisfiedRequired = satisfied,
                AvailableOptional = optionalClaims.Where(availableNames.Contains).ToList(),
            });
        }

        return matches;
    }

    /// <inheritdoc />
    public Task<string> BuildVpTokenAsync(
        CredentialMatch match,
        IReadOnlyList<string> approvedClaims,
        ParsedPresentationRequest request,
        JsonElement deviceJwk,
        Func<byte[], CancellationToken, Task<byte[]>> deviceSigner,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        // Single-ask flow: validate the approved set against the request-level required claims.
        return BuildSinglePresentationAsync(
            match, approvedClaims, request.RequiredClaims, request, deviceJwk, deviceSigner, ct);
    }

    /// <inheritdoc />
    public async Task<string> BuildVpTokenEnvelopeAsync(
        IReadOnlyList<ConsentedQuery> consented,
        ParsedPresentationRequest request,
        JsonElement deviceJwk,
        Func<byte[], CancellationToken, Task<byte[]>> deviceSigner,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(consented);
        ArgumentNullException.ThrowIfNull(request);
        if (consented.Count == 0)
        {
            throw new InvalidOperationException("A presentation must carry at least one consented query.");
        }

        // One SD-JWT presentation per query, each validated against its own required-claim set.
        var presentations = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var c in consented)
        {
            if (presentations.ContainsKey(c.QueryId))
            {
                throw new InvalidOperationException($"Duplicate query id '{c.QueryId}' in the consented set.");
            }
            var vp = await BuildSinglePresentationAsync(
                c.Match, c.ApprovedClaims, c.RequiredClaims, request, deviceJwk, deviceSigner, ct);
            presentations[c.QueryId] = [vp];
        }

        var envelope = new DcqlVpToken { Presentations = presentations };
        _logger.LogInformation(
            "Built multi-credential vp_token envelope: queries={Count}, audience={Aud}",
            presentations.Count, request.ClientId);
        return envelope.ToJson();
    }

    /// <summary>
    /// Build one SD-JWT presentation (credentialJwt~selected disclosures~KB-JWT) for a single query,
    /// disclosing only the approved subset and binding nonce + audience via a device-signed KB-JWT.
    /// Shared by the single-ask and multi-credential (envelope) build paths.
    /// </summary>
    private async Task<string> BuildSinglePresentationAsync(
        CredentialMatch match,
        IReadOnlyList<string> approvedClaims,
        IReadOnlyList<string> requiredClaims,
        ParsedPresentationRequest request,
        JsonElement deviceJwk,
        Func<byte[], CancellationToken, Task<byte[]>> deviceSigner,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(match);
        ArgumentNullException.ThrowIfNull(approvedClaims);
        ArgumentNullException.ThrowIfNull(requiredClaims);
        ArgumentNullException.ThrowIfNull(deviceSigner);

        // Sanity check: every required claim must be in the approved set.
        foreach (var required in requiredClaims)
        {
            if (!approvedClaims.Contains(required, StringComparer.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Approved claims do not cover required claim '{required}'.");
            }
        }

        var (credentialJwt, allDisclosures, _) = SplitSdJwt(match.Credential.RawSdJwt);
        if (credentialJwt is null)
        {
            throw new InvalidOperationException("Cached credential is not a valid SD-JWT.");
        }

        // Pick only the disclosures whose claim name was approved.
        var approvedSet = new HashSet<string>(approvedClaims, StringComparer.Ordinal);
        var selected = new List<string>(allDisclosures.Count);
        foreach (var seg in allDisclosures)
        {
            var name = ReadDisclosureName(seg);
            if (name is not null && approvedSet.Contains(name))
            {
                selected.Add(seg);
            }
        }

        // KB-JWT — header carries the device-key thumbprint as kid; payload binds nonce+aud.
        var kid = ComputeJwkThumbprint(deviceJwk);
        var header = new Dictionary<string, object>
        {
            ["alg"] = "ES256",
            ["typ"] = "kb+jwt",
            ["kid"] = kid,
        };
        // sd_hash per RFC 9901 §4.3 — SHA-256 of the to-be-presented hashable form.
        // Compute over the canonical "credentialJwt~disclosure1~..~" prefix.
        var hashable = credentialJwt + string.Concat(selected.Select(s => "~" + s)) + "~";
        var sdHash = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(hashable)));

        // Feature 138 US5 — the KB-JWT carries its own short, mandatory exp so a captured proof
        // cannot be replayed beyond a tight window even within an open verifier session. 120s
        // matches the verifier's KbJwtMaxLifetimeSeconds upper bound.
        var kbIssuedAt = _clock.GetUtcNow();
        var payload = new Dictionary<string, object>
        {
            ["iat"] = kbIssuedAt.ToUnixTimeSeconds(),
            ["exp"] = kbIssuedAt.AddSeconds(120).ToUnixTimeSeconds(),
            ["aud"] = request.ClientId,
            ["nonce"] = request.Nonce,
            ["sd_hash"] = sdHash,
        };

        var headerSeg = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(header));
        var payloadSeg = Base64Url.EncodeToString(JsonSerializer.SerializeToUtf8Bytes(payload));
        var signingInput = Encoding.ASCII.GetBytes($"{headerSeg}.{payloadSeg}");
        var signature = await deviceSigner(signingInput, ct);
        var kbJwt = $"{headerSeg}.{payloadSeg}.{Base64Url.EncodeToString(signature)}";

        var vpToken = hashable + kbJwt;
        _logger.LogInformation(
            "Built vp_token: credentialId={Id}, disclosures={Count}, audience={Aud}",
            match.Credential.Id, selected.Count, request.ClientId);
        return vpToken;
    }

    // ─────────────────────────── parsing helpers ─────────────────────────────────

    /// <summary>
    /// Split the SD-JWT compact form into (credentialJwt, disclosures, kbJwt). Mirror
    /// of <c>VerifiablePresentationValidator.SplitSdJwt</c> — duplicated here to avoid a
    /// project reference between wallet and verifier (they ship as separate apps).
    /// </summary>
    internal static (string? Credential, IReadOnlyList<string> Disclosures, string? KbJwt) SplitSdJwt(string vp)
    {
        var segments = vp.Split('~');
        if (segments.Length < 1) return (null, [], null);

        var credential = segments[0];
        var last = segments[^1];
        var hasKbJwt = !string.IsNullOrEmpty(last) && last.Count(c => c == '.') == 2;

        var disclosureCount = segments.Length - 1 - (hasKbJwt ? 1 : 0);
        var disclosures = new List<string>(Math.Max(disclosureCount, 0));
        for (int i = 1; i <= disclosureCount; i++)
        {
            if (!string.IsNullOrEmpty(segments[i])) disclosures.Add(segments[i]);
        }
        return (credential, disclosures, hasKbJwt ? last : null);
    }

    /// <summary>Read the claim name out of a single SD-JWT disclosure segment.</summary>
    internal static string? ReadDisclosureName(string segment)
    {
        try
        {
            var bytes = Base64Url.DecodeFromChars(segment);
            using var doc = JsonDocument.Parse(bytes);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            return doc.RootElement.GetArrayLength() switch
            {
                3 => doc.RootElement[1].GetString(),
                2 => doc.RootElement[0].GetString(),
                _ => null,
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Compute the RFC 7638 JWK thumbprint for an EC P-256 public JWK.</summary>
    internal static string ComputeJwkThumbprint(JsonElement jwk)
    {
        var crv = jwk.GetProperty("crv").GetString();
        var kty = jwk.GetProperty("kty").GetString();
        var x = jwk.GetProperty("x").GetString();
        var y = jwk.GetProperty("y").GetString();
        var canonical = $"{{\"crv\":\"{crv}\",\"kty\":\"{kty}\",\"x\":\"{x}\",\"y\":\"{y}\"}}";
        return Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
