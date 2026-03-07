// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Register.Service.Services;
using Sorcha.Register.Service.Tests.Helpers;
using Sorcha.ServiceClients.Register.Models;
using Xunit;

namespace Sorcha.Register.Service.Tests;

/// <summary>
/// Integration tests for the batch public key resolution endpoint (T011).
/// Tests the POST /api/registers/{registerId}/participants/resolve-public-keys endpoint.
/// </summary>
public class BatchPublicKeyResolutionTests : IClassFixture<RegisterServiceWebApplicationFactory>
{
    private readonly RegisterServiceWebApplicationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _registerId = "test-register-batch";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public BatchPublicKeyResolutionTests(RegisterServiceWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();

        // Seed the ParticipantIndexService with test data
        SeedParticipants();
    }

    private void SeedParticipants()
    {
        var index = _factory.Services.GetRequiredService<ParticipantIndexService>();

        // Active participant with ED25519 primary + P-256 secondary
        var alicePayload = CreatePayloadElement("alice-001", "Alice", "Acme Corp", "Active", 1,
            ("addr-alice-ed", "key-alice-ed", "ED25519", true),
            ("addr-alice-p256", "key-alice-p256", "P-256", false));
        index.IndexParticipant(_registerId, "tx-alice-1", alicePayload, DateTimeOffset.UtcNow);

        // Active participant with single address
        var bobPayload = CreatePayloadElement("bob-002", "Bob", "Acme Corp", "Active", 1,
            ("addr-bob-ed", "key-bob-ed", "ED25519", true));
        index.IndexParticipant(_registerId, "tx-bob-1", bobPayload, DateTimeOffset.UtcNow);

        // Revoked participant
        var charliePayload = CreatePayloadElement("charlie-003", "Charlie", "Acme Corp", "Revoked", 2,
            ("addr-charlie", "key-charlie", "ED25519", true));
        index.IndexParticipant(_registerId, "tx-charlie-1", charliePayload, DateTimeOffset.UtcNow);

        // Active participant with only P-256 key
        var dianaPayload = CreatePayloadElement("diana-004", "Diana", "Acme Corp", "Active", 1,
            ("addr-diana-p256", "key-diana-p256", "P-256", true));
        index.IndexParticipant(_registerId, "tx-diana-1", dianaPayload, DateTimeOffset.UtcNow);
    }

    #region Success Cases

