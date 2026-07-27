// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage.Presentations;
using Sorcha.PresentationLifecycle.Abstractions;
using Sorcha.Verifier.Engine.Dcql;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Integration;

/// <summary>
/// #1310 — the sorcha-wallet presentation callback
/// (<c>POST /api/presentations/callbacks/sorcha-wallet/{id}</c>) must accept BOTH wire shapes
/// that legitimately land on it: the spec-correct OpenID4VP 1.0 <c>direct_post</c>
/// (application/x-www-form-urlencoded <c>vp_token</c> + <c>state</c>) that
/// <c>Sorcha.Wallet.Pwa.Pages.Present</c> actually posts, and the Sorcha-internal
/// application/json <c>{ vpToken }</c> shape <c>DeviceBindingService</c> posts for the
/// bind-to-device flow. These tests exercise the HTTP-boundary content-type branch added to
/// <c>PresentationEndpoints.MapPresentationEndpoints</c> — they assert on the exact payload
/// object handed to <see cref="IPresentationConsumer.VerifyAsync"/>, not just the HTTP status,
/// so a regression that silently mis-shapes the payload (the same class of bug as #1310 itself)
/// would fail here even if the response still came back 200.
/// </summary>
public sealed class SorchaWalletCallbackContentTypeTests : IAsyncLifetime
{
    private readonly PresentationLifecycleWebApplicationFactory _factory;
    private readonly CapturingSorchaWalletConsumer _consumer = new();
    private HttpClient _client = null!;

    public SorchaWalletCallbackContentTypeTests()
    {
        _factory = new PresentationLifecycleWebApplicationFactory();
        // Must land in Consumers BEFORE the host starts (CreateClient) — the factory
        // registers extras during ConfigureWebHost only (see SorchaWalletExecuteIntegrationTests).
        _factory.Consumers.Add(_consumer);
    }

    public ValueTask InitializeAsync()
    {
        _client = _factory.CreateClient();
        _factory.ResetMocksAndState();
        _consumer.CapturedPayloads.Clear();
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private async Task<Guid> SeedPendingAsync()
    {
        var id = Guid.NewGuid();
        await _factory.PendingStore.StoreAsync(new PendingPresentation
        {
            PresentationRequestId = id,
            InstanceId = Guid.NewGuid(),
            ActionId = 1,
            RegisterId = "reg-content-type",
            BlueprintId = "bp-content-type",
            SubmitterWallet = "ws11qcitizen",
            ConsumerName = "sorcha-wallet",
            DraftPayloadJson = "{}",
            CredentialRequirementDigestHex = "deadbeef",
            RecordAbandonment = false,
            OutcomeDetailLevel = "minimal",
            ValidityWindowSeconds = 600,
            CreatedAt = DateTimeOffset.UtcNow,
            Nonce = "test-nonce",
            CredentialType = "AssuredIdentityCredential",
            RequiredClaimNames = ["givenName", "familyName"]
        });
        return id;
    }

    [Fact]
    public async Task Callback_FormEncodedDirectPost_UnwrapsEnvelope_AndDeliversBareCompactStringToConsumer()
    {
        // Arrange — the exact wire shape Present.razor's ConfirmAsync/ConfirmMultiAsync build
        // (DcqlVpToken-wrapped envelope keyed by the single-ask query id "credential").
        var requestId = await SeedPendingAsync();
        const string compactPresentation = "issuer-jwt~disclosure1~disclosure2~kb-jwt";
        var envelopeJson = $$"""{"credential":["{{compactPresentation}}"]}""";

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["vp_token"] = envelopeJson,
            ["state"] = requestId.ToString()
        });

        // Act
        var response = await _client.PostAsync(
            $"/api/presentations/callbacks/sorcha-wallet/{requestId}", form);

