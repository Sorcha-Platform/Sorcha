// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.AspNetCore.Mvc;
using Sorcha.Cryptography.Secp256k1.Siwe;
using Sorcha.Wallet.Core.Services.Interfaces;

namespace Sorcha.Wallet.Service.Endpoints;

/// <summary>
/// Feature 180 — Ethereum prove-control endpoints: expose a wallet's auxiliary Ethereum address, sign a
/// SIWE (EIP-4361) prove-control message, and verify an inbound SIWE (Sorcha as relying party). The
/// Ethereum private key is derived on demand from the wallet's seed and never returned; there is no
/// transaction or raw-digest signing here (Phase 4 covers transacting).
/// </summary>
public static class EthereumEndpoints
{
    /// <summary>Map the Ethereum prove-control endpoints.</summary>
    public static IEndpointRouteBuilder MapEthereumEndpoints(this IEndpointRouteBuilder app)
    {
        var walletGroup = app.MapGroup("/api/v1/wallets/{walletAddress}")
            .WithTags("Ethereum")
            .RequireAuthorization("CanManageWallets");

        walletGroup.MapGet("/ethereum-address", GetEthereumAddress)
            .WithName("GetEthereumAddress")
            .WithSummary("Get the wallet's Ethereum address")
            .WithDescription("Returns the EIP-55 Ethereum address of the wallet's auxiliary secp256k1 identity, derived from its seed.")
            .Produces<EthereumAddressResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        walletGroup.MapPost("/siwe/sign", SignSiwe)
            .WithName("SignSiwe")
            .WithSummary("Sign a SIWE prove-control message")
            .WithDescription("Produces a Sign-In With Ethereum (EIP-4361) message signed by the wallet's Ethereum key. Prove-control only — the key never signs a transaction and is never returned.")
            .Produces<SiweSignResult>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status404NotFound);

        // Relying-party verification is not wallet-scoped — any authenticated caller may verify a proof.
        app.MapPost("/api/v1/siwe/verify", VerifySiwe)
            .WithTags("Ethereum")
            .RequireAuthorization()
            .WithName("VerifySiwe")
            .WithSummary("Verify an inbound SIWE proof")
            .WithDescription("Verifies a Sign-In With Ethereum message + signature (Sorcha as relying party): recovers the signer, matches the message address, and checks nonce/domain/validity window.")
            .Produces<SiweVerifyResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem();

        return app;
    }

    private static async Task<IResult> GetEthereumAddress(
        string walletAddress,
        [FromServices] IEthereumIdentityService service,
        [FromQuery] int index = 0,
        CancellationToken cancellationToken = default)
    {
        var address = await service.GetAddressAsync(walletAddress, index, cancellationToken);
        return Results.Ok(new EthereumAddressResponse(address));
    }

    private static async Task<IResult> SignSiwe(
        string walletAddress,
        [FromBody] SiweSignRequest request,
        [FromServices] IEthereumIdentityService service,
        CancellationToken cancellationToken = default)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Domain) || string.IsNullOrWhiteSpace(request.Uri) || string.IsNullOrWhiteSpace(request.Nonce))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["domain, uri and nonce are required."]
            });
        }

        var message = new SiweMessage
        {
            Domain = request.Domain,
            Address = "0x0000000000000000000000000000000000000000", // set by the service to the wallet's own address
            Statement = request.Statement,
            Uri = request.Uri,
            Version = "1",
            ChainId = request.ChainId ?? 1,
            Nonce = request.Nonce,
            IssuedAt = string.IsNullOrWhiteSpace(request.IssuedAt) ? DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ") : request.IssuedAt!,
            ExpirationTime = request.ExpirationTime,
            NotBefore = request.NotBefore,
            RequestId = request.RequestId,
            Resources = request.Resources
        };

        var result = await service.SignSiweAsync(walletAddress, message, request.Index ?? 0, cancellationToken);
        return Results.Ok(result);
    }

    private static IResult VerifySiwe([FromBody] SiweVerifyRequest request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Message) || string.IsNullOrWhiteSpace(request.Signature))
        {
            return Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["message and signature are required."]
            });
        }

        byte[] signature;
        try
        {
            var hex = request.Signature.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? request.Signature[2..] : request.Signature;
            signature = Convert.FromHexString(hex);
        }
        catch
        {
            return Results.Ok(new SiweVerifyResponse(false, null, "Signature is not valid hex."));
        }

        var result = SiweVerifier.Verify(request.Message, signature,
            new SiweValidationOptions(request.ExpectedNonce, request.ExpectedDomain));
        return Results.Ok(new SiweVerifyResponse(result.Valid, result.Address, result.Reason));
    }
}

/// <summary>The wallet's Ethereum address.</summary>
public sealed record EthereumAddressResponse(string Address);

/// <summary>Request to sign a SIWE prove-control message.</summary>
public sealed record SiweSignRequest(
    string Domain,
    string Uri,
    string Nonce,
    long? ChainId = 1,
    string? Statement = null,
    string? IssuedAt = null,
    string? ExpirationTime = null,
    string? NotBefore = null,
    string? RequestId = null,
    IReadOnlyList<string>? Resources = null,
    int? Index = 0);

/// <summary>Request to verify a SIWE proof.</summary>
public sealed record SiweVerifyRequest(
    string Message,
    string Signature,
    string? ExpectedNonce = null,
    string? ExpectedDomain = null);

/// <summary>The result of verifying a SIWE proof.</summary>
public sealed record SiweVerifyResponse(bool Valid, string? Address, string? Reason);
