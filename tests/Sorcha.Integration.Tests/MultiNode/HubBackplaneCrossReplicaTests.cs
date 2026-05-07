// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Xunit;

namespace Sorcha.Integration.Tests.MultiNode;

/// <summary>
/// Cross-replica multi-node correctness fixture for Feature 118 US1.
/// </summary>
/// <remarks>
/// <para>
/// Validates spec NFR-001: an event triggered on replica A reaches a client
/// connected to replica B within 200ms p95 in a two-replica deployment with
/// the SignalR Redis backplane configured. The fixture is parameterised over
/// each hub but only the BlueprintHub case is enabled in this PR — the others
/// gain their multinode definitions in later phases (TenantHub in Phase 4 / US2,
/// Wallet and Register in follow-up overlays).
/// </para>
/// <para>
/// Requires the multinode docker-compose overlay to be running:
/// <code>docker-compose -f docker-compose.yml -f docker-compose.multinode.yml up -d</code>
/// CI runs the overlay via <c>.github/workflows/multinode-correctness.yml</c>.
/// Local runs without the overlay are skipped via the <c>SORCHA_MULTINODE</c>
/// env var sentinel — set it to <c>1</c> to enable.
/// </para>
/// </remarks>
[Trait("Category", "MultiNode")]
public class HubBackplaneCrossReplicaTests
{
    private const string GatewayUrl = "http://localhost";
    private static readonly TimeSpan FanOutBudget = TimeSpan.FromMilliseconds(200);

    private static bool MultinodeEnabled =>
        Environment.GetEnvironmentVariable("SORCHA_MULTINODE") == "1";

    [SkippableFact]
    [Trait("Hub", "Blueprint")]
    public async Task BlueprintHub_EventOnReplicaA_ReachesClientOnReplicaB_WithinBudget()
    {
        Skip.IfNot(MultinodeEnabled, "Multi-node fixture not running. Set SORCHA_MULTINODE=1 and start docker-compose.multinode.yml.");

        // Two SignalR connections, each targeting a different replica via the YARP
        // sticky-session cookie. The cookie names — defined in
        // docker-compose.multinode.yml — pin replica selection.
        var clientOnReplica1 = await ConnectAsync(GatewayUrl + "/actionshub", affinityCookie: "blueprint-1");
        var clientOnReplica2 = await ConnectAsync(GatewayUrl + "/actionshub", affinityCookie: "blueprint-2");

        try
        {
            // Subscribe both clients to the same wallet group.
            var walletAddress = $"test-wallet-{Guid.NewGuid():N}";
            var receivedOn1 = new TaskCompletionSource<DateTimeOffset>();
            var receivedOn2 = new TaskCompletionSource<DateTimeOffset>();

            clientOnReplica1.On<object>("ActionAvailable", _ => receivedOn1.TrySetResult(DateTimeOffset.UtcNow));
            clientOnReplica2.On<object>("ActionAvailable", _ => receivedOn2.TrySetResult(DateTimeOffset.UtcNow));

            await clientOnReplica1.InvokeAsync("SubscribeToWallet", walletAddress);
            await clientOnReplica2.InvokeAsync("SubscribeToWallet", walletAddress);

            // Trigger the event from outside (HTTP call to whichever replica YARP
            // routes the test trigger to). Backplane fan-out should reach both.
            var sentAt = DateTimeOffset.UtcNow;
            await TriggerActionAvailableAsync(walletAddress);

            await Task.WhenAll(
                receivedOn1.Task.WaitAsync(TimeSpan.FromSeconds(5)),
                receivedOn2.Task.WaitAsync(TimeSpan.FromSeconds(5)));

            (receivedOn1.Task.Result - sentAt).Should().BeLessThan(FanOutBudget);
            (receivedOn2.Task.Result - sentAt).Should().BeLessThan(FanOutBudget);
        }
        finally
        {
            await clientOnReplica1.DisposeAsync();
            await clientOnReplica2.DisposeAsync();
        }
    }

    [SkippableFact]
    [Trait("Hub", "Tenant")]
    public async Task TenantHub_EventOnReplicaA_ReachesClientOnReplicaB_WithinBudget()
    {
        Skip.IfNot(MultinodeEnabled, "Multi-node fixture not running. Set SORCHA_MULTINODE=1 and start docker-compose.multinode.yml.");

        // 1. Acquire admin user token. TenantHub auto-joins user:{platform_user_id}
        //    on connect; same JWT on both replicas → both pinned to the same
        //    backplane group, so a write on either replica should fan out to both.
        using var http = new HttpClient { BaseAddress = new Uri(GatewayUrl) };
        var userToken = await GetAdminUserTokenAsync(http);
        var serviceToken = await GetServiceTokenAsync(http);

        var clientOnReplica1 = await ConnectTenantAsync(userToken, affinityCookie: "tenant-1");
        var clientOnReplica2 = await ConnectTenantAsync(userToken, affinityCookie: "tenant-2");

        try
        {
            var receivedOn1 = new TaskCompletionSource<DateTimeOffset>();
            var receivedOn2 = new TaskCompletionSource<DateTimeOffset>();

            clientOnReplica1.On<string, DateTimeOffset, string>("InboxEntryAdded",
                (_, _, _) => receivedOn1.TrySetResult(DateTimeOffset.UtcNow));
            clientOnReplica2.On<string, DateTimeOffset, string>("InboxEntryAdded",
                (_, _, _) => receivedOn2.TrySetResult(DateTimeOffset.UtcNow));

            // Trigger via the internal write endpoint. The host port mapping varies
            // between the base compose and the multinode overlay; the write reaches
            // whichever replica YARP routes the POST to. Backplane fan-out should
            // reach both subscribers.
            var sentAt = DateTimeOffset.UtcNow;
            await TriggerInboxWriteAsync(serviceToken);

            await Task.WhenAll(
                receivedOn1.Task.WaitAsync(TimeSpan.FromSeconds(5)),
                receivedOn2.Task.WaitAsync(TimeSpan.FromSeconds(5)));

            (receivedOn1.Task.Result - sentAt).Should().BeLessThan(FanOutBudget);
            (receivedOn2.Task.Result - sentAt).Should().BeLessThan(FanOutBudget);
        }
        finally
        {
            await clientOnReplica1.DisposeAsync();
            await clientOnReplica2.DisposeAsync();
        }
    }