        // Assert
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"body: {raw}");

        _consumer.CapturedPayloads.Should().ContainSingle();
        var payload = _consumer.CapturedPayloads[0];
        payload.Should().BeOfType<SorchaWalletVerificationPayload>(
            "the form branch must deliver the same typed shape the consumer's VerifyAsync pattern-matches first, " +
            "carrying the UNWRAPPED bare compact presentation string — not the DCQL envelope");
        ((SorchaWalletVerificationPayload)payload).VpToken.Should().Be(compactPresentation);
    }

    [Fact]
    public async Task Callback_JsonBody_BehavesExactlyAsBefore_DeliversJsonElementToConsumer()
    {
        // Arrange — the Sorcha-internal shape DeviceBindingService.cs:418 posts for
        // bind-to-device, and demos/AIAS/rehearse.ps1 mirrors. Must be untouched by #1310.
        var requestId = await SeedPendingAsync();
        const string compactPresentation = "issuer-jwt~disclosure1~kb-jwt";

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/presentations/callbacks/sorcha-wallet/{requestId}",
            new { vpToken = compactPresentation });

        // Assert
        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"body: {raw}");

        _consumer.CapturedPayloads.Should().ContainSingle();
        var payload = _consumer.CapturedPayloads[0];
        payload.Should().BeOfType<JsonElement>(
            "the JSON branch must keep handing the consumer a raw JsonElement, exactly as before #1310");
        ((JsonElement)payload).GetProperty("vpToken").GetString().Should().Be(compactPresentation);
    }

    [Fact]
    public async Task Callback_FormEncoded_MissingVpToken_Returns400WithNamedError()
    {
        var requestId = await SeedPendingAsync();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["state"] = requestId.ToString()
            // vp_token deliberately omitted
        });

        var response = await _client.PostAsync(
            $"/api/presentations/callbacks/sorcha-wallet/{requestId}", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("vp_token_missing");
        _consumer.CapturedPayloads.Should().BeEmpty("a malformed request must never reach the consumer");
    }

    [Fact]
    public async Task Callback_FormEncoded_MalformedVpToken_ReturnsNamedDcqlError_Not500()
    {
        // A bare compact string (no leading '{') is the retired pre-DCQL dialect — DcqlVpToken.Parse
        // rejects it with a typed DcqlParseException, which the endpoint must turn into a named 400,
        // never an unhandled 500.
        var requestId = await SeedPendingAsync();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["vp_token"] = "not-json-a-bare-compact-string~kb",
            ["state"] = requestId.ToString()
        });

        var response = await _client.PostAsync(
            $"/api/presentations/callbacks/sorcha-wallet/{requestId}", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("LEGACY_DIALECT");
        _consumer.CapturedPayloads.Should().BeEmpty();
    }

    [Fact]
    public async Task Callback_FormEncoded_StateMismatch_Returns400_AndNeverReachesConsumer()
    {
        var requestId = await SeedPendingAsync();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["vp_token"] = """{"credential":["a~b"]}""",
            ["state"] = Guid.NewGuid().ToString() // does not match requestId
        });

        var response = await _client.PostAsync(
            $"/api/presentations/callbacks/sorcha-wallet/{requestId}", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("state_mismatch");
        _consumer.CapturedPayloads.Should().BeEmpty();
    }

    /// <summary>
    /// #1311 — the sorcha-wallet consumer verifies exactly ONE VerifierSession. A multi-entry
    /// envelope previously fell through to <c>.First()</c> and silently verified one arbitrary entry
    /// (Dictionary ordering is unspecified), reporting Success for the whole ask — a citizen
    /// presenting two credentials would be verified on one. This must now be a loud, named refusal.
    /// </summary>
    [Fact]
    public async Task Callback_FormEncoded_MultiEntryEnvelope_Returns400_NamedError_AndNeverReachesConsumer()
    {
        var requestId = await SeedPendingAsync();
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["vp_token"] = """{"credential":["a~b"],"other":["c~d"]}""",
            ["state"] = requestId.ToString()
        });

        var response = await _client.PostAsync(
            $"/api/presentations/callbacks/sorcha-wallet/{requestId}", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("vp_token_multiple_entries");
        _consumer.CapturedPayloads.Should().BeEmpty(
            "a multi-entry envelope must never reach the consumer — verifying one arbitrary entry " +
            "would silently pass a citizen who only satisfied one of two required credentials");
    }

    /// <summary>
    /// #1311 — the envelope's key must match a query id the served request object actually
    /// declared. Seeds <see cref="IRequestObjectStore"/> (the same store the anonymous
    /// <c>GET /request-object</c> route reads) with a request object declaring query id
    /// <c>"the-real-id"</c>, then posts an envelope keyed to a DIFFERENT id.
    /// </summary>
    [Fact]
    public async Task Callback_FormEncoded_EnvelopeKeyNotDeclaredQueryId_ReturnsNamedDcqlError()
    {
        var requestId = await SeedPendingAsync();
        var requestObjects = _factory.Services.GetRequiredService<IRequestObjectStore>();
        await requestObjects.StoreAsync(
            requestId, BuildUnsignedRequestObjectJwt("the-real-id"), TimeSpan.FromMinutes(10));

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["vp_token"] = """{"some-other-id":["a~b"]}""",
            ["state"] = requestId.ToString()
        });

        var response = await _client.PostAsync(
            $"/api/presentations/callbacks/sorcha-wallet/{requestId}", form);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain(DcqlErrorCodes.UnknownQueryId);
        _consumer.CapturedPayloads.Should().BeEmpty();
    }

    /// <summary>
    /// Regression guard for the fix above: when the envelope's key DOES match the declared query
    /// id, the callback must behave exactly as before (200, payload delivered unwrapped).
    /// </summary>
    [Fact]
    public async Task Callback_FormEncoded_EnvelopeKeyMatchesDeclaredQueryId_StillSucceeds()
    {
        var requestId = await SeedPendingAsync();
        var requestObjects = _factory.Services.GetRequiredService<IRequestObjectStore>();
        await requestObjects.StoreAsync(
            requestId, BuildUnsignedRequestObjectJwt("credential"), TimeSpan.FromMinutes(10));

        const string compactPresentation = "issuer-jwt~disclosure~kb-jwt";
        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["vp_token"] = $$"""{"credential":["{{compactPresentation}}"]}""",
            ["state"] = requestId.ToString()
        });

        var response = await _client.PostAsync(
            $"/api/presentations/callbacks/sorcha-wallet/{requestId}", form);

        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, $"body: {raw}");
        _consumer.CapturedPayloads.Should().ContainSingle();
        ((SorchaWalletVerificationPayload)_consumer.CapturedPayloads[0]).VpToken.Should().Be(compactPresentation);
    }

    /// <summary>
    /// Builds the same UNSIGNED (<c>alg: none</c>) request-object JWT shape
    /// <c>SorchaWalletPresentationConsumer.BuildUnsignedJwt</c> serves, carrying a minimal
    /// single-credential <c>dcql_query</c> with the given query id.
    /// </summary>
    private static string BuildUnsignedRequestObjectJwt(string queryId)
    {
        static string B64Url(byte[] bytes) => Convert.ToBase64String(bytes)
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        var header = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["alg"] = "none",
            ["typ"] = "oauth-authz-req+jwt"
        });
        var payload = JsonSerializer.SerializeToUtf8Bytes(new Dictionary<string, object>
        {
            ["dcql_query"] = new Dictionary<string, object>
            {
                ["credentials"] = new[]
                {
                    new Dictionary<string, object>
                    {
                        ["id"] = queryId,
                        ["format"] = "dc+sd-jwt",
                        ["meta"] = new Dictionary<string, object>
                        {
                            ["vct_values"] = new[] { "AssuredIdentityCredential" }
                        }
                    }
                }
            }
        });
        return $"{B64Url(header)}.{B64Url(payload)}.";
    }
}

