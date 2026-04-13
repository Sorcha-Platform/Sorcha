// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;

namespace Sorcha.AddressLookup.Tests;

/// <summary>
/// Tests for <see cref="AddressLookupService"/> — the composition root that
/// picks the best available provider for a given country.
/// </summary>
public class AddressLookupServiceTests
{
    private static AddressLookupService NewSut(params IAddressLookupProvider[] providers)
        => new(providers, Mock.Of<ILogger<AddressLookupService>>());

    [Fact]
    public async Task LookupAsync_NoProviders_ReturnsGracefulDegradation()
    {
        var sut = NewSut();

        var result = await sut.LookupAsync("EH1 1YZ");

        result.IsValid.Should().BeFalse();
        result.Provider.Should().Be("none");
    }

    [Fact]
    public async Task LookupAsync_OnlyValidateOnlyProvider_UsesIt()
    {
        var provider = StubProvider.ValidateOnly("postcodes.io", ["GB"], true);
        var sut = NewSut(provider);

        var result = await sut.LookupAsync("EH1 1YZ");

        result.Provider.Should().Be("postcodes.io");
        provider.LookupCalled.Should().BeTrue();
    }

    [Fact]
    public async Task LookupAsync_BothCapabilitiesAvailable_PrefersFullAddress()
    {
        var validate = StubProvider.ValidateOnly("postcodes.io", ["GB"], true);
        var fullAddress = StubProvider.FullAddress("os-places", ["GB"], true);
        var sut = NewSut(validate, fullAddress);

        var result = await sut.LookupAsync("EH1 1YZ");

        result.Provider.Should().Be("os-places");
        fullAddress.LookupCalled.Should().BeTrue();
        validate.LookupCalled.Should().BeFalse();
    }

    [Fact]
    public async Task LookupAsync_PreferredProviderUnavailable_FallsBackToValidateOnly()
    {
        var validate = StubProvider.ValidateOnly("postcodes.io", ["GB"], available: true);
        var fullAddress = StubProvider.FullAddress("os-places", ["GB"], available: false);
        var sut = NewSut(validate, fullAddress);

        var result = await sut.LookupAsync("EH1 1YZ");

        result.Provider.Should().Be("postcodes.io");
        validate.LookupCalled.Should().BeTrue();
        fullAddress.LookupCalled.Should().BeFalse();
    }

    [Fact]
    public async Task LookupAsync_NoProviderSupportsCountry_ReturnsNone()
    {
        var gbOnly = StubProvider.ValidateOnly("postcodes.io", ["GB"], true);
        var sut = NewSut(gbOnly);

        var result = await sut.LookupAsync("00100", "FI");

        result.Provider.Should().Be("none");
        gbOnly.LookupCalled.Should().BeFalse();
    }

    [Fact]
    public async Task LookupAsync_HealthCheckThrows_TreatsProviderAsUnavailable()
    {
        var flaky = StubProvider.ValidateOnly("flaky", ["GB"], available: true);
        flaky.HealthCheckThrows = true;
        var sut = NewSut(flaky);

        var result = await sut.LookupAsync("EH1 1YZ");

        result.Provider.Should().Be("none", "a provider whose health check throws should be considered unavailable");
    }

    [Fact]
    public async Task ListProvidersAsync_ReturnsAllRegisteredWithAvailability()
    {
        var a = StubProvider.ValidateOnly("postcodes.io", ["GB"], true);
        var b = StubProvider.FullAddress("os-places", ["GB"], false);
        var sut = NewSut(a, b);

        var providers = await sut.ListProvidersAsync();

        providers.Should().HaveCount(2);
        providers.Should().ContainSingle(p => p.Name == "postcodes.io" && p.Available);
        providers.Should().ContainSingle(p => p.Name == "os-places" && !p.Available);
    }

    [Fact]
    public async Task LookupAsync_EmptyPostcode_ReturnsNone()
    {
        var provider = StubProvider.ValidateOnly("postcodes.io", ["GB"], true);
        var sut = NewSut(provider);

        var result = await sut.LookupAsync("   ");

        result.Provider.Should().Be("none");
        provider.LookupCalled.Should().BeFalse();
    }

    // ----- test helper -----

    private sealed class StubProvider : IAddressLookupProvider
    {
        public bool HealthCheckThrows { get; set; }
        public bool LookupCalled { get; private set; }

        private readonly bool _available;

        private StubProvider(string name, AddressLookupCapability capability, IReadOnlyList<string> countries, bool available)
        {
            ProviderName = name;
            Capability = capability;
            SupportedCountries = countries;
            _available = available;
        }

        public static StubProvider ValidateOnly(string name, string[] countries, bool available)
            => new(name, AddressLookupCapability.ValidateOnly, countries, available);

        public static StubProvider FullAddress(string name, string[] countries, bool available)
            => new(name, AddressLookupCapability.FullAddress, countries, available);

        public string ProviderName { get; }
        public AddressLookupCapability Capability { get; }
        public IReadOnlyList<string> SupportedCountries { get; }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default)
        {
            if (HealthCheckThrows) throw new InvalidOperationException("boom");
            return Task.FromResult(_available);
        }

        public Task<AddressLookupResult> LookupAsync(
            string postcode,
            string? countryHint = null,
            CancellationToken cancellationToken = default)
        {
            LookupCalled = true;
            return Task.FromResult(new AddressLookupResult
            {
                Postcode = postcode,
                IsValid = true,
                Provider = ProviderName,
                Capability = Capability
            });
        }
    }
}
