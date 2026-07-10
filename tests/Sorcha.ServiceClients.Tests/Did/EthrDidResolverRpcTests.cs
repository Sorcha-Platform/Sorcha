// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging.Abstractions;
using Sorcha.ServiceClients.Did;
using Sorcha.ServiceClients.Evm;
using Sorcha.ServiceClients.Tests.Evm;
using static Sorcha.ServiceClients.Tests.Evm.EvmTestFixtures;

namespace Sorcha.ServiceClients.Tests.Did;

public class EthrDidResolverRpcTests
{
    private const string Identity = "0xAb5801a7D398351b8bE11C439e05C5B3259aeC9B";
    private const string NewOwner = "0x1111111111111111111111111111111111111111";
    private const string DelegateAddr = "0x2222222222222222222222222222222222222222";
    private const string Did = "did:ethr:0xAb5801a7D398351b8bE11C439e05C5B3259aeC9B";

    [Fact]
    public async Task Resolve_Rotated_EmitsCurrentOwnerControllerVm()
    {
        var rpc = new FakeEvmRpc
        {
            Changed = OkWord(UintWord(100)),
            Owner = OkWord(AddrWord(NewOwner)),
            LogsByBlock = { [100] = Logs(OwnerChanged(NewOwner, previous: 0)) }
        };

        var doc = await Resolver(rpc).ResolveAsync(Did);

        doc.Should().NotBeNull();
        var controller = doc!.VerificationMethod.Single(v => v.Id == $"{Did}#controller");
        controller.Type.Should().Be("EcdsaSecp256k1RecoveryMethod2020");
        controller.BlockchainAccountId.Should().Be($"eip155:1:{NewOwner}");
        doc.AssertionMethod.Should().Contain(controller.Id);
    }

    [Fact]
    public async Task Resolve_VeriKeyDelegate_EmitsDelegateRecoveryVmInAssertionMethod()
    {
        var rpc = new FakeEvmRpc
        {
            Changed = OkWord(UintWord(100)),
            Owner = OkWord(AddrWord(Identity)),
            LogsByBlock = { [100] = Logs(DelegateChanged("veriKey", DelegateAddr, Future, previous: 0)) }
        };

        var doc = await Resolver(rpc).ResolveAsync(Did);

        var del = doc!.VerificationMethod.Single(v => v.Id == $"{Did}#delegate-1");
        del.Type.Should().Be("EcdsaSecp256k1RecoveryMethod2020");
        del.BlockchainAccountId.Should().Be($"eip155:1:{DelegateAddr}");
        doc.AssertionMethod.Should().Contain(del.Id);
    }

    [Fact]
    public async Task Resolve_SigAuthDelegate_GoesToAuthenticationNotAssertion()
    {
        var rpc = new FakeEvmRpc
        {
            Changed = OkWord(UintWord(100)),
            Owner = OkWord(AddrWord(Identity)),
            LogsByBlock = { [100] = Logs(DelegateChanged("sigAuth", DelegateAddr, Future, previous: 0)) }
        };

        var doc = await Resolver(rpc).ResolveAsync(Did);

        var del = doc!.VerificationMethod.Single(v => v.Id == $"{Did}#delegate-1");
        doc.Authentication.Should().Contain(del.Id);
        doc.AssertionMethod.Should().NotContain(del.Id);
    }

    [Fact]
    public async Task Resolve_Secp256k1KeyAttribute_EmitsPublicKeyJwkVm()
    {
        // A valid secp256k1 point (generator G) as the published key.
        var g = Convert.FromHexString(
            "0479BE667EF9DCBBAC55A06295CE870B07029BFCDB2DCE28D959F2815B16F81798" +
            "483ADA7726A3C4655DA4FBFC0E1108A8FD17B448A68554199C47D08FFB10D4B8");
        var rpc = new FakeEvmRpc
        {
            Changed = OkWord(UintWord(100)),
            Owner = OkWord(AddrWord(Identity)),
            LogsByBlock = { [100] = Logs(AttributeChanged("did/pub/Secp256k1/veriKey/hex", g, Future, previous: 0)) }
        };

        var doc = await Resolver(rpc).ResolveAsync(Did);

        var vm = doc!.VerificationMethod.Single(v => v.Id == $"{Did}#delegate-1");
        vm.Type.Should().Be("JsonWebKey2020");
        vm.PublicKeyJwk!.Value.GetProperty("crv").GetString().Should().Be("secp256k1");
        doc.AssertionMethod.Should().Contain(vm.Id);
    }

    [Fact]
    public async Task Resolve_NoHistory_ReturnsDefaultDocument()
    {
        var rpc = new FakeEvmRpc { Changed = OkWord(UintWord(0)) };
        var doc = await Resolver(rpc).ResolveAsync(Did);

        doc!.VerificationMethod.Should().ContainSingle()
            .Which.BlockchainAccountId.Should().Be($"eip155:1:{Identity}");
    }

    [Fact]
    public async Task Resolve_RpcError_ReturnsNull_FailClosed()
    {
        var rpc = new FakeEvmRpc { Changed = EvmCallResult.Error };
        (await Resolver(rpc).ResolveAsync(Did)).Should().BeNull();
    }

    [Fact]
    public async Task Resolve_NullRegistry_OfflineDefaultDocument()
    {
        // WASM / offline composition — no registry, no network.
        var resolver = new EthrDidResolver(NullLogger<EthrDidResolver>.Instance, registry: null);
        var doc = await resolver.ResolveAsync(Did);

        doc!.VerificationMethod.Should().ContainSingle()
            .Which.BlockchainAccountId.Should().Be($"eip155:1:{Identity}");
    }

    private static EthrDidResolver Resolver(FakeEvmRpc rpc)
    {
        var registry = new Erc1056Registry(rpc, new EvmRpcOptions { AllowPrivateAddresses = true }, NullLogger.Instance, () => Now);
        return new EthrDidResolver(NullLogger<EthrDidResolver>.Instance, registry);
    }
}
