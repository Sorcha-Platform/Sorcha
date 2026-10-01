// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Register.Service.Services;
using Sorcha.Register.Service.Tests.Helpers;
using Sorcha.ServiceDefaults.Auth;
using Xunit;

namespace Sorcha.Register.Service.Tests.Endpoints;

/// <summary>
/// <c>GET /api/system-register/drift</c> (Feature 197 T009): SystemAdmin on the platform tier only,
/// state on the wire as its kebab-case name (platform wire form), computed at request time — never
/// the monitor's (possibly stale) snapshot.
/// </summary>
[Collection("RegisterWebApp")]
public class SystemRegisterDriftEndpointTests : IClassFixture<SystemRegisterDriftWebApplicationFactory>
{
    private readonly SystemRegisterDriftWebApplicationFactory _factory;

    public SystemRegisterDriftEndpointTests(SystemRegisterDriftWebApplicationFactory factory) => _factory = factory;

    private HttpClient ClientAs(string principal)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(DriftTestAuthHandler.PrincipalHeader, principal);
        return client;
    }

    [Fact]
    public async Task GetDrift_SystemAdminOnPlatformTier_Returns200WithStateAsKebabCaseString()
    {
        var response = await ClientAs("sysadmin-platform").GetAsync("/api/system-register/drift");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
        body.GetProperty("checkedAt").GetDateTimeOffset().Should().Be(SystemRegisterDriftWebApplicationFactory.CheckedAt);

        var entry = body.GetProperty("entries").EnumerateArray().Single();
        entry.GetProperty("blueprintId").GetString().Should().Be("register-creation-v1");
        entry.GetProperty("state").ValueKind.Should().Be(JsonValueKind.String);
        entry.GetProperty("state").GetString().Should().Be("image-ahead");
    }

    [Fact]
    public async Task GetDrift_StateChangedSinceMonitorSnapshot_ReportsCurrentStateNotSnapshot()
    {
        // A publish has just landed: the reporter now computes in-sync, while the monitor's snapshot
        // (taken before the publish) still says image-ahead. /drift must report the current state.
        _factory.Reporter.Entries =
        [
            new SystemBlueprintDriftEntry(
                "register-creation-v1", SystemBlueprintDriftState.InSync,
                "tx-image", 2, "tx-image", null, SystemRegisterDriftWebApplicationFactory.CheckedAt.AddMinutes(1))
        ];
        try
        {
            var response = await ClientAs("sysadmin-platform").GetAsync("/api/system-register/drift");

            response.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = JsonSerializer.Deserialize<JsonElement>(await response.Content.ReadAsStringAsync());
            var entry = body.GetProperty("entries").EnumerateArray().Single();
            entry.GetProperty("state").GetString().Should().Be("in-sync");
            entry.GetProperty("currentVersion").GetInt32().Should().Be(2);
        }
        finally
        {
            _factory.Reporter.Entries = SystemRegisterDriftWebApplicationFactory.ImageAheadEntries();
        }
    }

    [Fact]
    public async Task GetDrift_OrgAdministrator_Returns403()
    {
        var response = await ClientAs("org-admin").GetAsync("/api/system-register/drift");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>The tier gate composes on the role gate: SystemAdmin on a consumer token is refused.</summary>
    [Fact]
    public async Task GetDrift_SystemAdminOnConsumerTierToken_Returns403()
    {
        var response = await ClientAs("sysadmin-consumer").GetAsync("/api/system-register/drift");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task GetDrift_Unauthenticated_Returns401()
    {
        var response = await ClientAs("anonymous").GetAsync("/api/system-register/drift");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}

/// <summary>
/// Host with header-driven principals, a drift reporter the test controls, and a monitor snapshot that
/// deliberately disagrees with it (stale) — so a response served from the snapshot is detectable.
/// </summary>
public class SystemRegisterDriftWebApplicationFactory : RegisterServiceWebApplicationFactory
{
    internal static readonly DateTimeOffset CheckedAt = new(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>The reporter /drift must consult on every request.</summary>
    internal MutableReporter Reporter { get; } = new();

    internal static IReadOnlyList<SystemBlueprintDriftEntry> ImageAheadEntries() =>
    [
        new SystemBlueprintDriftEntry(
            "register-creation-v1", SystemBlueprintDriftState.ImageAhead,
            "tx-current", 1, "tx-image", null, CheckedAt)
    ];

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = DriftTestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = DriftTestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, DriftTestAuthHandler>(
                DriftTestAuthHandler.SchemeName, _ => { });

            services.RemoveAll<ISystemBlueprintDriftSnapshot>();
            services.AddSingleton<ISystemBlueprintDriftSnapshot>(new StaleSnapshot());
            services.RemoveAll<ISystemBlueprintDriftReporter>();
            services.AddSingleton<ISystemBlueprintDriftReporter>(Reporter);
        });
    }

    /// <summary>A snapshot from before the last change: a different state from the reporter's.</summary>
    private sealed class StaleSnapshot : ISystemBlueprintDriftSnapshot
    {
        public IReadOnlyList<SystemBlueprintDriftEntry>? Entries { get; } =
        [
            new SystemBlueprintDriftEntry(
                "register-creation-v1", SystemBlueprintDriftState.Missing,
                null, null, "tx-image", null, CheckedAt.AddHours(-1))
        ];
    }

    internal sealed class MutableReporter : ISystemBlueprintDriftReporter
    {
        public IReadOnlyList<SystemBlueprintDriftEntry> Entries { get; set; } = ImageAheadEntries();

        public Task<IReadOnlyList<SystemBlueprintDriftEntry>> ComputeAsync(CancellationToken ct = default)
            => Task.FromResult(Entries);
    }
}

internal sealed class DriftTestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal const string SchemeName = "DriftTestScheme";
    internal const string PrincipalHeader = "X-Test-Principal";
    private const string SystemAdminOrg = "00000000-0000-0000-0000-000000000001";
    internal const string OrgAdminOrg = "00000000-0000-0000-0000-0000000000a1";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var principal = Context.Request.Headers[PrincipalHeader].ToString();
        if (string.IsNullOrEmpty(principal) || principal == "anonymous")
            return Task.FromResult(AuthenticateResult.NoResult());

        var audiences = Context.RequestServices.GetService<SorchaAudiences>()
                        ?? new SorchaAudiences(installationName: null);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "test-user"),
            new(ClaimTypes.Name, "Test User"),
            new("sub", "test-user"),
        };

        switch (principal)
        {
            case "sysadmin-platform":
                claims.Add(new Claim("org_id", SystemAdminOrg));
                claims.Add(new Claim(ClaimTypes.Role, "SystemAdmin"));
                claims.Add(new Claim("aud", audiences.For(Tier.Platform)));
                break;
            case "sysadmin-consumer":
                claims.Add(new Claim("org_id", SystemAdminOrg));
                claims.Add(new Claim(ClaimTypes.Role, "SystemAdmin"));
                claims.Add(new Claim("aud", audiences.For(Tier.Consumer)));
                break;
            case "org-admin":
                claims.Add(new Claim("org_id", OrgAdminOrg));
                claims.Add(new Claim(ClaimTypes.Role, "Administrator"));
                claims.Add(new Claim("aud", audiences.For(Tier.Platform)));
                break;
            default:
                return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        return Task.FromResult(AuthenticateResult.Success(
            new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
    }
}
