// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Collections;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

using Moq;

using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Service.Credentials;
using Sorcha.Wallet.Service.Endpoints;

using Xunit;

namespace Sorcha.Wallet.Service.Tests.Endpoints;

/// <summary>
/// Guards the n1 defect (2026-07-14): a credential ingested BEFORE the nested
/// SD-JWT decoder fix (<see cref="Sorcha.Wallet.Service.Services.Implementation.SdJwtClaimProjection"/>)
/// keeps a stale, badly-decoded <c>CredentialEntity.ClaimsJson</c> in the store
/// forever — <c>{"address":{"_sd":["…"]}}</c> instead of a real address. There is
/// no backfill and no migration; <see cref="RawToken"/> is always the source of
/// truth, so both read endpoints must re-project from it rather than serve the
/// stored (possibly stale) <c>ClaimsJson</c> verbatim.
///
/// Uses the reflection-based static-handler invocation pattern established by
/// <c>CitizenWalletEnrolEndpointTests</c> — no <c>WebApplicationFactory</c>.
/// </summary>
public sealed class CredentialEndpointsHealingTests
{
    private const string WalletAddress = "ws1qholder1";
    private const string CredentialId = "urn:uuid:stale-address-credential";

    // --- SD-JWT construction helpers (RFC 9901 §4.2.1) — mirrors SdJwtClaimProjectionTests ---

    private static string B64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Disclosure(string salt, string name, object value)
    {
        var json = JsonSerializer.Serialize(new object[] { salt, name, value });
        return B64Url(Encoding.UTF8.GetBytes(json));
    }

    private static string Digest(string disclosure) =>
        B64Url(SHA256.HashData(Encoding.ASCII.GetBytes(disclosure)));

    private static string Token(object body, params string[] disclosures)
    {
        var header = B64Url(Encoding.UTF8.GetBytes("""{"alg":"ES256","typ":"dc+sd-jwt"}"""));
        var payload = B64Url(JsonSerializer.SerializeToUtf8Bytes(body));
        var jwt = $"{header}.{payload}.c2ln";
        return disclosures.Length == 0 ? jwt : jwt + "~" + string.Join("~", disclosures);
    }

    /// <summary>
    /// A valid SD-JWT carrying a NESTED disclosure for <c>address</c> (town + line1
    /// are individually disclosable children). This is a well-formed, fully
    /// reconstructable token — the bug is purely that the STORED ClaimsJson was
    /// decoded badly at ingest time, before the fix.
    /// </summary>
    private static string NestedAddressRawToken()
    {
        var town = Disclosure("s1", "town", "Edinburgh");
        var line1 = Disclosure("s2", "line1", "6/2 Warrender Park Terrace");
        var body = new Dictionary<string, object>
        {
            ["vct"] = "https://sorcha.dev/vc/assured-identity/v1",
            ["iss"] = "did:sorcha:org:ws11q",
            ["email"] = "stuart@stuartfraser.net",
            ["address"] = new Dictionary<string, object>
            {
                ["_sd"] = new[] { Digest(town), Digest(line1) }
            }
        };
        return Token(body, town, line1);
    }

