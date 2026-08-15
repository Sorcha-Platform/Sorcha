// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// F191 US1 — SC-006: the mTLS join, tested for real. A real Kestrel socket, a real TLS
// handshake with a real CA-chained client certificate, into the REAL mint pipeline
// (AddWorkloadMtlsListener → Kestrel RequireCertificate + WorkloadTrustBundle validation →
// ServiceAuthEndpoints → ServiceAuthService). No test authentication handler anywhere.
//
// Mutation checks performed for T021 (weaken → named test RED → restore):
//   1. WorkloadTrustBundle.Validate forced to `return true` (accept any root)
//      ⇒ WrongCa_HandshakeIsRefused + ExpiredLeaf_HandshakeIsRefused went RED.
//   2. ServiceAuthService.VerifyWorkloadIdentity SAN comparison removed (`return true`)
//      ⇒ SanClientIdMismatch_IsRefused went RED.

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Tests.Infrastructure;
using Sorcha.WorkloadIdentity;

namespace Sorcha.Tenant.Service.Tests.ServiceAuth;

/// <summary>
/// Kestrel-hosted Tenant factory with the workload mTLS listener configured. Reuses every
/// production-shape override from <see cref="TenantServiceWebApplicationFactory"/> (in-memory
/// stores, Testing environment) and adds ONLY the F191 listener configuration.
/// </summary>
public sealed class MtlsTenantFactory : TenantServiceWebApplicationFactory
{
    public const string Installation = "test"; // must match base factory JwtSettings:InstallationName

    public X509Certificate2 WorkloadCa { get; }
    public int MtlsPort { get; }
    public string TempDir { get; }

    public MtlsTenantFactory()
    {
        TempDir = Path.Combine(Path.GetTempPath(), $"mtls-mint-{Guid.NewGuid():N}");
        Directory.CreateDirectory(TempDir);

        WorkloadCa = WorkloadCertificateAuthority.CreateCertificateAuthority(Installation);
        File.WriteAllText(BundlePath, WorkloadTrustBundle.ExportBundlePem(new[] { WorkloadCa }));

        using var serverCert = WorkloadCertificateAuthority.IssueServerCertificate(WorkloadCa, "localhost");
        File.WriteAllBytes(ServerPfxPath, serverCert.Export(X509ContentType.Pfx, "pw"));

        MtlsPort = GetFreeTcpPort();

        // Real sockets, not TestServer — the whole point of this fixture (SC-006).
        // Must be called before the server initializes, hence here and not per-test.
        UseKestrel();
    }

    public string BundlePath => Path.Combine(TempDir, "bundle.pem");
    public string ServerPfxPath => Path.Combine(TempDir, "server.pfx");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        // UseSetting (not ConfigureAppConfiguration): these keys are read at BUILDER time by
        // AddWorkloadMtlsListener in Program.cs, so they must be in the host configuration
        // before Program's code runs. The explicit dynamic-port `urls` gives the extension a
        // plaintext listener to re-bind (as ASPNETCORE_URLS does in a real deployment).
        builder.UseSetting("urls", "http://127.0.0.1:0");
        builder.UseSetting(WorkloadIdentityConfig.MtlsServerCertificate, ServerPfxPath);
        builder.UseSetting(WorkloadIdentityConfig.MtlsServerCertificatePassword, "pw");
        builder.UseSetting(WorkloadIdentityConfig.MtlsTrustBundle, BundlePath);
        builder.UseSetting(WorkloadIdentityConfig.MtlsPort, MtlsPort.ToString());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            WorkloadCa.Dispose();
            try { Directory.Delete(TempDir, recursive: true); } catch { /* best effort */ }
        }
    }

    private static int GetFreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

public class MtlsMintIntegrationTests : IClassFixture<MtlsTenantFactory>, IAsyncLifetime
{
    private readonly MtlsTenantFactory _factory;
    private HttpClient _plaintextClient = null!;

