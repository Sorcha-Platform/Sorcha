// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sorcha.ServiceClients.OrgDidDocument;
using Sorcha.Tenant.Service.Configuration;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Services;
using Xunit;

namespace Sorcha.Tenant.Service.Tests.Services;

/// <summary>
/// Feature 120 US3 / T048 — confirms the published <c>OrgDidDocument</c> conforms to
/// W3C DID Core 1.0 structurally. A generic resolver that knows only IETF/W3C
/// standards must be able to dereference the document without Sorcha-specific tooling.
/// </summary>
public class OrgDidDocumentSchemaConformanceTests : IDisposable
{
    private const string SampleJwk = """{"kty":"OKP","crv":"Ed25519","x":"11qYAYKxCrfVS_7TyWQHOg7hcvPapiMlrwIaaPcHURo"}""";
    private const string SampleThumbprint = "kPrK_qmxVWaYVA9wwBF6Iuo3vVzz7TxHCTwXBygrS4k";

    private readonly SqliteConnection _connection;
    private readonly TenantDbContext _db;
    private readonly OrgDidDocumentService _sut;
    private readonly Guid _orgId = Guid.NewGuid();

    public OrgDidDocumentSchemaConformanceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TenantDbContext>().UseSqlite(_connection).Options;
        _db = new TenantDbContext(options);
        _db.Database.EnsureCreated();

        var settings = Options.Create(new TenantSettings { PlatformDomain = "sorcha.dev" });
        _sut = new OrgDidDocumentService(_db, settings, NullLogger<OrgDidDocumentService>.Instance);
    }

    private async Task<JsonElement> BuildAndParseDocument()
    {
        var snapshot = new OrgDidRegenerateRequest(
            _orgId, "Bootstrap", "ws1exampleaddress",
            [new OrgDidActiveKey(1, "ED25519", SampleJwk, SampleThumbprint)]);
        var row = await _sut.RegenerateFromSnapshotAsync(snapshot);
        return JsonDocument.Parse(row.DocumentJson).RootElement.Clone();
    }

    [Fact]
    public async Task DocumentRoot_HasW3cContext()
    {
        var doc = await BuildAndParseDocument();
        doc.GetProperty("@context").EnumerateArray().Select(e => e.GetString())
            .Should().Contain("https://www.w3.org/ns/did/v1");
    }

    [Fact]
    public async Task DocumentRoot_HasIdEqualToPrimaryDid()
    {
        var doc = await BuildAndParseDocument();
        doc.GetProperty("id").GetString().Should().StartWith("did:sorcha:org:");
    }

    [Fact]
    public async Task DocumentRoot_DeclaresAlsoKnownAsLinkingFederatedDidWeb()
    {
        var doc = await BuildAndParseDocument();
        var aka = doc.GetProperty("alsoKnownAs").EnumerateArray().Select(e => e.GetString()).ToArray();
        aka.Should().HaveCount(1);
        aka[0].Should().StartWith("did:web:sorcha.dev:orgs:");
    }

    [Fact]
    public async Task VerificationMethod_EveryEntryHasRequiredFields()
    {
        var doc = await BuildAndParseDocument();
        foreach (var vm in doc.GetProperty("verificationMethod").EnumerateArray())
        {
            vm.GetProperty("id").ValueKind.Should().Be(JsonValueKind.String);
            vm.GetProperty("type").GetString().Should().Be("JsonWebKey2020");
            vm.GetProperty("controller").ValueKind.Should().Be(JsonValueKind.String);
            vm.GetProperty("publicKeyJwk").ValueKind.Should().Be(JsonValueKind.Object);
        }
    }

    [Fact]
    public async Task VerificationMethod_EmitsDualEntriesPerKey_VersionedPlusThumbprint()
    {
        var doc = await BuildAndParseDocument();
        var ids = doc.GetProperty("verificationMethod")
            .EnumerateArray().Select(e => e.GetProperty("id").GetString()!).ToArray();

        ids.Should().HaveCount(2, "feature 120 D3 — dual-VM emission per active key");
        ids.Should().Contain(i => i!.EndsWith("#vc-issuance-1"));
        ids.Should().Contain(i => i!.EndsWith("#" + SampleThumbprint));
    }

    [Fact]
    public async Task AssertionMethod_ReferencesAllActiveVms()
    {
        var doc = await BuildAndParseDocument();
        var assertionIds = doc.GetProperty("assertionMethod")
            .EnumerateArray().Select(e => e.GetString()).ToArray();
        var vmIds = doc.GetProperty("verificationMethod")
            .EnumerateArray().Select(e => e.GetProperty("id").GetString()).ToArray();
        assertionIds.Should().BeEquivalentTo(vmIds);
    }

    [Fact]
    public async Task DocumentJson_ParsesViaSystemTextJson_NoSorchaSpecificExtensionsInMandatoryPositions()
    {
        // Generic-tooling smoke test — the published JSON must round-trip through plain
        // System.Text.Json without any Sorcha-specific converters or schema knowledge.
        var doc = await BuildAndParseDocument();
        // Any reader that walks the W3C-mandatory shape (@context, id, verificationMethod[*])
        // sees only standard types.
        doc.TryGetProperty("@context", out _).Should().BeTrue();
        doc.TryGetProperty("id", out _).Should().BeTrue();
        doc.TryGetProperty("verificationMethod", out _).Should().BeTrue();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
