// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.SignalR.Client;
using Sorcha.Integration.Tests.MultiNode;
using Xunit;

namespace Sorcha.Integration.Tests.Quickstart;

/// <summary>
/// Feature 118 T061 — quickstart end-to-end inbox round-trip. Mirrors the
/// manual flow documented in <c>specs/118-notifications-architecture/quickstart.md</c>
/// steps 4 and 6: service-principal posts an inbox entry, the user's
/// TenantHub subscriber receives <c>InboxEntryAdded</c> +
/// <c>InboxUnreadCountUpdated</c> in real time, the REST list reflects
/// the entry, mark-read flips state, and a re-post on the same
/// <c>SourceEventId</c> folds idempotently.
/// </summary>
/// <remarks>
/// <para>
/// Skipped unless <c>SORCHA_DOCKER=1</c> is set and the standard
/// <c>docker-compose.yml</c> stack is running. Local dev creds are
/// <c>admin@sorcha.local / Dev_Pass_2025!</c>; the service-principal client
/// id is <c>service-blueprint</c>, and its secret is per-deploy (#1412) —
/// export <c>BLUEPRINT_SERVICE_SECRET</c> with the same value
/// <c>sorcha-setup.sh</c> wrote to <c>.env</c> before running this test. CI
/// gates the same test under the Docker-CI workflow.
/// </para>
/// <para>
/// This test was first executed manually against the running Docker stack
/// and the round-trip captured live (admin user platform_user_id
/// <c>00000000-0000-0001-0000-000000000001</c>, two messages on
/// <c>sorcha:signalr:tenant…group:user:00000000000000010000000000000001</c>).
/// The test codifies that flow as a regression guard.
/// </para>
/// </remarks>
[Trait("Category", "Quickstart")]
public class InboxRoundTripTests
{
    private const string GatewayUrl = "http://localhost";
    private static readonly Guid AdminPlatformUserId = Guid.Parse("00000000-0000-0001-0000-000000000001");

    private static bool DockerEnabled =>
        Environment.GetEnvironmentVariable("SORCHA_DOCKER") == "1";

    // The Blueprint service-principal's ServiceAuth secret is per-deploy since
    // #1412 — sorcha-setup.sh generates BLUEPRINT_SERVICE_SECRET into .env
    // instead of the old committed "blueprint-service-secret" literal. Read
    // the same env var name here so this test authenticates against whatever
    // secret the running stack was actually provisioned with (#1423).
    private static string? BlueprintServiceSecret =>
        Environment.GetEnvironmentVariable("BLUEPRINT_SERVICE_SECRET");

