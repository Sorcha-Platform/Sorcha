// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Moq;
using Sorcha.Blueprint.Service.Storage;
using Sorcha.Blueprint.Service.Storage.Presentations;
using Sorcha.PresentationLifecycle.Abstractions;
using Sorcha.ServiceClients.Haip;
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
    public InMemoryPendingPresentationStore PendingStore { get; } = new();
    public CountingPresentationRateLimiter RateLimiter { get; } = new();
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
        ApplyDefaultMockSetups();
        PendingStore.Clear();
        RateLimiter.Reset();
        HaipConsumer.Reset();
    }

    private void ApplyDefaultMockSetups()
    {
        HaipClient
            .Setup(h => h.CreatePresentationRequestAsync(
                It.IsAny<string>(),
                It.IsAny<List<string>?>(),
                It.IsAny<List<string>?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CreatePresentationRequestResult(
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

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IHaipServiceClient>();
            services.AddSingleton(HaipClient.Object);

            services.RemoveAll<IValidatorServiceClient>();
            services.AddSingleton(ValidatorClient.Object);

            services.RemoveAll<IPendingPresentationStore>();
            services.AddSingleton<IPendingPresentationStore>(PendingStore);

            services.RemoveAll<IPresentationRateLimiter>();
            services.AddSingleton<IPresentationRateLimiter>(RateLimiter);

            services.RemoveAll<IEnumerable<IPresentationConsumer>>();
            services.RemoveAll<IPresentationConsumer>();
            services.AddSingleton<IPresentationConsumer>(HaipConsumer);
            foreach (var extra in Consumers)
            {
                services.AddSingleton(extra);
            }

            ApplyDefaultMockSetups();
        });
    }
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
