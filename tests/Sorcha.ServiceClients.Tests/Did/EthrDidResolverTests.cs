// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.ServiceClients.Did;

namespace Sorcha.ServiceClients.Tests.Did;

public class EthrDidResolverTests
{
    private static readonly EthrDidResolver Resolver = new(NullLogger<EthrDidResolver>.Instance);
    private const string Address = "0xAb5801a7D398351b8bE11C439e05C5B3259aeC9B";

    [Fact]
    public void CanResolve_EthrMethod_True()
    {
        Resolver.CanResolve("ethr").Should().BeTrue();
        Resolver.CanResolve("pkh").Should().BeFalse();
    }

    [Theory]
    [InlineData("did:ethr:{ADDR}", "eip155:1:{ADDR}")]                 // bare = mainnet default
    [InlineData("did:ethr:mainnet:{ADDR}", "eip155:1:{ADDR}")]        // named network
    [InlineData("did:ethr:sepolia:{ADDR}", "eip155:11155111:{ADDR}")] // named testnet
    [InlineData("did:ethr:0x89:{ADDR}", "eip155:137:{ADDR}")]         // hex chain-id (polygon)
    public async Task Resolve_AddressForms_EmitControllerRecoveryVm(string didTemplate, string expectedCaip)
    {
        var did = didTemplate.Replace("{ADDR}", Address);
        var expected = expectedCaip.Replace("{ADDR}", Address);

        var doc = await Resolver.ResolveAsync(did);

        doc.Should().NotBeNull();
        doc!.Id.Should().Be(did);
        var vm = doc.VerificationMethod.Should().ContainSingle().Subject;
        vm.Id.Should().Be($"{did}#controller");
        vm.Type.Should().Be("EcdsaSecp256k1RecoveryMethod2020");
        vm.PublicKeyJwk.Should().BeNull();
        vm.BlockchainAccountId.Should().Be(expected);
        doc.AssertionMethod.Should().Contain(vm.Id);
        doc.Authentication.Should().Contain(vm.Id);
    }

    [Theory]
    [InlineData("did:ethr:0x1234")]                     // short address
    [InlineData("did:ethr:narnia:{ADDR}")]              // unknown network name
    [InlineData("did:ethr:")]                           // empty
    [InlineData("did:pkh:eip155:1:{ADDR}")]             // wrong method
    public async Task Resolve_Malformed_ReturnsNull(string didTemplate)
    {
        var did = didTemplate.Replace("{ADDR}", Address);
        (await Resolver.ResolveAsync(did)).Should().BeNull();
    }
}
