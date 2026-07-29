// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Sorcha.Wallet.Core.Domain;
using Sorcha.Wallet.Core.Domain.Entities;
using Sorcha.Wallet.Core.Repositories.Interfaces;
using Sorcha.Wallet.Service.Authorization;

using WalletEntity = Sorcha.Wallet.Core.Domain.Entities.Wallet;

namespace Sorcha.Wallet.Service.Tests.Authorization;

/// <summary>
/// G1 (catch-up security review 2026-07-29) — behaviour of the wallet-ownership gate.
///
/// The wallet-scoped route groups authorized on <c>CanManageWallets</c>, which is literally "the
/// token carries any non-empty <c>org_id</c>, OR it is a service token", and never compared the
/// caller to the <c>{walletAddress}</c> in the route. Consumer-tier citizen tokens carry
/// <c>org_id</c>, so every authenticated citizen satisfied it for every wallet — and no handler in
/// those groups checked ownership either.
/// </summary>
public class WalletOwnershipGateTests
{
    private const string VictimWallet = "ws1victimwallet";
    private const string VictimOwner = "victim-platform-user-id";
    private const string Attacker = "attacker-platform-user-id";

    private static WalletEntity Wallet(string address, string owner) => new()
    {
        Address = address,
        Owner = owner,
        Tenant = "default",
        Name = "test",
        Algorithm = "ED25519",
        EncryptedPrivateKey = "x",
        EncryptionKeyId = "k",
        Status = WalletStatus.Active
    };

