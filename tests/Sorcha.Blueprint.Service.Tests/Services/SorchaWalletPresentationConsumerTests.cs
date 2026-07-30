// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.PresentationLifecycle.Abstractions;
using Sorcha.Verifier.Engine;
using Sorcha.Verifier.Engine.Dcql;
using Sorcha.Verifier.Engine.Models;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Services;

/// <summary>
/// Feature 127 — verifies the Sorcha-wallet consumer correctly bridges
/// F111's <see cref="IPresentationConsumer"/> contract onto
/// <see cref="IVerifiablePresentationValidator"/>. Covers the
/// success path, the decline-reason mappings, missing-claim
/// detection, payload-type robustness, and the
/// <see cref="IPresentationConsumer.BuildInitiationAsync"/>
/// extension contract.
/// </summary>
public sealed class SorchaWalletPresentationConsumerTests
{
    private readonly Mock<IVerifiablePresentationValidator> _validator = new();
    private readonly SorchaWalletPresentationConsumer _sut;

    public SorchaWalletPresentationConsumerTests()
    {
        _sut = new SorchaWalletPresentationConsumer(_validator.Object, NullLogger<SorchaWalletPresentationConsumer>.Instance);
    }

    private static PresentationInitiationContext NewContext() => new(
        PresentationRequestId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        InstanceId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
        ActionId: 1,
        RegisterId: "reg-test",
        BlueprintId: "bp-test",
        SubmitterWallet: "ws11qqtest",
        RequirementsDigest: new byte[32],
        InitiatedAt: DateTimeOffset.UtcNow,
        VerifierClientId: null,
        CredentialType: "AssuredIdentityCredential",
        RequiredClaimNames: ["givenName", "familyName"],
        PublicBaseUrl: "https://gateway.example");

