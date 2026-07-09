// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Buffers.Text;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Sorcha.Blueprint.Service.Credentials;
using Sorcha.ServiceClients.Did;

namespace Sorcha.Blueprint.Service.Tests.Credentials;

/// <summary>Feature 178 — the address-form issuer path through the engine issuer-key resolver.</summary>
public class DidX5cIssuerKeyResolverTests
{
    private const string Did = "did:pkh:eip155:1:0xAb5801a7D398351b8bE11C439e05C5B3259aeC9B";
    private const string AccountId = "eip155:1:0xAb5801a7D398351b8bE11C439e05C5B3259aeC9B";

    [Fact]
    public async Task ResolveAsync_AddressFormIssuer_CarriesBlockchainAccountIdNotKey()
    {
        var vmId = $"{Did}#blockchainAccountId";
        var doc = new DidDocument
        {
            Id = Did,
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = vmId,
                    Type = "EcdsaSecp256k1RecoveryMethod2020",
                    Controller = Did,
                    BlockchainAccountId = AccountId
                }
            ],
            AssertionMethod = [vmId]
        };

        var registry = new Mock<IDidResolverRegistry>();
        registry.Setup(r => r.ResolveAsync(Did, It.IsAny<CancellationToken>())).ReturnsAsync(doc);

        var resolver = new DidX5cIssuerKeyResolver(NullLogger<DidX5cIssuerKeyResolver>.Instance, registry.Object);

        var result = await resolver.ResolveAsync(BuildSdJwt(Did));

        result.Should().NotBeNull();
        result!.BlockchainAccountId.Should().Be(AccountId);
        result.Algorithm.Should().Be("ES256K");
        result.PublicKey.Should().BeEmpty();
        result.SigningKeyId.Should().Be($"{Did}#blockchainAccountId");
    }

    [Fact]
    public async Task ResolveAsync_AddressFormVmNotInAssertionMethod_ReturnsNull()
    {
        var vmId = $"{Did}#blockchainAccountId";
        var doc = new DidDocument
        {
            Id = Did,
            VerificationMethod =
            [
                new VerificationMethod
                {
                    Id = vmId,
                    Type = "EcdsaSecp256k1RecoveryMethod2020",
                    Controller = Did,
                    BlockchainAccountId = AccountId
                }
            ],
            AssertionMethod = ["did:pkh:eip155:1:0xdifferent#blockchainAccountId"] // matched VM excluded
        };

        var registry = new Mock<IDidResolverRegistry>();
        registry.Setup(r => r.ResolveAsync(Did, It.IsAny<CancellationToken>())).ReturnsAsync(doc);

        var resolver = new DidX5cIssuerKeyResolver(NullLogger<DidX5cIssuerKeyResolver>.Instance, registry.Object);

        (await resolver.ResolveAsync(BuildSdJwt(Did))).Should().BeNull();
    }

    private static string BuildSdJwt(string iss)
    {
        var header = Base64Url.EncodeToString(Encoding.UTF8.GetBytes("""{"alg":"ES256K"}"""));
        var payload = Base64Url.EncodeToString(Encoding.UTF8.GetBytes($$"""{"iss":"{{iss}}"}"""));
        return $"{header}.{payload}.c2lnbmF0dXJl~";
    }
}
