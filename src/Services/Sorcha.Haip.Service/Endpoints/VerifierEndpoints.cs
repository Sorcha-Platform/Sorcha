// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.ComponentModel.DataAnnotations;
using System.Text.Json;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Haip.Service.Models;
using Sorcha.Haip.Service.Services;
using Sorcha.Verifier.Engine.Dcql;

namespace Sorcha.Haip.Service.Endpoints;

/// <summary>
/// OpenID4VP verifier endpoints — Authorization Request creation, Request Object
/// serving, direct_post callback, and verification result retrieval.
/// </summary>
public static class VerifierEndpoints
{
    /// <summary>
    /// The DCQL credential-query id used by the single-ask verifier API (Feature 181 US1).
    /// Multi-query requests (US2) carry caller-supplied ids; this surface asks for exactly
    /// one credential, keyed by this constant on both the request and response sides.
    /// </summary>
    internal const string DefaultQueryId = "credential";

    /// <summary>
    /// Maps verifier endpoints under /api/v1/verifier.
    /// </summary>
    public static void MapVerifierEndpoints(this WebApplication app)
    {
        // Authenticated — Blueprint Service, citizen wallet PWA (consumer tier), and desk verifier
        // (platform/org tier) all create presentation requests (Feature 164 B3: both human-verifier
        // callers are accepted alongside Blueprint's service-to-service path).
        app.MapPost("/api/v1/verifier/requests", CreatePresentationRequest)
            .WithName("CreatePresentationRequest")
            .WithTags("HAIP Verifier")
            .WithSummary("Create a Presentation Request")
            .WithDescription(
                "Creates an OpenID4VP Presentation Request for an external HAIP wallet. " +
                "Returns the Authorization Request URI for QR code rendering. " +
                "Accepted from any authenticated caller: Blueprint Service (service tier), " +
                "citizen wallet PWA (consumer tier), or desk verifier (platform/org tier).")
            .Produces<object>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .WithRequestValidation() // VAL-001 (#327): DataAnnotations on CreatePresentationRequestBody → 400 ValidationProblem
            .RequireAuthorization(); // Feature 164 B3: any authenticated caller (FR-008)

        // Public — wallet fetches the signed Request Object
        app.MapGet("/api/v1/verifier/requests/{requestId:guid}/request-object", GetRequestObject)
            .WithName("GetRequestObject")
            .WithTags("HAIP Verifier")
            .WithSummary("Get the signed Request Object JWT")
            .WithDescription(
                "Returns the signed JWT Request Object containing the dcql_query, " +
                "nonce, and response_mode. Wallets fetch this via the request_uri from the QR code.")
            .Produces(StatusCodes.Status503ServiceUnavailable)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status410Gone)
            .AllowAnonymous();

        // Public — wallet submits vp_token via direct_post
        app.MapPost("/api/v1/verifier/requests/{requestId:guid}/direct-post", HandleDirectPost)
            .WithName("HandleDirectPost")
            .WithTags("HAIP Verifier")
            .WithSummary("Submit a VP Token via direct_post")
            .WithDescription(
                "HAIP wallet submits the vp_token and presentation_submission via direct_post. " +
                "The verifier validates the presentation and stores the result.")
            .Produces(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status410Gone)
            .AllowAnonymous()
            .DisableAntiforgery(); // OID4VP direct_post — wallet-to-verifier, no browser CSRF

