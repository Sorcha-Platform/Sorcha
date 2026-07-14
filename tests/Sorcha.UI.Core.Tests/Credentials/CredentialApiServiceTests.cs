// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Moq.Protected;
using Sorcha.UI.Core.Services.Credentials;
using Xunit;

namespace Sorcha.UI.Core.Tests.Credentials;

public class CredentialApiServiceTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    [Fact]
    public async Task GetCredentialsAsync_ReturnsViewModels()
    {
        var responseData = new[]
        {
            new { id = "cred-1", type = "LicenseCredential", issuerDid = "did:web:example.com",
                  subjectDid = "did:sorcha:w:abc", issuedAt = DateTimeOffset.UtcNow,
                  expiresAt = (DateTimeOffset?)null, status = "Active" },
            new { id = "cred-2", type = "IdentityAttestation", issuerDid = "did:sorcha:w:xyz123456789",
                  subjectDid = "did:sorcha:w:abc", issuedAt = DateTimeOffset.UtcNow.AddDays(-10),
                  expiresAt = (DateTimeOffset?)DateTimeOffset.UtcNow.AddDays(100), status = "Expired" }
        };

        var service = CreateServiceWithResponse(HttpStatusCode.OK,
            JsonSerializer.Serialize(responseData, JsonOptions));

        var result = await service.GetCredentialsAsync("wallet-1");

        result.Should().HaveCount(2);
        result[0].CredentialId.Should().Be("cred-1");
        result[0].Type.Should().Be("LicenseCredential");
        result[0].IssuerName.Should().Be("example.com"); // did:web: extraction
        result[0].AvailableActions.Should().Contain("Present");

        result[1].CredentialId.Should().Be("cred-2");
        result[1].Status.Should().Be("Expired");
        result[1].AvailableActions.Should().NotContain("Present");
    }

    [Fact]
    public async Task GetCredentialsAsync_ServiceError_ReturnsEmptyList()
    {
        var service = CreateServiceWithResponse(HttpStatusCode.InternalServerError, "");

        var result = await service.GetCredentialsAsync("wallet-1");

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetCredentialDetailAsync_ReturnsDetailViewModel()
    {
        var entity = new
        {
            id = "cred-1",
            type = "LicenseCredential",
            issuerDid = "did:web:example.com",
            subjectDid = "did:sorcha:w:abc",
            issuedAt = DateTimeOffset.UtcNow,
            expiresAt = (DateTimeOffset?)DateTimeOffset.UtcNow.AddDays(365),
            status = "Active",
            claimsJson = JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["license_type"] = "ClassA",
                ["holder_name"] = "Alice"
            }),
            usagePolicy = "LimitedUse",
            maxPresentations = 3,
            presentationCount = 1,
            displayConfigJson = JsonSerializer.Serialize(new
            {
                backgroundColor = "#FF5722",
                textColor = "#000000",
                icon = "Shield",
                cardLayout = "Compact"
            }),
            statusListUrl = "https://example.com/status/1",
            issuanceBlueprintId = "bp-1"
        };

        var service = CreateServiceWithResponse(HttpStatusCode.OK,
            JsonSerializer.Serialize(entity, JsonOptions));

        var result = await service.GetCredentialDetailAsync("wallet-1", "cred-1");

        result.Should().NotBeNull();
        result!.CredentialId.Should().Be("cred-1");
        result.Claims.Should().ContainKey("license_type");
        result.UsagePolicy.Should().Be("LimitedUse");
        result.MaxPresentations.Should().Be(3);
        result.PresentationCount.Should().Be(1);
        result.DisplayConfig.BackgroundColor.Should().Be("#FF5722");
        result.StatusListUrl.Should().Be("https://example.com/status/1");
    }

    [Fact]
    public async Task GetCredentialDetailAsync_NotFound_ReturnsNull()
    {
        var service = CreateServiceWithResponse(HttpStatusCode.NotFound, "");

        var result = await service.GetCredentialDetailAsync("wallet-1", "cred-missing");

        result.Should().BeNull();
    }

    [Fact]
    public async Task DeleteCredentialAsync_Success_ReturnsTrue()
    {
        var service = CreateServiceWithResponse(HttpStatusCode.NoContent, "");

        var result = await service.DeleteCredentialAsync("wallet-1", "cred-1");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task DeleteCredentialAsync_NotFound_ReturnsFalse()
    {
        var service = CreateServiceWithResponse(HttpStatusCode.NotFound, "");

        var result = await service.DeleteCredentialAsync("wallet-1", "cred-missing");

        result.Should().BeFalse();
    }

    [Fact]
    public async Task UpdateCredentialStatusAsync_Success_ReturnsTrue()
    {
        var service = CreateServiceWithResponse(HttpStatusCode.OK, "");

        var result = await service.UpdateCredentialStatusAsync("wallet-1", "cred-1", "Revoked");

        result.Should().BeTrue();
    }

    [Fact]
    public async Task GetCredentialsAsync_IssuerNameExtraction_SorchaWallet()
    {
        var responseData = new[]
        {
            new { id = "cred-1", type = "Test", issuerDid = "did:sorcha:w:abcdef1234567890",
                  subjectDid = "did:sorcha:w:abc", issuedAt = DateTimeOffset.UtcNow,
                  expiresAt = (DateTimeOffset?)null, status = "Active" }
        };

        var service = CreateServiceWithResponse(HttpStatusCode.OK,
            JsonSerializer.Serialize(responseData, JsonOptions));

        var result = await service.GetCredentialsAsync("wallet-1");

        result[0].IssuerName.Should().Be("abcdef12...");
    }

    [Fact]
    public async Task GetCredentialDetailAsync_NullDisplayConfig_UsesDefaults()
    {
        var entity = new
        {
            id = "cred-1",
            type = "Test",
            issuerDid = "did:web:example.com",
            subjectDid = "did:sorcha:w:abc",
            issuedAt = DateTimeOffset.UtcNow,
            expiresAt = (DateTimeOffset?)null,
            status = "Active",
            claimsJson = "{}",
            usagePolicy = "Reusable",
            maxPresentations = (int?)null,
            presentationCount = 0,
            displayConfigJson = (string?)null,
            statusListUrl = (string?)null,
            issuanceBlueprintId = (string?)null
        };

        var service = CreateServiceWithResponse(HttpStatusCode.OK,
            JsonSerializer.Serialize(entity, JsonOptions));

        var result = await service.GetCredentialDetailAsync("wallet-1", "cred-1");

        result.Should().NotBeNull();
        result!.DisplayConfig.BackgroundColor.Should().Be("#1976D2"); // default
    }

    private static CredentialApiService CreateServiceWithResponse(HttpStatusCode statusCode, string content)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = statusCode,
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://test.example.com")
        };

        return new CredentialApiService(httpClient, NullLogger<CredentialApiService>.Instance);
    }

    private static CredentialApiService CreateServiceReturning(string json) =>
        CreateServiceWithResponse(HttpStatusCode.OK, json);

    [Fact]
    public async Task GetCredentialsAsync_PopulatesDisclosableClaimsFromResponse()
    {
        // The card's padlock is only meaningful if this survives the wire.
        var json = """
        [{
          "id": "urn:credential:1", "type": "AssuredIdentityCredential",
          "issuerDid": "did:sorcha:org:ws11q", "subjectDid": "did:sorcha:w:ws11q",
          "status": "Active", "issuedAt": "2026-07-01T00:00:00Z",
          "claimsJson": "{\"email\":\"a@b.c\",\"address\":{\"town\":\"Edinburgh\"}}",
          "disclosableClaims": ["address"]
        }]
        """;
        var service = CreateServiceReturning(json);

        var result = await service.GetCredentialsAsync("ws11q");

        result.Should().ContainSingle();
        result[0].DisclosableClaims.Should().BeEquivalentTo(["address"]);
    }

    [Fact]
    public async Task GetCredentialsAsync_ObjectClaim_NeverRendersAsRawJson()
    {
        // The n1 defect, guarded at the rendering layer: even if the server
        // regressed and sent a digest array, no card may print raw JSON.
        var json = """
        [{
          "id": "urn:credential:1", "type": "AssuredIdentityCredential",
          "issuerDid": "did:sorcha:org:ws11q", "subjectDid": "did:sorcha:w:ws11q",
          "status": "Active", "issuedAt": "2026-07-01T00:00:00Z",
          "claimsJson": "{\"address\":{\"_sd\":[\"zSH_kfTeW2Mlc\"]}}",
          "disclosableClaims": []
        }]
        """;
        var service = CreateServiceReturning(json);

        var result = await service.GetCredentialsAsync("ws11q");

        foreach (var value in result[0].HighlightClaims.Values)
        {
            value.Should().NotContain("_sd");
            value.Should().NotStartWith("{");
        }
    }

    [Fact]
    public async Task GetCredentialsAsync_BuildsClaimSummaryOfNamesNotValues()
    {
        var json = """
        [{
          "id": "urn:credential:1", "type": "AssuredIdentityCredential",
          "issuerDid": "did:sorcha:org:ws11q", "subjectDid": "did:sorcha:w:ws11q",
          "status": "Active", "issuedAt": "2026-07-01T00:00:00Z",
          "claimsJson": "{\"email\":\"stuart@stuartfraser.net\",\"dateOfBirth\":\"1980-01-01\"}",
          "disclosableClaims": ["email"]
        }]
        """;
        var service = CreateServiceReturning(json);

        var result = await service.GetCredentialsAsync("ws11q");

        result[0].ClaimSummary.Should().Contain("Email");
        result[0].ClaimSummary.Should().Contain("Date of birth");
        result[0].ClaimSummary.Should().NotContain("stuart@stuartfraser.net", "a list must never print claim values");
        result[0].ClaimSummary.Should().NotContain("1980");
    }

    [Fact]
    public async Task GetCredentialsAsync_HumanisesTheCredentialType()
    {
        var json = """
        [{
          "id": "urn:credential:1", "type": "AssuredIdentityCredential",
          "issuerDid": "did:sorcha:org:ws11q", "subjectDid": "did:sorcha:w:ws11q",
          "status": "Active", "issuedAt": "2026-07-01T00:00:00Z",
          "claimsJson": "{}", "disclosableClaims": []
        }]
        """;
        var service = CreateServiceReturning(json);

        var result = await service.GetCredentialsAsync("ws11q");

        result[0].DisplayName.Should().Be("Assured Identity");
    }
}
