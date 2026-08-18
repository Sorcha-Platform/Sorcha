// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;

using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;

using Sorcha.ServiceClients.Auth;
using Sorcha.ServiceClients.Wallet;

using Xunit;

namespace Sorcha.ServiceClients.Tests.Wallet;

/// <summary>
/// Wire-shape tests for <see cref="WalletServiceClient.IssueCredentialAsync"/>.
/// Ensures the Blueprint Service-supplied tenant id reaches the Wallet Service in
/// the request body so the HAIP issuer can fetch the org cert chain for the
/// <c>x5c</c> JWS header (Feature 096 US3 end-to-end wiring).
/// </summary>
public class WalletServiceClientIssueCredentialTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    [Fact]
    public async Task IssueCredentialAsync_WithTenantId_IncludesTenantIdInRequestBody()
    {
        // Arrange — capture the request body so we can assert tenantId is on the wire.
        string? capturedBody = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (req, ct) =>
            {
                capturedBody = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            credentialId = "urn:uuid:test",
                            type = "TestCredential",
                            issuerDid = "did:sorcha:w:issuer",
                            subjectDid = "did:sorcha:w:recipient",
                            claims = new Dictionary<string, object> { ["foo"] = "bar" },
                            issuedAt = DateTimeOffset.UtcNow,
                            rawToken = "eyJ.test.token",
                        }, JsonOptions),
                        System.Text.Encoding.UTF8,
                        "application/json"),
                };
            });

        var client = BuildClient(handler);

        // Act
        await client.IssueCredentialAsync(
            issuerWalletAddress: "ws1qissuer",
            credentialType: "TestCredential",
            claims: new Dictionary<string, object> { ["foo"] = "bar" },
            recipientWallet: "ws1qrecipient",
            tenantId: "11111111-1111-1111-1111-111111111111");

        // Assert
        capturedBody.Should().NotBeNullOrEmpty();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.TryGetProperty("tenantId", out var tenantEl).Should().BeTrue(
            "the Wallet endpoint relies on tenantId to fetch the issuer's org cert chain (096 US3)");
        tenantEl.GetString().Should().Be("11111111-1111-1111-1111-111111111111");
    }

    [Fact]
    public async Task IssueCredentialAsync_WithoutTenantId_OmitsTenantIdFromRequestBody()
    {
        // Backward-compat: pre-096-US3 callers (and internal callers without a JWT
        // org context) MUST keep working; the field should be omitted entirely so the
        // server-side IssueCredentialRequest deserialiser leaves TenantId null.
        //
        // This guarantee depends on WalletServiceClient using
        // JsonSerializerOptions { DefaultIgnoreCondition = WhenWritingNull } —
        // verified at the static field declaration. If that ever changes, this
        // assertion becomes the canary.
        string? capturedBody = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (req, ct) =>
            {
                capturedBody = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            credentialId = "urn:uuid:test",
                            type = "TestCredential",
                            issuerDid = "did:sorcha:w:issuer",
                            subjectDid = "did:sorcha:w:recipient",
                            claims = new Dictionary<string, object> { ["foo"] = "bar" },
                            issuedAt = DateTimeOffset.UtcNow,
                            rawToken = "eyJ.test.token",
                        }, JsonOptions),
                        System.Text.Encoding.UTF8,
                        "application/json"),
                };
            });

        var client = BuildClient(handler);

        await client.IssueCredentialAsync(
            issuerWalletAddress: "ws1qissuer",
            credentialType: "TestCredential",
            claims: new Dictionary<string, object> { ["foo"] = "bar" },
            recipientWallet: "ws1qrecipient");

        capturedBody.Should().NotBeNullOrEmpty();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.TryGetProperty("tenantId", out _).Should().BeFalse();
    }

    [Fact]
    public async Task IssueCredentialAsync_WithVctAndDisplayName_IncludesBothInRequestBody_AndMapsDisplayConfigOut()
    {
        // Credential VCT decoupling: the client must carry the canonical vct URI and the authored
        // display name on the wire, and surface the response's displayConfigJson back on the result.
        string? capturedBody = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (req, ct) =>
            {
                capturedBody = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            credentialId = "urn:uuid:test",
                            type = "https://credentials.sorcha.dev/assured-identity",
                            issuerDid = "did:sorcha:w:issuer",
                            subjectDid = "did:sorcha:w:recipient",
                            claims = new Dictionary<string, object> { ["foo"] = "bar" },
                            issuedAt = DateTimeOffset.UtcNow,
                            rawToken = "eyJ.test.token",
                            displayConfigJson = "{\"credentialName\":\"Assured Identity\"}",
                        }, JsonOptions),
                        System.Text.Encoding.UTF8,
                        "application/json"),
                };
            });

        var client = BuildClient(handler);

        var result = await client.IssueCredentialAsync(
            issuerWalletAddress: "ws1qissuer",
            credentialType: "AssuredIdentityCredential",
            claims: new Dictionary<string, object> { ["foo"] = "bar" },
            recipientWallet: "ws1qrecipient",
            vct: "https://credentials.sorcha.dev/assured-identity",
            displayName: "Assured Identity");

        capturedBody.Should().NotBeNullOrEmpty();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.TryGetProperty("vct", out var vctEl).Should().BeTrue(
            "the citizen entity's Type is stamped from the vct URI the client sends");
        vctEl.GetString().Should().Be("https://credentials.sorcha.dev/assured-identity");
        doc.RootElement.TryGetProperty("displayName", out var displayEl).Should().BeTrue(
            "the issuer records the authored credential name from the client-supplied displayName");
        displayEl.GetString().Should().Be("Assured Identity");

        result.DisplayConfigJson.Should().Be("{\"credentialName\":\"Assured Identity\"}",
            "the client must surface the response displayConfigJson so the envelope can carry it to the citizen");
    }

    [Fact]
    public async Task IssueCredentialAsync_WithoutVctOrDisplayName_OmitsBothFromRequestBody()
    {
        // Backward-compat: legacy callers that supply neither must not put null placeholders on the
        // wire (WalletServiceClient uses WhenWritingNull) so the server falls back to CredentialType.
        string? capturedBody = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (req, ct) =>
            {
                capturedBody = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            credentialId = "urn:uuid:test",
                            type = "TestCredential",
                            issuerDid = "did:sorcha:w:issuer",
                            subjectDid = "did:sorcha:w:recipient",
                            claims = new Dictionary<string, object> { ["foo"] = "bar" },
                            issuedAt = DateTimeOffset.UtcNow,
                            rawToken = "eyJ.test.token",
                        }, JsonOptions),
                        System.Text.Encoding.UTF8,
                        "application/json"),
                };
            });

        var client = BuildClient(handler);

        await client.IssueCredentialAsync(
            issuerWalletAddress: "ws1qissuer",
            credentialType: "TestCredential",
            claims: new Dictionary<string, object> { ["foo"] = "bar" },
            recipientWallet: "ws1qrecipient");

        capturedBody.Should().NotBeNullOrEmpty();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.TryGetProperty("vct", out _).Should().BeFalse();
        doc.RootElement.TryGetProperty("displayName", out _).Should().BeFalse();
    }

    [Fact]
    public async Task IssueCredentialAsync_WithRegisterId_PutsItOnTheWire()
    {
        // #1482: the issuer's credential row is what the credential-lifecycle endpoints read, and
        // without a RegisterId they skip the CredentialStatusChange ledger write — silently, at
        // Debug level. The result was that a revocation never reached the ledger on ANY deployment:
        // node-local, unauditable, and impossible to replicate to another node.
        string? capturedBody = null;
        var handler = new Mock<HttpMessageHandler>();
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns<HttpRequestMessage, CancellationToken>(async (req, ct) =>
            {
                capturedBody = req.Content is null ? null : await req.Content.ReadAsStringAsync(ct);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        JsonSerializer.Serialize(new
                        {
                            credentialId = "urn:uuid:test",
                            type = "TestCredential",
                            issuerDid = "did:sorcha:w:issuer",
                            subjectDid = "did:sorcha:w:recipient",
                            claims = new Dictionary<string, object> { ["foo"] = "bar" },
                            issuedAt = DateTimeOffset.UtcNow,
                            rawToken = "eyJ.test.token",
                        }, JsonOptions),
                        System.Text.Encoding.UTF8,
                        "application/json"),
                };
            });

        var client = BuildClient(handler);

        await client.IssueCredentialAsync(
            issuerWalletAddress: "ws1qissuer",
            credentialType: "TestCredential",
            claims: new Dictionary<string, object> { ["foo"] = "bar" },
            recipientWallet: "ws1qrecipient",
            registerId: "2141b08339d34c27824536ec250b025e");

        capturedBody.Should().NotBeNullOrEmpty();
        using var doc = JsonDocument.Parse(capturedBody!);
        doc.RootElement.TryGetProperty("registerId", out var regEl).Should().BeTrue(
            "the issuer's credential row needs the register so a later revocation can be written "
            + "to the ledger (#1482)");
        regEl.GetString().Should().Be("2141b08339d34c27824536ec250b025e");
    }

    private static WalletServiceClient BuildClient(Mock<HttpMessageHandler> handler)
    {
        var http = new HttpClient(handler.Object);
        var auth = new Mock<IServiceAuthClient>();
        auth.Setup(a => a.GetTokenAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync("test-token");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ServiceClients:WalletService:Address"] = "http://localhost:5001",
            })
            .Build();
        var logger = Mock.Of<ILogger<WalletServiceClient>>();
        return new WalletServiceClient(http, auth.Object, config, logger);
    }
}
