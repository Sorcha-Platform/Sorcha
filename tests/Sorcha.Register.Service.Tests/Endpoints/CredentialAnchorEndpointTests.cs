// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Register.Core.Storage;
using Sorcha.Register.Models;
using Sorcha.Register.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Register.Service.Tests.Endpoints;

/// <summary>
/// Integration tests for the public credential anchor read (Feature 155, T024/T025).
/// Covers GET /api/registers/{registerId}/credentials/{credentialId}/anchor — locating a
/// credential's issuance transaction and returning its F079 Merkle inclusion proof, plus
/// re-verifying that proof against the existing POST /inclusion-proofs/verify endpoint.
/// </summary>
[Collection("RegisterWebApp")]
public class CredentialAnchorEndpointTests : IClassFixture<RegisterServiceWebApplicationFactory>
{
    private readonly RegisterServiceWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _testRegisterId;

    public CredentialAnchorEndpointTests(RegisterServiceWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        var register = factory.CreateTestRegisterAsync("Anchor Test Register", "anchor-test-tenant").Result;
        _testRegisterId = register.Id;
    }

    [Fact]
    public async Task GetCredentialAnchor_WithSealedIssuanceTransaction_ShouldReturn200WithValidProof()
    {
        // Arrange — seed a sealed credential-issuance transaction
        var credentialId = "urn:uuid:" + Guid.NewGuid().ToString("N");
        var txId = await SeedSealedCredentialIssuanceTransactionAsync(credentialId);

        // Act
        var response = await _client.GetAsync(
            $"/api/registers/{_testRegisterId}/credentials/{Uri.EscapeDataString(credentialId)}/anchor");

        // Assert — 200 with the expected shape
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<JsonElement>();

        result.GetProperty("registerId").GetString().Should().Be(_testRegisterId);
        result.GetProperty("credentialId").GetString().Should().Be(credentialId);
        result.GetProperty("txId").GetString().Should().Be(txId);
        result.GetProperty("docketNumber").GetInt64().Should().BeGreaterThanOrEqualTo(0);
        result.TryGetProperty("sealedAt", out _).Should().BeTrue();
        result.GetProperty("status").GetString().Should().Be("Active");

        var proof = result.GetProperty("inclusionProof");
        var transactionHash = proof.GetProperty("transactionHash").GetString();
        var merkleRoot = proof.GetProperty("merkleRoot").GetString();
        var proofPath = proof.GetProperty("proofPath");

        transactionHash.Should().NotBeNullOrEmpty();
        merkleRoot.Should().NotBeNullOrEmpty();

        // Act — re-verify the returned proof against the public verify endpoint
        var verifyRequest = new
        {
            transactionHash,
            merkleRoot,
            proofPath = proofPath.EnumerateArray().Select(step => new
            {
                hash = step.GetProperty("hash").GetString(),
                position = step.GetProperty("position").GetInt32()
            }).ToArray()
        };

        var verifyResponse = await _client.PostAsJsonAsync(
            $"/api/registers/{_testRegisterId}/inclusion-proofs/verify", verifyRequest);

        // Assert — the proof verifies
        verifyResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var verifyResult = await verifyResponse.Content.ReadFromJsonAsync<JsonElement>();
        verifyResult.GetProperty("isValid").GetBoolean().Should().BeTrue();
        verifyResult.GetProperty("computedRoot").GetString().Should().Be(merkleRoot);
    }

