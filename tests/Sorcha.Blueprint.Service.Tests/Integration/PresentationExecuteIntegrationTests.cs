// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Endpoints;
using Sorcha.Blueprint.Service.Middleware;
using Sorcha.Blueprint.Service.Models.Requests;
using Sorcha.Blueprint.Service.Models.Responses;
using Sorcha.Blueprint.Service.Storage.Presentations;
using Xunit;
using ActionModel = Sorcha.Blueprint.Models.Action;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;

namespace Sorcha.Blueprint.Service.Tests.Integration;

/// <summary>
/// T025 / T026 / T050 / T051 — integration tests that drive the full
/// <c>POST /api/instances/{id}/actions/{n}/execute</c> pipeline for actions
/// that carry HAIP <see cref="CredentialRequirement"/>s. Covers:
///   T025 — 202 Accepted + AwaitingPresentation=true + presentation-initiated tx written
///   T026 — 4th attempt with Threshold=3 returns 429 with Retry-After
///   T050 — decline → retry produces a new presentationRequestId; per-attempt sentinels report decline + success
///   T051 — second submission after a successful outcome returns 409 Conflict (US3 retry gate)
/// </summary>
public class PresentationExecuteIntegrationTests
    : IClassFixture<PresentationLifecycleWebApplicationFactory>, IAsyncLifetime
{
    private readonly PresentationLifecycleWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private const string DelegationTokenHeader = "X-Delegation-Token";
    private const string TestRegister = "reg-execute-integration";
    private const string CitizenWallet = "0x1234567890abcdef";

    public PresentationExecuteIntegrationTests(PresentationLifecycleWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    public ValueTask InitializeAsync()
    {
        _factory.ResetMocksAndState();
        _client.DefaultRequestHeaders.Remove(DelegationTokenHeader);
        _client.DefaultRequestHeaders.Add(DelegationTokenHeader, "test-delegation-token");
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    /// <summary>
    /// Create + publish a blueprint whose Action 1 requires a HAIP presentation,
    /// then create an instance of it. Returns the instance id.
    /// </summary>
    private async Task<string> SeedInstanceAsync(string? blueprintIdOverride = null)
    {
        var blueprint = new BlueprintModel
        {
            Id = blueprintIdOverride ?? $"bp-exec-{Guid.NewGuid():N}",
            Title = "HAIP-required blueprint",
            Description = "Citizen submits a HAIP-required action",
            Version = 1,
            Participants = new List<ParticipantModel>
            {
                new() { Id = "citizen", Name = "Citizen" },
                new() { Id = "verifier", Name = "Verifier" }
            },
            Actions = new List<ActionModel>
            {
                new()
                {
                    Id = 1,
                    Title = "Submit with HAIP credential",
                    Sender = "citizen",
                    IsStartingAction = true,
                    CredentialRequirements = new List<CredentialRequirement>
                    {
                        new()
                        {
                            Type = "AssuredIdentityCredential",
                            PresentationSource = PresentationSource.HaipExternalWallet
                        }
                    }
                }
            }
        };

        var create = await _client.PostAsJsonAsync("/api/blueprints", blueprint);
        create.EnsureSuccessStatusCode();
        var created = await create.Content.ReadFromJsonAsync<BlueprintModel>();

        var publish = await _client.PostAsync(
            $"/api/blueprints/{created!.Id}/publish",
            JsonContent.Create(new { registerId = TestRegister }));
        publish.EnsureSuccessStatusCode();

        var instanceResp = await _client.PostAsJsonAsync("/api/instances", new
        {
            blueprintId = created.Id,
            registerId = TestRegister,
            tenantId = "test-tenant-789"
        });
        instanceResp.StatusCode.Should().Be(HttpStatusCode.Created);
        var instanceJson = await instanceResp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(instanceJson);
        return doc.RootElement.GetProperty("id").GetString()!;
    }

    private ActionSubmissionRequest MakeExecuteRequest(string blueprintId) => new()
    {
        BlueprintId = blueprintId,
        ActionId = "1",
        SenderWallet = CitizenWallet,
        RegisterAddress = TestRegister,
        PayloadData = new Dictionary<string, object> { ["applicantNote"] = "test" }
    };

    private async Task<string> GetBlueprintIdAsync(string instanceId)
    {
        var response = await _client.GetAsync($"/api/instances/{instanceId}");
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("blueprintId").GetString()!;
    }

    [Fact]
    public async Task Execute_HaipRequired_Returns202_WithAwaitingPresentation_T025()
    {
        // Arrange
        var instanceId = await SeedInstanceAsync();
        var blueprintId = await GetBlueprintIdAsync(instanceId);

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/instances/{instanceId}/actions/1/execute",
            MakeExecuteRequest(blueprintId));

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        response.Headers.Location.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().StartWith("/api/presentations/");

        var body = await response.Content.ReadFromJsonAsync<ActionSubmissionResponse>();
        body.Should().NotBeNull();
        body!.AwaitingPresentation.Should().BeTrue();
        body.IsComplete.Should().BeFalse();
        body.PresentationRequest.Should().NotBeNull();
        body.PresentationRequest!.PresentationRequestUri.Should().StartWith("openid4vp://");
        body.PresentationRequest.CredentialType.Should().Be("AssuredIdentityCredential");

        // A pending state was stored for this requestId.
        var pending = await _factory.PendingStore.GetAsync(body.PresentationRequest.RequestId);
        pending.Should().NotBeNull();
        pending!.ConsumerName.Should().Be("haip");
        pending.SubmitterWallet.Should().Be(CitizenWallet);
        pending.InitiatedTransactionId.Should().NotBeNullOrWhiteSpace();

        // HAIP was asked to mint a presentation request.
        _factory.HaipClient.Verify(h => h.CreatePresentationRequestAsync(
            "AssuredIdentityCredential",
            It.IsAny<List<string>?>(),
            It.IsAny<List<string>?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Execute_HaipRequired_AboveRateLimit_Returns429_T026()
    {
        // Arrange
        _factory.RateLimiter.Threshold = 3; // tight for the test
        var instanceId = await SeedInstanceAsync();
        var blueprintId = await GetBlueprintIdAsync(instanceId);

        // Act — 3 submissions inside the window should all be 202; the 4th tips
        // over the threshold and must return 429 with a Retry-After header.
        for (int i = 0; i < 3; i++)
        {
            var ok = await _client.PostAsJsonAsync(
                $"/api/instances/{instanceId}/actions/1/execute",
                MakeExecuteRequest(blueprintId));
            ok.StatusCode.Should().Be(HttpStatusCode.Accepted,
                $"submission #{i + 1} should be below the threshold of 3");
        }

        var rejected = await _client.PostAsJsonAsync(
            $"/api/instances/{instanceId}/actions/1/execute",
            MakeExecuteRequest(blueprintId));

        // Assert
        rejected.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        rejected.Headers.TryGetValues("Retry-After", out var retryAfter).Should().BeTrue();
        retryAfter!.First().Should().Match(v => int.Parse(v) > 0);
    }

    [Fact]
    public async Task Execute_DeclineThenRetry_Produces2Initiated_And2Outcome_T050()
    {
        // Arrange
        var instanceId = await SeedInstanceAsync();
        var blueprintId = await GetBlueprintIdAsync(instanceId);

        // First submission — always returns 202.
        var first = await _client.PostAsJsonAsync(
            $"/api/instances/{instanceId}/actions/1/execute",
            MakeExecuteRequest(blueprintId));
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var firstBody = await first.Content.ReadFromJsonAsync<ActionSubmissionResponse>();
        var firstRequestId = firstBody!.PresentationRequest!.RequestId;

        // Decline callback — verifier rejects the credential.
        _factory.HaipConsumer.NextOutcome = new Sorcha.PresentationLifecycle.Abstractions.PresentationOutcome(
            Kind: Sorcha.PresentationLifecycle.Abstractions.PresentationOutcomeKind.Decline,
            VerifiedClaims: null,
            Reason: Sorcha.PresentationLifecycle.Abstractions.PresentationDeclineReason.ExpiredCredential,
            VerifierDiagnostics: null,
            PresentationSubmissionHash: null);
        var declineResp = await _client.PostAsJsonAsync(
            $"/api/presentations/callbacks/haip/{firstRequestId}", new { });
        declineResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Retry — the citizen presents a different (valid) credential.
        var retry = await _client.PostAsJsonAsync(
            $"/api/instances/{instanceId}/actions/1/execute",
            MakeExecuteRequest(blueprintId));
        retry.StatusCode.Should().Be(HttpStatusCode.Accepted,
            "a fresh attempt after a declined outcome must be allowed");
        var retryBody = await retry.Content.ReadFromJsonAsync<ActionSubmissionResponse>();
        var retryRequestId = retryBody!.PresentationRequest!.RequestId;
        retryRequestId.Should().NotBe(firstRequestId,
            "retry must produce a new presentationRequestId");

        // Success callback on the retry.
        _factory.HaipConsumer.NextOutcome = new Sorcha.PresentationLifecycle.Abstractions.PresentationOutcome(
            Kind: Sorcha.PresentationLifecycle.Abstractions.PresentationOutcomeKind.Success,
            VerifiedClaims: new Dictionary<string, object> { ["name"] = "Alice" },
            Reason: null,
            VerifierDiagnostics: null,
            PresentationSubmissionHash: "sha256:retry");
        var successResp = await _client.PostAsJsonAsync(
            $"/api/presentations/callbacks/haip/{retryRequestId}", new { });
        successResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Assert — each requestId has its own sentinel; first is "decline", second is "success".
        var firstSentinel = await _factory.PendingStore.GetOutcomeSentinelAsync(firstRequestId);
        firstSentinel.Should().Be("decline");
        var retrySentinel = await _factory.PendingStore.GetOutcomeSentinelAsync(retryRequestId);
        retrySentinel.Should().Be("success");

        // HAIP was called for both the initial submission and the retry —
        // proxy assertion that two PresentationInitiated tx cycles ran. Direct
        // validator-submission counts are covered by the builder + service
        // unit tests; this integration suite focuses on sentinel state + API
        // surface, not transaction accounting.
        _factory.HaipClient.Verify(h => h.CreatePresentationRequestAsync(
            It.IsAny<string>(), It.IsAny<List<string>?>(), It.IsAny<List<string>?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Execute_AfterSuccessfulOutcome_Returns409_T051()
    {
        // Arrange
        var instanceId = await SeedInstanceAsync();
        var blueprintId = await GetBlueprintIdAsync(instanceId);

        // First submission — 202.
        var first = await _client.PostAsJsonAsync(
            $"/api/instances/{instanceId}/actions/1/execute",
            MakeExecuteRequest(blueprintId));
        first.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var firstBody = await first.Content.ReadFromJsonAsync<ActionSubmissionResponse>();
        var firstRequestId = firstBody!.PresentationRequest!.RequestId;

        // Success callback — writes PresentationOutcome with outcomeKind=success.
        _factory.HaipConsumer.NextOutcome = new Sorcha.PresentationLifecycle.Abstractions.PresentationOutcome(
            Kind: Sorcha.PresentationLifecycle.Abstractions.PresentationOutcomeKind.Success,
            VerifiedClaims: new Dictionary<string, object> { ["name"] = "Alice" },
            Reason: null,
            VerifierDiagnostics: null,
            PresentationSubmissionHash: "sha256:test");

        // Seed the Register mock BEFORE the callback fires. The retry gate in
        // ActionExecutionService consults GetTransactionsByInstanceIdAsync on
        // the *next* /execute, not during the callback itself, so the setup
        // order doesn't create a race — by the time the second submission
        // arrives the outcome tx is visible to the gate.
        _factory.RegisterClientForRetryGate(
            instanceId,
            actionId: 1,
            outcomeKind: "success",
            outcomeTxId: "outcome-tx-success",
            registerId: TestRegister);

        var successResp = await _client.PostAsJsonAsync(
            $"/api/presentations/callbacks/haip/{firstRequestId}", new { });
        successResp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act — second submission must be blocked by the US3 retry gate.
        var second = await _client.PostAsJsonAsync(
            $"/api/instances/{instanceId}/actions/1/execute",
            MakeExecuteRequest(blueprintId));

        // Assert
        second.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }
}
