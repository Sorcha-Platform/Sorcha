// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text;
using System.Text.Json;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

using Sorcha.Serialization;
using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Wallet;
using Sorcha.Wallet.Core.Domain.Entities;

using Xunit;

namespace Sorcha.ServiceClients.Tests.Wallet;

/// <summary>
/// Contract tests for <see cref="WalletServiceClient.GetCredentialAsync"/> — the READ path.
/// </summary>
/// <remarks>
/// <para>
/// Regression cover for issue #1475, where credential revoke / suspend / reinstate / refresh were
/// all dead on every deployment. The wallet returns the stored <c>CredentialEntity</c>
/// (<c>id</c>, <c>claimsJson</c>); the client deserialised it into
/// <see cref="CredentialIssuanceResult"/>, whose <c>required</c> members are <c>credentialId</c>
/// and <c>claims</c>. The resulting <c>JsonException</c> was swallowed into <c>null</c> and the
/// caller reported a bodiless 404 for a credential that plainly existed.
/// </para>
/// <para>
/// The existing endpoint tests could not catch this: they MOCK <c>IWalletServiceClient</c>, so
/// they stub out the very seam that was broken. These tests drive the real client over a real
/// HTTP round trip, and build the fixture by serialising an actual <see cref="CredentialEntity"/>
/// with the same options the Wallet Service uses — so a rename on EITHER side fails here rather
/// than surfacing in production as "credential not found".
/// </para>
/// </remarks>
public class WalletServiceClientGetCredentialTests
{
    private const string IssuerWallet = "ws11qrddwqag97avv058p4q4phgpqc36j9yw4596j0ceuzklmnl3v6puq5c53e0";
    private const string SubjectWallet = "ws11qrlyjd4jwdky9lnyw200zauxj7qhvp6m22ee0e9xrcvkkfgc3fvus9j2ugf";
    private const string CredentialId = "urn:uuid:a7915fc6-3ab7-4ca8-a783-69ff66d53a66";

    /// <summary>
    /// Builds the exact payload the Wallet Service returns from
    /// <c>GET /api/v1/wallets/{walletAddress}/credentials/{credentialId}</c>, which is
    /// <c>Results.Ok(credential)</c> over the persisted entity.
    /// </summary>
    private static string RealWalletReadPayload()
    {
        var entity = new CredentialEntity
        {
            Id = CredentialId,
            Type = "https://sorcha.dev/vc/cyber-essentials-uac/v1",
            IssuerDid = IssuerWallet,
            SubjectDid = SubjectWallet,
            ClaimsJson = """{"compliant":true,"assessmentDate":"2026-06-02","scopeDeviceCount":42}""",
            IssuedAt = DateTimeOffset.Parse("2026-08-17T16:46:06.088115+00:00"),
            ExpiresAt = DateTimeOffset.Parse("2027-08-17T16:46:06.088115+00:00"),
            RawToken = "eyJhbGciOiJFZERTQSJ9.eyJpc3MiOiJkaWQ6c29yY2hhIn0.sig",
            Status = CredentialStatus.Active,
            WalletAddress = IssuerWallet,
            RegisterId = "2141b08339d34c27824536ec250b025e",
            StatusListUrl = "https://n1.sorcha.dev/api/v1/credentials/status-lists/list-1",
            StatusListIndex = 0,
            DisplayConfigJson = """{"credentialName":"Cyber Essentials UAC"}"""
        };

        return JsonSerializer.Serialize(entity, SorchaJson.Options);
    }