    [Fact]
    public async Task GetCredentialAnchor_WithUnknownCredentialId_ShouldReturn404()
    {
        // Arrange — a credential id that was never issued
        var unknownCredentialId = "urn:uuid:" + Guid.NewGuid().ToString("N");

        // Act
        var response = await _client.GetAsync(
            $"/api/registers/{_testRegisterId}/credentials/{Uri.EscapeDataString(unknownCredentialId)}/anchor");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetCredentialAnchor_IsAnonymous_NoAuthHeaderRequired()
    {
        // Arrange — seed a sealed issuance transaction, then issue a request with a fresh
        // client that carries no special authorization (the test scheme auto-auths, but the
        // endpoint is mapped AllowAnonymous; this asserts it does not 401/403).
        var credentialId = "urn:uuid:" + Guid.NewGuid().ToString("N");
        await SeedSealedCredentialIssuanceTransactionAsync(credentialId);

        // Act
        var response = await _client.GetAsync(
            $"/api/registers/{_testRegisterId}/credentials/{Uri.EscapeDataString(credentialId)}/anchor");

        // Assert
        response.StatusCode.Should().NotBe(HttpStatusCode.Unauthorized);
        response.StatusCode.Should().NotBe(HttpStatusCode.Forbidden);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ===========================
    // Helper Methods
    // ===========================

    /// <summary>
    /// Seeds a credential-issuance transaction (with the Feature-155 tracking metadata) and
    /// seals it in a docket, returning the transaction id.
    /// </summary>
    private async Task<string> SeedSealedCredentialIssuanceTransactionAsync(string credentialId)
    {
        using var scope = _factory.Services.CreateScope();
        var repository = scope.ServiceProvider.GetRequiredService<IRegisterRepository>();

        var txId = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes(credentialId + Guid.NewGuid())))
            .ToLowerInvariant();

        // A sibling transaction so the docket's Merkle tree has >1 leaf — this yields a
        // non-empty proof path, which the public POST /inclusion-proofs/verify endpoint requires.
        var siblingTxId = Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(
                System.Text.Encoding.UTF8.GetBytes("sibling-" + credentialId + Guid.NewGuid())))
            .ToLowerInvariant();

        // Create a docket first so we can stamp DocketNumber on the transactions.
        var docket = new Docket
        {
            RegisterId = _testRegisterId,
            TransactionIds = new List<string> { txId, siblingTxId },
            Hash = ComputeSha256Hex(txId),
            PreviousHash = string.Empty,
            TimeStamp = DateTime.UtcNow
        };
        var insertedDocket = await repository.InsertDocketAsync(docket);

        var siblingTransaction = new TransactionModel
        {
            RegisterId = _testRegisterId,
            TxId = siblingTxId,
            PrevTxId = string.Empty,
            Version = 1,
            SenderWallet = "filler_wallet",
            RecipientsWallets = new[] { "filler_recipient" },
            TimeStamp = DateTime.UtcNow,
            PayloadCount = 1,
            Payloads = new[]
            {
                new PayloadModel
                {
                    WalletAccess = new[] { "filler_wallet" },
                    PayloadSize = 64,
                    Hash = ComputeSha256Hex("sibling-payload-" + credentialId),
                    Data = "sibling_data"
                }
            },
            Signature = "filler_signature",
            DocketNumber = insertedDocket.Id
        };
        await repository.InsertTransactionAsync(siblingTransaction);

        var transaction = new TransactionModel
        {
            RegisterId = _testRegisterId,
            TxId = txId,
            PrevTxId = string.Empty,
            Version = 1,
            SenderWallet = "issuer_wallet",
            RecipientsWallets = new[] { "holder_wallet" },
            TimeStamp = DateTime.UtcNow,
            PayloadCount = 1,
            Payloads = new[]
            {
                new PayloadModel
                {
                    WalletAccess = new[] { "issuer_wallet" },
                    PayloadSize = 256,
                    Hash = ComputeSha256Hex("issuance-payload-" + credentialId),
                    Data = "issuance_data"
                }
            },
            Signature = "issuer_signature",
            DocketNumber = insertedDocket.Id,
            MetaData = new TransactionMetaData
            {
                RegisterId = _testRegisterId,
                TrackingData = new Dictionary<string, string>
                {
                    ["type"] = "credential-issuance",
                    ["credentialId"] = credentialId,
                    ["credentialType"] = "AssuredIdentityCredential"
                }
            }
        };

        await repository.InsertTransactionAsync(transaction);
        return txId;
    }

    private static string ComputeSha256Hex(string input)
    {
        var hash = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
