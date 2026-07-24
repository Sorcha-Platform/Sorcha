// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Microsoft.Extensions.Configuration;

using Sorcha.ServiceClients.Configuration;

namespace Sorcha.ServiceClients.Tests.Configuration;

/// <summary>
/// Pins the service-address key cascade.
/// </summary>
/// <remarks>
/// <para>
/// An audit found 19 distinct key spellings addressing 8 services — Tenant alone had four — with
/// six call sites each hand-rolling a different fallback chain. Which key a deployment had to set
/// therefore depended on which client happened to resolve it.
/// </para>
/// <para>
/// The load-bearing assertion here is <see cref="CanonicalKey_IsWhatDeploymentsAlreadySet"/>: real
/// docker-compose and n1 deployments set <c>ServiceClients__{X}Service__Address</c>. If that key
/// ever stopped resolving first, running nodes would silently lose their configuration.
/// </para>
/// </remarks>
public sealed class SorchaServiceAddressesTests
{
    private static IConfiguration Config(params (string Key, string Value)[] pairs) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(pairs.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)))
            .Build();

    [Theory]
    [InlineData(SorchaService.Tenant, "ServiceClients:TenantService:Address")]
    [InlineData(SorchaService.Wallet, "ServiceClients:WalletService:Address")]
    [InlineData(SorchaService.Register, "ServiceClients:RegisterService:Address")]
    [InlineData(SorchaService.Blueprint, "ServiceClients:BlueprintService:Address")]
    [InlineData(SorchaService.Validator, "ServiceClients:ValidatorService:Address")]
    [InlineData(SorchaService.Peer, "ServiceClients:PeerService:Address")]
    [InlineData(SorchaService.Haip, "ServiceClients:HaipService:Address")]
    [InlineData(SorchaService.ApiGateway, "ServiceClients:ApiGateway:Address")]
    public void CanonicalKey_IsWhatDeploymentsAlreadySet(SorchaService service, string expected)
    {
        // These are the exact keys docker-compose.yml sets (as ServiceClients__X__Address).
        SorchaServiceAddresses.CanonicalKey(service).Should().Be(expected);
        SorchaServiceAddresses.TryResolve(Config((expected, "http://x:8080")), service)
            .Should().Be("http://x:8080");
    }

    [Theory]
    [InlineData("ServiceClients:TenantService:Address")]
    [InlineData("ServiceClients:Tenant:BaseAddress")]
    [InlineData("Services:TenantService:BaseAddress")]
    [InlineData("Services:Tenant:Url")]
    [InlineData("TenantService:Endpoint")]
    public void EveryHistoricalSpelling_StillResolves(string key)
    {
        // Dropping a spelling would silently unbind a running deployment's configuration. The
        // resolver exists to end the drift, not to break deployments.
        SorchaServiceAddresses.TryResolve(Config((key, "http://tenant:8080")), SorchaService.Tenant)
            .Should().Be("http://tenant:8080");
    }

    [Fact]
    public void CanonicalKey_WinsOverEveryLegacySpelling()
    {
        var config = Config(
            ("ServiceClients:TenantService:Address", "http://canonical:8080"),
            ("ServiceClients:Tenant:BaseAddress", "http://legacy-a:8080"),
            ("Services:TenantService:BaseAddress", "http://legacy-b:8080"),
            ("Services:Tenant:Url", "http://legacy-c:8080"),
            ("TenantService:Endpoint", "http://legacy-d:8080"));

        SorchaServiceAddresses.TryResolve(config, SorchaService.Tenant).Should().Be("http://canonical:8080");
    }

    [Fact]
    public void NothingConfigured_ReturnsNull_SoTheCallerKeepsItsOwnDefault()
    {
        // The resolver deliberately supplies no default: existing call-site defaults are not
        // interchangeable (http://tenant-service vs the Aspire https+http:// discovery scheme vs
        // an explicit :8080 vs throwing), so unifying them is a separate decision.
        SorchaServiceAddresses.TryResolve(Config(), SorchaService.Tenant).Should().BeNull();
    }

    [Fact]
    public void BlankValue_IsTreatedAsUnset_AndFallsThrough()
    {
        var config = Config(
            ("ServiceClients:TenantService:Address", "   "),
            ("Services:Tenant:Url", "http://fallback:8080"));

        SorchaServiceAddresses.TryResolve(config, SorchaService.Tenant).Should().Be("http://fallback:8080");
    }

    [Fact]
    public void KeysFor_LeadsWithTheCanonicalKey_ForEveryService()
    {
        foreach (var service in Enum.GetValues<SorchaService>())
        {
            SorchaServiceAddresses.KeysFor(service)[0]
                .Should().Be(SorchaServiceAddresses.CanonicalKey(service));
        }
    }
}
