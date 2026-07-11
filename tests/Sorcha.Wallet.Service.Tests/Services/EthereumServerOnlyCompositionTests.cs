// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Wallet.Core.Services.Implementation;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Services.Interfaces;
using Xunit;
using IdentityInterface = Sorcha.Wallet.Core.Services.Interfaces.IEthereumIdentityService;

namespace Sorcha.Wallet.Service.Tests.Services;

/// <summary>
/// Feature 182 (Phase 4) — value-moving signing is server-side only. The WASM PWA
/// (<c>Sorcha.Wallet.Pwa</c>) references neither <c>Sorcha.Wallet.Core</c> nor <c>Sorcha.Wallet.Portable</c>,
/// so it cannot obtain the transaction signer or orchestrator. These structural assertions pin the layering
/// that guarantees the capability never reaches the browser: the sanctioned signer lives in the server-side
/// Core assembly (not the WASM-safe Portable interface assembly), and the orchestrator lives in the service.
/// </summary>
public class EthereumServerOnlyCompositionTests
{
    [Fact]
    public void TransactionSigner_LivesInServerSideCore_NotThePortableWasmSurface()
    {
        typeof(IEthereumTransactionSigner).Assembly.GetName().Name.Should().Be("Sorcha.Wallet.Core");
        typeof(EthereumIdentityService).Assembly.GetName().Name.Should().Be("Sorcha.Wallet.Core");

        // The prove-control interface is the WASM-safe surface; it must NOT be co-located with the signer.
        typeof(IdentityInterface).Assembly.GetName().Name.Should().Be("Sorcha.Wallet.Portable");
    }

    [Fact]
    public void Orchestrator_LivesInTheWalletService()
    {
        typeof(IEthereumTransactionService).Assembly.GetName().Name.Should().Be("Sorcha.Wallet.Service");
    }

    [Fact]
    public void PortableWasmAssembly_ContainsNoTransactionSigningType()
    {
        var portable = typeof(IdentityInterface).Assembly;
        portable.GetTypes()
            .Where(t => t.Name.Contains("Transaction", StringComparison.Ordinal)
                        && (t.Name.Contains("Signer", StringComparison.Ordinal) || t.Name.Contains("EthereumTransaction", StringComparison.Ordinal)))
            .Should().BeEmpty("the WASM-safe surface must expose no native ETH transaction-signing type");
    }
}