    [Fact]
    public async Task ResolvePublicKeysBatch_FoundAddresses_ReturnsInResolvedDict()
    {
        // Arrange
        var request = new BatchPublicKeyRequest
        {
            WalletAddresses = ["addr-alice-ed", "addr-bob-ed"]
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_registerId}/participants/resolve-public-keys",
            request,
            JsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<BatchPublicKeyResponse>(JsonOptions);
        result.Should().NotBeNull();
        result!.Resolved.Should().HaveCount(2);
        result.Resolved.Should().ContainKey("addr-alice-ed");
        result.Resolved.Should().ContainKey("addr-bob-ed");

        result.Resolved["addr-alice-ed"].ParticipantId.Should().Be("alice-001");
        result.Resolved["addr-alice-ed"].PublicKey.Should().Be("key-alice-ed");
        result.Resolved["addr-alice-ed"].Algorithm.Should().Be("ED25519");
        result.Resolved["addr-alice-ed"].Status.Should().Be("Active");

        result.Resolved["addr-bob-ed"].ParticipantId.Should().Be("bob-002");
        result.Resolved["addr-bob-ed"].PublicKey.Should().Be("key-bob-ed");

        result.NotFound.Should().BeEmpty();
        result.Revoked.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolvePublicKeysBatch_NotFoundAddresses_ReturnsInNotFoundArray()
    {
        // Arrange
        var request = new BatchPublicKeyRequest
        {
            WalletAddresses = ["nonexistent-addr-1", "nonexistent-addr-2"]
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_registerId}/participants/resolve-public-keys",
            request,
            JsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<BatchPublicKeyResponse>(JsonOptions);
        result.Should().NotBeNull();
        result!.Resolved.Should().BeEmpty();
        result.NotFound.Should().HaveCount(2);
        result.NotFound.Should().Contain("nonexistent-addr-1");
        result.NotFound.Should().Contain("nonexistent-addr-2");
        result.Revoked.Should().BeEmpty();
    }

    [Fact]
    public async Task ResolvePublicKeysBatch_RevokedAddresses_ReturnsInRevokedArray()
    {
        // Arrange
        var request = new BatchPublicKeyRequest
        {
            WalletAddresses = ["addr-charlie"]
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_registerId}/participants/resolve-public-keys",
            request,
            JsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<BatchPublicKeyResponse>(JsonOptions);
        result.Should().NotBeNull();
        result!.Resolved.Should().BeEmpty();
        result.NotFound.Should().BeEmpty();
        result.Revoked.Should().HaveCount(1);
        result.Revoked.Should().Contain("addr-charlie");
    }

    [Fact]
    public async Task ResolvePublicKeysBatch_MixedResults_CategorisesCorrectly()
    {
        // Arrange
        var request = new BatchPublicKeyRequest
        {
            WalletAddresses = ["addr-alice-ed", "nonexistent-addr", "addr-charlie", "addr-bob-ed"]
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_registerId}/participants/resolve-public-keys",
            request,
            JsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<BatchPublicKeyResponse>(JsonOptions);
        result.Should().NotBeNull();

        result!.Resolved.Should().HaveCount(2);
        result.Resolved.Should().ContainKey("addr-alice-ed");
        result.Resolved.Should().ContainKey("addr-bob-ed");

        result.NotFound.Should().HaveCount(1);
        result.NotFound.Should().Contain("nonexistent-addr");

        result.Revoked.Should().HaveCount(1);
        result.Revoked.Should().Contain("addr-charlie");
    }

    #endregion

    #region Validation Cases

    [Fact]
    public async Task ResolvePublicKeysBatch_EmptyAddresses_Returns400BadRequest()
    {
        // Arrange
        var request = new { walletAddresses = Array.Empty<string>() };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_registerId}/participants/resolve-public-keys",
            request,
            JsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task ResolvePublicKeysBatch_MoreThan200Addresses_Returns400BadRequest()
    {
        // Arrange
        var addresses = Enumerable.Range(1, 201).Select(i => $"addr-{i}").ToArray();
        var request = new BatchPublicKeyRequest
        {
            WalletAddresses = addresses
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_registerId}/participants/resolve-public-keys",
            request,
            JsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region Deduplication

    [Fact]
    public async Task ResolvePublicKeysBatch_DuplicateAddresses_DeduplicatedAndResolvedOnce()
    {
        // Arrange
        var request = new BatchPublicKeyRequest
        {
            WalletAddresses = ["addr-alice-ed", "addr-alice-ed", "addr-alice-ed"]
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_registerId}/participants/resolve-public-keys",
            request,
            JsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<BatchPublicKeyResponse>(JsonOptions);
        result.Should().NotBeNull();
        result!.Resolved.Should().HaveCount(1);
        result.Resolved.Should().ContainKey("addr-alice-ed");
        result.NotFound.Should().BeEmpty();
        result.Revoked.Should().BeEmpty();
    }

    #endregion

    #region Algorithm Filter

    [Fact]
    public async Task ResolvePublicKeysBatch_WithAlgorithmFilter_ReturnsOnlyMatchingAlgorithm()
    {
        // Arrange — Alice has both ED25519 (primary) and P-256 addresses
        // Request addr-alice-p256 (a P-256 address) but filter for P-256 algorithm
        var request = new BatchPublicKeyRequest
        {
            WalletAddresses = ["addr-alice-p256"],
            Algorithm = "P-256"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_registerId}/participants/resolve-public-keys",
            request,
            JsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<BatchPublicKeyResponse>(JsonOptions);
        result.Should().NotBeNull();
        result!.Resolved.Should().HaveCount(1);
        result.Resolved["addr-alice-p256"].Algorithm.Should().Be("P-256");
        result.Resolved["addr-alice-p256"].PublicKey.Should().Be("key-alice-p256");
    }

    [Fact]
    public async Task ResolvePublicKeysBatch_AlgorithmFilterNoMatch_ReturnsInNotFound()
    {
        // Arrange — Diana only has P-256 key; request with ED25519 filter
        var request = new BatchPublicKeyRequest
        {
            WalletAddresses = ["addr-diana-p256"],
            Algorithm = "ED25519"
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_registerId}/participants/resolve-public-keys",
            request,
            JsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<BatchPublicKeyResponse>(JsonOptions);
        result.Should().NotBeNull();
        result!.Resolved.Should().BeEmpty();
        result.NotFound.Should().Contain("addr-diana-p256");
    }

    #endregion

    #region Edge Cases

    [Fact]
    public async Task ResolvePublicKeysBatch_SingleAddress_Works()
    {
        // Arrange
        var request = new BatchPublicKeyRequest
        {
            WalletAddresses = ["addr-bob-ed"]
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            $"/api/registers/{_registerId}/participants/resolve-public-keys",
            request,
            JsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<BatchPublicKeyResponse>(JsonOptions);
        result.Should().NotBeNull();
        result!.Resolved.Should().HaveCount(1);
        result.Resolved["addr-bob-ed"].ParticipantName.Should().Be("Bob");
    }

    [Fact]
    public async Task ResolvePublicKeysBatch_NonExistentRegister_ReturnsAllNotFound()
    {
        // Arrange
        var request = new BatchPublicKeyRequest
        {
            WalletAddresses = ["addr-alice-ed"]
        };

        // Act
        var response = await _client.PostAsJsonAsync(
            "/api/registers/nonexistent-register/participants/resolve-public-keys",
            request,
            JsonOptions);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<BatchPublicKeyResponse>(JsonOptions);
        result.Should().NotBeNull();
        result!.Resolved.Should().BeEmpty();
        result.NotFound.Should().Contain("addr-alice-ed");
    }

    #endregion

    #region Helper Methods

    private static JsonElement CreatePayloadElement(
        string participantId,
        string participantName,
        string organizationName,
        string status,
        int version,
        params (string addr, string key, string algo, bool primary)[] addresses)
    {
        var addrArray = addresses.Select(a => new
        {
            walletAddress = a.addr,
            publicKey = a.key,
            algorithm = a.algo,
            primary = a.primary
        });

        var payload = new
        {
            participantId,
            participantName,
            organizationName,
            status,
            version,
            addresses = addrArray
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    #endregion
}