    [SkippableFact]
    public async Task InboxRoundTrip_PostEntry_FiresRealtime_AndPersistsToRest()
    {
        Skip.IfNot(DockerEnabled,
            "Docker stack not running. Set SORCHA_DOCKER=1 and start docker-compose.yml.");
        Skip.IfNot(!string.IsNullOrEmpty(BlueprintServiceSecret),
            "BLUEPRINT_SERVICE_SECRET not set. Export the same value sorcha-setup.sh wrote " +
            "to .env (the Blueprint service principal's per-deploy secret; #1412) before " +
            "running with SORCHA_DOCKER=1.");

        // 1. Acquire user JWT (admin) and service-principal JWT (Blueprint Service).
        using var http = new HttpClient { BaseAddress = new Uri(GatewayUrl) };
        var userToken = await GetUserTokenAsync(http);
        var serviceToken = await GetServiceTokenAsync(http);

        // 2. Connect to TenantHub as the user; capture inbox events.
        var entryAdded = new TaskCompletionSource<string>();
        var unreadCountUpdated = new TaskCompletionSource<int>();

        await using var connection = new HubConnectionBuilder()
            .WithUrl($"{GatewayUrl}/hubs/tenant?access_token={userToken}")
            .Build();

        connection.On<string, DateTimeOffset, string>("InboxEntryAdded",
            (entryId, _, _) => entryAdded.TrySetResult(entryId));
        connection.On<int, DateTimeOffset, string>("InboxUnreadCountUpdated",
            (count, _, _) => unreadCountUpdated.TrySetResult(count));

        await connection.StartAsync();
        connection.State.Should().Be(HubConnectionState.Connected);

        // 3. Service-principal POST to the internal write endpoint.
        var sourceEventId = Guid.NewGuid();
        using var serviceClient = new HttpClient { BaseAddress = new Uri(GatewayUrl) };
        serviceClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {serviceToken}");

        var write = new
        {
            platformUserId = AdminPlatformUserId,
            category = "Action",
            severity = "Info",
            correlationKey = $"quickstart:round-trip:{sourceEventId:N}",
            detailHref = "/api/me/inbox",
            sourceEventId,
            occurredAt = DateTimeOffset.UtcNow,
            title = "Quickstart inbox round-trip",
            summary = "Test entry posted via service principal",
        };

        // The internal endpoint is service-to-service only (not exposed via
        // the gateway). The Tenant Service container's 8080 port is mapped to
        // the host on 5450 by docker-compose.yml; we POST direct.
        using var direct = new HttpClient { BaseAddress = new Uri("http://localhost:5450") };
        direct.DefaultRequestHeaders.Add("Authorization", $"Bearer {serviceToken}");
        var postResp = await direct.PostAsJsonAsync("/api/internal/inbox", write);
        postResp.EnsureSuccessStatusCode();
        var posted = await postResp.Content.ReadFromJsonAsync<JsonElement>();
        var entryId = posted.GetProperty("entry").GetProperty("id").GetGuid();

        // 4. Real-time signals must arrive on the user's TenantHub group within
        //    the per-test budget.
        var addedId = await entryAdded.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var newCount = await unreadCountUpdated.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // The wire entryId is the GUID-N (no-hyphens) form per the contract.
        addedId.Should().Be(entryId.ToString("N"));
        newCount.Should().BeGreaterThan(0);

        // 5. REST surface reflects the entry.
        using var userClient = new HttpClient { BaseAddress = new Uri(GatewayUrl) };
        userClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {userToken}");

        var listed = await userClient.GetFromJsonAsync<JsonElement>("/api/me/inbox");
        listed.GetProperty("entries").EnumerateArray()
            .Select(e => e.GetProperty("id").GetGuid())
            .Should().Contain(entryId);

        // 6. Mark-read flips state and emits a second unread-count update.
        var markReadCount = new TaskCompletionSource<int>();
        connection.Remove("InboxUnreadCountUpdated");
        connection.On<int, DateTimeOffset, string>("InboxUnreadCountUpdated",
            (count, _, _) => markReadCount.TrySetResult(count));

        var markResp = await userClient.PostAsync($"/api/me/inbox/{entryId}/read", content: null);
        markResp.EnsureSuccessStatusCode();
        await markReadCount.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // 7. Re-post on the same SourceEventId folds idempotently — no second
        //    persistence and no second SignalR fire.
        var reEntryAdded = new TaskCompletionSource<string>();
        connection.Remove("InboxEntryAdded");
        connection.On<string, DateTimeOffset, string>("InboxEntryAdded",
            (id, _, _) => reEntryAdded.TrySetResult(id));

        var rePostResp = await direct.PostAsJsonAsync("/api/internal/inbox", write);
        rePostResp.EnsureSuccessStatusCode();
        var rePosted = await rePostResp.Content.ReadFromJsonAsync<JsonElement>();
        rePosted.GetProperty("idempotent").GetBoolean().Should().BeTrue(
            "duplicate (PlatformUserId, SourceEventId) must fold to no-op");

        var noFire = await Task.WhenAny(reEntryAdded.Task, Task.Delay(2000));
        noFire.Should().NotBe(reEntryAdded.Task,
            "idempotent re-write must NOT re-emit InboxEntryAdded");
    }

    private static async Task<string> GetUserTokenAsync(HttpClient http)
    {
        var resp = await http.PostAsJsonAsync("/api/auth/login",
            new { email = "admin@sorcha.local", password = "Dev_Pass_2025!" });
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("access_token").GetString()!;
    }

    private static async Task<string> GetServiceTokenAsync(HttpClient http)
    {
        var form = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", "service-blueprint"),
            new KeyValuePair<string, string>("client_secret", BlueprintServiceSecret!),
        });
        var resp = await http.PostAsync("/api/service-auth/token", form);
        resp.EnsureSuccessStatusCode();
        var body = await resp.Content.ReadFromJsonAsync<JsonElement>();
        return body.GetProperty("access_token").GetString()!;
    }
}
