// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Service.Services.Implementation;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage.Presentations;
using Sorcha.PresentationLifecycle.Abstractions;
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