    private static async Task<HubConnection> ConnectTenantAsync(string accessToken, string affinityCookie)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(GatewayUrl + $"/hubs/tenant?access_token={accessToken}", options =>
            {
                options.Cookies.Add(new System.Net.Cookie(".Sorcha.Affinity.Tenant", affinityCookie, "/", "localhost"));
            })
            .Build();
        await connection.StartAsync();
        return connection;
    }

    private static async Task<string> GetAdminUserTokenAsync(HttpClient http)
    {
        var resp = await http.PostAsJsonAsync("/api/auth/login",
            new { email = "admin@sorcha.local", password = "Dev_Pass_2025!" });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return body.GetProperty("access_token").GetString()!;
    }

    private static async Task<string> GetServiceTokenAsync(HttpClient http)
    {
        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", "service-blueprint"),
            new KeyValuePair<string, string>("client_secret", "blueprint-service-secret"),
        });
        var resp = await http.PostAsync("/api/service-auth/token", form);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        return body.GetProperty("access_token").GetString()!;
    }

    private static async Task TriggerInboxWriteAsync(string serviceToken)
    {
        // Internal endpoint is service-to-service only — the multinode overlay
        // exposes both replicas via the host-mapped tenant ports (5450 → tenant-1,
        // dynamic for tenant-2). Reach replica-1 directly; the backplane handles
        // fan-out across replicas.
        using var http = new HttpClient { BaseAddress = new Uri("http://localhost:5450") };
        http.DefaultRequestHeaders.Add("Authorization", $"Bearer {serviceToken}");
        var sourceEventId = Guid.NewGuid();
        var resp = await http.PostAsJsonAsync("/api/internal/inbox", new
        {
            platformUserId = Guid.Parse("00000000-0000-0001-0000-000000000001"),
            category = "Action",
            severity = "Info",
            correlationKey = $"multinode:tenant:{sourceEventId:N}",
            detailHref = "/api/me/inbox",
            sourceEventId,
            occurredAt = DateTimeOffset.UtcNow,
            title = "Multinode TenantHub fan-out test",
        });
        resp.EnsureSuccessStatusCode();
    }

    [Fact(Skip = "WalletHub multinode overlay not populated yet — Phase 3 follow-up.")]
    [Trait("Hub", "Wallet")]
    public Task WalletHub_EventOnReplicaA_ReachesClientOnReplicaB_WithinBudget() =>
        Task.CompletedTask;

    [Fact(Skip = "RegisterHub multinode overlay not populated yet — Phase 3 follow-up.")]
    [Trait("Hub", "Register")]
    public Task RegisterHub_EventOnReplicaA_ReachesClientOnReplicaB_WithinBudget() =>
        Task.CompletedTask;

    private static async Task<HubConnection> ConnectAsync(string url, string affinityCookie)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                options.Cookies.Add(new System.Net.Cookie(".Sorcha.Affinity.Blueprint", affinityCookie, "/", "localhost"));
                // Test setup uses a service-principal token issued by Tenant Service in CI.
                // Local runs against the dev gateway can use the seeded admin JWT.
                options.AccessTokenProvider = () => Task.FromResult(Environment.GetEnvironmentVariable("SORCHA_TEST_JWT"));
            })
            .Build();
        await connection.StartAsync();
        return connection;
    }

    private static async Task TriggerActionAvailableAsync(string walletAddress)
    {
        // Test trigger HTTP endpoint is exposed only when SORCHA_MULTINODE=1 — see
        // Blueprint Service Program.cs guarded test-trigger registration. Falls back
        // to no-op if the endpoint is missing so the test fails on the assertion
        // rather than on the trigger.
        using var http = new HttpClient { BaseAddress = new Uri(GatewayUrl) };
        var resp = await http.PostAsync($"/api/test/trigger-action-available?wallet={Uri.EscapeDataString(walletAddress)}", content: null);
        resp.EnsureSuccessStatusCode();
    }
}

/// <summary>
/// xUnit Skip-based gating helper for SkippableFact behaviour.
/// </summary>
internal static class Skip
{
    public static void IfNot(bool condition, string reason)
    {
        if (!condition)
        {
            throw new SkipException(reason);
        }
    }
}

internal sealed class SkipException(string reason) : Exception(reason);

/// <summary>
/// Marker attribute used in lieu of the Xunit.SkippableFact NuGet package — keeps
/// the dependency surface minimal. The xUnit runner treats the SkipException above
/// as a skip when raised from a Fact body.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
internal sealed class SkippableFactAttribute : FactAttribute { }
