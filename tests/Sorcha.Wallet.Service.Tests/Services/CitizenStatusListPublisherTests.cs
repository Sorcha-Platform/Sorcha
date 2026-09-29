// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Sorcha.Wallet.Core.Data;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Core.Services.Interfaces;
using Microsoft.Extensions.Logging.Abstractions;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Crypto.Signers;
using Sorcha.Cryptography.Core;
using Sorcha.ServiceClients.Did;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;
using Xunit;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Tests for <see cref="CitizenStatusListPublisher"/> (Feature 114, #1759).
/// </summary>
/// <remarks>
/// #1759: every list must be signed under a DID a verifier can resolve to the key that signed it —
/// the org's issuer DID when it has a VC-issuance key, otherwise a did:key of its status key. Keys
/// come from the real <see cref="CryptoModule"/> (the format derivation actually returns); the fixture
/// used to hand the publisher a DER P-256 blob the real derivation never produces. Signatures are
/// checked by resolving the list's OWN iss through the real <see cref="KeyDidResolver"/>.
/// </remarks>
public sealed class CitizenStatusListPublisherTests : IDisposable
{
    private readonly TestCitizenWalletDbContext _db;
    private readonly Mock<IWalletRepository> _repoMock = new();
    private readonly Mock<IKeyManagementService> _keyMgmtMock = new();
    private readonly Mock<IIssuanceKeyService> _issuanceKeys = new();
    private readonly IConfiguration _configuration;
    private readonly CitizenStatusListPublisher _publisher;

    private KeySet _statusKey;
    private const string SigningWalletAddress = "ws1qstatuslist1";

    public CitizenStatusListPublisherTests()
    {
        var dbOptions = new DbContextOptionsBuilder<TestCitizenWalletDbContext>()
            .UseInMemoryDatabase($"CitizenStatusListPublisherTests-{Guid.NewGuid():N}")
            .Options;
        _db = new TestCitizenWalletDbContext(dbOptions);

        _configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CitizenWallet:PublicBaseUrl"] = "https://verify.test"
            })
            .Build();

        UseStatusWallet("ED25519");

        _keyMgmtMock
            .Setup(k => k.DecryptPrivateKeyAsync(It.IsAny<string>(), It.IsAny<string>()))
            .ReturnsAsync(new byte[64]);

        // A keyless org by default: no VC-issuance key, so lists are signed under a did:key.
        _issuanceKeys
            .Setup(k => k.GetActiveSigningMaterialAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IssuanceSigningMaterial?)null);

        _publisher = new CitizenStatusListPublisher(
            _db,
            _repoMock.Object,
            _keyMgmtMock.Object,
            _issuanceKeys.Object,
            TimeProvider.System,
            _configuration,
            Mock.Of<ILogger<CitizenStatusListPublisher>>());
    }

    /// <summary>The org's status wallet uses <paramref name="algorithm"/>; derivation returns a real key of it.</summary>
    private void UseStatusWallet(string algorithm)
    {
        var network = algorithm == "ED25519" ? WalletNetworks.ED25519 : WalletNetworks.NISTP256;
        _statusKey = new CryptoModule().GenerateKeySetAsync(network).GetAwaiter().GetResult().Value!;

        _repoMock
            .Setup(r => r.GetByAddressAsync(SigningWalletAddress, false, false, false, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WalletEntity
            {
                Address = SigningWalletAddress,
                EncryptedPrivateKey = "encrypted-master",
                EncryptionKeyId = "k",
                Algorithm = algorithm,
                Owner = "org-1",
                Tenant = "tenant-1",
                Name = "Org Status Signer"
            });

        // A fresh copy per call — the publisher zeroises the private key after signing.
        _keyMgmtMock
            .Setup(k => k.DeriveKeyAtPathAsync(It.IsAny<byte[]>(), It.IsAny<DerivationPath>(), algorithm))
            .ReturnsAsync(() => ((byte[])_statusKey.PrivateKey.Key!.Clone(), (byte[])_statusKey.PublicKey.Key!.Clone()));
    }

    /// <summary>Gives the org an active VC-issuance key (a real Ed25519 key) and returns its public key.</summary>
    private byte[] GiveOrgAnIssuanceKey(Guid orgId, string issuerDid = "did:sorcha:org:ws11qorg")
    {
        var keys = new CryptoModule().GenerateKeySetAsync(WalletNetworks.ED25519).GetAwaiter().GetResult().Value!;
        _issuanceKeys
            .Setup(k => k.GetActiveSigningMaterialAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new IssuanceSigningMaterial(
                orgId, issuerDid, issuerDid + "#vc-issuance-0",
                (byte[])keys.PrivateKey.Key!.Clone(), "ED25519", 0));
        return keys.PublicKey.Key!;
    }

    [Fact]
    public async Task AllocateIndexAsync_FirstAllocation_CreatesListZero_AndReturnsIndexZero()
    {
        var orgId = Guid.NewGuid();

        var (listId, idx) = await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);

        listId.Should().Be(0);
        idx.Should().Be(0);

        var lists = await _db.CitizenDeviceStatusLists.Where(l => l.OrganizationId == orgId).ToListAsync();
        lists.Should().HaveCount(1);
        lists[0].LastAllocatedIndex.Should().Be(0);
        lists[0].SignedJwt.Should().NotBeNullOrEmpty(because: "list is signed on creation");
    }

    [Fact]
    public async Task AllocateIndexAsync_Sequential_IncrementsIndex()
    {
        var orgId = Guid.NewGuid();

        var (l1, i1) = await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);
        var (l2, i2) = await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);
        var (l3, i3) = await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);

        l1.Should().Be(0); l2.Should().Be(0); l3.Should().Be(0);
        i1.Should().Be(0); i2.Should().Be(1); i3.Should().Be(2);
    }

    [Fact]
    public async Task FlipAsync_SetsBit_IncrementsRevokedCount_ResignsList()
    {
        var orgId = Guid.NewGuid();
        var (listId, idx) = await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);

        var firstJwt = (await _publisher.GetSignedListAsync(orgId, listId))!;

        // Add a tiny delay so iat changes
        await Task.Delay(1100);

        await _publisher.FlipAsync(orgId, listId, idx, SigningWalletAddress);

        var list = await _db.CitizenDeviceStatusLists.FirstAsync(l => l.OrganizationId == orgId);
        list.RevokedCount.Should().Be(1);
        (list.Bitstring[idx / 8] & (1 << (idx % 8))).Should().NotBe(0);
        list.SignedJwt.Should().NotBe(firstJwt, because: "list was re-signed after flip");
    }

    [Fact]
    public async Task FlipAsync_Idempotent_DoesNotDoubleCountRevocations()
    {
        var orgId = Guid.NewGuid();
        var (listId, idx) = await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);

        await _publisher.FlipAsync(orgId, listId, idx, SigningWalletAddress);
        await _publisher.FlipAsync(orgId, listId, idx, SigningWalletAddress);

        var list = await _db.CitizenDeviceStatusLists.FirstAsync(l => l.OrganizationId == orgId);
        list.RevokedCount.Should().Be(1);
    }

    [Fact]
    public async Task GetSignedListAsync_Unknown_ReturnsNull()
    {
        var jwt = await _publisher.GetSignedListAsync(Guid.NewGuid(), 0);
        jwt.Should().BeNull();
    }

    [Fact]
    public async Task BuildStatusListUri_UsesConfiguredBaseUrl()
    {
        var orgId = Guid.NewGuid();
        var uri = _publisher.BuildStatusListUri(orgId, 7);

        uri.Should().Be($"https://verify.test/api/v1/wallet/status/{orgId}/citizen-devices/7.statuslist+jwt");
    }

    [Fact]
    public async Task SignedJwt_KeylessOrg_IsAStatusListSignedUnderADidKey()
    {
        var orgId = Guid.NewGuid();
        await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);
        var (header, payload) = Parts((await _publisher.GetSignedListAsync(orgId, 0))!);

        header.GetProperty("alg").GetString().Should().Be("EdDSA");
        header.GetProperty("typ").GetString().Should().Be("statuslist+jwt");
        var iss = payload.GetProperty("iss").GetString()!;
        iss.Should().StartWith("did:key:z6Mk", "an org with no issuer DID signs as a did:key of its status key");
        header.GetProperty("kid").GetString().Should().StartWith(iss + "#");
        (await _publisher.GetSignerDidAsync(orgId, 0)).Should().Be(iss);

        payload.GetProperty("sub").GetString().Should().Be(_publisher.BuildStatusListUri(orgId, 0));
        payload.GetProperty("status_list").GetProperty("bits").GetInt32().Should().Be(1);
        payload.GetProperty("exp").GetInt64().Should().BeGreaterThan(payload.GetProperty("iat").GetInt64());
    }

    [Theory]
    [InlineData("ED25519")]
    [InlineData("NISTP256")]
    public async Task SignedJwt_KeylessOrg_VerifiesAgainstTheKeyItsOwnIssResolvesTo(string statusWalletAlgorithm)
    {
        UseStatusWallet(statusWalletAlgorithm);
        var orgId = Guid.NewGuid();
        await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);
        var jwt = (await _publisher.GetSignedListAsync(orgId, 0))!;
        var (header, payload) = Parts(jwt);

        // What a verifier does: resolve the list's iss, take the verification method its kid names.
        var doc = await new KeyDidResolver(NullLogger<KeyDidResolver>.Instance)
            .ResolveAsync(payload.GetProperty("iss").GetString()!);
        doc.Should().NotBeNull();
        var vm = doc!.VerificationMethod.Single(v => v.Id == header.GetProperty("kid").GetString());

        VerifiesWith(jwt, vm.PublicKeyJwk!.Value).Should().BeTrue();
    }

    [Fact]
    public async Task SignedJwt_OrgWithIssuanceKey_IsSignedUnderTheOrgsIssuerDidAndKey()
    {
        var orgId = Guid.NewGuid();
        var orgPublicKey = GiveOrgAnIssuanceKey(orgId);

        await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);
        var jwt = (await _publisher.GetSignedListAsync(orgId, 0))!;
        var (header, payload) = Parts(jwt);

        payload.GetProperty("iss").GetString().Should().Be("did:sorcha:org:ws11qorg",
            "a device-bound copy's list must carry the same iss as the copy");
        header.GetProperty("kid").GetString().Should().Be("did:sorcha:org:ws11qorg#vc-issuance-0");
        VerifiesWith(jwt, OkpJwk(orgPublicKey)).Should().BeTrue();
    }

    [Fact]
    public async Task AllocateIndexAsync_OrgGainsAnIssuanceKey_OpensANewListAndTheOldOneKeepsItsSigner()
    {
        var orgId = Guid.NewGuid();
        var (oldList, oldIdx) = await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);
        var oldSigner = await _publisher.GetSignerDidAsync(orgId, oldList);

        GiveOrgAnIssuanceKey(orgId);
        var (newList, _) = await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);

        newList.Should().Be(oldList + 1, "a list's signer is fixed, so a new signer needs a new list");
        (await _publisher.GetSignerDidAsync(orgId, newList)).Should().Be("did:sorcha:org:ws11qorg");

        // Credentials already pointing at the old list must keep resolving the signer they were given.
        await _publisher.FlipAsync(orgId, oldList, oldIdx, SigningWalletAddress);
        var (_, payload) = Parts((await _publisher.GetSignedListAsync(orgId, oldList))!);
        payload.GetProperty("iss").GetString().Should().Be(oldSigner);
    }

    [Fact]
    public async Task FlipAsync_OrgSignedListWhoseKeyIsGone_FailsClosedAndKeepsThePreviousToken()
    {
        var orgId = Guid.NewGuid();
        GiveOrgAnIssuanceKey(orgId);
        var (listId, idx) = await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);
        var before = await _publisher.GetSignedListAsync(orgId, listId);

        _issuanceKeys
            .Setup(k => k.GetActiveSigningMaterialAsync(orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IssuanceSigningMaterial?)null);
        var act = () => _publisher.FlipAsync(orgId, listId, idx, SigningWalletAddress);

        // Never re-sign under a different key: credentials pin to the recorded signer.
        await act.Should().ThrowAsync<InvalidOperationException>();
        (await _publisher.GetSignedListAsync(orgId, listId)).Should().Be(before);
    }

    [Fact]
    public async Task SignedJwt_StatusListBitsRoundTripDeflate_AndShowsRevokedBit()
    {
        var orgId = Guid.NewGuid();
        var (listId, idx) = await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);
        await _publisher.FlipAsync(orgId, listId, idx, SigningWalletAddress);

        var jwt = (await _publisher.GetSignedListAsync(orgId, listId))!;
        var payload = JsonDocument.Parse(Decode(jwt.Split('.')[1])).RootElement;
        var lstB64 = payload.GetProperty("status_list").GetProperty("lst").GetString()!;

        var compressed = Base64Url.DecodeFromChars(lstB64);
        using var ms = new MemoryStream(compressed);
        using var inflater = new ZLibStream(ms, CompressionMode.Decompress);
        using var output = new MemoryStream();
        await inflater.CopyToAsync(output);
        var bits = output.ToArray();

        bits.Length.Should().Be(32_768 / 8);
        (bits[idx / 8] & (1 << (idx % 8))).Should().NotBe(0);
    }

    [Fact]
    public async Task FlipAsync_IetfSpecGoldenVector_ProducesTheExactBytesTheSpecStates()
    {
        // Issue #1499. draft-ietf-oauth-status-list-09 §4.1's worked example: 16 entries with status
        // values 1,0,0,1,1,1,0,1,1,1,0,0,0,1,0,1 (index 0..15) pack — per the spec's own text,
        // "least significant bit (0) to the most significant bit (7)" — to bytes 0xb9, 0xa3. This
        // pins the WRITE side against the literal spec bytes, not just against this rail's own
        // reader (which would pass even if both sides agreed on the wrong convention).
        var orgId = Guid.NewGuid();
        int[] setIndices = [0, 3, 4, 5, 7, 8, 9, 13, 15];

        (int ListId, int Index) alloc = default;
        for (var i = 0; i <= 15; i++)
        {
            alloc = await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);
        }

        foreach (var idx in setIndices)
        {
            await _publisher.FlipAsync(orgId, alloc.ListId, idx, SigningWalletAddress);
        }

        var list = await _db.CitizenDeviceStatusLists.FirstAsync(l => l.OrganizationId == orgId);
        list.Bitstring[0].Should().Be(0xb9);
        list.Bitstring[1].Should().Be(0xa3);
    }

    [Fact]
    public async Task FlipAsync_OutOfRangeIndex_ThrowsArgumentOutOfRange()
    {
        var orgId = Guid.NewGuid();
        await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);

        var act = () => _publisher.FlipAsync(orgId, 0, 999_999, SigningWalletAddress);

        await act.Should().ThrowAsync<ArgumentOutOfRangeException>();
    }

    [Fact]
    public async Task FlipAsync_UnknownList_ThrowsKeyNotFound()
    {
        var act = () => _publisher.FlipAsync(Guid.NewGuid(), 0, 0, SigningWalletAddress);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task FlipAsync_OrgKeyNowSignsUnderADifferentIssuerDid_FailsClosed()
    {
        var orgId = Guid.NewGuid();
        GiveOrgAnIssuanceKey(orgId, "did:sorcha:org:ws11qorg");
        var (listId, idx) = await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);

        GiveOrgAnIssuanceKey(orgId, "did:sorcha:org:ws11qsomeoneelse");
        var act = () => _publisher.FlipAsync(orgId, listId, idx, SigningWalletAddress);

        await act.Should().ThrowAsync<InvalidOperationException>(
            "credentials pin this list to did:sorcha:org:ws11qorg; a list signed as anyone else is useless to them");
    }

    [Fact]
    public async Task FlipAsync_StatusKeyNoLongerDerivesTheRecordedDidKey_FailsClosed()
    {
        var orgId = Guid.NewGuid();
        var (listId, idx) = await _publisher.AllocateIndexAsync(orgId, SigningWalletAddress);

        UseStatusWallet("ED25519"); // a different key now comes out of derivation
        var act = () => _publisher.FlipAsync(orgId, listId, idx, SigningWalletAddress);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task StatusSignerDidKey_P256_BothYParities_ResolveToTheKeyTheyCameFrom()
    {
        // Point compression encodes y's parity in the prefix byte. A random key per run would test one
        // parity at a time, so a parity bug would fail only half the runs — search seeds for both.
        var resolver = new KeyDidResolver(NullLogger<KeyDidResolver>.Instance);
        var seen = new HashSet<int>();
        for (byte seed = 1; seen.Count < 2 && seed < 64; seed++)
        {
            var keys = (await new CryptoModule().GenerateKeySetAsync(
                WalletNetworks.NISTP256, Enumerable.Repeat(seed, 32).ToArray())).Value!;
            var pub = keys.PublicKey.Key!;
            if (!seen.Add(pub[63] & 1)) continue;

            var (did, kid) = StatusSignerDidKey.FromPublicKey("NISTP256", pub);
            var jwk = (await resolver.ResolveAsync(did))!.VerificationMethod.Single(v => v.Id == kid).PublicKeyJwk!.Value;

            Base64Url.DecodeFromChars(jwk.GetProperty("x").GetString()).Should().Equal(pub[..32]);
            Base64Url.DecodeFromChars(jwk.GetProperty("y").GetString()).Should().Equal(pub[32..64]);
        }
        seen.Should().HaveCount(2, "the test must exercise both y parities");
    }

    private static (JsonElement Header, JsonElement Payload) Parts(string jwt)
    {
        var parts = jwt.Split('.');
        parts.Should().HaveCount(3);
        return (JsonDocument.Parse(Decode(parts[0])).RootElement, JsonDocument.Parse(Decode(parts[1])).RootElement);
    }

    private static JsonElement OkpJwk(byte[] publicKey) => JsonSerializer.SerializeToElement(new
    {
        kty = "OKP", crv = "Ed25519", x = Base64Url.EncodeToString(publicKey)
    });

    /// <summary>Independent verification (BouncyCastle / BCL) — never the publisher's own code.</summary>
    private static bool VerifiesWith(string jwt, JsonElement jwk)
    {
        var parts = jwt.Split('.');
        var input = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        var sig = Base64Url.DecodeFromChars(parts[2]);
        if (jwk.GetProperty("kty").GetString() == "OKP")
        {
            var v = new Ed25519Signer();
            v.Init(false, new Ed25519PublicKeyParameters(Base64Url.DecodeFromChars(jwk.GetProperty("x").GetString()), 0));
            v.BlockUpdate(input, 0, input.Length);
            return v.VerifySignature(sig);
        }
        using var ecdsa = ECDsa.Create(new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = Base64Url.DecodeFromChars(jwk.GetProperty("x").GetString()),
                Y = Base64Url.DecodeFromChars(jwk.GetProperty("y").GetString()),
            },
        });
        return ecdsa.VerifyData(input, sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    private static string Decode(string base64Url)
    {
        var bytes = Base64Url.DecodeFromChars(base64Url);
        return Encoding.UTF8.GetString(bytes);
    }

    public void Dispose()
    {
        _db.Dispose();
    }
}