    public MtlsMintIntegrationTests(MtlsTenantFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync()
    {
        // CreateClient starts the Kestrel host; in Kestrel mode WAF may hand back an address on
        // an unspecified host ([::]) that HttpClient cannot target, so resolve the REAL bound
        // http address from the server and build the plaintext client from that.
        _factory.CreateClient().Dispose();
        var addresses = _factory.Services
            .GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!
            .Addresses;
        var httpAddress = addresses
            .First(a => a.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
            .Replace("[::]", "localhost")
            .Replace("0.0.0.0", "localhost")
            .Replace("127.0.0.1", "localhost");
        _plaintextClient = new HttpClient { BaseAddress = new Uri(httpAddress) };
        await _factory.SeedTestDataAsync();
        await SeedWorkloadPrincipalsAsync();
    }

    public ValueTask DisposeAsync()
    {
        _plaintextClient?.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task SeedWorkloadPrincipalsAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();

        void AddPrincipal(string clientId, ServicePrincipalStatus status, params string[] scopes)
        {
            if (db.ServicePrincipals.Any(p => p.ClientId == clientId)) return;
            db.ServicePrincipals.Add(new ServicePrincipal
            {
                Id = Guid.NewGuid(),
                ServiceName = clientId,
                ClientId = clientId,
                ClientSecretEncrypted = new byte[48],
                Scopes = scopes,
                Status = status,
                CreatedAt = DateTimeOffset.UtcNow,
            });
        }

        AddPrincipal("service-blueprint", ServicePrincipalStatus.Active, "blueprints:read", "wallets:sign");
        AddPrincipal("service-wallet", ServicePrincipalStatus.Active, "wallets:read");
        AddPrincipal("service-suspended", ServicePrincipalStatus.Suspended, "blueprints:read");
        AddPrincipal("tenant-service", ServicePrincipalStatus.Active, "tenant:delegate", "wallets:sign");
        await db.SaveChangesAsync();
    }

    private X509Certificate2 IssueLeaf(string clientId, X509Certificate2? issuer = null,
        DateTimeOffset? notBefore = null, DateTimeOffset? notAfter = null) =>
        WorkloadCertificateAuthority.IssueServiceCertificate(
            issuer ?? _factory.WorkloadCa,
            SpiffeId.ForService(MtlsTenantFactory.Installation, clientId),
            "test-host",
            notBefore: notBefore,
            notAfter: notAfter);

    private HttpClient CreateMtlsClient(X509Certificate2 clientCertificate)
    {
        var handler = new SocketsHttpHandler();
        handler.SslOptions.ClientCertificates = new X509CertificateCollection { clientCertificate };
        handler.SslOptions.LocalCertificateSelectionCallback = (_, _, _, _, _) => clientCertificate;
        // Server identity is not under test here — the join under test is the SERVER validating
        // the CLIENT certificate and the mint handler matching its SPIFFE identity.
        handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        return new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://localhost:{_factory.MtlsPort}"),
        };
    }

    private static FormUrlEncodedContent SecretlessMintForm(string clientId, string? scope = null)
    {
        var fields = new Dictionary<string, string>
        {
            ["grant_type"] = "client_credentials",
            ["client_id"] = clientId,
        };
        if (scope is not null) fields["scope"] = scope;
        return new FormUrlEncodedContent(fields);
    }

    private static Dictionary<string, JsonElement> DecodeJwtPayload(string jwt)
    {
        var payload = jwt.Split('.')[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + (4 - payload.Length % 4) % 4, '=');
        var json = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(payload));
        return JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;
    }

    [Fact]
    public async Task SecretlessMint_WithMatchingWorkloadCertificate_MintsServiceToken()
    {
        using var leaf = IssueLeaf("service-blueprint");
        using var client = CreateMtlsClient(leaf);

        var response = await client.PostAsync(
            "/api/internal/service-auth/token",
            SecretlessMintForm("service-blueprint"),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var accessToken = body.GetProperty("access_token").GetString();
        accessToken.Should().NotBeNullOrEmpty();

        // The minted token must be shape-identical to the secret path's: service token_type,
        // the requesting client_id, and the installation's service-tier audience.
        var claims = DecodeJwtPayload(accessToken!);
        claims["token_type"].GetString().Should().Be("service");
        claims["client_id"].GetString().Should().Be("service-blueprint");
        claims["aud"].GetString().Should().Be($"{MtlsTenantFactory.Installation}:service");
    }

    [Fact]
    public async Task CertAndSecretPath_MintClaimSetsWithIdenticalShape()
    {
        // Secret path (plaintext listener, seeded test principal) vs cert path — the claim KEYS
        // must be identical; per-mint values (jti, iat, exp, nbf, sub-per-principal) may differ.
        var secretResponse = await _plaintextClient.PostAsync(
            "/api/internal/service-auth/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "test-client-id",
                ["client_secret"] = "test-client-secret",
            }),
            TestContext.Current.CancellationToken);
        secretResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var secretToken = (await secretResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("access_token").GetString()!;

        using var leaf = IssueLeaf("service-blueprint");
        using var client = CreateMtlsClient(leaf);
        var certResponse = await client.PostAsync(
            "/api/internal/service-auth/token",
            SecretlessMintForm("service-blueprint"),
            TestContext.Current.CancellationToken);
        certResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var certToken = (await certResponse.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken))
            .GetProperty("access_token").GetString()!;

