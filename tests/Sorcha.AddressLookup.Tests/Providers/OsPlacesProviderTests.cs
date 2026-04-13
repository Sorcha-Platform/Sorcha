// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.AddressLookup.Providers;

namespace Sorcha.AddressLookup.Tests.Providers;

/// <summary>
/// Tests for <see cref="OsPlacesProvider"/>. Validates construction requires
/// an API key, the full-address response shape is parsed correctly, rate-limit
/// and other failure modes degrade gracefully, and the query carries the
/// supplied API key.
/// </summary>
public class OsPlacesProviderTests
{
    private const string ValidResponse = """
        {
          "header": { "totalresults": 1 },
          "results": [
            {
              "DPA": {
                "ADDRESS": "1 ROYAL MILE, EDINBURGH, EH1 1YZ",
                "BUILDING_NUMBER": "1",
                "THOROUGHFARE_NAME": "ROYAL MILE",
                "POST_TOWN": "EDINBURGH",
                "POSTCODE": "EH1 1YZ"
              }
            }
          ]
        }
        """;

    private const string TestApiKey = "test-api-key-abc123";

    private static OsPlacesProvider NewSut(FakeHttpMessageHandler handler, string? apiKey = TestApiKey)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.os.uk/search/places/v1/") };
        var options = Options.Create(new OsPlacesOptions { ApiKey = apiKey });
        return new OsPlacesProvider(http, options, Mock.Of<ILogger<OsPlacesProvider>>());
    }

    [Fact]
    public void Constructor_NullApiKey_Throws()
    {
        var http = new HttpClient(FakeHttpMessageHandler.Status(HttpStatusCode.OK));
        var options = Options.Create(new OsPlacesOptions { ApiKey = null });
        var logger = Mock.Of<ILogger<OsPlacesProvider>>();

        var act = () => new OsPlacesProvider(http, options, logger);

        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*requires an API key*");
    }

    [Fact]
    public void Constructor_EmptyApiKey_Throws()
    {
        var http = new HttpClient(FakeHttpMessageHandler.Status(HttpStatusCode.OK));
        var options = Options.Create(new OsPlacesOptions { ApiKey = "   " });
        var logger = Mock.Of<ILogger<OsPlacesProvider>>();

        var act = () => new OsPlacesProvider(http, options, logger);

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public async Task LookupAsync_ValidPostcode_ReturnsFullAddressCandidates()
    {
        var handler = FakeHttpMessageHandler.Json(HttpStatusCode.OK, ValidResponse);
        var sut = NewSut(handler);

        var result = await sut.LookupAsync("EH1 1YZ");

        result.IsValid.Should().BeTrue();
        result.Provider.Should().Be("os-places");
        result.Capability.Should().Be(AddressLookupCapability.FullAddress);
        result.Candidates.Should().NotBeNull();
        result.Candidates!.Should().HaveCount(1);

        var candidate = result.Candidates![0];
        candidate.Line1.Should().Be("1 ROYAL MILE");
        candidate.Town.Should().Be("EDINBURGH");
        candidate.Postcode.Should().Be("EH1 1YZ");
        candidate.Country.Should().Be("GB");
        candidate.DisplayLabel.Should().Contain("ROYAL MILE").And.Contain("EDINBURGH");
    }

    [Fact]
    public async Task LookupAsync_PropagatesApiKeyInQueryString()
    {
        var handler = FakeHttpMessageHandler.Json(HttpStatusCode.OK, ValidResponse);
        var sut = NewSut(handler);

        await sut.LookupAsync("EH1 1YZ");

        handler.LastRequest.Should().NotBeNull();
        var url = handler.LastRequest!.RequestUri!.ToString();
        url.Should().Contain($"key={TestApiKey}");
        url.Should().Contain("postcode=");
    }

    [Fact]
    public async Task LookupAsync_RateLimited_DegradesGracefully()
    {
        var sut = NewSut(FakeHttpMessageHandler.Status(HttpStatusCode.TooManyRequests));

        var result = await sut.LookupAsync("EH1 1YZ");

        result.IsValid.Should().BeFalse();
        result.Provider.Should().Be("os-places");
        result.Candidates.Should().BeNull();
    }

    [Fact]
    public async Task LookupAsync_NotFound_DegradesGracefully()
    {
        var sut = NewSut(FakeHttpMessageHandler.Status(HttpStatusCode.NotFound));

        var result = await sut.LookupAsync("ZZ99 9ZZ");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task LookupAsync_MalformedJson_DegradesGracefully()
    {
        var sut = NewSut(FakeHttpMessageHandler.Json(HttpStatusCode.OK, "{ this is not valid json }"));

        var result = await sut.LookupAsync("EH1 1YZ");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task LookupAsync_EmptyResultsArray_ReturnsInvalidResult()
    {
        var emptyResponse = """{ "header": { "totalresults": 0 }, "results": [] }""";
        var sut = NewSut(FakeHttpMessageHandler.Json(HttpStatusCode.OK, emptyResponse));

        var result = await sut.LookupAsync("EH1 1YZ");

        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task LookupAsync_NonGbCountryHint_DegradesWithoutCallingUpstream()
    {
        var handler = FakeHttpMessageHandler.Json(HttpStatusCode.OK, ValidResponse);
        var sut = NewSut(handler);

        var result = await sut.LookupAsync("75001", "FR");

        result.IsValid.Should().BeFalse();
        handler.CallCount.Should().Be(0);
    }
}
