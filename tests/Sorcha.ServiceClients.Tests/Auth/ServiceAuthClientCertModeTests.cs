// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Https;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.ServiceClients.Auth;
using Sorcha.WorkloadIdentity;

namespace Sorcha.ServiceClients.Tests.Auth;

/// <summary>
/// F191 US1 — ServiceAuthClient certificate mode. These tests run against a REAL Kestrel mTLS
/// endpoint (loopback, ephemeral port) so the TLS join — client cert attached, server cert
/// pinned to the workload trust bundle — is genuinely exercised, not mocked.
/// </summary>
public sealed class ServiceAuthClientCertModeTests : IDisposable
{
    private const string Installation = "sorcha";
    private const string ClientId = "service-blueprint";

    private readonly string _tempDir;
    private readonly X509Certificate2 _ca;
    private readonly string _clientPfxPath;
    private readonly string _bundlePath;

    public ServiceAuthClientCertModeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"svc-auth-cert-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);

        _ca = WorkloadCertificateAuthority.CreateCertificateAuthority(Installation);
        using var clientLeaf = WorkloadCertificateAuthority.IssueServiceCertificate(
            _ca, SpiffeId.ForService(Installation, ClientId), "blueprint-service");

        _clientPfxPath = Path.Combine(_tempDir, "client.pfx");
        File.WriteAllBytes(_clientPfxPath, clientLeaf.Export(X509ContentType.Pfx, "pw"));