    /// <summary>Decode the base64url payload (middle segment) of a compact JWT.</summary>
    private static JsonElement DecodeJwtPayload(string jwt)
    {
        var segment = jwt.Split('.')[1];
        var padded = segment.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded));
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    /// <summary>
    /// A context carrying the session fields the lifecycle persists on pending state (nonce +
    /// credential requirement), so <c>VerifyAsync</c> can rebuild a real <see cref="VerifierSession"/>
    /// and drive the validator. Contrast with <see cref="NewContext"/>, which carries no nonce and
    /// is used for the "no rebuildable session" decline paths and the BuildInitiationAsync tests.
    /// </summary>
    private static PresentationInitiationContext ContextWithSession(
        string? verifierClientId = null,
        DateTimeOffset? expiresAt = null,
        IReadOnlyList<string>? requiredClaimNames = null,
        string? credentialType = null) => new(
        PresentationRequestId: Guid.Parse("11111111-1111-1111-1111-111111111111"),
        InstanceId: Guid.Parse("22222222-2222-2222-2222-222222222222"),
        ActionId: 1,
        RegisterId: "reg-test",
        BlueprintId: "bp-test",
        SubmitterWallet: "ws11qqtest",
        RequirementsDigest: new byte[32],
        InitiatedAt: DateTimeOffset.UtcNow.AddMinutes(-1),
        VerifierClientId: verifierClientId,
        CredentialType: credentialType ?? "https://sorcha.dev/vc/assured-identity/v1",
        RequiredClaimNames: requiredClaimNames ?? ["givenName", "familyName"],
        PublicBaseUrl: "https://gateway.example",
        Nonce: "ctx-nonce-1",
        ExpiresAt: expiresAt ?? DateTimeOffset.UtcNow.AddMinutes(5));

    [Fact]
    public void ConsumerName_IsTheRegisteredString()
    {
        _sut.ConsumerName.Should().Be("sorcha-wallet");
    }

    [Fact]
    public async Task VerifyAsync_ReturnsSuccess_WhenValidatorAccepts()
    {
        _validator
            .Setup(v => v.ValidateAsync(
                It.Is<VerifierSession>(s => s.Nonce == "ctx-nonce-1"
                    && s.RequiredVct == "https://sorcha.dev/vc/assured-identity/v1"
                    && s.RequiredClaims.SequenceEqual(new[] { "givenName", "familyName" })),
                "vp-token", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationOutcome
            {
                Accepted = true,
                DisclosedClaims = new Dictionary<string, object?>
                {
                    ["givenName"] = "Sarah",
                    ["familyName"] = "Example"
                },
                Errors = [],
                CompletedAt = DateTimeOffset.UtcNow
            });

        var payload = new SorchaWalletVerificationPayload { VpToken = "vp-token" };

        var outcome = await _sut.VerifyAsync(ContextWithSession(), payload, CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Success);
        outcome.VerifiedClaims.Should().NotBeNull();
        outcome.VerifiedClaims!.Should().ContainKey("givenName");
        outcome.VerifiedClaims!.Should().ContainKey("familyName");
        outcome.PresentationSubmissionHash.Should().StartWith("sha256:");
        outcome.Reason.Should().BeNull();
    }

    [Fact]
    public async Task VerifyAsync_EmitsAllVerifiedDisclosedClaims_RequiredClaimsActOnlyAsGate()
    {
        // Task 6 fix round — full-disclosure pass-through (design §4.1): requiredClaims GATE
        // the presentation (must all be present), but VerifiedClaims carries EVERY claim the
        // validator verified from the citizen's consented disclosure. Minimal disclosure is
        // enforced at the wallet's consent sheet (what enters the vp_token) and by the
        // validator's digest anchoring (only issuer-committed disclosures verify) — not by a
        // server-side truncation that would mint device copies with holes.
        var context = ContextWithSession(requiredClaimNames: ["givenName"]);
        _validator
            .Setup(v => v.ValidateAsync(
                It.Is<VerifierSession>(s => s.RequiredClaims.SequenceEqual(new[] { "givenName" })),
                It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationOutcome
            {
                Accepted = true,
                DisclosedClaims = new Dictionary<string, object?>
                {
                    ["givenName"] = "Sarah",
                    ["familyName"] = "Example",
                    ["email"] = "sarah@example.org",
                    ["portrait"] = "base64…"
                },
                Errors = [],
                CompletedAt = DateTimeOffset.UtcNow
            });

        var outcome = await _sut.VerifyAsync(context,
            new SorchaWalletVerificationPayload { VpToken = "vp" },
            CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Success);
        outcome.VerifiedClaims!.Keys.Should().BeEquivalentTo(
            new[] { "givenName", "familyName", "email", "portrait" },
            "every verified disclosed claim passes through — the copy mirrors the root");
    }

    [Fact]
    public async Task VerifyAsync_RootWithoutOptionalClaim_PassesGate_AndCopiesOnlyWhatTheRootCarries()
    {
        // A citizen with no middle name: the root never carried the claim, the gate requires
        // only the core, and the emitted set gracefully omits it — no failure, no hole.
        var context = ContextWithSession(requiredClaimNames: ["givenName", "familyName", "dateOfBirth"]);
        _validator
            .Setup(v => v.ValidateAsync(
                It.Is<VerifierSession>(s => s.RequiredClaims.SequenceEqual(new[] { "givenName", "familyName", "dateOfBirth" })),
                It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationOutcome
            {
                Accepted = true,
                DisclosedClaims = new Dictionary<string, object?>
                {
                    ["givenName"] = "Sarah",
                    ["familyName"] = "Example",
                    ["dateOfBirth"] = "1968-04-12",
                    ["email"] = "sarah@example.org",
                    ["address"] = "12 Brae Road"
                    // no middleName, no fullName, no portrait — the root never carried them
                },
                Errors = [],
                CompletedAt = DateTimeOffset.UtcNow
            });

        var outcome = await _sut.VerifyAsync(context,
            new SorchaWalletVerificationPayload { VpToken = "vp" },
            CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Success,
            "an absent OPTIONAL claim must never fail the bind");
        outcome.VerifiedClaims!.Keys.Should().BeEquivalentTo(
            new[] { "givenName", "familyName", "dateOfBirth", "email", "address" });
        outcome.VerifiedClaims!.Should().NotContainKey("middleName");
    }

    [Fact]
    public async Task VerifyAsync_ReturnsSchemaMismatch_WhenRequiredClaimMissing()
    {
        var context = ContextWithSession(requiredClaimNames: ["givenName", "familyName"]);
        _validator
            .Setup(v => v.ValidateAsync(
                It.Is<VerifierSession>(s => s.RequiredClaims.SequenceEqual(new[] { "givenName", "familyName" })),
                It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationOutcome
            {
                Accepted = true,
                DisclosedClaims = new Dictionary<string, object?> { ["givenName"] = "Sarah" },
                Errors = [],
                CompletedAt = DateTimeOffset.UtcNow
            });

        var outcome = await _sut.VerifyAsync(context,
            new SorchaWalletVerificationPayload { VpToken = "vp" },
            CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Decline);
        outcome.Reason.Should().Be(PresentationDeclineReason.SchemaMismatch);
    }

    [Theory]
    [InlineData("credential revoked",            PresentationDeclineReason.Revoked)]
    [InlineData("credential expired",            PresentationDeclineReason.ExpiredCredential)]
    [InlineData("issuer not trusted",            PresentationDeclineReason.WrongIssuer)]
    [InlineData("KB-JWT signature invalid",      PresentationDeclineReason.SignatureInvalid)]
    [InlineData("claim disclosure mismatch",     PresentationDeclineReason.SchemaMismatch)]
    [InlineData("network timeout",               PresentationDeclineReason.VerifierError)]
    public async Task VerifyAsync_MapsValidatorErrors_ToDeclineReason(string error, PresentationDeclineReason expected)
    {
        var context = ContextWithSession(requiredClaimNames: ["givenName"]);
        _validator
            .Setup(v => v.ValidateAsync(It.IsAny<VerifierSession>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationOutcome
            {
                Accepted = false,
                DisclosedClaims = new Dictionary<string, object?>(),
                Errors = [error],
                CompletedAt = DateTimeOffset.UtcNow
            });

        var outcome = await _sut.VerifyAsync(context,
            new SorchaWalletVerificationPayload { VpToken = "vp" },
            CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Decline);
        outcome.Reason.Should().Be(expected);
    }

    [Fact]
    public async Task VerifyAsync_ReturnsVerifierError_WhenPayloadHasNoSession()
    {
        var payload = new SorchaWalletVerificationPayload { VpToken = "vp" };

        var outcome = await _sut.VerifyAsync(NewContext(), payload, CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Decline);
        outcome.Reason.Should().Be(PresentationDeclineReason.VerifierError);
        outcome.VerifierDiagnostics.Should().NotBeNull();
        outcome.VerifierDiagnostics!["error"].Should().Be("session-missing");
    }

    // ── #1195 Phase 2 / Task 6b (C) — session reconstruction from pending context (T032) ──
    // G2 fix — the wire payload carries no `session` field at all any more (see
    // ContextWithSession above, declared with the class's other fixtures). Every test below
    // proves the ONLY session the validator ever sees is the one rebuilt from `context`.

    [Fact]
    public async Task VerifyAsync_NoPayloadSession_ReconstructsSessionFromContext()
    {
        // The wallet posts only {vpToken} — the session the validator needs is rebuilt
        // from the pending-presentation context (nonce, vct, required claims, client id).
        VerifierSession? seen = null;
        _validator
            .Setup(v => v.ValidateAsync(It.IsAny<VerifierSession>(), "vp-token", null, It.IsAny<CancellationToken>()))
            .Callback((VerifierSession s, string _, string? _, CancellationToken _) => seen = s)
            .ReturnsAsync(new VerificationOutcome
            {
                Accepted = true,
                DisclosedClaims = new Dictionary<string, object?>
                {
                    ["givenName"] = "Sarah",
                    ["familyName"] = "Example"
                },
                Errors = [],
                CompletedAt = DateTimeOffset.UtcNow
            });

        var outcome = await _sut.VerifyAsync(
            ContextWithSession(verifierClientId: "did:sorcha:org:aias"),
            new SorchaWalletVerificationPayload { VpToken = "vp-token" },
            CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Success);
        seen.Should().NotBeNull("the consumer must rebuild the VerifierSession from the context");
        seen!.Nonce.Should().Be("ctx-nonce-1", "the KB-JWT nonce check binds to the initiation nonce");
        seen.RequiredVct.Should().Be("https://sorcha.dev/vc/assured-identity/v1");
        seen.RequiredClaims.Should().BeEquivalentTo(["givenName", "familyName"]);
        seen.ClientId.Should().Be("did:sorcha:org:aias",
            "the KB-JWT aud check must bind to the SAME client_id the request object carried");
    }

    [Fact]
    public async Task VerifyAsync_ClientSuppliedSessionInJson_IsIgnored_ServerSessionUsed()
    {
        // G2 regression — an authenticated citizen could POST their OWN `session` object in the
        // callback JSON, carrying an attacker-chosen RequiredVct, an EMPTIED RequiredClaims gate,
        // and an attacker nonce/clientId. The consumer used it VERBATIM instead of rebuilding from
        // the server's pending row (context), so a presentation of ANY held credential could
        // satisfy ANY credential gate. The fix must ignore a client-supplied session entirely and
        // always rebuild from context.
        VerifierSession? seen = null;
        _validator
            .Setup(v => v.ValidateAsync(It.IsAny<VerifierSession>(), "vp-token", null, It.IsAny<CancellationToken>()))
            .Callback((VerifierSession s, string _, string? _, CancellationToken _) => seen = s)
            .ReturnsAsync(new VerificationOutcome
            {
                Accepted = true,
                DisclosedClaims = new Dictionary<string, object?>
                {
                    ["givenName"] = "Sarah",
                    ["familyName"] = "Example"
                },
                Errors = [],
                CompletedAt = DateTimeOffset.UtcNow
            });

        // The attacker's own session, shaped to trivially pass: a weaker/different vct, an
        // EMPTY required-claims gate, and a nonce/clientId of the attacker's choosing.
        var attackerPayloadJson = JsonSerializer.Serialize(new
        {
            vpToken = "vp-token",
            session = new
            {
                sessionId = "attacker-sess",
                clientId = "did:sorcha:org:attacker",
                nonce = "attacker-nonce",
                requiredVct = "SomeOtherWeakerCredential",
                requiredClaims = Array.Empty<string>(),
                purpose = "credential-gate",
                createdAt = DateTimeOffset.UtcNow,
                expiresAt = DateTimeOffset.UtcNow.AddMinutes(10)
            }
        });
        using var doc = JsonDocument.Parse(attackerPayloadJson);

        var outcome = await _sut.VerifyAsync(
            ContextWithSession(verifierClientId: "did:sorcha:org:aias"),
            doc.RootElement.Clone(),
            CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Success);
        seen.Should().NotBeNull("the validator must still be called with a server-rebuilt session");
        seen!.Nonce.Should().Be("ctx-nonce-1",
            "the server's pending-row nonce must be used — never the client-supplied one");
        seen.RequiredVct.Should().Be("https://sorcha.dev/vc/assured-identity/v1",
            "the server's credential type must be used — never a client-chosen one");
        seen.RequiredClaims.Should().BeEquivalentTo(["givenName", "familyName"],
            "the server's required-claims gate must be used — never a client-emptied list");
        seen.ClientId.Should().Be("did:sorcha:org:aias",
            "the server-resolved verifier client id must be used — never the client-supplied one");
    }

    [Fact]
    public async Task VerifyAsync_ReconstructedSession_ClientIdFallback_MatchesTheRequestObjectPlaceholder()
    {
        // BuildInitiationAsync serves client_id 'did:sorcha:org:UNKNOWN' when no verifier DID
        // resolves; the reconstructed session must use the SAME fallback or the wallet's
        // aud-bound KB-JWT can never verify.
        VerifierSession? seen = null;
        _validator
            .Setup(v => v.ValidateAsync(It.IsAny<VerifierSession>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .Callback((VerifierSession s, string _, string? _, CancellationToken _) => seen = s)
            .ReturnsAsync(new VerificationOutcome
            {
                Accepted = true,
                DisclosedClaims = new Dictionary<string, object?>
                {
                    ["givenName"] = "S",
                    ["familyName"] = "E"
                },
                Errors = [],
                CompletedAt = DateTimeOffset.UtcNow
            });

        await _sut.VerifyAsync(
            ContextWithSession(verifierClientId: null),
            new SorchaWalletVerificationPayload { VpToken = "vp" },
            CancellationToken.None);

        var descriptor = await _sut.BuildInitiationAsync(
            ContextWithSession(verifierClientId: null), CancellationToken.None);
        var servedClientId = System.Web.HttpUtility.ParseQueryString(
            new Uri(descriptor.AuthorizationRequestUri.Replace("openid4vp://authorize", "https://x/a")).Query)["client_id"];

        seen!.ClientId.Should().Be(servedClientId,
            "session ClientId and request-object client_id must come from the same resolution rule");
    }

    [Fact]
    public async Task VerifyAsync_ContextSessionExpired_DeclinesNamed_AndNeverValidates()
    {
        var outcome = await _sut.VerifyAsync(
            ContextWithSession(expiresAt: DateTimeOffset.UtcNow.AddMinutes(-2)),
            new SorchaWalletVerificationPayload { VpToken = "vp" },
            CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Decline);
        outcome.VerifierDiagnostics.Should().NotBeNull();
        outcome.VerifierDiagnostics!["error"].Should().Be("session-expired",
            "an expired session must be distinguishable from an unknown one");
        _validator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task VerifyAsync_ContextWithoutNonce_StaysSessionMissing()
    {
        // A context that predates the session wiring (no nonce persisted) cannot form a
        // verifiable session — the named session-missing decline is preserved.
        var outcome = await _sut.VerifyAsync(
            NewContext(),
            new SorchaWalletVerificationPayload { VpToken = "vp" },
            CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Decline);
        outcome.VerifierDiagnostics!["error"].Should().Be("session-missing");
        _validator.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task VerifyAsync_ReturnsVerifierError_WhenPayloadTypeIsUnexpected()
    {
        var outcome = await _sut.VerifyAsync(NewContext(), 42, CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Decline);
        outcome.Reason.Should().Be(PresentationDeclineReason.VerifierError);
    }

    [Fact]
    public async Task VerifyAsync_AcceptsJsonElementPayload_AndDeserialises()
    {
        // G2 fix — the wire payload no longer carries a `session` field at all, so a JsonElement
        // payload has nothing to smuggle a session through. The consumer must still rebuild the
        // VerifierSession from context and reach the validator.
        _validator
            .Setup(v => v.ValidateAsync(It.IsAny<VerifierSession>(), "vp-from-json", null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new VerificationOutcome
            {
                Accepted = true,
                DisclosedClaims = new Dictionary<string, object?> { ["givenName"] = "S" },
                Errors = [],
                CompletedAt = DateTimeOffset.UtcNow
            });

        var json = JsonSerializer.Serialize(new SorchaWalletVerificationPayload
        {
            VpToken = "vp-from-json"
        });
        using var doc = JsonDocument.Parse(json);

        var outcome = await _sut.VerifyAsync(
            ContextWithSession(requiredClaimNames: ["givenName"]), doc.RootElement.Clone(), CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Success);
    }

    [Fact]
    public async Task VerifyAsync_ReturnsVerifierError_WhenValidatorThrows()
    {
        var context = ContextWithSession(requiredClaimNames: ["givenName"]);
        _validator
            .Setup(v => v.ValidateAsync(It.IsAny<VerifierSession>(), It.IsAny<string>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var outcome = await _sut.VerifyAsync(context,
            new SorchaWalletVerificationPayload { VpToken = "vp" },
            CancellationToken.None);

        outcome.Kind.Should().Be(PresentationOutcomeKind.Decline);
        outcome.Reason.Should().Be(PresentationDeclineReason.VerifierError);
        outcome.VerifierDiagnostics!["error"].Should().Be(nameof(InvalidOperationException));
    }

    [Fact]
    public async Task BuildInitiationAsync_ReturnsAuthorizeUri_WithRequestUriPointingAtServedRequestObject()
    {
        // Feature 181 (T014) — the authorize URI is the request_uri form: it carries ONLY
        // client_id + request_uri; the ask itself lives in the served request object.
        var ctx = NewContext();

        var descriptor = await _sut.BuildInitiationAsync(ctx, CancellationToken.None);

        descriptor.Should().NotBeNull();
        descriptor.AuthorizationRequestUri.Should().StartWith("openid4vp://authorize?client_id=");
        descriptor.AuthorizationRequestUri.Should().Contain("&request_uri=");

        var expectedRequestUri =
            $"https://gateway.example/api/presentations/{ctx.PresentationRequestId:N}/request-object";
        descriptor.RequestUri.Should().Be(expectedRequestUri);
        descriptor.AuthorizationRequestUri.Should().Contain(Uri.EscapeDataString(expectedRequestUri));
        descriptor.Nonce.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task BuildInitiationAsync_RequestObjectJwt_CarriesDcqlQueryAndCallbackUris()
    {
        var ctx = NewContext();

        var descriptor = await _sut.BuildInitiationAsync(ctx, CancellationToken.None);

        descriptor.RequestObjectJwt.Should().NotBeNullOrWhiteSpace();
        var payload = DecodeJwtPayload(descriptor.RequestObjectJwt!);

        payload.GetProperty("client_id").GetString().Should().Be("did:sorcha:org:UNKNOWN");
        payload.GetProperty("response_type").GetString().Should().Be("vp_token");
        payload.GetProperty("response_mode").GetString().Should().Be("direct_post");
        payload.GetProperty("response_uri").GetString().Should().EndWith(
            $"/api/presentations/callbacks/sorcha-wallet/{ctx.PresentationRequestId}");
        payload.GetProperty("nonce").GetString().Should().Be(descriptor.Nonce);
        payload.GetProperty("state").GetString().Should().Be(ctx.PresentationRequestId.ToString());

        // dcql_query — single ask keyed "credential" carrying vct + claim paths.
        var dcql = payload.GetProperty("dcql_query");
        var credentials = dcql.GetProperty("credentials");
        credentials.GetArrayLength().Should().Be(1);
        var credential = credentials[0];
        credential.GetProperty("id").GetString().Should().Be("credential");
        credential.GetProperty("format").GetString().Should().Be("dc+sd-jwt");
        credential.GetProperty("meta").GetProperty("vct_values")[0].GetString()
            .Should().Be("AssuredIdentityCredential");

        var claimPaths = credential.GetProperty("claims").EnumerateArray()
            .Select(c => c.GetProperty("path")[0].GetString())
            .ToList();
        claimPaths.Should().BeEquivalentTo(new[] { "givenName", "familyName" });
    }

    [Fact]
    public async Task BuildInitiationAsync_RequestObjectJwt_IsUnsignedWithAuthzReqType()
    {
        var descriptor = await _sut.BuildInitiationAsync(NewContext(), CancellationToken.None);

        descriptor.RequestObjectJwt!.Should().EndWith(".", "the unsigned JWT carries an empty signature segment");
        var headerSegment = descriptor.RequestObjectJwt!.Split('.')[0];
        var padded = headerSegment.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
        var header = JsonSerializer.Deserialize<JsonElement>(
            System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(padded)));

        header.GetProperty("alg").GetString().Should().Be("none");
        header.GetProperty("typ").GetString().Should().Be("oauth-authz-req+jwt");
    }

    [Fact]
    public async Task BuildInitiationAsync_GeneratesFreshNoncePerCall()
    {
        var ctx = NewContext();

        var first = await _sut.BuildInitiationAsync(ctx, CancellationToken.None);
        var second = await _sut.BuildInitiationAsync(ctx, CancellationToken.None);

        first.Nonce.Should().NotBe(second.Nonce);
        DecodeJwtPayload(first.RequestObjectJwt!).GetProperty("nonce").GetString()
            .Should().Be(first.Nonce);
        DecodeJwtPayload(second.RequestObjectJwt!).GetProperty("nonce").GetString()
            .Should().Be(second.Nonce);
    }

    [Fact]
    public async Task BuildInitiationAsync_EmitsResolvedVerifierDid_AsClientId()
    {
        // Spec 5 — the lifecycle service supplies the council org DID via VerifierClientId.
        var ctx = NewContext() with { VerifierClientId = "did:sorcha:org:ws11qstrathcarron" };

        var descriptor = await _sut.BuildInitiationAsync(ctx, CancellationToken.None);

        descriptor.AuthorizationRequestUri.Should().Contain(
            "client_id=" + Uri.EscapeDataString("did:sorcha:org:ws11qstrathcarron"));
        descriptor.AuthorizationRequestUri.Should().NotContain("did:sorcha:org:UNKNOWN");
        DecodeJwtPayload(descriptor.RequestObjectJwt!).GetProperty("client_id").GetString()
            .Should().Be("did:sorcha:org:ws11qstrathcarron");
    }

    [Fact]
    public async Task BuildInitiationAsync_FallsBackToPlaceholder_WhenVerifierDidNull()
    {
        // Graceful degradation — unresolved org DID never blocks the gate.
        var ctx = NewContext() with { VerifierClientId = null };

        var descriptor = await _sut.BuildInitiationAsync(ctx, CancellationToken.None);

        descriptor.AuthorizationRequestUri.Should().Contain(
            "client_id=" + Uri.EscapeDataString("did:sorcha:org:UNKNOWN"));
        DecodeJwtPayload(descriptor.RequestObjectJwt!).GetProperty("client_id").GetString()
            .Should().Be("did:sorcha:org:UNKNOWN");
    }

    [Fact]
    public async Task BuildInitiationAsync_ServesDeclaredMultiCredentialQuery_WhenProvided()
    {
        // Feature 181 US2 (T029) — when the lifecycle service supplies a pre-built multi-credential
        // query (two asks + a credential_sets alternative), the consumer serves it verbatim rather
        // than collapsing to the single-ask build.
        var declared = DcqlRequestBuilder.Build(
            [
                DcqlCredentialAsk.SdJwt("identity", "AssuredIdentityCredential", ["givenName"]),
                DcqlCredentialAsk.SdJwt("residence", "ProofOfAddressCredential", ["postcode"]),
            ],
            alternatives:
            [
                new DcqlAlternativeGroup([["identity"], ["residence"]], Required: true, Purpose: "Prove who you are"),
            ]);
        var ctx = NewContext() with { DeclaredDcqlQueryJson = DcqlRequestBuilder.ToJson(declared) };

        var descriptor = await _sut.BuildInitiationAsync(ctx, CancellationToken.None);

        var dcql = DecodeJwtPayload(descriptor.RequestObjectJwt!).GetProperty("dcql_query");
        var ids = dcql.GetProperty("credentials").EnumerateArray()
            .Select(c => c.GetProperty("id").GetString())
            .ToList();
        ids.Should().BeEquivalentTo(new[] { "identity", "residence" });

        var sets = dcql.GetProperty("credential_sets");
        sets.GetArrayLength().Should().Be(1);
        sets[0].GetProperty("required").GetBoolean().Should().BeTrue();
        sets[0].GetProperty("purpose").GetString().Should().Be("Prove who you are");
    }

    [Fact]
    public async Task BuildInitiationAsync_FallsBackToSingleAsk_WhenDeclaredQueryMalformed()
    {
        // A serialization mishap must never block the gate — the consumer falls back to the
        // single-ask build from CredentialType + RequiredClaimNames.
        var ctx = NewContext() with { DeclaredDcqlQueryJson = "{not valid json" };

        var descriptor = await _sut.BuildInitiationAsync(ctx, CancellationToken.None);

        var dcql = DecodeJwtPayload(descriptor.RequestObjectJwt!).GetProperty("dcql_query");
        var credentials = dcql.GetProperty("credentials");
        credentials.GetArrayLength().Should().Be(1);
        credentials[0].GetProperty("id").GetString().Should().Be("credential");
        credentials[0].GetProperty("meta").GetProperty("vct_values")[0].GetString()
            .Should().Be("AssuredIdentityCredential");
    }
}
