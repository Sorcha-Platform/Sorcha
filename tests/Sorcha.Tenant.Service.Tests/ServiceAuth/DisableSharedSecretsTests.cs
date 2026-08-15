// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Tests.Infrastructure;
using Sorcha.WorkloadIdentity;

namespace Sorcha.Tenant.Service.Tests.ServiceAuth;

/// <summary>
/// F191 US3 — the per-deployment retire switch. With ServiceAuth:DisableSharedSecrets=true,
/// every secret-presenting service-auth request is refused with an explicit error while the
/// certificate path continues minting; the condition is visible in startup logs.
/// (The flag-false coexistence matrix is proven by MtlsMintIntegrationTests.)
/// </summary>
public sealed class DisabledSecretsTenantFactory : MtlsTenantFactory
{
    /// <summary>Captured log lines (message only) for the startup-visibility assertion.</summary>
    public List<string> CapturedLogs { get; } = [];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.UseSetting(WorkloadIdentityConfig.DisableSharedSecrets, "true");
        builder.ConfigureLogging(logging => logging.AddProvider(new CaptureLoggerProvider(CapturedLogs)));
    }

    private sealed class CaptureLoggerProvider(List<string> sink) : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => new CaptureLogger(sink);
        public void Dispose() { }

        private sealed class CaptureLogger(List<string> sink) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (sink) sink.Add(formatter(state, exception));
            }
        }
    }
}

public class DisableSharedSecretsTests : IClassFixture<DisabledSecretsTenantFactory>, IAsyncLifetime
{
    private readonly DisabledSecretsTenantFactory _factory;
    private HttpClient _plaintextClient = null!;

    public DisableSharedSecretsTests(DisabledSecretsTenantFactory factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync()
    {
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
        await SeedCertPrincipalAsync();
    }

    public ValueTask DisposeAsync()
    {
        _plaintextClient?.Dispose();
        return ValueTask.CompletedTask;
    }

    private async Task SeedCertPrincipalAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<TenantDbContext>();
        if (!db.ServicePrincipals.Any(p => p.ClientId == "service-blueprint"))
        {
            db.ServicePrincipals.Add(new ServicePrincipal
            {
                Id = Guid.NewGuid(),
                ServiceName = "service-blueprint",
                ClientId = "service-blueprint",
                ClientSecretEncrypted = new byte[48],
                Scopes = ["blueprints:read"],
                Status = ServicePrincipalStatus.Active,
                CreatedAt = DateTimeOffset.UtcNow,
            });
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task SecretClientCredentials_IsRefusedWithExplicitError()
    {
        // The seeded test principal's secret is VALID — the refusal must be about the flag,
        // not the credential.
        var response = await _plaintextClient.PostAsync(
            "/api/internal/service-auth/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "test-client-id",
                ["client_secret"] = "test-client-secret",
            }),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("disabled", "the refusal must be explicit, not a generic 401");
        body.Should().Contain("DisableSharedSecrets");
    }

    [Fact]
    public async Task SecretDelegatedToken_IsRefusedWithExplicitError()
    {
        var response = await _plaintextClient.PostAsJsonAsync(
            "/api/internal/service-auth/token/delegated",
            new
            {
                clientId = "test-client-id",
                clientSecret = "test-client-secret",
                delegatedUserId = TestDataSeeder.AdminUserId,
            },
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken);
        body.Should().Contain("disabled");
    }

    [Fact]
    public async Task CertificatePath_StillMints()
    {
        using var leaf = WorkloadCertificateAuthority.IssueServiceCertificate(
            _factory.WorkloadCa,
            SpiffeId.ForService(MtlsTenantFactory.Installation, "service-blueprint"),
            "test-host");

        var handler = new SocketsHttpHandler();
        handler.SslOptions.ClientCertificates = new X509CertificateCollection { leaf };
        handler.SslOptions.LocalCertificateSelectionCallback = (_, _, _, _, _) => leaf;
        handler.SslOptions.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        using var client = new HttpClient(handler)
        {
            BaseAddress = new Uri($"https://localhost:{_factory.MtlsPort}"),
        };

        var response = await client.PostAsync(
            "/api/internal/service-auth/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "service-blueprint",
            }),
            TestContext.Current.CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>(TestContext.Current.CancellationToken);
        body.GetProperty("access_token").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void StartupLog_StatesSecretsAreDisabled()
    {
        List<string> snapshot;
        lock (_factory.CapturedLogs) snapshot = [.. _factory.CapturedLogs];

        snapshot.Should().Contain(
            line => line.Contains("DisableSharedSecrets", StringComparison.OrdinalIgnoreCase)
                 && line.Contains("disabled", StringComparison.OrdinalIgnoreCase),
            "a mis-flipped deployment must be diagnosable from startup logs");
    }
}
