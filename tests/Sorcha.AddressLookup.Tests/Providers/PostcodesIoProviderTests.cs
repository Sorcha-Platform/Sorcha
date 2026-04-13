// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using Microsoft.Extensions.Logging;
using Sorcha.AddressLookup.Providers;

namespace Sorcha.AddressLookup.Tests.Providers;

/// <summary>
/// Tests for <see cref="PostcodesIoProvider"/>. Validates the
/// happy-path 2xx round trip, the 404 / 5xx graceful-degradation paths,
/// postcode normalisation, and country-hint rejection.
/// </summary>
public class PostcodesIoProviderTests
{
    private const string ValidResponse = """
        {
          "status": 200,
          "result": {
            "postcode": "EH1 1YZ",
            "post_town": "EDINBURGH",
            "admin_district": "City of Edinburgh",
            "region": "Scotland",
            "country": "Scotland",
            "latitude": 55.951,
            "longitude": -3.189
          }
        }
        """;

    private static PostcodesIoProvider NewSut(FakeHttpMessageHandler handler)
    {
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://api.postcodes.io/") };
        return new PostcodesIoProvider(http, Mock.Of<ILogger<PostcodesIoProvider>>());
    }

    [Fact]
    public async Task LookupAsync_ValidPostcode_ReturnsValidateOnlyResult()
    {
        var sut = NewSut(FakeHttpMessageHandler.Json(HttpStatusCode.OK, ValidResponse));

        var result = await sut.LookupAsync("EH11YZ");

        result.IsValid.Should().BeTrue();
        result.Provider.Should().Be("postcodes.io");
        result.Capability.Should().Be(AddressLookupCapability.ValidateOnly);
        result.Postcode.Should().Be("EH1 1YZ", "postcode normalised to uppercase + single space before inward code");
        result.Metadata.Should().NotBeNull();
        result.Metadata!.Town.Should().Be("EDINBURGH");
        result.Metadata.Region.Should().Be("Scotland");
        result.Metadata.Country.Should().Be("GB");
        result.Metadata.Latitude.Should().BeApproximately(55.951, 0.001);
        result.Candidates.Should().BeNull();
    }

    [Fact]
    public async Task LookupAsync_NotFoundStatus_ReturnsInvalidResult()
    {
        var sut = NewSut(FakeHttpMessageHandler.Status(HttpStatusCode.NotFound));

        var result = await sut.LookupAsync("ZZ999ZZ");

        result.IsValid.Should().BeFalse();
        result.Provider.Should().Be("postcodes.io");
        result.Metadata.Should().BeNull();
    }

    [Fact]
    public async Task LookupAsync_InternalServerError_DegradesGracefully()
    {
        var sut = NewSut(FakeHttpMessageHandler.Status(HttpStatusCode.InternalServerError));

        var result = await sut.LookupAsync("EH1 1YZ");

        result.IsValid.Should().BeFalse();
        result.Provider.Should().Be("postcodes.io");
    }

    [Fact]
    public async Task LookupAsync_NetworkException_DegradesGracefully()
    {
        var sut = NewSut(FakeHttpMessageHandler.Throws(new HttpRequestException("DNS failure")));

        var result = await sut.LookupAsync("EH1 1YZ");

        result.IsValid.Should().BeFalse();
        result.Provider.Should().Be("postcodes.io");
    }

    [Fact]
    public async Task LookupAsync_NonGbCountryHint_DegradesGracefullyWithoutCallingUpstream()
    {
        var handler = FakeHttpMessageHandler.Json(HttpStatusCode.OK, ValidResponse);
        var sut = NewSut(handler);

        var result = await sut.LookupAsync("00100", "FI");

        result.IsValid.Should().BeFalse();
        handler.CallCount.Should().Be(0, "postcodes.io only serves GB — we should short-circuit");
    }

    [Theory]
    [InlineData("eh11yz", "EH1 1YZ")]
    [InlineData("EH1 1YZ", "EH1 1YZ")]
    [InlineData("  EH11YZ  ", "EH1 1YZ")]
    [InlineData("SW1A1AA", "SW1A 1AA")]
    [InlineData("sw1a 1aa", "SW1A 1AA")]
    public void NormalisePostcode_ProducesCanonicalForm(string input, string expected)
    {
        PostcodesIoProvider.NormalisePostcode(input).Should().Be(expected);
    }

    [Fact]
    public async Task IsAvailableAsync_HealthProbeSucceeds_ReturnsTrue()
    {
        var sut = NewSut(FakeHttpMessageHandler.Status(HttpStatusCode.OK));

        var available = await sut.IsAvailableAsync();

        available.Should().BeTrue();
    }

    [Fact]
    public async Task IsAvailableAsync_HealthProbeThrows_ReturnsFalse()
    {
        var sut = NewSut(FakeHttpMessageHandler.Throws(new HttpRequestException("boom")));

        var available = await sut.IsAvailableAsync();

        available.Should().BeFalse();
    }
}