    /// <summary>
    /// Builds an HttpContext with the given route wallet address, claims, and a repository
    /// containing <paramref name="stored"/>.
    /// </summary>
    private static HttpContext Context(
        string? routeWalletAddress,
        IEnumerable<Claim> claims,
        WalletEntity? stored)
    {
        var repository = new Mock<IWalletRepository>();
        repository
            .Setup(r => r.GetByAddressAsync(
                It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<bool>(), It.IsAny<bool>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((string address, bool _, bool _, bool _, CancellationToken _) =>
                stored is not null && stored.Address == address ? stored : null);

        var services = new ServiceCollection();
        services.AddSingleton(repository.Object);
        services.AddSingleton<Microsoft.Extensions.Logging.ILoggerFactory>(NullLoggerFactory.Instance);

        var http = new DefaultHttpContext
        {
            RequestServices = services.BuildServiceProvider(),
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"))
        };
        http.Request.Method = "GET";
        http.Request.Path = "/api/v1/wallets/x/credentials";

        if (routeWalletAddress is not null)
        {
            http.Request.RouteValues["walletAddress"] = routeWalletAddress;
        }

        return http;
    }

    private static Claim[] Citizen(string platformUserId) =>
    [
        new("platform_user_id", platformUserId),
        // The pre-fix policy passed on org_id alone. Present here deliberately: the gate must deny
        // this caller anyway, proving the decision is ownership and not org membership.
        new("org_id", "00000000-0000-0000-0000-000000000001")
    ];

    [Fact]
    public async Task ForeignWallet_IsDenied()
    {
        var http = Context(VictimWallet, Citizen(Attacker), Wallet(VictimWallet, VictimOwner));

        var result = await WalletOwnershipGate.EvaluateAsync(http);

        result.Should().NotBeNull("a citizen must not act on a wallet they do not own");
        result.Should().BeAssignableTo<IResult>();
        result!.GetType().Name.Should().Contain("Forbid");
    }

    [Fact]
    public async Task OwnWallet_IsAllowed()
    {
        var http = Context(VictimWallet, Citizen(VictimOwner), Wallet(VictimWallet, VictimOwner));

        var result = await WalletOwnershipGate.EvaluateAsync(http);

        result.Should().BeNull("null means 'continue to the handler' — the owner must get through");
    }

    [Fact]
    public async Task ServiceToken_IsAllowed_EvenForAWalletItDoesNotOwn()
    {
        // Blueprint's credential issuance posts to the ISSUING ORG's wallet while the recipient is
        // a different citizen. Removing this bypass would break issuance.
        var claims = new Claim[] { new("token_type", "service") };
        var http = Context(VictimWallet, claims, Wallet(VictimWallet, VictimOwner));

        var result = await WalletOwnershipGate.EvaluateAsync(http);

        result.Should().BeNull("internal service-to-service callers must keep working");
    }

    [Fact]
    public async Task LegacyWalletOwnedBySubClaim_IsAllowed()
    {
        // Wallets created before #878 carry Owner = UserIdentity.Id (the `sub` / NameIdentifier)
        // rather than platform_user_id. The gate must match WalletEndpoints.GetCurrentUser's
        // preference order, or it would silently deny citizens their own older wallets.
        var claims = new Claim[] { new(ClaimTypes.NameIdentifier, "legacy-sub-id") };
        var http = Context(VictimWallet, claims, Wallet(VictimWallet, "legacy-sub-id"));

        var result = await WalletOwnershipGate.EvaluateAsync(http);

        result.Should().BeNull("legacy sub-owned wallets must still be reachable by their owner");
    }

    [Fact]
    public async Task PlatformUserIdWins_OverNameIdentifier()
    {
        // Both claims present and different — the wallet is owned by the platform_user_id, which is
        // what post-#878 creation stamps. Preferring NameIdentifier here would deny the true owner.
        var claims = new Claim[]
        {
            new("platform_user_id", VictimOwner),
            new(ClaimTypes.NameIdentifier, "org-scoped-identity-row-id")
        };
        var http = Context(VictimWallet, claims, Wallet(VictimWallet, VictimOwner));

        var result = await WalletOwnershipGate.EvaluateAsync(http);

        result.Should().BeNull();
    }

    [Fact]
    public async Task UnknownWallet_IsNotFound_RegardlessOfCaller()
    {
        // 404 for everyone, so the answer never reveals whether a wallet exists.
        var http = Context(VictimWallet, Citizen(Attacker), stored: null);

        var result = await WalletOwnershipGate.EvaluateAsync(http);

        result.Should().NotBeNull();
        result!.GetType().Name.Should().Contain("NotFound");
    }

    [Fact]
    public async Task UnidentifiablePrincipal_IsUnauthorized()
    {
        var http = Context(VictimWallet, claims: [], stored: Wallet(VictimWallet, VictimOwner));

        var result = await WalletOwnershipGate.EvaluateAsync(http);

        result.Should().NotBeNull();
        result!.GetType().Name.Should().Contain("Unauthorized");
    }

    [Fact]
    public async Task AddressRouteValueSpelling_IsAlsoHonoured()
    {
        // The wallet-scoped routes are not consistently named: the credentials and delegation
        // groups use {walletAddress}, while WalletEndpoints uses {address}. The gate must read both,
        // or applying it to WalletEndpoints' mutating routes would fail closed on every request
        // instead of checking ownership.
        var http = Context(routeWalletAddress: null, Citizen(Attacker), Wallet(VictimWallet, VictimOwner));
        http.Request.RouteValues["address"] = VictimWallet;

        var result = await WalletOwnershipGate.EvaluateAsync(http);

        result.Should().NotBeNull("the {address} spelling must be recognised and the foreign wallet denied");
        result!.GetType().Name.Should().Contain("Forbid");
    }

    [Fact]
    public async Task MissingRouteValue_FailsClosed()
    {
        // The gate applied to a route with no wallet address in its template is a wiring mistake.
        // It must refuse rather than wave the request through, or a mis-wiring would look like a
        // working control — which is the whole failure mode this gate exists to end.
        var http = Context(routeWalletAddress: null, Citizen(Attacker), Wallet(VictimWallet, VictimOwner));

        var result = await WalletOwnershipGate.EvaluateAsync(http);

        result.Should().NotBeNull("a mis-wired gate must fail closed, not open");
    }
}