    /// <summary>
    /// Reproduces exactly what shipped to a citizen's phone: the pre-fix decoder
    /// resolved only the TOP-LEVEL <c>_sd</c>, so <c>address</c> was stored verbatim
    /// as its own raw digest-array placeholder rather than reconstructed.
    /// </summary>
    private static CredentialEntity StaleCredentialWithGoodRawToken() => new()
    {
        Id = CredentialId,
        Type = "AssuredIdentityCredential",
        IssuerDid = "did:sorcha:org:ws11q",
        SubjectDid = WalletAddress,
        ClaimsJson = """{"email":"stuart@stuartfraser.net","address":{"_sd":["zSH_kfTeW2Mlc"]}}""",
        RawToken = NestedAddressRawToken(),
        IssuedAt = DateTimeOffset.UtcNow.AddDays(-30),
        Status = CredentialStatus.Active,
        WalletAddress = WalletAddress,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-30),
    };

    /// <summary>
    /// C1: a credential whose <c>RawToken</c> is NOT a decodable SD-JWT — e.g. a
    /// Feature 185 mdoc/CBOR proximity credential, a W3C JSON-LD VC, or a
    /// truncated token. <c>StoreCredentialRequest</c> accepts any RawToken +
    /// ClaimsJson pair, so this row is legitimate, not stale. The stored
    /// ClaimsJson is the ONLY source of truth in this case; unconditional
    /// re-projection must not wipe it to "{}".
    /// </summary>
    private const string MdocCredentialId = "urn:uuid:mdoc-proximity-credential";

    private static CredentialEntity MdocCredential() => new()
    {
        Id = MdocCredentialId,
        Type = "MobileDrivingLicence",
        IssuerDid = "did:sorcha:org:ws11q",
        SubjectDid = WalletAddress,
        ClaimsJson = """{"documentNumber":"D1234567","drivingPrivileges":["B"]}""",
        // A base64-encoded CBOR blob has no JWT structure at all (no '.' separators),
        // so SdJwtClaimProjection.Project cannot decode it and returns Empty.
        RawToken = "o2ppc3N1ZXJBdXRohEOhASag2BhZAcqjZ2RvY1R5cGV1b3JnLmlzby4xODAxMy41LjEubUw=",
        IssuedAt = DateTimeOffset.UtcNow.AddDays(-10),
        Status = CredentialStatus.Active,
        WalletAddress = WalletAddress,
        CreatedAt = DateTimeOffset.UtcNow.AddDays(-10),
    };

    // --- ListCredentials ---

    private static async Task<IResult> InvokeListCredentials(
        string walletAddress, ICredentialStore store, string? statusFilter = null)
    {
        var method = typeof(CredentialEndpoints).GetMethod(
            "ListCredentials", BindingFlags.NonPublic | BindingFlags.Static)!;
        method.Should().NotBeNull("ListCredentials handler should exist");

        var result = method.Invoke(null, [walletAddress, store, CancellationToken.None, statusFilter]);
        return await (Task<IResult>)result!;
    }

    private static JsonElement GetSingleListedClaimsJsonElement(IResult result)
    {
        var value = result.GetType().GetProperty("Value")!.GetValue(result);
        value.Should().NotBeNull();

        var items = ((IEnumerable)value!).Cast<object>().ToList();
        items.Should().ContainSingle();

        var item = items[0];
        var claimsJson = (string)item.GetType().GetProperty("ClaimsJson")!.GetValue(item)!;
        using var doc = JsonDocument.Parse(claimsJson);
        return doc.RootElement.Clone();
    }

    [Fact]
    public async Task ListCredentials_StaleStoredClaimsJson_ReconstructsAddressFromRawToken()
    {
        var store = new Mock<ICredentialStore>();
        store.Setup(s => s.GetByWalletAsync(WalletAddress, It.IsAny<CancellationToken>()))
            .ReturnsAsync([StaleCredentialWithGoodRawToken()]);

        var result = await InvokeListCredentials(WalletAddress, store.Object);

        var root = GetSingleListedClaimsJsonElement(result);
        var address = root.GetProperty("address");

        address.ValueKind.Should().Be(JsonValueKind.Object);
        address.GetProperty("town").GetString().Should().Be("Edinburgh");
        address.GetProperty("line1").GetString().Should().Be("6/2 Warrender Park Terrace");
    }

    [Fact]
    public async Task ListCredentials_StaleStoredClaimsJson_NeverServesRawSdDigestArray()
    {
        var store = new Mock<ICredentialStore>();
        store.Setup(s => s.GetByWalletAsync(WalletAddress, It.IsAny<CancellationToken>()))
            .ReturnsAsync([StaleCredentialWithGoodRawToken()]);

        var result = await InvokeListCredentials(WalletAddress, store.Object);

        var root = GetSingleListedClaimsJsonElement(result);
        root.GetRawText().Should().NotContain("_sd");
    }

    [Fact]
    public async Task ListCredentials_NonSdJwtRawToken_ServesStoredClaimsJsonInstead()
    {
        var store = new Mock<ICredentialStore>();
        store.Setup(s => s.GetByWalletAsync(WalletAddress, It.IsAny<CancellationToken>()))
            .ReturnsAsync([MdocCredential()]);

        var result = await InvokeListCredentials(WalletAddress, store.Object);

        var root = GetSingleListedClaimsJsonElement(result);
        root.GetProperty("documentNumber").GetString().Should().Be("D1234567");
    }

    [Fact]
    public async Task ListCredentials_NonSdJwtRawToken_DisclosableClaimsIsEmptyNotThrow()
    {
        var store = new Mock<ICredentialStore>();
        store.Setup(s => s.GetByWalletAsync(WalletAddress, It.IsAny<CancellationToken>()))
            .ReturnsAsync([MdocCredential()]);

        var result = await InvokeListCredentials(WalletAddress, store.Object);

        var value = result.GetType().GetProperty("Value")!.GetValue(result);
        var items = ((IEnumerable)value!).Cast<object>().ToList();
        items.Should().ContainSingle();

        var disclosable = (IEnumerable<string>)items[0].GetType()
            .GetProperty("DisclosableClaims")!.GetValue(items[0])!;
        disclosable.Should().BeEmpty();
    }

    // --- GetCredential ---

    private static async Task<IResult> InvokeGetCredential(
        string walletAddress, string credentialId, ICredentialStore store)
    {
        var method = typeof(CredentialEndpoints).GetMethod(
            "GetCredential", BindingFlags.NonPublic | BindingFlags.Static)!;
        method.Should().NotBeNull("GetCredential handler should exist");

        var result = method.Invoke(null, [walletAddress, credentialId, store, CancellationToken.None]);
        return await (Task<IResult>)result!;
    }

    [Fact]
    public async Task GetCredential_StaleStoredClaimsJson_ReconstructsAddressFromRawToken()
    {
        var store = new Mock<ICredentialStore>();
        store.Setup(s => s.GetByIdForWalletAsync(CredentialId, WalletAddress, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StaleCredentialWithGoodRawToken());

        var result = await InvokeGetCredential(WalletAddress, CredentialId, store.Object);

        var ok = result.Should().BeOfType<Ok<CredentialEntity>>().Subject;
        using var doc = JsonDocument.Parse(ok.Value!.ClaimsJson);
        var address = doc.RootElement.GetProperty("address");

        address.ValueKind.Should().Be(JsonValueKind.Object);
        address.GetProperty("town").GetString().Should().Be("Edinburgh");
        address.GetProperty("line1").GetString().Should().Be("6/2 Warrender Park Terrace");
    }

    [Fact]
    public async Task GetCredential_StaleStoredClaimsJson_NeverServesRawSdDigestArray()
    {
        var store = new Mock<ICredentialStore>();
        store.Setup(s => s.GetByIdForWalletAsync(CredentialId, WalletAddress, It.IsAny<CancellationToken>()))
            .ReturnsAsync(StaleCredentialWithGoodRawToken());

        var result = await InvokeGetCredential(WalletAddress, CredentialId, store.Object);

        var ok = result.Should().BeOfType<Ok<CredentialEntity>>().Subject;
        ok.Value!.ClaimsJson.Should().NotContain("_sd");
    }

    [Fact]
    public async Task GetCredential_NonSdJwtRawToken_ServesStoredClaimsJsonInstead()
    {
        var store = new Mock<ICredentialStore>();
        store.Setup(s => s.GetByIdForWalletAsync(MdocCredentialId, WalletAddress, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MdocCredential());

        var result = await InvokeGetCredential(WalletAddress, MdocCredentialId, store.Object);

        var ok = result.Should().BeOfType<Ok<CredentialEntity>>().Subject;
        using var doc = JsonDocument.Parse(ok.Value!.ClaimsJson);
        doc.RootElement.GetProperty("documentNumber").GetString().Should().Be("D1234567");
    }

    [Fact]
    public async Task GetCredential_UnknownCredential_ReturnsNotFound()
    {
        var store = new Mock<ICredentialStore>();
        store.Setup(s => s.GetByIdForWalletAsync(CredentialId, WalletAddress, It.IsAny<CancellationToken>()))
            .ReturnsAsync((CredentialEntity?)null);

        var result = await InvokeGetCredential(WalletAddress, CredentialId, store.Object);

        result.Should().BeOfType<NotFound>();
    }
}
