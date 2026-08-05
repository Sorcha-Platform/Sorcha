// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Service.Tests.Helpers;
using Sorcha.ServiceDefaults.Auth;
using Xunit;

namespace Sorcha.Register.Service.Tests.Endpoints;

/// <summary>
/// The two provenance endpoints: authorization, missing-evidence behaviour, and the rule that the
/// spine runs no verification.
/// </summary>
/// <remarks>
/// <para>
/// The most important test here is
/// <see cref="GetDocketTrail_WhenEvidenceCannotBeAssembled_Returns200WithUnverifiedRows_NotAn5xx"/>.
/// A 500 tells an auditor nothing; the whole point of the surface is to say WHICH link could not be
/// established (FR-020, SC-009).
/// </para>
/// <para>
/// Authentication is header-driven rather than baked per-factory, so the role gate and the tier gate
/// can both be exercised in one host without static state that would race under parallel test
/// execution.
/// </para>
/// </remarks>
[Collection("RegisterWebApp")]
public class ProvenanceEndpointTests : IClassFixture<ProvenanceWebApplicationFactory>
{
    private readonly ProvenanceWebApplicationFactory _factory;
    private readonly string _registerId;

    public ProvenanceEndpointTests(ProvenanceWebApplicationFactory factory)
    {
        _factory = factory;
        _registerId = factory.CreateTestRegisterAsync("Provenance Test Register", "prov-test-tenant").Result.Id;
    }

