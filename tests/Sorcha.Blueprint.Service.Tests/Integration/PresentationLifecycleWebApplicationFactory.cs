// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Sorcha.Blueprint.Service.Services.Interfaces;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.Blueprint.Service.Storage.Presentations;
using Sorcha.PresentationLifecycle.Abstractions;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using Sorcha.ServiceClients.Haip;
using Sorcha.ServiceClients.Register;
using Sorcha.ServiceClients.Validator;

namespace Sorcha.Blueprint.Service.Tests.Integration;

/// <summary>
/// WebApplicationFactory for Feature 111 integration tests. Extends the base
/// factory with mocks for IHaipServiceClient, IValidatorServiceClient, and
/// in-memory replacements for IPendingPresentationStore + IPresentationRateLimiter
/// so the Redis layer is bypassed (unit-tested separately) and the HTTP-boundary
/// wiring can be exercised end-to-end.
/// </summary>
public sealed class PresentationLifecycleWebApplicationFactory : BlueprintServiceWebApplicationFactory
{
    public Mock<IHaipServiceClient> HaipClient { get; } = new();
    public Mock<IValidatorServiceClient> ValidatorClient { get; } = new();
    public Mock<IRegisterServiceClient> RegisterClient { get; } = new();
    public InMemoryPendingPresentationStore PendingStore { get; } = new();
    public CountingPresentationRateLimiter RateLimiter { get; } = new();

