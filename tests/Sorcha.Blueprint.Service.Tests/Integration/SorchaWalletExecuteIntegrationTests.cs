// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Blueprint.Models.Credentials;
using Sorcha.Blueprint.Service.Models.Requests;
using Sorcha.Blueprint.Service.Models.Responses;
using Sorcha.PresentationLifecycle.Abstractions;
using Xunit;
using ActionModel = Sorcha.Blueprint.Models.Action;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;

namespace Sorcha.Blueprint.Service.Tests.Integration;

/// <summary>
/// #1195 Phase 2 / Task 6b (A + B) — integration coverage for the SorchaWallet presentation
/// rail: a starting action gated with <c>presentationSource: SorchaWallet</c> must initiate the
/// F111 lifecycle from <c>/execute</c> exactly like the HAIP source does (202 +
/// AwaitingPresentation + pending state under consumer <c>sorcha-wallet</c>), and the wallet's
/// direct_post target <c>POST /api/presentations/callbacks/sorcha-wallet/{id}</c> must be
/// reachable with a consumer-tier token (route-metadata assertion) and dispatch to the
/// sorcha-wallet consumer.
/// </summary>
public class SorchaWalletExecuteIntegrationTests : IAsyncLifetime
{
    private readonly PresentationLifecycleWebApplicationFactory _factory;
    private readonly TestSorchaWalletConsumer _sorchaWalletConsumer = new();
    private HttpClient _client = null!;
    private const string TestRegister = "reg-sw-execute";
    private const string CitizenWallet = "ws1qcitizen-sw";

    public SorchaWalletExecuteIntegrationTests()
    {
        _factory = new PresentationLifecycleWebApplicationFactory();
        // Must land in Consumers BEFORE the host starts (CreateClient) — the factory
        // registers extras during ConfigureWebHost only.
        _factory.Consumers.Add(_sorchaWalletConsumer);
    }

