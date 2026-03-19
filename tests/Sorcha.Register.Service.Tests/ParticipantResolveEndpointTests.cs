// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Sorcha.Register.Service.Services;
using Sorcha.Register.Service.Tests.Helpers;
using Xunit;

namespace Sorcha.Register.Service.Tests;

/// <summary>
/// Tests for the GET /api/registers/{registerId}/participants/resolve endpoint.
/// Verifies participant resolution by blueprint role ID and organisation name.
/// </summary>
public class ParticipantResolveEndpointTests : IClassFixture<RegisterServiceWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly string _registerId = "test-register-resolve";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ParticipantResolveEndpointTests(RegisterServiceWebApplicationFactory factory)
    {
        _client = factory.CreateClient();

        var index = factory.Services.GetRequiredService<ParticipantIndexService>();
        SeedParticipants(index);
    }

    private void SeedParticipants(ParticipantIndexService index)
    {
        // Active organisational participant: ID Department
        var idDeptPayload = CreatePayloadElement("id-dept", "Identity Department", "AshwickCouncil", "Active", 1,
            ("ws11q-id-dept-1", "key-id-dept-1", "ED25519", true),
            ("ws11q-id-dept-2", "key-id-dept-2", "ED25519", false));
        index.IndexParticipant(_registerId, "tx-id-dept-1", idDeptPayload, DateTimeOffset.UtcNow);

        // Active organisational participant: Service Department
        var svcDeptPayload = CreatePayloadElement("svc-dept", "Service Department", "AshwickCouncil", "Active", 1,
            ("ws11q-svc-dept-1", "key-svc-dept-1", "ED25519", true));
        index.IndexParticipant(_registerId, "tx-svc-dept-1", svcDeptPayload, DateTimeOffset.UtcNow);

        // Revoked participant
        var revokedPayload = CreatePayloadElement("revoked-dept", "Revoked Department", "AshwickCouncil", "Revoked", 2,
            ("ws11q-revoked-1", "key-revoked-1", "ED25519", true));
        index.IndexParticipant(_registerId, "tx-revoked-1", revokedPayload, DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Resolve_ExistingParticipant_ReturnsRecord()
    {
        var response = await _client.GetAsync(
            $"/api/registers/{_registerId}/participants/resolve?participantId=id-dept&orgName=AshwickCouncil");

        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, because: $"Response body: {body}");

        var json = JsonSerializer.Deserialize<JsonElement>(body, JsonOptions);
        json.GetProperty("participantId").GetString().Should().Be("id-dept");
        json.GetProperty("participantName").GetString().Should().Be("Identity Department");
        json.GetProperty("organisationName").GetString().Should().Be("AshwickCouncil");
        json.GetProperty("status").GetString().Should().Be("Active");
        json.GetProperty("addresses").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Resolve_ByIdOnly_ReturnsRecord()
    {
        var response = await _client.GetAsync(
            $"/api/registers/{_registerId}/participants/resolve?participantId=svc-dept");

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>(JsonOptions);
        json.GetProperty("participantId").GetString().Should().Be("svc-dept");
    }

    [Fact]
    public async Task Resolve_NotFound_Returns404()
    {
        var response = await _client.GetAsync(
            $"/api/registers/{_registerId}/participants/resolve?participantId=nonexistent");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Resolve_RevokedParticipant_Returns410()
    {
        var response = await _client.GetAsync(
            $"/api/registers/{_registerId}/participants/resolve?participantId=revoked-dept");

        response.StatusCode.Should().Be(HttpStatusCode.Gone);
    }

    [Fact]
    public async Task Resolve_WrongOrganisation_Returns404()
    {
        var response = await _client.GetAsync(
            $"/api/registers/{_registerId}/participants/resolve?participantId=id-dept&orgName=WrongOrg");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

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

    private static async Task<JsonElement> ReadFromJsonAsync<T>(HttpContent content, JsonSerializerOptions options)
    {
        var stream = await content.ReadAsStreamAsync();
        return await JsonSerializer.DeserializeAsync<JsonElement>(stream, options);
    }
}