        // Authenticated — Blueprint Service, citizen wallet PWA (consumer tier), and desk verifier
        // (platform/org tier) all poll for results (Feature 164 B3: both human-verifier callers accepted).
        app.MapGet("/api/v1/verifier/requests/{requestId:guid}/result", GetVerificationResult)
            .WithName("GetVerificationResult")
            .WithTags("HAIP Verifier")
            .WithSummary("Get verification result")
            .WithDescription(
                "Returns the verification outcome for a presentation request once the wallet has " +
                "submitted its vp_token via direct_post — the verified claims, accepted/rejected state, " +
                "and any failure reasons. Blueprint Service polls this to gate the next workflow step on " +
                "successful presentation. Also consumed by the citizen wallet PWA and desk verifier " +
                "to drive the shared client-side verdict (Feature 164 B3).")
            .Produces<VerificationResult>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .RequireAuthorization(); // Feature 164 B3: any authenticated caller (FR-008)
    }

    private static async Task<IResult> CreatePresentationRequest(
        [FromBody] CreatePresentationRequestBody request,
        PresentationRequestStore store,
        IConfiguration configuration,
        RequestObjectSigner signer,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CredentialType))
            return Results.BadRequest(new { error = "CredentialType is required" });

        var issuerUrl = configuration.GetValue<string>("Haip:IssuerUrl")
            ?? "https://sorcha.example/haip";

        // Feature 181 US6 (T053) — client_id is the verifier's prefixed x509_san_dns identity, backed by
        // the verifier certificate's SAN dNSName (retires TODO(098)). The wallet authenticates the verifier
        // by verifying the request-object JWS against the x5c leaf whose SAN matches this host.
        var clientId = signer.ClientId;
        var presRequest = await store.CreateAsync(
            clientId: clientId,
            credentialType: request.CredentialType,
            requiredClaims: request.RequiredClaims,
            acceptedIssuers: request.AcceptedIssuers,
            baseUrl: issuerUrl,
            // Feature 181 US2 — a multi-credential request carries its full declared query; the
            // per-query verification loop + GetRequestObject both key off DeclaredQuery.
            declaredQuery: request.DeclaredQuery,
            ct: ct);

        var requestUri = $"{issuerUrl}/api/v1/verifier/requests/{presRequest.Id}/request-object";
        var authzRequestUri = $"openid4vp://authorize?client_id={Uri.EscapeDataString(clientId)}&request_uri={Uri.EscapeDataString(requestUri)}";

        return Results.Created($"/api/v1/verifier/requests/{presRequest.Id}", new
        {
            requestId = presRequest.Id,
            authorizationRequestUri = authzRequestUri,
            requestUri,
            nonce = presRequest.Nonce,
            expiresAt = presRequest.ExpiresAt,
            state = presRequest.State.ToString()
        });
    }

    private static async Task<IResult> GetRequestObject(
        Guid requestId,
        PresentationRequestStore store,
        RequestObjectSigner signer,
        CancellationToken ct)
    {
        var request = await store.GetAsync(requestId, ct);
        if (request == null)
            return Results.NotFound(new { error = $"Presentation request '{requestId}' not found" });

        if (request.ExpiresAt < DateTimeOffset.UtcNow)
            return Results.Json(new { error = "Presentation request has expired" }, statusCode: 410);

        // Build the Request Object payload per OpenID4VP.
        // iat is mandatory for signed Request Objects (RFC 9101 §10.8 — CSRF window).
        // iss is the verifier's identifier — same value as client_id for now; spec 096
        // will swap this to the verifier's DID / x509_san_uri.
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // Feature 181 (T010) — the credential ask is expressed in DCQL (OpenID4VP 1.0 final);
        // Presentation Exchange is retired. Built via the ONE shared builder (FR-008).
        // Feature 181 US2 — a multi-credential request carries its full declared query; the single-ask
        // request builds the one-credential query from CredentialType + RequiredClaims (back-compatible).
        var dcqlQuery = request.DeclaredQuery ?? DcqlRequestBuilder.Build(
            [DcqlCredentialAsk.SdJwt(DefaultQueryId, request.CredentialType, request.RequiredClaims ?? [])]);

        var requestObjectPayload = new Dictionary<string, object>
        {
            ["iss"] = request.ClientId,
            ["aud"] = "https://self-issued.me/v2",
            ["iat"] = nowSeconds,
            ["exp"] = request.ExpiresAt.ToUnixTimeSeconds(),
            ["response_type"] = "vp_token",
            ["response_mode"] = "direct_post",
            ["response_uri"] = request.ResponseUri,
            ["client_id"] = request.ClientId,
            ["nonce"] = request.Nonce,
            ["state"] = request.Id.ToString(),
            ["dcql_query"] = JsonSerializer.SerializeToElement(dcqlQuery, DcqlJson.Options)
        };

        // HAIP 1.0 §6.1 and RFC 9101 §4 require the Request Object to be a signed JWT
        // with typ="oauth-authz-req+jwt". Wallets refuse to act on an unsigned JSON body.
        var requestObjectJwt = signer.Sign(requestObjectPayload);

        // Content type per RFC 9101 §4: application/oauth-authz-req+jwt. Wallets use this
        // to distinguish a signed Request Object from an unsigned JSON request.
        return Results.Text(requestObjectJwt, contentType: "application/oauth-authz-req+jwt");
    }

    private static async Task<IResult> HandleDirectPost(
        Guid requestId,
        [FromForm] string? vp_token,
        [FromForm] string? presentation_submission,
        [FromForm] string? state,
        PresentationRequestStore store,
        HaipPresentationVerifier verifier,
        MdocPresentationVerifier mdocVerifier,
        PresentationCallbackRelay? callbackRelay,
        ILoggerFactory loggerFactory,
        CancellationToken ct)
    {
        var logger = loggerFactory.CreateLogger("Sorcha.Haip.Service.Endpoints.VerifierEndpoints");

        var request = await store.GetAsync(requestId, ct);
        if (request == null)
            return Results.NotFound(new { error = "Presentation request not found" });

        if (request.ExpiresAt < DateTimeOffset.UtcNow)
            return Results.Json(new { error = "Presentation request has expired" }, statusCode: 410);

        // Validate state parameter against request ID (OID4VP §6.2 — required for CSRF protection)
        if (string.IsNullOrEmpty(state))
            return Results.BadRequest(new { error = "state parameter is required" });

        if (state != requestId.ToString())
            return Results.BadRequest(new { error = "state parameter does not match the request" });

        if (string.IsNullOrWhiteSpace(vp_token))
            return Results.BadRequest(new { error = "vp_token is required" });

        // Feature 181 (T011) — presentation_submission is retired with Presentation Exchange;
        // its presence marks a legacy-dialect wallet (FR-007).
        if (!string.IsNullOrEmpty(presentation_submission))
        {
            HaipDialectMetrics.RecordRejection("direct-post");
            return Results.BadRequest(new
            {
                error = DcqlErrorCodes.LegacyDialect,
                error_description = "presentation_submission is the retired Presentation Exchange dialect; submit an OpenID4VP 1.0 object-keyed vp_token."
            });
        }

        // Feature 181 (T011) — vp_token is ALWAYS the object-keyed envelope
        // ({ "<queryId>": ["<presentation>"] }) for both formats. A bare compact string is the
        // retired dialect (400 LEGACY_DIALECT); an entry keyed to an id the request never
        // declared fails with DCQL_UNKNOWN_QUERY_ID (FR-003).
        DcqlVpToken envelope;
        try
        {
            envelope = DcqlVpToken.Parse(vp_token);
        }
        catch (DcqlParseException ex)
        {
            HaipDialectMetrics.RecordRejection("direct-post");
            return Results.BadRequest(new { error = ex.Code, error_description = ex.Message });
        }

        // Feature 181 — reject entries keyed to ids the request never declared (FR-003). Single-ask
        // requests declare just DefaultQueryId; multi-credential requests declare their DCQL ids.
        var declaredIds = request.DeclaredQuery is { } declared
            ? declared.Credentials.Select(c => c.Id).ToList()
            : [DefaultQueryId];

        var unknownIds = envelope.Presentations.Keys
            .Where(k => !declaredIds.Contains(k, StringComparer.Ordinal))
            .ToList();
        if (unknownIds.Count > 0)
        {
            return Results.BadRequest(new
            {
                error = DcqlErrorCodes.UnknownQueryId,
                error_description = $"vp_token entries keyed to undeclared query id(s): {string.Join(", ", unknownIds)}."
            });
        }

        // Per-entry format dispatch: an SD-JWT VC presentation is ~-delimited; an mdoc presentation is a
        // bare base64url DeviceResponse (verified via the unified ITrustEvaluator over the issuer x5chain).
        // Issue #1198 — acceptedVcts is the SET the SD-JWT verifier gates on. DCQL meta.vct_values is
        // a set by specification, so passing only its first entry (as this did before the gate
        // existed) would falsely reject a holder presenting a legitimate alternative. mdoc ignores
        // it: an mdoc entry has no vct, and its doctype is gated by the mdoc handler's requirement.
        async Task<VerificationResult> VerifyEntryAsync(
            string presentation, IReadOnlyList<string>? acceptedVcts, List<string>? requiredClaims)
        {
            if (!presentation.Contains('~'))
            {
                return await mdocVerifier.VerifyAsync(
                    presentation, clientId: request.ClientId, nonce: request.Nonce,
                    responseUri: request.ResponseUri, trustPolicy: BuildMdocTrustPolicy(),
                    requiredClaims: requiredClaims, ct: ct);
            }
            return await verifier.VerifyAsync(
                presentation, expectedNonce: request.Nonce, expectedAudience: request.ClientId,
                requiredClaims: requiredClaims, acceptedIssuers: request.AcceptedIssuers,
                acceptedVctValues: acceptedVcts, ct: ct);
        }

        VerificationResult result;
        if (request.DeclaredQuery is null)
        {
            // Single-ask flow (unchanged): the one declared query is DefaultQueryId.
            if (!envelope.Presentations.TryGetValue(DefaultQueryId, out var presentations))
            {
                return Results.BadRequest(new
                {
                    error = DcqlErrorCodes.Invalid,
                    error_description = $"vp_token carries no entry for query id '{DefaultQueryId}'."
                });
            }
            var singleAskVct = string.IsNullOrWhiteSpace(request.CredentialType)
                ? null
                : new[] { request.CredentialType };
            result = await VerifyEntryAsync(presentations[0], singleAskVct, request.RequiredClaims);
        }
        else
        {
            // Multi-query flow (Feature 181 US2): verify each declared query, then apply the
            // credential_sets (or AND-of-all) rule to the overall verdict.
            result = new VerificationResult { PerQuery = new(StringComparer.Ordinal) };
            var validIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var cq in request.DeclaredQuery.Credentials)
            {
                // The whole accepted set, not VctValues[0] — see VerifyEntryAsync. Null for an mdoc
                // query (no vct); its doctype is the mdoc handler's concern.
                var acceptedVcts = cq.Meta.VctValues is { Count: > 0 } ? cq.Meta.VctValues : null;
                var (required, _) = DcqlRequestParser.SplitClaims(cq);
                if (!envelope.Presentations.TryGetValue(cq.Id, out var entries) || entries.Count == 0)
                {
                    result.PerQuery[cq.Id] = new PerQueryVerification { IsValid = false, Errors = { $"No presentation provided for query '{cq.Id}'." } };
                    continue;
                }

                var one = await VerifyEntryAsync(entries[0], acceptedVcts, required.ToList());
                result.PerQuery[cq.Id] = new PerQueryVerification { IsValid = one.IsValid, Issuer = one.Issuer, Errors = one.Errors };
                if (one.IsValid)
                {
                    validIds.Add(cq.Id);
                    result.Issuer ??= one.Issuer;
                    result.TrustEvidence ??= one.TrustEvidence;
                    foreach (var (k, v) in one.VerifiedClaims)
                    {
                        result.VerifiedClaims[$"{cq.Id}:{k}"] = v;
                    }
                }
            }

            result.IsValid = AllRequiredSatisfied(request.DeclaredQuery, validIds);
            if (!result.IsValid)
            {
                result.Errors.Add("One or more required credential queries were not satisfied.");
            }
        }

        // Store the result + the raw submitted envelope (PR B1) so a verifier client can
        // re-validate locally and build its own rich verdict. presentation_submission is
        // gone with the dialect — the stored column stays null (wire shape unchanged).
        await store.MarkCompletedAsync(requestId, result, vp_token, presentationSubmission: null, ct);

        // Feature 111: relay the outcome to Blueprint Service for lifecycle transaction writing.
        if (callbackRelay is not null)
        {
            await callbackRelay.RelayAsync(requestId, result, ct);
        }

        if (result.IsValid)
        {
            logger.LogInformation(
                "Presentation verified for request {RequestId}: {ClaimCount} claims",
                requestId, result.VerifiedClaims.Count);
            return Results.Ok(new { redirect_uri = (string?)null });
        }
        else
        {
            logger.LogWarning(
                "Presentation verification failed for request {RequestId}: {Errors}",
                requestId, string.Join("; ", result.Errors));
            return Results.BadRequest(new
            {
                error = "invalid_presentation",
                error_description = string.Join("; ", result.Errors)
            });
        }
    }

    private static async Task<IResult> GetVerificationResult(
        Guid requestId,
        PresentationRequestStore store,
        CancellationToken ct)
    {
        var request = await store.GetAsync(requestId, ct);
        if (request == null)
            return Results.NotFound(new { error = "Presentation request not found" });

        if (request.Result == null)
        {
            return Results.Ok(new
            {
                requestId = request.Id,
                state = request.State.ToString(),
                result = (VerificationResult?)null,
                // PR B1: raw submitted presentation for client-side re-validation (null pre-submission).
                vpToken = request.SubmittedVpToken,
                presentationSubmission = request.PresentationSubmission
            });
        }

        return Results.Ok(new
        {
            requestId = request.Id,
            state = request.State.ToString(),
            result = request.Result,
            // PR B1: raw submitted presentation so a verifier client can build its own rich verdict.
            vpToken = request.SubmittedVpToken,
            presentationSubmission = request.PresentationSubmission
        });
    }

    /// <summary>
    /// The trust policy applied to mso_mdoc presentations: chain the issuer x5chain to a tenant
    /// X.509 anchor. Blueprint requirements that name a specific operator trust-list add a
    /// <c>trustlist</c> source via their own <c>TrustPolicy</c>; the endpoint default is x509-tenant.
    /// </summary>
    private static TrustPolicy BuildMdocTrustPolicy() => new()
    {
        Sources = [new TrustSourceRef { Kind = TrustSourceKind.X509Tenant, ConfersAssurance = AssuranceLevel.Substantial }],
        Combinator = TrustCombinator.AnyOf,
        MinAssuranceLevel = AssuranceLevel.Low
    };

    /// <summary>
    /// Feature 181 US2 — the overall verdict rule: with <c>credential_sets</c>, every required set must
    /// have one option whose ids all verified; without sets, every declared credential query must verify.
    /// </summary>
    internal static bool AllRequiredSatisfied(DcqlQuery query, ISet<string> validIds)
    {
        if (query.CredentialSets is { Count: > 0 } sets)
        {
            foreach (var set in sets)
            {
                if (!set.Required) continue;
                var met = set.Options.Any(opt => opt.Count > 0 && opt.All(validIds.Contains));
                if (!met) return false;
            }
            return true;
        }

        return query.Credentials.All(c => validIds.Contains(c.Id));
    }
}

/// <summary>Request to create a presentation request.</summary>
public class CreatePresentationRequestBody
{
    /// <summary>Credential type (vct) the presentation must satisfy (required).</summary>
    [Required(AllowEmptyStrings = false)]
    public required string CredentialType { get; init; }

    public List<string>? RequiredClaims { get; init; }
    public List<string>? AcceptedIssuers { get; init; }

    /// <summary>
    /// Feature 181 US2 — optional pre-built DCQL query covering every credential ask (and any
    /// <c>credential_sets</c> alternatives). When supplied, it is served verbatim as the request
    /// object's <c>dcql_query</c>; null ⇒ a single-ask query is built from
    /// <see cref="CredentialType"/> + <see cref="RequiredClaims"/>.
    /// </summary>
    public DcqlQuery? DeclaredQuery { get; init; }
}
