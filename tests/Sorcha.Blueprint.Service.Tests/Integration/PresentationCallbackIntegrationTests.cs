// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Sorcha.Blueprint.Service.Endpoints;
using Sorcha.Blueprint.Service.Storage.Presentations;
using Sorcha.PresentationLifecycle.Abstractions;
using Sorcha.ServiceClients.Validator;
using Xunit;

namespace Sorcha.Blueprint.Service.Tests.Integration;

/// <summary>
/// T037-T038, T059 — integration tests for the Feature 111 callback + status
/// endpoints. Exercises the HTTP-boundary wiring (path binding, auth, JSON
/// shape) plus the consumer dispatch → outcome-tx-write → sentinel-state-machine
/// through the real ASP.NET Core pipeline.
/// </summary>
public class PresentationCallbackIntegrationTests : IClassFixture<PresentationLifecycleWebApplicationFactory>
{
    private readonly PresentationLifecycleWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PresentationCallbackIntegrationTests(PresentationLifecycleWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    /// <summary>
    /// Seed a pending presentation in the in-memory store — simulates a prior
    /// successful InitiateAsync — so callback tests don't have to go through
    /// the full /execute pipeline.
    /// </summary>
    private Guid SeedPending(
        string consumerName = "haip",
        string outcomeDetailLevel = "minimal",
        bool recordAbandonment = false)
    {
        var id = Guid.NewGuid();
        _factory.PendingStore.StoreAsync(new PendingPresentation
        {
            PresentationRequestId = id,
            InstanceId = Guid.NewGuid(),
            ActionId = 3,
            RegisterId = "reg-integration",
            BlueprintId = "bp-integration",
            SubmitterWallet = "ws11qtestcitizen",
            ConsumerName = consumerName,
            DraftPayloadJson = "{}",
            CredentialRequirementDigestHex = "deadbeef",
            RecordAbandonment = recordAbandonment,
            OutcomeDetailLevel = outcomeDetailLevel,
            ValidityWindowSeconds = 600,
            CreatedAt = DateTimeOffset.UtcNow,
            InitiatedTransactionId = "tx-initiated-abc"
        }).GetAwaiter().GetResult();
        return id;
    }

    [Fact]
    public async Task Callback_SuccessOutcome_WritesTx_AndMarksSentinelSuccess_T037()
    {
        // Arrange
        var requestId = SeedPending();
        _factory.HaipConsumer.NextOutcome = new PresentationOutcome(
            Kind: PresentationOutcomeKind.Success,
            VerifiedClaims: new Dictionary<string, object> { ["name"] = "Alice" },
            Reason: null,
            VerifierDiagnostics: null,
            PresentationSubmissionHash: "sha256:abc");

        var capturedSubmission = default(TransactionSubmission);
        _factory.ValidatorClient
            .Setup(v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .Callback<TransactionSubmission, CancellationToken>((sub, _) => capturedSubmission = sub)
            .ReturnsAsync((TransactionSubmission sub, CancellationToken _) =>
                new TransactionSubmissionResult { Success = true, TransactionId = sub.TransactionId });

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/presentations/callbacks/haip/{requestId}",
            new { vp_token = "...", state = requestId.ToString() });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PresentationCallbackResponse>();
        body.Should().NotBeNull();
        body!.Kind.Should().Be("Success");
        body.OutcomeTransactionId.Should().NotBeNullOrWhiteSpace();
        body.IdempotentReplay.Should().BeFalse();
        body.LateAfterAbandonment.Should().BeFalse();

        // Sentinel advanced to "success"
        var sentinel = await _factory.PendingStore.GetOutcomeSentinelAsync(requestId);
        sentinel.Should().Be("success");

        // Validator saw a PresentationOutcome submission with outcomeKind=success
        capturedSubmission.Should().NotBeNull();
        capturedSubmission!.Metadata.Should().ContainKey("outcomeKind")
            .WhoseValue.Should().Be("success");

        // HAIP consumer was invoked with the right context
        _factory.HaipConsumer.InvokedContexts.Should().Contain(c =>
            c.PresentationRequestId == requestId);
    }

    [Fact]
    public async Task Callback_DeclineOutcome_WritesTx_AndMarksSentinelDecline_T038a()
    {
        // Arrange
        var requestId = SeedPending();
        _factory.HaipConsumer.NextOutcome = new PresentationOutcome(
            Kind: PresentationOutcomeKind.Decline,
            VerifiedClaims: null,
            Reason: PresentationDeclineReason.ExpiredCredential,
            VerifierDiagnostics: null,
            PresentationSubmissionHash: null);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/presentations/callbacks/haip/{requestId}",
            new { error = "expired_credential" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PresentationCallbackResponse>();
        body!.Kind.Should().Be("Decline");
        body.IdempotentReplay.Should().BeFalse();

        var sentinel = await _factory.PendingStore.GetOutcomeSentinelAsync(requestId);
        sentinel.Should().Be("decline");
    }

    [Fact]
    public async Task Callback_DuplicateCallback_IsIdempotentReplay_NoNewTx_T038b()
    {
        // Arrange
        var requestId = SeedPending();
        _factory.HaipConsumer.NextOutcome = new PresentationOutcome(
            PresentationOutcomeKind.Success, new Dictionary<string, object>(), null, null, "sha");

        int validatorSubmitCount = 0;
        _factory.ValidatorClient
            .Setup(v => v.SubmitTransactionAsync(It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .Callback(() => Interlocked.Increment(ref validatorSubmitCount))
            .ReturnsAsync(new TransactionSubmissionResult { Success = true });

        // Act — first callback
        var first = await _client.PostAsJsonAsync(
            $"/api/presentations/callbacks/haip/{requestId}", new { });
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        var firstBody = await first.Content.ReadFromJsonAsync<PresentationCallbackResponse>();
        firstBody!.IdempotentReplay.Should().BeFalse();

        // Act — duplicate callback (same requestId)
        var second = await _client.PostAsJsonAsync(
            $"/api/presentations/callbacks/haip/{requestId}", new { });

        // Assert
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        var secondBody = await second.Content.ReadFromJsonAsync<PresentationCallbackResponse>();
        secondBody!.IdempotentReplay.Should().BeTrue();
        secondBody.OutcomeTransactionId.Should().BeEmpty();

        // Only one tx hit the validator.
        validatorSubmitCount.Should().Be(1);
    }

    [Fact]
    public async Task Callback_UnknownRequestId_Returns400()
    {
        // Arrange — no pending state seeded
        var response = await _client.PostAsJsonAsync(
            $"/api/presentations/callbacks/haip/{Guid.NewGuid()}",
            new { });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Callback_ConsumerNameMismatch_Returns400()
    {
        // Arrange — pending state says "haip" but callback path says "other"
        var requestId = SeedPending(consumerName: "haip");

        // Act — swap to the unknown consumer via URL path
        var response = await _client.PostAsJsonAsync(
            $"/api/presentations/callbacks/other-consumer/{requestId}",
            new { });

        // Assert — no consumer matches "other-consumer" in DI, InvalidOperationException → 400
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Callback_LateAfterAbandonment_WritesOutcome_MarksAbandonedWithOutcome_T059()
    {
        // Arrange — force the sentinel to "abandoned" as if the sweeper ran already.
        var requestId = SeedPending(recordAbandonment: true);
        _factory.PendingStore.ForceSentinel(requestId, "abandoned");
        _factory.HaipConsumer.NextOutcome = new PresentationOutcome(
            PresentationOutcomeKind.Success,
            new Dictionary<string, object> { ["name"] = "Alice" },
            null, null, "sha");

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/presentations/callbacks/haip/{requestId}", new { });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PresentationCallbackResponse>();
        body!.LateAfterAbandonment.Should().BeTrue();
        body.IdempotentReplay.Should().BeFalse();
        body.OutcomeTransactionId.Should().NotBeNullOrWhiteSpace();

        var sentinel = await _factory.PendingStore.GetOutcomeSentinelAsync(requestId);
        sentinel.Should().Be("abandoned+outcome");
    }

    [Fact]
    public async Task StatusEndpoint_AwaitingPresentation_ReturnsPendingStateOnly()
    {
        // Arrange
        var requestId = SeedPending();

        // Act
        var response = await _client.GetAsync($"/api/presentations/{requestId}/status");

        // Assert — state + expiresAt only; no registerId / instanceId / consumer
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PresentationStatusResponse>();
        body.Should().NotBeNull();
        body!.PresentationRequestId.Should().Be(requestId);
        body.State.Should().Be("awaiting-presentation");
        body.ExpiresAt.Should().NotBeNull();

        // Payload JSON must not leak privileged fields.
        var json = await response.Content.ReadAsStringAsync();
        json.Should().NotContain("registerId");
        json.Should().NotContain("instanceId");
        json.Should().NotContain("consumerName");
    }

    [Fact]
    public async Task StatusEndpoint_AfterSuccessCallback_ReturnsSuccess()
    {
        // Arrange — seed + success callback
        var requestId = SeedPending();
        await _client.PostAsJsonAsync($"/api/presentations/callbacks/haip/{requestId}", new { });

        // Act
        var response = await _client.GetAsync($"/api/presentations/{requestId}/status");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<PresentationStatusResponse>();
        body!.State.Should().Be("success");
    }

    [Fact]
    public async Task StatusEndpoint_UnknownRequestId_Returns404()
    {
        var response = await _client.GetAsync($"/api/presentations/{Guid.NewGuid()}/status");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
