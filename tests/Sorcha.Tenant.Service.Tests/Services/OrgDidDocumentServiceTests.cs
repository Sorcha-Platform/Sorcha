// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Sorcha.ServiceClients.OrgDidDocument;
using Sorcha.Tenant.Service.Configuration;
using Sorcha.Tenant.Service.Data;
using Sorcha.Tenant.Service.Models;
using Sorcha.Tenant.Service.Services;

namespace Sorcha.Tenant.Service.Tests.Services;

public class OrgDidDocumentServiceTests : IDisposable
{
    private const string SampleJwk = """{"kty":"OKP","crv":"Ed25519","x":"11qYAYKxCrfVS_7TyWQHOg7hcvPapiMlrwIaaPcHURo"}""";
    private const string SampleThumbprint = "kPrK_qmxVWaYVA9wwBF6Iuo3vVzz7TxHCTwXBygrS4k";

    private readonly SqliteConnection _connection;
    private readonly TenantDbContext _db;
    private readonly OrgDidDocumentService _sut;
    private readonly Guid _orgId = Guid.NewGuid();

    public OrgDidDocumentServiceTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<TenantDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new TenantDbContext(options);
        _db.Database.EnsureCreated();

        var settings = Options.Create(new TenantSettings { PlatformDomain = "sorcha.dev" });
        _sut = new OrgDidDocumentService(_db, settings, NullLogger<OrgDidDocumentService>.Instance);
    }

    private OrgDidRegenerateRequest BuildSnapshot(int rotationIndex = 1) => new(
        OrganizationId: _orgId,
        KeyEventReason: "IssuanceKeyDerived",
        WalletAddress: "ws1exampleaddress",
        ActiveKeys: [new OrgDidActiveKey(rotationIndex, "ED25519", SampleJwk, SampleThumbprint)]);

    [Fact]
    public async Task RegenerateFromSnapshot_FirstCall_PersistsRowWithDualVms()
    {
        var row = await _sut.RegenerateFromSnapshotAsync(BuildSnapshot());

        row.PrimaryDid.Should().Be("did:sorcha:org:ws1exampleaddress");
        row.FederatedDid.Should().Be($"did:web:sorcha.dev:orgs:{_orgId}");
        row.Version.Should().Be(1);
        row.LastRegenerationReason.Should().Be(KeyEventReason.IssuanceKeyDerived);

        // Document carries both versioned and thumbprint VM ids.
        row.DocumentJson.Should().Contain("#vc-issuance-1");
        row.DocumentJson.Should().Contain($"#{SampleThumbprint}");
        row.DocumentJson.Should().Contain("alsoKnownAs");
        row.DocumentJson.Should().Contain($"did:web:sorcha.dev:orgs:{_orgId}");
    }

    [Fact]
    public async Task RegenerateFromSnapshot_IdenticalSnapshot_IsNoOp()
    {
        var first = await _sut.RegenerateFromSnapshotAsync(BuildSnapshot());
        var firstVersion = first.Version;

        var second = await _sut.RegenerateFromSnapshotAsync(BuildSnapshot());
        second.Version.Should().Be(firstVersion);
        second.KeyVersionFingerprint.Should().Be(first.KeyVersionFingerprint);
    }

    [Fact]
    public async Task RegenerateFromSnapshot_DifferentRotation_BumpsVersion()
    {
        await _sut.RegenerateFromSnapshotAsync(BuildSnapshot(rotationIndex: 1));
        var second = await _sut.RegenerateFromSnapshotAsync(BuildSnapshot(rotationIndex: 2));

        second.Version.Should().Be(2);
        second.DocumentJson.Should().Contain("#vc-issuance-2");
    }

    [Fact]
    public async Task GetAsync_AfterRegenerate_ReturnsSameRow()
    {
        var written = await _sut.RegenerateFromSnapshotAsync(BuildSnapshot());
        var fetched = await _sut.GetAsync(_orgId);

        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(written.Id);
    }

    [Fact]
    public async Task RegenerateFromSnapshot_EmptyKeys_Throws()
    {
        var bad = new OrgDidRegenerateRequest(_orgId, "Bootstrap", "ws1addr", []);
        var act = async () => await _sut.RegenerateFromSnapshotAsync(bad);
        await act.Should().ThrowAsync<ArgumentException>();
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