    [Fact]
    public async Task GetCredentialAsync_MapsTheWalletsRealStoredCredentialShape()
    {
        // This is the test that goes RED against the pre-#1475 client: the wallet's payload has
        // no "credentialId" and no "claims", so the old deserialisation threw and returned null.
        var client = BuildClient(_ => Respond(HttpStatusCode.OK, RealWalletReadPayload()));

        var result = await client.GetCredentialAsync(IssuerWallet, CredentialId);

        result.Should().NotBeNull(
            "the wallet returned the credential — a mapping failure must never present as absence");
        result!.CredentialId.Should().Be(CredentialId, "the wallet sends this as 'id'");
        result.IssuerDid.Should().Be(IssuerWallet, "the lifecycle endpoints authorise on this");
        result.SubjectDid.Should().Be(SubjectWallet);
        result.Type.Should().Be("https://sorcha.dev/vc/cyber-essentials-uac/v1");
        result.RegisterId.Should().Be("2141b08339d34c27824536ec250b025e",
            "revocation submits a CredentialStatusChange transaction against this register");
        result.StatusListIndex.Should().Be(0);
        result.StatusListUrl.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task GetCredentialAsync_ExpandsClaimsJsonIntoTheClaimsDictionary()
    {
        // The wallet stores claims as a serialised STRING; consumers read them as a dictionary.
        var client = BuildClient(_ => Respond(HttpStatusCode.OK, RealWalletReadPayload()));

        var result = await client.GetCredentialAsync(IssuerWallet, CredentialId);

        result!.Claims.Should().ContainKey("compliant");
        result.Claims.Should().ContainKey("assessmentDate");
        result.Claims.Should().ContainKey("scopeDeviceCount");
    }

    [Theory]
    // The wallet serialises CredentialStatus with KebabCaseLower, so these are the real wire values.
    [InlineData("active", "Active")]
    [InlineData("suspended", "Suspended")]
    [InlineData("revoked", "Revoked")]
    [InlineData("pending-acceptance", "PendingAcceptance")]
    // Already-canonical values must survive untouched.
    [InlineData("Active", "Active")]
    [InlineData("PendingAcceptance", "PendingAcceptance")]
    public async Task GetCredentialAsync_NormalisesTheStatusToTheDocumentedPascalCase(
        string wireStatus, string expected)
    {
        // The lifecycle gate is `Status is not ("Active" or "Suspended")` — an exact, case-sensitive
        // match. Passing the wire spelling through unchanged refuses an Active credential with
        // "Credential must be in Active or Suspended state to revoke" while every surface shows it
        // as active. That was the second defect behind #1475.
        var payload = RealWalletReadPayload().Replace("\"status\":\"active\"", $"\"status\":\"{wireStatus}\"");
        payload.Should().Contain(wireStatus, "the fixture must actually carry the status under test");

        var client = BuildClient(_ => Respond(HttpStatusCode.OK, payload));

        var result = await client.GetCredentialAsync(IssuerWallet, CredentialId);

        result!.Status.Should().Be(expected);
    }

    [Fact]
    public async Task GetCredentialAsync_DoesNotMakeNonRevocableStatesLookRevocable()
    {
        // Normalisation must not flatten every state into something the gate accepts —
        // pending-acceptance and revoked have to keep failing it.
        var payload = RealWalletReadPayload().Replace("\"status\":\"active\"", "\"status\":\"pending-acceptance\"");
        var client = BuildClient(_ => Respond(HttpStatusCode.OK, payload));

        var result = await client.GetCredentialAsync(IssuerWallet, CredentialId);

        (result!.Status is "Active" or "Suspended").Should().BeFalse(
            "a credential the holder has not accepted is not revocable");
    }

    [Fact]
    public async Task GetCredentialAsync_ReturnsNull_WhenTheWalletGenuinelyHasNoSuchCredential()
    {
        var client = BuildClient(_ => Respond(HttpStatusCode.NotFound, string.Empty));

        var result = await client.GetCredentialAsync(IssuerWallet, CredentialId);

        result.Should().BeNull("404 is the one status that legitimately means absence");
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetCredentialAsync_Throws_WhenTheLookupItselfFails(HttpStatusCode status)
    {
        // Collapsing these to null is what made #1475 silent: a lookup failure was reported to
        // the caller as "no such credential", which is a different and misleading claim.
        var client = BuildClient(_ => Respond(status, """{"error":"nope"}"""));

        var act = () => client.GetCredentialAsync(IssuerWallet, CredentialId);

        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetCredentialAsync_Throws_WhenTheResponseCarriesNoIdentifier()
    {
        // A 200 whose body cannot be mapped is an error about the READ, not an absent credential.
        var client = BuildClient(_ => Respond(HttpStatusCode.OK, """{"type":"SomeCredential"}"""));

        var act = () => client.GetCredentialAsync(IssuerWallet, CredentialId);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }

    private static HttpResponseMessage Respond(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static WalletServiceClient BuildClient(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>((req, _) => Task.FromResult(respond(req)));

        var http = new HttpClient(handler.Object);
        var auth = new Mock<IServiceAuthClient>();
        auth.Setup(a => a.GetTokenAsync(It.IsAny<CancellationToken>())).ReturnsAsync("test-token");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceClients:WalletService:Address"] = "http://localhost:5001",
            })
            .Build();

        return new WalletServiceClient(http, auth.Object, config, Mock.Of<ILogger<WalletServiceClient>>());
    }
}