    /// <summary>
    /// Seed a fake <c>PresentationOutcome</c> transaction for
    /// <c>GetTransactionsByInstanceIdAsync</c> so the US3 retry gate in
    /// <c>ActionExecutionService</c> finds a prior outcome and blocks new
    /// attempts with 409.
    /// </summary>
    public void RegisterClientForRetryGate(
        string instanceId,
        int actionId,
        string outcomeKind,
        string outcomeTxId,
        string registerId)
    {
        RegisterClient
            .Setup(r => r.GetTransactionsByInstanceIdAsync(
                It.IsAny<string>(), instanceId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransactionModel>
            {
                new()
                {
                    TxId = outcomeTxId,
                    RegisterId = registerId,
                    SenderWallet = "test",
                    MetaData = new TransactionMetaData
                    {
                        TransactionType = TransactionType.PresentationOutcome,
                        InstanceId = instanceId,
                        ActionId = (uint)actionId,
                        TrackingData = new Dictionary<string, string>
                        {
                            ["outcomeKind"] = outcomeKind
                        }
                    }
                }
            });
    }

    /// <remarks>
    /// Populate before the first call to <see cref="WebApplicationFactory{TEntryPoint}.CreateClient()"/>.
    /// Additions after host creation have no effect because <c>ConfigureWebHost</c>
    /// only runs once per factory instance (shared via <c>IClassFixture</c>).
    /// </remarks>
    public List<IPresentationConsumer> Consumers { get; } = new();

    /// <summary>
    /// A fake HAIP consumer that always succeeds. Individual tests can add or
    /// override consumers via <see cref="Consumers"/> before the host starts.
    /// </summary>
    public TestHaipConsumer HaipConsumer { get; } = new();

    /// <summary>
    /// Reset all mock state and in-memory stores between tests. The factory is
    /// shared via <c>IClassFixture</c> for speed, so tests must call this at the
    /// start of each method to prevent shared-state bleed (Moq last-setup-wins,
    /// accumulating <see cref="TestHaipConsumer.InvokedContexts"/>, rate-limit
    /// counters, pending hashes, sentinel values).
    /// </summary>
    public void ResetMocksAndState()
    {
        HaipClient.Reset();
        ValidatorClient.Reset();
        RegisterClient.Reset();
        ApplyDefaultMockSetups();
        PendingStore.Clear();
        RateLimiter.Reset();
        HaipConsumer.Reset();
    }

    private void ApplyDefaultMockSetups()
    {
        // Register defaults — blueprint-publish succeeds, instance-tx queries
        // return an empty list (retry gate sees no prior outcomes by default).
        RegisterClient
            .Setup(r => r.PublishBlueprintToRegisterAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BlueprintPublicationResult { PublicationTxId = "tx-publication-1" });
        // Feature 142 — the test principal (org_id ...456) must hold a publish-governance role on
        // the target register so the publish endpoint's server-side governance hard gate (FR-027)
        // passes. This factory replaces the base IRegisterServiceClient mock, so the roster default
        // is re-applied here and survives ResetMocksAndState. (The rehearsal soft gate is satisfied
        // by the base factory's AlwaysPassRehearsalPassStore.)
        RegisterClient
            .Setup(r => r.GetGovernanceRosterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string regId, CancellationToken _) => new GovernanceRosterResponse
            {
                RegisterId = regId,
                MemberCount = 1,
                Members =
                {
                    new RosterMember
                    {
                        Subject = "did:sorcha:org:00000000-0000-0000-0000-000000000456",
                        Role = "Owner",
                        Algorithm = "ED25519",
                        GrantedAt = DateTimeOffset.UtcNow,
                    }
                }
            });
        RegisterClient
            .Setup(r => r.GetTransactionsByInstanceIdAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransactionModel>());
        // Feature 119 — PresentationLifecycleService.IsPredecessorSealedAsync calls
        // GetTransactionAsync to decide whether to take the inline submit path or
        // defer to the seal coordinator. Returning a non-null tx here keeps tests
        // on the inline path (validator IS called, sentinels are written immediately),
        // matching the wire shape these tests assert on. The deferred path is unit
        // tested in PresentationLifecycleServiceTests.
        RegisterClient
            .Setup(r => r.GetTransactionAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string regId, string txId, CancellationToken _) => new TransactionModel
            {
                TxId = txId,
                RegisterId = regId,
                SenderWallet = "test",
                TimeStamp = DateTime.UtcNow
            });
        RegisterClient
            .Setup(r => r.GetRegisterAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string regId, CancellationToken _) => new Sorcha.Register.Models.Register
            {
                Id = regId,
                Name = "Test Register",
                Status = RegisterStatus.Online
            });

        // Each CreatePresentationRequestAsync call generates a fresh RequestId —
        // critical for retry tests where two submissions must produce distinct ids.
        HaipClient
            .Setup(h => h.CreatePresentationRequestAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new CreatePresentationRequestResult(
                RequestId: Guid.NewGuid(),
                AuthorizationRequestUri: "openid4vp://authorize?request_uri=...",
                RequestUri: "https://haip.test/request-object",
                Nonce: "test-nonce",
                ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(10)));

        ValidatorClient
            .Setup(v => v.GetNextSequenceNumberAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(1L);

        ValidatorClient
            .Setup(v => v.SubmitTransactionAsync(
                It.IsAny<TransactionSubmission>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((TransactionSubmission sub, CancellationToken _) =>
                new TransactionSubmissionResult
                {
                    Success = true,
                    TransactionId = sub.TransactionId,
                    RegisterId = sub.RegisterId
                });
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureAppConfiguration((_, config) =>
        {
            // ServiceAuthClient constructs eagerly in some code paths and needs
            // these present; values don't matter because all outbound clients
            // are mocked.
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceAuth:ClientId"] = "test-client",
                ["ServiceAuth:ClientSecret"] = "test-secret",
                ["ServiceAuth:TokenEndpoint"] = "http://localhost/token",
                ["ServiceAuth:Scopes"] = "wallets:sign registers:write blueprints:manage"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHaipServiceClient>();
            services.AddSingleton(HaipClient.Object);

            services.RemoveAll<IValidatorServiceClient>();
            services.AddSingleton(ValidatorClient.Object);

            // Replace the base factory's IRegisterServiceClient mock so this
            // factory can drive RegisterClientForRetryGate() and watch the
            // blueprint-publish call.
            services.RemoveAll<IRegisterServiceClient>();
            services.AddSingleton(RegisterClient.Object);

            services.RemoveAll<IPendingPresentationStore>();
            services.AddSingleton<IPendingPresentationStore>(PendingStore);

            // #1195 Phase 2 (Task 6b) — the sorcha-wallet initiation path (first exercised by
            // SorchaWalletExecuteIntegrationTests) mints a ClaimsFetchToken and stashes the
            // served request object; the outcome path stashes disclosed claims. The production
            // Redis stores dereference the base factory's null GetDatabase() and NRE (same
            // reason the seal coordinator is replaced above) — swap in in-memory doubles.
            services.RemoveAll<IClaimsFetchTokenStore>();
            services.AddSingleton<IClaimsFetchTokenStore, InMemoryClaimsFetchTokenStore>();
            services.RemoveAll<IRequestObjectStore>();
            services.AddSingleton<IRequestObjectStore, InMemoryRequestObjectStore>();
            services.RemoveAll<IDisclosedClaimsStore>();
            services.AddSingleton<IDisclosedClaimsStore, InMemoryDisclosedClaimsStore>();

            services.RemoveAll<IPresentationRateLimiter>();
            services.AddSingleton<IPresentationRateLimiter>(RateLimiter);

            services.RemoveAll<IEnumerable<IPresentationConsumer>>();
            services.RemoveAll<IPresentationConsumer>();
            services.AddSingleton<IPresentationConsumer>(HaipConsumer);
            foreach (var extra in Consumers)
            {
                services.AddSingleton(extra);
            }

            // Feature 119 — RedisPresentationSealCoordinator dereferences the
            // IConnectionMultiplexer mock's GetDatabase() (which returns null in
            // the base factory) and NREs. Replace it with a no-op test double —
            // the inline submit path runs unconditionally because GetTransactionAsync
            // is mocked to return a sealed predecessor (see ApplyDefaultMockSetups).
            // The advancement enqueue (success path, line 477+) becomes a no-op,
            // which is fine: the integration tests assert on sentinel state and
            // validator submissions, not on advancement timing (covered by unit tests).
            services.RemoveAll<IPresentationSealCoordinator>();
            services.AddSingleton<IPresentationSealCoordinator, NoOpPresentationSealCoordinator>();

            ApplyDefaultMockSetups();
        });
    }
}

/// <summary>
/// No-op test double for <see cref="IPresentationSealCoordinator"/>. The
/// production <c>RedisPresentationSealCoordinator</c> needs a real Redis
/// database, which the integration test factory does not provide. Tests that
/// exercise the deferred submit/advancement paths live in
/// <c>PresentationLifecycleServiceTests</c> with a Moq'd coordinator.
/// </summary>
internal sealed class NoOpPresentationSealCoordinator : IPresentationSealCoordinator
{
    public Task EnqueueSubmissionAsync(SealAwaitingSubmission submission, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task EnqueueAdvancementAsync(SealAwaitingAdvancement advancement, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<int> DrainOnSealAsync(string sealedTxId, CancellationToken cancellationToken = default)
        => Task.FromResult(0);

    public Task<SweepResult> RunRecoverySweepAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new SweepResult(0, 0));
}

/// <summary>In-memory test double for <see cref="IClaimsFetchTokenStore"/> (no TTL enforcement).</summary>
internal sealed class InMemoryClaimsFetchTokenStore : IClaimsFetchTokenStore
{
    private readonly ConcurrentDictionary<string, Guid> _tokens = new();

    public Task StoreAsync(string token, Guid presentationRequestId, TimeSpan ttl, CancellationToken ct = default)
    {
        _tokens[token] = presentationRequestId;
        return Task.CompletedTask;
    }

    public Task<Guid?> GetAndRemoveAsync(string token, CancellationToken ct = default)
        => Task.FromResult<Guid?>(_tokens.TryRemove(token, out var id) ? id : null);
}

/// <summary>In-memory test double for <see cref="IRequestObjectStore"/> (no TTL enforcement).</summary>
internal sealed class InMemoryRequestObjectStore : IRequestObjectStore
{
    private readonly ConcurrentDictionary<Guid, string> _objects = new();

    public Task StoreAsync(Guid presentationRequestId, string requestObjectJwt, TimeSpan ttl, CancellationToken ct = default)
    {
        _objects[presentationRequestId] = requestObjectJwt;
        return Task.CompletedTask;
    }

    public Task<string?> GetAsync(Guid presentationRequestId, CancellationToken ct = default)
        => Task.FromResult(_objects.GetValueOrDefault(presentationRequestId));
}

/// <summary>In-memory test double for <see cref="IDisclosedClaimsStore"/> (no TTL enforcement).</summary>
internal sealed class InMemoryDisclosedClaimsStore : IDisclosedClaimsStore
{
    private readonly ConcurrentDictionary<Guid, IReadOnlyDictionary<string, object>> _claims = new();

    public Task StoreAsync(Guid presentationRequestId, IReadOnlyDictionary<string, object> claims, TimeSpan ttl, CancellationToken ct = default)
    {
        _claims[presentationRequestId] = claims;
        return Task.CompletedTask;
    }

    public Task<IReadOnlyDictionary<string, object>?> GetAsync(Guid presentationRequestId, CancellationToken ct = default)
        => Task.FromResult(_claims.GetValueOrDefault(presentationRequestId));
}

/// <summary>
/// In-memory test double for <see cref="IPendingPresentationStore"/>. No TTL
/// enforcement — tests that care about expiry manipulate the hash directly.
/// </summary>
public sealed class InMemoryPendingPresentationStore : IPendingPresentationStore
{
    private readonly ConcurrentDictionary<Guid, PendingPresentation> _pending = new();
    private readonly ConcurrentDictionary<Guid, string> _sentinel = new();

    public Task StoreAsync(PendingPresentation pending, CancellationToken ct = default)
    {
        _pending[pending.PresentationRequestId] = pending;
        return Task.CompletedTask;
    }

    public Task<PendingPresentation?> GetAsync(Guid presentationRequestId, CancellationToken ct = default)
        => Task.FromResult(_pending.GetValueOrDefault(presentationRequestId));

    public Task DeleteAsync(Guid presentationRequestId, CancellationToken ct = default)
    {
        _pending.TryRemove(presentationRequestId, out _);
        return Task.CompletedTask;
    }

    public Task<bool> TryClaimOutcomeSentinelAsync(
        Guid presentationRequestId, string claimantValue, int validityWindowSeconds, CancellationToken ct = default)
        => Task.FromResult(_sentinel.TryAdd(presentationRequestId, claimantValue));

    public Task<string?> GetOutcomeSentinelAsync(Guid presentationRequestId, CancellationToken ct = default)
        => Task.FromResult(_sentinel.GetValueOrDefault(presentationRequestId));

    public Task SetOutcomeSentinelAsync(
        Guid presentationRequestId, string value, int validityWindowSeconds, CancellationToken ct = default)
    {
        _sentinel[presentationRequestId] = value;
        return Task.CompletedTask;
    }

    public Task DeleteOutcomeSentinelAsync(Guid presentationRequestId, CancellationToken ct = default)
    {
        _sentinel.TryRemove(presentationRequestId, out _);
        return Task.CompletedTask;
    }

    /// <remarks>
    /// Test-only stub: ignores <paramref name="withinDuration"/> and returns all
    /// pending keys up to <paramref name="max"/>. The Redis adapter honours the
    /// TTL-window filter; integration tests that exercise sweeper timing must
    /// either extend this or use the real <c>RedisPendingPresentationStore</c>.
    /// </remarks>
    public Task<IReadOnlyList<Guid>> ListPendingNearExpiryAsync(TimeSpan withinDuration, int max, CancellationToken ct = default)
        => Task.FromResult<IReadOnlyList<Guid>>(_pending.Keys.Take(max).ToList());

    /// <summary>Manually force the sentinel value for late-outcome-after-abandonment tests.</summary>
    public void ForceSentinel(Guid id, string value) => _sentinel[id] = value;

    /// <summary>Clear all pending state + sentinels between tests.</summary>
    public void Clear()
    {
        _pending.Clear();
        _sentinel.Clear();
    }
}

/// <summary>
/// In-memory test double for <see cref="IPresentationRateLimiter"/> with
/// counting semantics matching the Redis implementation. Test code sets
/// <see cref="Threshold"/> directly to control the response.
/// </summary>
public sealed class CountingPresentationRateLimiter : IPresentationRateLimiter
{
    private readonly ConcurrentDictionary<string, int> _counts = new();

    public int Threshold { get; set; } = 10;
    public int WindowSeconds { get; set; } = 600;

    public Task<PresentationRateLimitResult> CheckAsync(
        string walletAddress, string registerId, CancellationToken ct = default)
    {
        var key = $"{walletAddress}:{registerId}";
        var count = _counts.AddOrUpdate(key, 1, (_, v) => v + 1);
        var allowed = count <= Threshold;
        return Task.FromResult(new PresentationRateLimitResult(
            Allowed: allowed,
            CurrentCount: count,
            Threshold: Threshold,
            RetryAfter: allowed ? null : TimeSpan.FromSeconds(WindowSeconds)));
    }

    /// <summary>Reset counters between tests.</summary>
    public void Reset()
    {
        _counts.Clear();
        Threshold = 10;
        WindowSeconds = 600;
    }
}

/// <summary>
/// Controllable HAIP consumer for integration tests. Each test sets
/// <see cref="NextOutcome"/> before POSTing a callback.
/// </summary>
public sealed class TestHaipConsumer : IPresentationConsumer
{
    public string ConsumerName => "haip";

    public PresentationOutcome NextOutcome { get; set; } = DefaultOutcome();

    public List<PresentationInitiationContext> InvokedContexts { get; } = new();

    public Task<PresentationOutcome> VerifyAsync(
        PresentationInitiationContext context, object verifierPayload, CancellationToken ct)
    {
        InvokedContexts.Add(context);
        return Task.FromResult(NextOutcome);
    }

    /// <summary>Reset invocation tracking + outcome between tests.</summary>
    public void Reset()
    {
        InvokedContexts.Clear();
        NextOutcome = DefaultOutcome();
    }

    private static PresentationOutcome DefaultOutcome() => new(
        Kind: PresentationOutcomeKind.Success,
        VerifiedClaims: new Dictionary<string, object> { ["name"] = "Test" },
        Reason: null,
        VerifierDiagnostics: null,
        PresentationSubmissionHash: "sha256:test");
}
