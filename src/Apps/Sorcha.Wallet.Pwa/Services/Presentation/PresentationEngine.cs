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
using Sorcha.Verifier.Engine;
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

        // Fetch and decode the Request Object.
        var requestObjectJwt = await requestObjectFetcher(requestUri, ct);
        var payload = DecodeJwtPayload(requestObjectJwt);

        using (payload)
        {
            var root = payload.RootElement;
            var clientId = GetRequiredString(root, "client_id");

            // Feature 181 US6 — verifier authentication. Validate the signed request object against
            // the client_id host. A present-but-invalid signature (tampering) or a SAN/host mismatch
            // is a HARD refusal — the flow stops here, no consent is shown. Everything else renders a
            // three-state VerifierAuthState on the consent surface. v1 passes null anchors, so a valid
            // signed request caps at AuthenticUntrusted (FR-027: absent anchors never block).
            var auth = new RequestObjectValidator().Validate(requestObjectJwt, clientId, anchors: null);
            RequestAuthMetrics.Record(auth); // T058 — count every verifier-auth outcome (refused + rendered)
            if (auth.Refused)
            {
                throw new DcqlParseException(
                    auth.RefusalCode!,
                    auth.RefusalCode == RequestObjectErrorCodes.RequestHostMismatch
                        ? "The verifier's identity did not match its request."
                        : "This request could not be verified and was refused.");
            }

            var query = DcqlRequestParser.ParseFromRequestObjectPayload(root);

            // US1 consumes the first credential query (multi-query consent is US2).
            var credential = query.Credentials[0];
            var (required, optional) = DcqlRequestParser.SplitClaims(credential);
            var purpose = query.CredentialSets is { Count: > 0 } ? query.CredentialSets[0].Purpose : null;

            return new ParsedPresentationRequest
            {
                ClientId = clientId,
                ResponseUri = GetRequiredString(root, "response_uri"),
                Nonce = GetRequiredString(root, "nonce"),
                State = GetRequiredString(root, "state"),
                Query = query,
                // Feature 185: an mdoc query carries `doctype_value`, NOT `vct_values` — so demanding vct
                // here rejected every mso_mdoc request outright, even though F181's DCQL vocabulary has
                // always been able to express one. The wallet could not so much as PARSE a request for the
                // credential type that proximity presentation exists to present.
                //
                // A query with neither is genuinely malformed and still fails, but it fails at the query, not
                // at the whole request.
                RequiredVct = QueryIdentifier(credential),
                RequiredClaims = required,
                OptionalClaims = optional,
                Purpose = purpose,
                ResponseMode = root.TryGetProperty("response_mode", out var rm) && rm.ValueKind == JsonValueKind.String
                    ? rm.GetString()!
                    : "direct_post",
                VerifierAuthentication = auth.AuthState,
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

        var format = request.Query.Credentials.Count > 0
            ? request.Query.Credentials[0].Format
            : DcqlFormats.SdJwtVc;

        return MatchCandidates(
            format, request.RequiredVct, request.RequiredClaims, request.OptionalClaims, credentials);
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
            var vct = QueryIdentifier(cq);
            var (required, optional) = DcqlRequestParser.SplitClaims(cq);
            var candidates = MatchCandidates(cq.Format, vct, required, optional, credentials);
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

    /// <summary>
    /// The identifier a credential query matches on: <c>vct_values</c> for SD-JWT, <c>doctype_value</c> for
    /// mdoc.
    /// </summary>
    /// <remarks>
    /// One place, so that the two formats' identifiers can never be silently confused for one another — they
    /// are different strings and a credential must never be matched against the wrong one.
    /// </remarks>
    private static string QueryIdentifier(DcqlCredentialQuery query) =>
        string.Equals(query.Format, DcqlFormats.MsoMdoc, StringComparison.Ordinal)
            ? query.Meta.DoctypeValue ?? string.Empty
            : query.Meta.VctValues is { Count: > 0 } vcts ? vcts[0] : string.Empty;

    /// <summary>Maps a DCQL format string to the wallet's cached-credential format.</summary>
    private static CachedCredentialFormat FormatOf(string dcqlFormat) =>
        string.Equals(dcqlFormat, DcqlFormats.MsoMdoc, StringComparison.Ordinal)
            ? CachedCredentialFormat.MsoMdoc
            : CachedCredentialFormat.SdJwtVc;

    private static IReadOnlyList<CredentialMatch> MatchCandidates(
        string dcqlFormat,
        string identifier,
        IReadOnlyList<string> requiredClaims,
        IReadOnlyList<string> optionalClaims,
        IReadOnlyList<CachedCredential> credentials)
    {
        // A query with no identifier matches nothing. Returning every credential instead would be a
        // catastrophic default — the citizen would be offered credentials the verifier never asked for.
        if (string.IsNullOrEmpty(identifier))
            return [];

        var wantedFormat = FormatOf(dcqlFormat);

        var matches = new List<CredentialMatch>();
        foreach (var credential in credentials)
        {
            // The format gate comes FIRST. Without it an mdoc docType could be matched against an SD-JWT's
            // vct — they are different namespaces of string and a collision, however unlikely, would present
            // the wrong credential entirely.
            if (credential.Format != wantedFormat) continue;

            if (!string.Equals(credential.MatchIdentifier, identifier, StringComparison.Ordinal)) continue;

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

        // KB-JWT — header carries the signing key's thumbprint as kid; payload binds nonce+aud.
        // The alg follows the SIGNING key's JWK (#1195 Phase 2): the device key is EC P-256
        // (ES256), but a holder-cnf root presented server-custody may sign under an Ed25519
        // holder key (EdDSA). Hardcoding ES256 here was the mirror image of the verifier-side
        // "ES256-only verifier rejected Ed25519-holder presentations" bug.
        var kid = ComputeJwkThumbprint(deviceJwk);
        var header = new Dictionary<string, object>
        {
            ["alg"] = JoseAlgorithmFor(deviceJwk),
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

    /// <summary>
    /// Compute the RFC 7638 JWK thumbprint. Handles EC (P-256 — canonical members
    /// crv, kty, x, y) and OKP (Ed25519 — canonical members crv, kty, x). The OKP arm
    /// exists for the holder-cnf root (#1195 Phase 2): the citizen's server-custodied
    /// holder key is Ed25519 for the default wallet algorithm.
    /// </summary>
    internal static string ComputeJwkThumbprint(JsonElement jwk)
    {
        var crv = jwk.GetProperty("crv").GetString();
        var kty = jwk.GetProperty("kty").GetString();
        var x = jwk.GetProperty("x").GetString();
        var canonical = string.Equals(kty, "OKP", StringComparison.Ordinal)
            ? $"{{\"crv\":\"{crv}\",\"kty\":\"{kty}\",\"x\":\"{x}\"}}"
            : $"{{\"crv\":\"{crv}\",\"kty\":\"{kty}\",\"x\":\"{x}\",\"y\":\"{jwk.GetProperty("y").GetString()}\"}}";
        return Base64Url.EncodeToString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }

    /// <inheritdoc />
    public PresentationSelection Select(
        ParsedPresentationRequest request,
        IReadOnlyList<CachedCredential> credentials,
        string? deviceThumbprint,
        string? holderThumbprint,
        PresentationSurface surface = PresentationSurface.Auto)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(credentials);

        // Only credentials that actually satisfy the request are candidates. Classify each by its key
        // binding relative to THIS device: a copy we can device-sign, the holder-cnf root (server
        // custody), a legacy unbound credential (Phase-1 device-signed), a foreign device's copy
        // (never selectable here), or unclassifiable (bound, but no holder thumbprint to compare).
        CredentialMatch? deviceCopy = null;
        CredentialMatch? root = null;
        CredentialMatch? unbound = null;
        var sawUnknown = false;
        foreach (var candidate in Match(request, credentials))
        {
            switch (ClassifyBinding(candidate.Credential.RawSdJwt, deviceThumbprint, holderThumbprint))
            {
                case CredentialBinding.ThisDevice:
                    deviceCopy ??= candidate;
                    break;
                case CredentialBinding.HolderRoot:
                    root ??= candidate;
                    break;
                case CredentialBinding.Unbound:
                    unbound ??= candidate;
                    break;
                case CredentialBinding.Unknown:
                    sawUnknown = true;
                    break;
                // CredentialBinding.Foreign — never selectable on this device.
            }
        }

        switch (surface)
        {
            // In person / offline: device-signed only. A device-cnf copy first; a legacy unbound
            // credential still device-signs (nothing for the verifier to check it against). A root
            // WITHOUT a copy ⇒ the distinct bind-first outcome (route to the Task 6 button); never
            // the root itself (device-signing it cannot verify), never a doomed present.
            case PresentationSurface.InPerson:
                if (deviceCopy is not null)
                    return PresentationSelection.Selected(deviceCopy, PresentationSigningMode.Device);
                if (unbound is not null)
                    return PresentationSelection.Selected(unbound, PresentationSigningMode.Device);
                if (root is not null)
                    return PresentationSelection.BindFirst(root);
                return sawUnknown ? PresentationSelection.HolderKeyUnavailable : PresentationSelection.None;

            // Web / remote: the holder-cnf root, server custody. Without a root, a this-device copy or
            // a legacy unbound credential stands in (device-signed — still verifies).
            case PresentationSurface.Remote:
                if (root is not null)
                    return PresentationSelection.Selected(root, PresentationSigningMode.ServerCustody);
                if (deviceCopy is not null)
                    return PresentationSelection.Selected(deviceCopy, PresentationSigningMode.Device);
                if (unbound is not null)
                    return PresentationSelection.Selected(unbound, PresentationSigningMode.Device);
                return sawUnknown ? PresentationSelection.HolderKeyUnavailable : PresentationSelection.None;

            // Auto (default): prefer a device copy this device can sign for, else the root, else legacy.
            default:
                if (deviceCopy is not null)
                    return PresentationSelection.Selected(deviceCopy, PresentationSigningMode.Device);
                if (root is not null)
                    return PresentationSelection.Selected(root, PresentationSigningMode.ServerCustody);
                if (unbound is not null)
                    return PresentationSelection.Selected(unbound, PresentationSigningMode.Device);
                return sawUnknown ? PresentationSelection.HolderKeyUnavailable : PresentationSelection.None;
        }
    }

    /// <summary>The key binding of a cached credential relative to THIS device (#1195 Phase 2, Task 7).</summary>
    internal enum CredentialBinding
    {
        /// <summary>Its <c>cnf</c> thumbprint is the citizen's holder key — the root, presented server-custody.</summary>
        HolderRoot,

        /// <summary>Its <c>cnf</c> thumbprint is THIS device's key — a device copy this device can sign.</summary>
        ThisDevice,

        /// <summary>No readable <c>cnf</c> — a legacy credential; keeps its Phase-1 device-signed behaviour.</summary>
        Unbound,

        /// <summary>A different device's copy (cnf matches neither key) — never selectable here.</summary>
        Foreign,

        /// <summary>Bound to SOMETHING that is not this device, and no holder thumbprint to compare —
        /// root and foreign copy cannot be told apart. Selection fails closed, never guesses.</summary>
        Unknown,
    }

    /// <summary>
    /// Classify a cached credential's key binding relative to THIS device. Discrimination is purely by
    /// RFC 7638 <c>cnf.jwk</c> thumbprint (via <see cref="DeviceBindingService.TryGetCnfJwkThumbprint"/>,
    /// the Task 6 helper) against BOTH the device key and the holder key — the same rule the Task 5
    /// server-side policy uses. Key-TYPE discrimination is deliberately absent: a P-256 wallet's holder
    /// key (and therefore its root's <c>cnf</c>) is EC, indistinguishable from a device key by type.
    /// This layer guards the recorded trap: device-signing the root, or signing a foreign device's copy,
    /// yields a KB-JWT that fails verification downstream with no local error.
    /// </summary>
    internal static CredentialBinding ClassifyBinding(
        string rawSdJwt, string? deviceThumbprint, string? holderThumbprint)
    {
        if (!DeviceBindingService.TryGetCnfJwkThumbprint(rawSdJwt, out var cnfThumbprint))
            return CredentialBinding.Unbound;

        if (!string.IsNullOrEmpty(deviceThumbprint) &&
            string.Equals(cnfThumbprint, deviceThumbprint, StringComparison.Ordinal))
            return CredentialBinding.ThisDevice;

        if (string.IsNullOrEmpty(holderThumbprint))
            return CredentialBinding.Unknown;

        return string.Equals(cnfThumbprint, holderThumbprint, StringComparison.Ordinal)
            ? CredentialBinding.HolderRoot
            : CredentialBinding.Foreign;
    }

    /// <summary>The JOSE alg for a signing key's public JWK: OKP/Ed25519 → EdDSA; EC P-256 → ES256.</summary>
    internal static string JoseAlgorithmFor(JsonElement jwk) =>
        jwk.TryGetProperty("kty", out var kty) &&
        string.Equals(kty.GetString(), "OKP", StringComparison.Ordinal)
            ? "EdDSA"
            : "ES256";
}
