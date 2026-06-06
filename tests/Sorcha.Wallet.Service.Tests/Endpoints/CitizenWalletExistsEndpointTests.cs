// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Reflection;
using System.Security.Claims;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Moq;
using Sorcha.CitizenWallet.Abstractions.Models;
using Sorcha.Wallet.Core.Domain;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Service.Endpoints;
using Xunit;
using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Service.Tests.Endpoints;

/// <summary>
/// Feature 149 — tests for the <see cref="CitizenWalletEndpoints"/>
/// <c>WalletExists</c> handler. Reflection-based static-handler invocation
/// (same pattern as <see cref="CitizenWalletEnrolEndpointTests"/>). The handler
/// must ALWAYS return 200 with an explicit boolean for an authenticated caller —
/// no 401/404 ambiguity — so the PWA gets a clean walletless signal.
/// </summary>
public sealed class CitizenWalletExistsEndpointTests
{
    private static readonly Guid PlatformUserId = Guid.NewGuid();
    private const string CitizenWallet = "ws1qcitizen1";

    private readonly Mock<IWalletRepository> _walletRepository = new();

    private static HttpContext BuildHttpContext(Guid? platformUserId, string? walletAddress)
    {
        var ctx = new DefaultHttpContext();
        var claims = new List<Claim>();
        if (platformUserId is not null)
            claims.Add(new Claim("platform_user_id", platformUserId.Value.ToString()));
        if (walletAddress is not null)
            claims.Add(new Claim("wallet_address", walletAddress));
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
        return ctx;
    }

    private async Task<IResult> InvokeAsync(HttpContext context)
    {
        var method = typeof(CitizenWalletEndpoints).GetMethod(
            "WalletExists",
            BindingFlags.NonPublic | BindingFlags.Static)!;
        method.Should().NotBeNull("WalletExists handler should exist");

        var result = method.Invoke(null, [context, _walletRepository.Object, CancellationToken.None]);
        return await (Task<IResult>)result!;
    }

    [Fact]
    public async Task WalletExists_WhenWalletAddressClaimPresent_ReturnsOkTrue()
    {
        var ctx = BuildHttpContext(PlatformUserId, CitizenWallet);

        var result = await InvokeAsync(ctx);

        var ok = result.Should().BeOfType<Ok<WalletExistsResponse>>().Subject;
        ok.Value!.HasWallet.Should().BeTrue();
    }

    [Fact]
    public async Task WalletExists_WhenRepositoryOwnsWallet_ReturnsOkTrue()
    {
        // Production path: F136 consumer tokens omit the wallet_address claim, so
        // a real "has wallet" result is reached via the GetByOwnerAsync fallback.
        _walletRepository
            .Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                new WalletEntity
                {
                    Address = CitizenWallet,
                    EncryptedPrivateKey = "k",
                    EncryptionKeyId = "kid",
                    Algorithm = "ED25519",
                    Owner = PlatformUserId.ToString(),
                    Tenant = "default",
                    Name = "citizen",
                    Status = WalletStatus.Active
                }
            });

        var ctx = BuildHttpContext(PlatformUserId, walletAddress: null);

        var result = await InvokeAsync(ctx);

        var ok = result.Should().BeOfType<Ok<WalletExistsResponse>>().Subject;
        ok.Value!.HasWallet.Should().BeTrue();
    }

    [Fact]
    public async Task WalletExists_WhenNoWalletResolves_ReturnsOkFalse()
    {
        // No wallet_address claim and the repository owns no wallet for the caller.
        _walletRepository
            .Setup(r => r.GetByOwnerAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<WalletEntity>());

        var ctx = BuildHttpContext(PlatformUserId, walletAddress: null);

        var result = await InvokeAsync(ctx);

        var ok = result.Should().BeOfType<Ok<WalletExistsResponse>>().Subject;
        ok.Value!.HasWallet.Should().BeFalse();
    }
}