    public ValueTask InitializeAsync()
    {
        _client = _factory.CreateClient();
        _factory.ResetMocksAndState();
        _client.DefaultRequestHeaders.Add("X-Delegation-Token", "test-delegation-token");
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    private async Task<(string InstanceId, string BlueprintId)> SeedSorchaWalletInstanceAsync()
    {
        var blueprint = new BlueprintModel
        {
            Id = $"bp-sw-{Guid.NewGuid():N}",
            Title = "SorchaWallet-gated blueprint",
            Description = "Citizen presents from the Sorcha Wallet PWA",
            Version = 1,
            Participants = new List<ParticipantModel>
            {
                new() { Id = "citizen", Name = "Citizen" },
                new() { Id = "issuer", Name = "Issuer" }
            },
            Actions = new List<ActionModel>
            {
                new()
                {
                    Id = 1,
                    Title = "Bind your identity to this device",
                    Sender = "citizen",
                    IsStartingAction = true,
                    CredentialRequirements = new List<CredentialRequirement>
                    {
                        new()
                        {
                            Type = "https://sorcha.dev/vc/assured-identity/v1",
                            PresentationSource = PresentationSource.SorchaWallet,
                            RequiredClaims =
                            [
                                new ClaimConstraint { ClaimName = "givenName" },
                                new ClaimConstraint { ClaimName = "familyName" }
                            ]
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
        using var doc = JsonDocument.Parse(await instanceResp.Content.ReadAsStringAsync());
        return (doc.RootElement.GetProperty("id").GetString()!, created.Id);
    }

    [Fact]
    public async Task Execute_SorchaWalletRequired_Returns202_WithAwaitingPresentation_AndSorchaWalletPending()
    {
        var (instanceId, blueprintId) = await SeedSorchaWalletInstanceAsync();

        var response = await _client.PostAsJsonAsync(
            $"/api/instances/{instanceId}/actions/1/execute",
            new ActionSubmissionRequest
            {
                BlueprintId = blueprintId,
                ActionId = "1",
                SenderWallet = CitizenWallet,
                RegisterAddress = TestRegister,
                PayloadData = new Dictionary<string, object>
                {
                    ["deviceKey"] = new Dictionary<string, object>
                    {
                        ["holderJwk"] = new { kty = "EC", crv = "P-256", x = "devX", y = "devY" }
                    }
                }
            });

        var raw = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Accepted,
            $"a SorchaWallet-gated starting action must initiate the F111 lifecycle, not fall into internal verification (body: {raw})");

        var body = await response.Content.ReadFromJsonAsync<ActionSubmissionResponse>();
        body!.AwaitingPresentation.Should().BeTrue();
        body.PresentationRequest.Should().NotBeNull();
        body.PresentationRequest!.PresentationRequestUri.Should().StartWith("openid4vp://",
            "the wallet drives the presentation from this authorization request URI");

        var pending = await _factory.PendingStore.GetAsync(body.PresentationRequest.RequestId);
        pending.Should().NotBeNull();
        pending!.ConsumerName.Should().Be("sorcha-wallet");
        pending.SubmitterWallet.Should().Be(CitizenWallet);
        pending.Nonce.Should().Be(TestSorchaWalletConsumer.TestNonce,
            "Task 6b (C): the initiation nonce must be persisted so the callback can rebuild the verifier session");
        pending.CredentialType.Should().Be("https://sorcha.dev/vc/assured-identity/v1");
        pending.RequiredClaimNames.Should().BeEquivalentTo(["givenName", "familyName"]);
    }

    [Fact]
    public async Task Execute_ThenSorchaWalletCallback_DispatchesToTheSorchaWalletConsumer_WithSessionContext()
    {
        var (instanceId, blueprintId) = await SeedSorchaWalletInstanceAsync();

        var execute = await _client.PostAsJsonAsync(
            $"/api/instances/{instanceId}/actions/1/execute",
            new ActionSubmissionRequest
            {
                BlueprintId = blueprintId,
                ActionId = "1",
                SenderWallet = CitizenWallet,
                RegisterAddress = TestRegister,
                PayloadData = new Dictionary<string, object> { ["note"] = "bind" }
            });
        execute.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var body = await execute.Content.ReadFromJsonAsync<ActionSubmissionResponse>();
        var requestId = body!.PresentationRequest!.RequestId;

        // The wallet's direct_post: JSON {vpToken} to the sorcha-wallet callback route.
        var callback = await _client.PostAsJsonAsync(
            $"/api/presentations/callbacks/sorcha-wallet/{requestId}",
            new { vpToken = "vp-compact~kb" });

        callback.StatusCode.Should().Be(HttpStatusCode.OK,
            "the wallet-reachable callback route must dispatch the outcome");
        _sorchaWalletConsumer.InvokedContexts.Should().ContainSingle();

        var ctx = _sorchaWalletConsumer.InvokedContexts[0];
        ctx.PresentationRequestId.Should().Be(requestId);
        // Task 6b (C) — the session fields persisted at initiation must reach the consumer.
        ctx.Nonce.Should().Be(TestSorchaWalletConsumer.TestNonce);
        ctx.CredentialType.Should().Be("https://sorcha.dev/vc/assured-identity/v1");
        ctx.RequiredClaimNames.Should().BeEquivalentTo(["givenName", "familyName"]);
        ctx.ExpiresAt.Should().NotBeNull("the callback path must know the session's expiry");
    }

    [Fact]
    public void CallbackRoutes_SorchaWalletIsConsumerTier_GenericStaysServiceTier()
    {
        // Task 6b (B) — the policy split: the wallet posts with a CITIZEN token, so the
        // sorcha-wallet callback route carries RequireConsumerAudience; every other
        // consumer's callback stays service-to-service.
        var endpoints = _factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .ToList();

        var sorchaWalletRoute = endpoints.Should().ContainSingle(e =>
                e.RoutePattern.RawText != null &&
                e.RoutePattern.RawText.Contains("callbacks/sorcha-wallet/", StringComparison.Ordinal))
            .Subject;
        sorchaWalletRoute.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Should().Contain(a => a.Policy == "RequireConsumerAudience",
                "the Sorcha wallet PWA posts the outcome with a consumer-tier citizen token");

        var genericRoute = endpoints.Should().ContainSingle(e =>
                e.RoutePattern.RawText != null &&
                e.RoutePattern.RawText.Contains("callbacks/{consumerName}/", StringComparison.Ordinal))
            .Subject;
        genericRoute.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Should().Contain(a => a.Policy == "RequireService",
                "other consumers (HAIP relay) remain service-to-service");
    }
}

/// <summary>
/// Controllable sorcha-wallet consumer for the Task 6b integration tests: implements
/// <see cref="IPresentationConsumer.BuildInitiationAsync"/> (the F127 generic initiation
/// dispatch) and records every <see cref="VerifyAsync"/> context.
/// </summary>
public sealed class TestSorchaWalletConsumer : IPresentationConsumer
{
    public const string TestNonce = "sw-test-nonce-1";

    public string ConsumerName => "sorcha-wallet";

    public List<PresentationInitiationContext> InvokedContexts { get; } = new();

    public PresentationOutcome NextOutcome { get; set; } = new(
        Kind: PresentationOutcomeKind.Success,
        VerifiedClaims: new Dictionary<string, object> { ["givenName"] = "Sarah" },
        Reason: null,
        VerifierDiagnostics: null,
        PresentationSubmissionHash: "sha256:sw-test");

    public Task<PresentationOutcome> VerifyAsync(
        PresentationInitiationContext context, object verifierPayload, CancellationToken cancellationToken)
    {
        InvokedContexts.Add(context);
        return Task.FromResult(NextOutcome);
    }

    public Task<ConsumerInitiationDescriptor> BuildInitiationAsync(
        PresentationInitiationContext context, CancellationToken cancellationToken)
        => Task.FromResult(new ConsumerInitiationDescriptor(
            AuthorizationRequestUri:
                $"openid4vp://authorize?client_id=did%3Asorcha%3Aorg%3Atest&request_uri=https%3A%2F%2Fgw.test%2Fapi%2Fpresentations%2F{context.PresentationRequestId:N}%2Frequest-object",
            RequestUri: $"https://gw.test/api/presentations/{context.PresentationRequestId:N}/request-object",
            Nonce: TestNonce,
            RequestObjectJwt: null));
}
