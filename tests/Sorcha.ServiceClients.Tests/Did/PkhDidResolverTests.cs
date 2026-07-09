// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.ServiceClients.Did;

namespace Sorcha.ServiceClients.Tests.Did;

public class PkhDidResolverTests
{
    private static readonly PkhDidResolver Resolver = new(NullLogger<PkhDidResolver>.Instance);
    private const string Address = "0xAb5801a7D398351b8bE11C439e05C5B3259aeC9B";

    [Fact]
    public void CanResolve_PkhMethod_True()
    {
        Resolver.CanResolve("pkh").Should().BeTrue();
        Resolver.CanResolve("ethr").Should().BeFalse();
    }

    [Fact]
    public async Task Resolve_Eip155_EmitsRecoveryMethodInAssertionMethod()
    {
        var did = $"did:pkh:eip155:1:{Address}";

        var doc = await Resolver.ResolveAsync(did);

        doc.Should().NotBeNull();
        doc!.Id.Should().Be(did);
        var vm = doc.VerificationMethod.Should().ContainSingle().Subject;
        vm.Id.Should().Be($"{did}#blockchainAccountId");
        vm.Type.Should().Be("EcdsaSecp256k1RecoveryMethod2020");
        vm.Controller.Should().Be(did);
        vm.PublicKeyJwk.Should().BeNull();
        vm.BlockchainAccountId.Should().Be($"eip155:1:{Address}");
        doc.AssertionMethod.Should().Contain(vm.Id);
        doc.Authentication.Should().Contain(vm.Id);
    }

    [Fact]
    public async Task Resolve_NonMainnetChain_PreservesChainId()
    {
        var did = $"did:pkh:eip155:137:{Address}";
        var doc = await Resolver.ResolveAsync(did);
        doc!.VerificationMethod[0].BlockchainAccountId.Should().Be($"eip155:137:{Address}");
    }

    [Theory]
    [InlineData("did:pkh:eip155:1:0x1234")]                                    // short address
    [InlineData("did:pkh:bip122:000000000019d6689c085ae165831e93:0x1234")]    // non-eip155 namespace
    [InlineData("did:pkh:eip155:1:0xZZ5801a7D398351b8bE11C439e05C5B3259aeC9B")] // non-hex
    [InlineData("did:pkh:eip155:1")]                                           // missing address
    [InlineData("did:pkh:")]                                                   // empty
    [InlineData("did:key:z6Mk")]                                               // wrong method
    public async Task Resolve_Malformed_ReturnsNull(string did)
    {
        (await Resolver.ResolveAsync(did)).Should().BeNull();
    }
}