        var secretKeys = DecodeJwtPayload(secretToken).Keys.Except(["scope"]).Order();
        var certKeys = DecodeJwtPayload(certToken).Keys.Except(["scope"]).Order();
        certKeys.Should().Equal(secretKeys, "downstream consumers must not be able to distinguish mint paths");
    }

    [Fact]
    public async Task WrongCa_HandshakeIsRefused()
    {
        using var foreignCa = WorkloadCertificateAuthority.CreateCertificateAuthority(MtlsTenantFactory.Installation);
        using var foreignLeaf = IssueLeaf("service-blueprint", issuer: foreignCa);
        using var client = CreateMtlsClient(foreignLeaf);

        var act = () => client.PostAsync(
            "/api/internal/service-auth/token",
            SecretlessMintForm("service-blueprint"),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>(
            "a certificate not chained to the workload bundle must fail at the TLS handshake");
    }

    [Fact]
    public async Task ExpiredLeaf_HandshakeIsRefused()
    {
        using var expiredLeaf = IssueLeaf("service-blueprint",
            notBefore: DateTimeOffset.UtcNow.AddDays(-10), notAfter: DateTimeOffset.UtcNow.AddDays(-1));
        using var client = CreateMtlsClient(expiredLeaf);

        var act = () => client.PostAsync(
            "/api/internal/service-auth/token",
            SecretlessMintForm("service-blueprint"),
            TestContext.Current.CancellationToken);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task SanClientIdMismatch_IsRefused()
    {
        // Valid certificate for service-wallet presented while requesting service-blueprint:
        // handshake succeeds (trusted material), mint must refuse on identity mismatch.
        using var walletLeaf = IssueLeaf("service-wallet");
        using var client = CreateMtlsClient(walletLeaf);

        var response = await client.PostAsync(
            "/api/internal/service-auth/token",
            SecretlessMintForm("service-blueprint"),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnknownPrincipal_IsRefused()
    {
        using var ghostLeaf = IssueLeaf("service-ghost");
        using var client = CreateMtlsClient(ghostLeaf);

        var response = await client.PostAsync(
            "/api/internal/service-auth/token",
            SecretlessMintForm("service-ghost"),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SuspendedPrincipal_IsRefused()
    {
        using var suspendedLeaf = IssueLeaf("service-suspended");
        using var client = CreateMtlsClient(suspendedLeaf);

        var response = await client.PostAsync(
            "/api/internal/service-auth/token",
            SecretlessMintForm("service-suspended"),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SecretPathOnPlaintextListener_StillMints_Coexistence()
    {
        var response = await _plaintextClient.PostAsync(
            "/api/internal/service-auth/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "test-client-id",
                ["client_secret"] = "test-client-secret",
            }),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SecretlessMint_OnPlaintextListener_IsRefused()
    {
        // No TLS ⇒ no client certificate ⇒ the secretless form must not mint anywhere
        // outside the mTLS listener.
        var response = await _plaintextClient.PostAsync(
            "/api/internal/service-auth/token",
            SecretlessMintForm("service-blueprint"),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task DelegatedToken_SecretlessWithCertificate_Mints()
    {
        using var leaf = IssueLeaf("tenant-service");
        using var client = CreateMtlsClient(leaf);

        var response = await client.PostAsJsonAsync(
            "/api/internal/service-auth/token/delegated",
            new { clientId = "tenant-service", delegatedUserId = TestDataSeeder.AdminUserId },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        var claims = DecodeJwtPayload(body.GetProperty("access_token").GetString()!);
        claims["delegated_user_id"].GetString().Should().Be(TestDataSeeder.AdminUserId.ToString());
    }
}