    private HttpClient ClientAs(string principal)
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Add(ProvenanceTestAuthHandler.PrincipalHeader, principal);
        return client;
    }

    // ── Authorization (FR-019) ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task Spine_Unauthenticated_Returns401()
    {
        var response = await ClientAs("anonymous").GetAsync($"/api/provenance/registers/{_registerId}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Trail_Unauthenticated_Returns401()
    {
        var response = await ClientAs("anonymous")
            .GetAsync($"/api/provenance/registers/{_registerId}/dockets/0");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Spine_AuthenticatedButNotAnAdministrator_Returns403()
    {
        var response = await ClientAs("platform-non-admin")
            .GetAsync($"/api/provenance/registers/{_registerId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// The tier gate is composed ON the role gate, not instead of it (CLAUDE.md pattern #13). A
    /// consumer-tier token carrying an administrator role must still be refused — otherwise a citizen
    /// surface could reach an administrative evidence view.
    /// </summary>
    [Fact]
    public async Task Spine_AdministratorRoleOnAConsumerTierToken_Returns403()
    {
        var response = await ClientAs("consumer-admin")
            .GetAsync($"/api/provenance/registers/{_registerId}");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Trail_AdministratorRoleOnAConsumerTierToken_Returns403()
    {
        var response = await ClientAs("consumer-admin")
            .GetAsync($"/api/provenance/registers/{_registerId}/dockets/0");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── Not found ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Spine_UnknownRegister_Returns404()
    {
        var response = await ClientAs("platform-admin")
            .GetAsync("/api/provenance/registers/ffffffffffffffffffffffffffffffff");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Trail_UnknownDocket_Returns404()
    {
        var response = await ClientAs("platform-admin")
            .GetAsync($"/api/provenance/registers/{_registerId}/dockets/99999");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── The important one: missing evidence is a 200, never a 5xx ────────────────────────────

    /// <summary>
    /// A docket with nothing recoverable — no predecessor held, no sealed commitment, no proposer, no
    /// votes, and a register whose origin this node's anchor does not cover — must still render.
    /// Every check reports unverified with a reason naming what stopped it.
    /// </summary>
    [Fact]
    public async Task GetDocketTrail_WhenEvidenceCannotBeAssembled_Returns200WithUnverifiedRows_NotAn5xx()
    {
        var docketNumber = await SeedBareDocketAsync();

        var response = await ClientAs("platform-admin")
            .GetAsync($"/api/provenance/registers/{_registerId}/dockets/{docketNumber}");

        response.StatusCode.Should().Be(HttpStatusCode.OK,
            "an auditor needs to know WHICH link could not be established; a 5xx tells them nothing");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var checks = body.GetProperty("checks").EnumerateArray().ToList();

        checks.Should().HaveCount(5, "one check per layer, always");

        checks.Select(c => c.GetProperty("layer").GetString())
            .Should().Equal("anchor", "chain", "seal", "signers", "proposer");

        foreach (var check in checks)
        {
            var layer = check.GetProperty("layer").GetString();
            var status = check.GetProperty("status").GetString();

            status.Should().Be("unverified",
                "layer {0} had no evidence to work from and must not claim otherwise", layer);

            check.GetProperty("reason").GetString().Should().NotBeNullOrWhiteSpace(
                "layer {0} must name what stopped it (SC-009)", layer);

            check.GetProperty("checkedAgainst").GetString().Should().NotBeNullOrWhiteSpace(
                "layer {0} must state its basis even when it could not run (FR-002)", layer);
        }
    }

    /// <summary>
    /// A single-validator deployment records no votes. The signers check must report unverified —
    /// never verified (SC-005). This is the defect the feature most needs not to have.
    /// </summary>
    [Fact]
    public async Task GetDocketTrail_WithNoRecordedVotes_ReportsSignersUnverified_NeverVerified()
    {
        var docketNumber = await SeedBareDocketAsync();

        var response = await ClientAs("platform-admin")
            .GetAsync($"/api/provenance/registers/{_registerId}/dockets/{docketNumber}");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var signers = body.GetProperty("checks").EnumerateArray()
            .Single(c => c.GetProperty("layer").GetString() == "signers");

        signers.GetProperty("status").GetString().Should().Be("unverified");
        signers.GetProperty("status").GetString().Should().NotBe("verified",
            "a green tick for quorum on a single-validator node would be the feature lying");
    }

    // ── T030: the spine runs no verification (plan D6, SC-007) ───────────────────────────────

    /// <summary>
    /// The spine response must carry no check results at all. This is asserted on the wire, not just
    /// on the type, because the cost being avoided is a caller entering the expensive path by
    /// accident.
    /// </summary>
    [Fact]
    public async Task Spine_CarriesNoCheckResults()
    {
        await SeedBareDocketAsync();
        await SeedBareDocketAsync();
        await SeedBareDocketAsync();

        var response = await ClientAs("platform-admin")
            .GetAsync($"/api/provenance/registers/{_registerId}?pageSize=200");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var raw = await response.Content.ReadAsStringAsync();
        raw.Should().NotContain("\"checks\"");
        raw.Should().NotContain("\"status\"");
        raw.Should().NotContain("\"checkedAgainst\"");

        var body = JsonSerializer.Deserialize<JsonElement>(raw);
        foreach (var entry in body.GetProperty("dockets").EnumerateArray())
        {
            entry.TryGetProperty("status", out _).Should().BeFalse();
            entry.TryGetProperty("checks", out _).Should().BeFalse();
        }
    }

    /// <summary>
    /// The DTO must have no status field to populate. Enforcing the absence on the type stops the
    /// wire-level test above being satisfied by a field that merely happens to serialise as null.
    /// </summary>
    [Fact]
    public void DocketSpineEntry_HasNoStatusFieldToPopulate()
    {
        var properties = typeof(Sorcha.Register.Service.Provenance.DocketSpineEntry)
            .GetProperties()
            .Select(p => p.Name)
            .ToList();

        properties.Should().NotContain(n => n.Contains("Status", StringComparison.OrdinalIgnoreCase));
        properties.Should().NotContain(n => n.Contains("Check", StringComparison.OrdinalIgnoreCase));
        properties.Should().NotContain(n => n.Contains("Verif", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Spine_Pages_AndReportsWhereToResume()
    {
        for (var i = 0; i < 4; i++)
        {
            await SeedBareDocketAsync();
        }

        var response = await ClientAs("platform-admin")
            .GetAsync($"/api/provenance/registers/{_registerId}?pageSize=2");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("dockets").GetArrayLength().Should().Be(2);
        body.GetProperty("hasMore").GetBoolean().Should().BeTrue();
        body.GetProperty("nextFromDocket").ValueKind.Should().NotBe(JsonValueKind.Null);
    }

    /// <summary>Phase 2's route is reserved, so its shape is settled before it is built.</summary>
    [Fact]
    public async Task InstanceLineage_IsReservedAndReports501()
    {
        var response = await ClientAs("platform-admin")
            .GetAsync($"/api/provenance/instances/{Guid.NewGuid():N}");

        response.StatusCode.Should().Be(HttpStatusCode.NotImplemented);
    }

    // ── Seeding ──────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// A docket carrying nothing a check can use: no sealed Merkle root, no proposer, no votes, and a
    /// predecessor this node does not hold. Exactly the shape a partial replica or a pre-Feature-187
    /// register produces.
    /// </summary>
    private async Task<ulong> SeedBareDocketAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRegisterRepository>();

        var existing = (await repository.GetDocketsAsync(_registerId)).ToList();
        var next = existing.Count == 0 ? 1UL : existing.Max(d => d.Id) + 1;

        var docket = new DocketHeader
        {
            Id = next,
            RegisterId = _registerId,
            // Claims a predecessor that is deliberately absent from this node.
            PreviousHash = $"absent-predecessor-{next}",
            Hash = $"hash-{next}-{Guid.NewGuid():N}",
            TransactionIds = [],
            TimeStamp = DateTime.UtcNow,
            MerkleRoot = string.Empty,
            ProposerValidatorId = string.Empty,
            Votes = [],
        };

        var inserted = await repository.InsertDocketAsync(docket);
        return inserted.Id;
    }
}

/// <summary>
/// A Register Service host whose authentication is driven by a request header, so one host can
/// serve anonymous, non-admin, consumer-tier-admin and platform-tier-admin principals.
/// </summary>
public class ProvenanceWebApplicationFactory : RegisterServiceWebApplicationFactory
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);

        builder.ConfigureServices(services =>
        {
            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = ProvenanceTestAuthHandler.SchemeName;
                options.DefaultChallengeScheme = ProvenanceTestAuthHandler.SchemeName;
            })
            .AddScheme<AuthenticationSchemeOptions, ProvenanceTestAuthHandler>(
                ProvenanceTestAuthHandler.SchemeName, _ => { });
        });
    }
}

/// <summary>
/// Builds a principal from the <c>X-Test-Principal</c> header.
/// </summary>
/// <remarks>
/// Header-driven rather than static, so tests running in parallel against one host cannot see each
/// other's principal.
/// </remarks>
internal sealed class ProvenanceTestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    internal const string SchemeName = "ProvenanceTestScheme";
    internal const string PrincipalHeader = "X-Test-Principal";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var principal = Context.Request.Headers[PrincipalHeader].ToString();

        if (string.IsNullOrEmpty(principal) || principal == "anonymous")
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var audiences = Context.RequestServices.GetService<SorchaAudiences>()
                        ?? new SorchaAudiences(installationName: null);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "test-user"),
            new(ClaimTypes.Name, "Test User"),
            new("sub", "test-user"),
            new("org_id", "test-org-001"),
        };

        switch (principal)
        {
            case "platform-admin":
                claims.Add(new Claim(ClaimTypes.Role, "Administrator"));
                claims.Add(new Claim("aud", audiences.For(Tier.Platform)));
                break;

            case "platform-non-admin":
                claims.Add(new Claim("aud", audiences.For(Tier.Platform)));
                break;

            case "consumer-admin":
                // Deliberately holds the role but only the consumer audience: the tier gate must
                // refuse it even though the role gate would not.
                claims.Add(new Claim(ClaimTypes.Role, "Administrator"));
                claims.Add(new Claim("aud", audiences.For(Tier.Consumer)));
                break;

            default:
                return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity(claims, SchemeName, ClaimTypes.Name, ClaimTypes.Role);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