/// <summary>
/// Test double that records the exact <c>object verifierPayload</c> instance
/// <see cref="IPresentationLifecycleService.HandleOutcomeAsync"/> hands to the consumer, so tests
/// can assert on payload SHAPE (typed record vs. JsonElement, unwrapped vs. enveloped) rather than
/// just the HTTP response.
/// </summary>
public sealed class CapturingSorchaWalletConsumer : IPresentationConsumer
{
    public string ConsumerName => "sorcha-wallet";

    public List<object> CapturedPayloads { get; } = new();

    public PresentationOutcome NextOutcome { get; set; } = new(
        Kind: PresentationOutcomeKind.Success,
        VerifiedClaims: new Dictionary<string, object> { ["givenName"] = "Sarah" },
        Reason: null,
        VerifierDiagnostics: null,
        PresentationSubmissionHash: "sha256:content-type-test");

    public Task<PresentationOutcome> VerifyAsync(
        PresentationInitiationContext context, object verifierPayload, CancellationToken cancellationToken)
    {
        CapturedPayloads.Add(verifierPayload);
        return Task.FromResult(NextOutcome);
    }

    public Task<ConsumerInitiationDescriptor> BuildInitiationAsync(
        PresentationInitiationContext context, CancellationToken cancellationToken)
        => throw new NotSupportedException("Not exercised by these content-type tests.");
}