        _bundlePath = Path.Combine(_tempDir, "bundle.pem");
        File.WriteAllText(_bundlePath, WorkloadTrustBundle.ExportBundlePem(new[] { _ca }));
    }

    public void Dispose()
    {
        _ca.Dispose();
        Directory.Delete(_tempDir, recursive: true);
    }

    private IConfiguration BuildConfig(
        string? certSource,
        string? bundle,
        string? mtlsAddress,
        string? secret = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["ServiceAuth:ClientId"] = ClientId,
            ["ServiceAuth:Scopes"] = "blueprints:read",
        };
        if (secret is not null) values["ServiceAuth:ClientSecret"] = secret;
        if (certSource is not null) values[WorkloadIdentityConfig.ClientCertificate] = certSource;
        values[WorkloadIdentityConfig.ClientCertificatePassword] = "pw";
        if (bundle is not null) values[WorkloadIdentityConfig.TrustBundle] = bundle;
        if (mtlsAddress is not null) values[WorkloadIdentityConfig.MtlsTokenAddress] = mtlsAddress;

        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }

    /// <summary>
    /// Minimal real mTLS token server: requires a client certificate, validates it against the
    /// workload bundle at handshake, records what the handler saw, returns a fake token.
    /// </summary>
    private sealed class MtlsTokenServer : IAsyncDisposable
    {
        private readonly WebApplication _app;
        public string Address { get; }
        public string? SeenClientCertSubject { get; private set; }
        public bool SeenClientSecretField { get; private set; }
        public int RequestCount { get; private set; }

        public MtlsTokenServer(X509Certificate2 serverCert, WorkloadTrustBundle clientTrust)
        {
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(k => k.Listen(System.Net.IPAddress.Loopback, 0, listen =>
                listen.UseHttps(https =>
                {
                    https.ServerCertificate = serverCert;
                    https.ClientCertificateMode = ClientCertificateMode.RequireCertificate;
                    https.ClientCertificateValidation = (cert, _, _) => clientTrust.Validate(cert, out _);
                })));

            _app = builder.Build();
            _app.MapPost("/api/internal/service-auth/token", async (HttpContext ctx) =>
            {
                RequestCount++;
                SeenClientCertSubject = ctx.Connection.ClientCertificate?.Subject;
                var form = await ctx.Request.ReadFormAsync();
                SeenClientSecretField = form.ContainsKey("client_secret") && !string.IsNullOrEmpty(form["client_secret"]);
                return Results.Json(new { access_token = "cert-minted-token", token_type = "Bearer", expires_in = 3600 });
            });
            _app.Start();
            // Bundle-pinned validation replaces hostname checking, so the raw loopback URL is fine.
            Address = _app.Urls.Single();
        }

        public async ValueTask DisposeAsync() => await _app.DisposeAsync();
    }

    private X509Certificate2 IssueServerCert(X509Certificate2 issuer) =>
        WorkloadCertificateAuthority.IssueServerCertificate(issuer, "localhost");

    [Fact]
    public async Task CertMode_AcquiresTokenOverRealMtls_WithoutSendingAnySecret()
    {
        using var serverCert = IssueServerCert(_ca);
        var bundle = WorkloadTrustBundle.LoadFromFile(_bundlePath);
        await using var server = new MtlsTokenServer(serverCert, bundle);

        var config = BuildConfig(_clientPfxPath, _bundlePath, server.Address);
        using var client = new ServiceAuthClient(new HttpClient(), config, NullLogger<ServiceAuthClient>.Instance);

        var token = await client.GetTokenAsync(TestContext.Current.CancellationToken);

        token.Should().Be("cert-minted-token");
        server.SeenClientCertSubject.Should().Contain(ClientId, "the workload client certificate must be presented");
        server.SeenClientSecretField.Should().BeFalse("certificate mode must not transmit any shared secret");
    }

    [Fact]
    public async Task CertMode_ServerNotSignedByBundle_IsRefusedByClient()
    {
        // Server presents a certificate from a DIFFERENT CA — the client must refuse the
        // connection (bundle-pinned server validation), so no request reaches the handler.
        using var foreignCa = WorkloadCertificateAuthority.CreateCertificateAuthority("other-install");
        using var foreignServerCert = IssueServerCert(foreignCa);
        var clientTrust = WorkloadTrustBundle.LoadFromFile(_bundlePath);
        await using var server = new MtlsTokenServer(foreignServerCert, clientTrust);

        var config = BuildConfig(_clientPfxPath, _bundlePath, server.Address);
        using var client = new ServiceAuthClient(new HttpClient(), config, NullLogger<ServiceAuthClient>.Instance);

        var token = await client.GetTokenAsync(TestContext.Current.CancellationToken);

        token.Should().BeNull();
        server.RequestCount.Should().Be(0, "the TLS handshake must fail before any request is made");
    }

    [Fact]
    public async Task CertMode_NoSecretConfigured_StillConstructsAndMints()
    {
        // The whole point of F191: no ServiceAuth:ClientSecret anywhere.
        using var serverCert = IssueServerCert(_ca);
        await using var server = new MtlsTokenServer(serverCert, WorkloadTrustBundle.LoadFromFile(_bundlePath));

        var config = BuildConfig(_clientPfxPath, _bundlePath, server.Address, secret: null);
        using var client = new ServiceAuthClient(new HttpClient(), config, NullLogger<ServiceAuthClient>.Instance);

        var token = await client.GetTokenAsync(TestContext.Current.CancellationToken);

        token.Should().Be("cert-minted-token");
    }

    [Fact]
    public void CertMode_UnreadableCertificate_ThrowsAtConstruction_NeverFallsBackToSecret()
    {
        // FR-009: configured-but-broken certificate material is a startup failure even though a
        // perfectly valid secret is configured — a silent fallback would mask a dead cert deploy.
        var missingPfx = Path.Combine(_tempDir, "missing.pfx");
        var config = BuildConfig(missingPfx, _bundlePath, "https://localhost:1", secret: "valid-secret");

        var act = () => new ServiceAuthClient(new HttpClient(), config, NullLogger<ServiceAuthClient>.Instance);

        act.Should().Throw<WorkloadCertificateLoadException>().WithMessage($"*{missingPfx}*");
    }

    [Fact]
    public void CertMode_MissingTrustBundle_ThrowsAtConstruction()
    {
        var config = BuildConfig(_clientPfxPath, bundle: null, mtlsAddress: "https://localhost:1");

        var act = () => new ServiceAuthClient(new HttpClient(), config, NullLogger<ServiceAuthClient>.Instance);

        act.Should().Throw<WorkloadCertificateLoadException>().WithMessage($"*{WorkloadIdentityConfig.TrustBundle}*");
    }

    [Fact]
    public void LegacyMode_NoCertNoSecret_ThrowsExactlyAsToday()
    {
        var config = BuildConfig(certSource: null, bundle: null, mtlsAddress: null, secret: null);

        var act = () => new ServiceAuthClient(new HttpClient(), config, NullLogger<ServiceAuthClient>.Instance);

        act.Should().Throw<InvalidOperationException>().WithMessage("*ServiceAuth:ClientSecret*");
    }

    [Fact]
    public async Task BothConfigured_CertificateWins()
    {
        using var serverCert = IssueServerCert(_ca);
        await using var server = new MtlsTokenServer(serverCert, WorkloadTrustBundle.LoadFromFile(_bundlePath));

        var config = BuildConfig(_clientPfxPath, _bundlePath, server.Address, secret: "legacy-secret");
        using var client = new ServiceAuthClient(new HttpClient(), config, NullLogger<ServiceAuthClient>.Instance);

        var token = await client.GetTokenAsync(TestContext.Current.CancellationToken);

        token.Should().Be("cert-minted-token");
        server.RequestCount.Should().Be(1, "token acquisition must go to the mTLS endpoint, not the legacy address");
        server.SeenClientSecretField.Should().BeFalse("the configured secret is ignored in certificate mode");
    }
}
