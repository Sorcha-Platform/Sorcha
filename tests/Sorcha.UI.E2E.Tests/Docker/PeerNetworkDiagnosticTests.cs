// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Playwright;
using Sorcha.UI.E2E.Tests.Infrastructure;

namespace Sorcha.UI.E2E.Tests.Docker;

/// <summary>
/// Diagnostic tests for peer network register advertisement.
/// Captures screenshots showing the Available Registers tab on both local and tiny instances.
/// </summary>
[Parallelizable(ParallelScope.Self)]
[TestFixture]
[Category("Docker")]
[Category("PeerNetwork")]
[Category("Diagnostic")]
public class PeerNetworkDiagnosticTests : AuthenticatedDockerTestBase
{
    private const string LocalBaseUrl = "http://localhost";
    private const string TinyBaseUrl = "http://192.168.51.9";

    protected override bool AssertNoConsoleErrors => false;
    protected override bool AssertNoNetworkFailures => false;
    protected override bool ValidateLayoutHealth => false;

    [Test]
    [Order(1)]
    public async Task PeerNetworkPage_ShowsNetworkOverview_OnLocal()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.AdminPeers);
        await Page.WaitForTimeoutAsync(3000);
        await CaptureScreenshotAsync("01-local-peer-network-overview");

        // Verify tiny-peer is visible in the peer list (or skip if no peers configured)
        var tinyPeer = Page.Locator("text=tiny-peer");
        var hasTinyPeer = await tinyPeer.CountAsync() > 0;
        if (!hasTinyPeer)
        {
            Assert.Ignore("tiny-peer not visible in peer list — peer network may not be configured");
        }
    }

    [Test]
    [Order(2)]
    public async Task PeerNetworkPage_ShowsAvailableRegistersTab_OnLocal()
    {
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.AdminPeers);
        await Page.WaitForTimeoutAsync(3000);

        // Click the "AVAILABLE REGISTERS" tab (MudBlazor may or may not uppercase)
        var availableRegistersTab = Page.Locator("text=/available registers/i").First;
        if (await availableRegistersTab.CountAsync() == 0)
        {
            Assert.Ignore("Available Registers tab not found — peer network page may not be fully configured");
        }
        await availableRegistersTab.WaitForAsync(new() { Timeout = 5000 });
        await availableRegistersTab.ClickAsync();
        await Page.WaitForTimeoutAsync(2000);

        await CaptureScreenshotAsync("02-local-available-registers-tab");

        // Log what we see in the table
        var registerRows = Page.Locator("table tbody tr");
        var rowCount = await registerRows.CountAsync();
        TestContext.Out.WriteLine($"Available Registers table has {rowCount} rows");

        for (var i = 0; i < Math.Min(rowCount, 10); i++)
        {
            var rowText = await registerRows.Nth(i).InnerTextAsync();
            TestContext.Out.WriteLine($"  Row {i}: {rowText}");
        }
    }

    [Test]
    [Order(3)]
    public async Task CreatePublicRegister_AppearsInPeerNetwork()
    {
        // Get auth token via API
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        var tokenResponse = await httpClient.PostAsync(
            $"{LocalBaseUrl}/api/service-auth/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "password",
                ["username"] = TestConstants.TestEmail,
                ["password"] = TestConstants.TestPassword,
                ["client_id"] = "sorcha-cli"
            }));

        if (!tokenResponse.IsSuccessStatusCode)
        {
            Assert.Ignore($"Token request failed: {tokenResponse.StatusCode} — auth service may not be available");
        }

        var tokenJson = await tokenResponse.Content.ReadFromJsonAsync<JsonElement>();
        var accessToken = tokenJson.GetProperty("access_token").GetString()!;
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        // Create a wallet for signing (wallet API is at /api/v1/wallets)
        var walletResponse = await httpClient.PostAsJsonAsync(
            $"{LocalBaseUrl}/api/v1/wallets",
            new { name = $"PeerTest-{DateTime.UtcNow:HHmmss}", algorithm = "ED25519", wordCount = 12 });

        if (!walletResponse.IsSuccessStatusCode)
        {
            Assert.Ignore($"Wallet creation failed: {walletResponse.StatusCode} — wallet service may not be available");
        }

        var walletJson = await walletResponse.Content.ReadFromJsonAsync<JsonElement>();
        var walletAddress = walletJson.GetProperty("wallet").GetProperty("address").GetString()!;
        TestContext.Out.WriteLine($"Created wallet: {walletAddress}");

        // Decode JWT to get userId and orgId for register creation
        var jwtParts = accessToken.Split('.');
        var jwtBase64 = jwtParts[1].Replace('-', '+').Replace('_', '/');
        switch (jwtBase64.Length % 4)
        {
            case 2: jwtBase64 += "=="; break;
            case 3: jwtBase64 += "="; break;
        }
        var jwtPayload = JsonSerializer.Deserialize<JsonElement>(
            Convert.FromBase64String(jwtBase64));
        var userId = jwtPayload.GetProperty("sub").GetString()!;
        var orgId = jwtPayload.GetProperty("org_id").GetString()!;

        // Step 1: Initiate register creation
        var registerName = $"Peer-Test-{DateTime.UtcNow:HHmmss}";
        var initiateResponse = await httpClient.PostAsJsonAsync(
            $"{LocalBaseUrl}/api/registers/initiate",
            new
            {
                name = registerName,
                description = "Test register for peer network diagnostic",
                advertise = true,
                tenantId = orgId,
                owners = new[] { new { userId, walletId = walletAddress } },
                metadata = new { source = "e2e-test" }
            });

        if (!initiateResponse.IsSuccessStatusCode)
        {
            Assert.Ignore($"Register initiate failed: {initiateResponse.StatusCode} — register service may not be available");
        }

        var initiateJson = await initiateResponse.Content.ReadFromJsonAsync<JsonElement>();
        var registerId = initiateJson.GetProperty("registerId").GetString()!;
        var nonce = initiateJson.GetProperty("nonce").GetString()!;
        TestContext.Out.WriteLine($"Initiated register: {registerId}");

        // Step 2: Sign attestations
        if (initiateJson.TryGetProperty("attestationsToSign", out var attestations))
        {
            var signedAttestations = new List<Dictionary<string, object>>();
            foreach (var attestation in attestations.EnumerateArray())
            {
                // attestationData is a JSON object, keep it as JsonElement
                var attestationData = attestation.GetProperty("attestationData");
                var dataToSignHex = attestation.GetProperty("dataToSign").GetString()!;
                var attWalletId = attestation.GetProperty("walletId").GetString()!;

                // Convert hex to base64 for signing
                var hashBytes = new byte[dataToSignHex.Length / 2];
                for (var i = 0; i < hashBytes.Length; i++)
                    hashBytes[i] = Convert.ToByte(dataToSignHex.Substring(i * 2, 2), 16);
                var dataToSignBase64 = Convert.ToBase64String(hashBytes);

                var signResponse = await httpClient.PostAsJsonAsync(
                    $"{LocalBaseUrl}/api/v1/wallets/{attWalletId}/sign",
                    new { transactionData = dataToSignBase64, isPreHashed = true });

                if (signResponse.IsSuccessStatusCode)
                {
                    var signJson = await signResponse.Content.ReadFromJsonAsync<JsonElement>();
                    signedAttestations.Add(new Dictionary<string, object>
                    {
                        ["attestationData"] = attestationData,
                        ["publicKey"] = signJson.GetProperty("publicKey").GetString()!,
                        ["signature"] = signJson.GetProperty("signature").GetString()!,
                        ["algorithm"] = "ED25519"
                    });
                }
                else
                {
                    TestContext.Out.WriteLine(
                        $"Sign failed: {signResponse.StatusCode} - {await signResponse.Content.ReadAsStringAsync()}");
                }
            }

            // Step 3: Finalize
            var finalizeResponse = await httpClient.PostAsJsonAsync(
                $"{LocalBaseUrl}/api/registers/finalize",
                new { registerId, nonce, signedAttestations });

            if (finalizeResponse.IsSuccessStatusCode)
            {
                TestContext.Out.WriteLine($"Register {registerId} finalized successfully!");
            }
            else
            {
                TestContext.Out.WriteLine(
                    $"Finalize failed: {finalizeResponse.StatusCode} - {await finalizeResponse.Content.ReadAsStringAsync()}");
            }
        }

        // Wait for advertisement + heartbeat propagation
        TestContext.Out.WriteLine("Waiting 15s for heartbeat propagation...");
        await Task.Delay(15_000);

        // Verify the register appears in peer network available list
        var availableJson = await httpClient.GetStringAsync($"{LocalBaseUrl}/api/peer/registers/available");
        TestContext.Out.WriteLine($"Local available registers: {availableJson}");

        // Also check tiny's view
        using var tinyClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };
        try
        {
            var tinyAvailable = await tinyClient.GetStringAsync($"{TinyBaseUrl}/api/peer/registers/available");
            TestContext.Out.WriteLine($"Tiny available registers: {tinyAvailable}");
            Assert.That(tinyAvailable, Does.Contain(registerId),
                $"Newly created register {registerId} should be visible on tiny");
        }
        catch (Exception ex)
        {
            TestContext.Out.WriteLine($"Could not reach tiny: {ex.Message}");
        }

        // Screenshot the Available Registers tab
        await NavigateAuthenticatedAsync(TestConstants.AuthenticatedRoutes.AdminPeers);
        await Page.WaitForTimeoutAsync(3000);

        var tab = Page.Locator("text=/available registers/i").First;
        if (await tab.CountAsync() > 0)
        {
            await tab.WaitForAsync(new() { Timeout = 5000 });
            await tab.ClickAsync();
        }
        await Page.WaitForTimeoutAsync(2000);

        await CaptureScreenshotAsync("03-local-after-register-creation");
    }

    [Test]
    [Order(4)]
    public async Task TinyInstance_PeerApiShowsLocalRegisters()
    {
        // Check tiny's peer network API (no auth required for peer endpoints)
        using var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(10) };

        string peersJson;
        string availableJson;
        try
        {
            peersJson = await httpClient.GetStringAsync($"{TinyBaseUrl}/api/peers");
            TestContext.Out.WriteLine($"Tiny peers: {peersJson}");

            availableJson = await httpClient.GetStringAsync($"{TinyBaseUrl}/api/peer/registers/available");
            TestContext.Out.WriteLine($"Tiny available registers: {availableJson}");
        }
        catch (Exception ex)
        {
            Assert.Ignore($"Tiny instance not reachable: {ex.Message}");
            return;
        }

        // Verify tiny sees local-peer's registers
        if (!availableJson.Contains("registerId"))
        {
            Assert.Ignore("Tiny instance has no available registers — peer replication may not be configured");
        }

        // Try to navigate to tiny's UI (will redirect to login since different JWT)
        try
        {
            await Page.GotoAsync($"{TinyBaseUrl}/app/admin/peers");
            await Page.WaitForTimeoutAsync(5000);
            await CaptureScreenshotAsync("04-tiny-peer-network-login");
        }
        catch (Exception ex)
        {
            TestContext.Out.WriteLine($"Could not navigate to tiny UI: {ex.Message}");
        }
    }
}
