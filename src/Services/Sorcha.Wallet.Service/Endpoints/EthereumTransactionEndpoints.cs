// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Numerics;
using Microsoft.AspNetCore.Mvc;
using Sorcha.Wallet.Service.Services.Implementation;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Endpoints;

/// <summary>
/// Feature 182 (Phase 4) — native ETH transacting endpoints: send a transfer (fire-and-report-hash),
/// preview its cost (read-only), and look up its receipt status. Server-side only; sends are gated by the
/// <c>CanTransactEthereum</c> policy plus the transacting policy (chain allowlist, mainnet gate, value cap,
/// fee ceiling). Amounts are wei as decimal strings (BigInteger-safe). The private key is never returned.
/// </summary>
public static class EthereumTransactionEndpoints
{
    /// <summary>Map the Ethereum transacting endpoints.</summary>
    public static IEndpointRouteBuilder MapEthereumTransactionEndpoints(this IEndpointRouteBuilder app)
    {
        var walletGroup = app.MapGroup("/api/v1/wallets/{walletAddress}/ethereum/transactions")
            .WithTags("Ethereum")
            .RequireAuthorization("CanTransactEthereum");

        walletGroup.MapPost("", SendTransfer)
            .WithName("SendEthereumTransfer")
            .WithSummary("Send a native ETH transfer")
            .WithDescription("Prepares (nonce/gas/fees), signs, and broadcasts a native ETH transfer from the wallet's Ethereum identity, returning the transaction hash immediately. Refuses (broadcasts nothing) on any policy violation or network/estimation failure.")
            .Produces<SendTransferResponse>(StatusCodes.Status200OK)
            .Produces<RefusalProblem>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces<RefusalProblem>(StatusCodes.Status404NotFound)
            .Produces<RefusalProblem>(StatusCodes.Status502BadGateway);

        walletGroup.MapPost("/preview", PreviewTransfer)
            .WithName("PreviewEthereumTransfer")
            .WithSummary("Preview the cost of a native ETH transfer")
            .WithDescription("Computes nonce, gas, fees, and estimated total cost for an intended transfer without signing or broadcasting. Read-only.")
            .Produces<PreviewResponse>(StatusCodes.Status200OK)
            .Produces<RefusalProblem>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden)
            .Produces<RefusalProblem>(StatusCodes.Status404NotFound)
            .Produces<RefusalProblem>(StatusCodes.Status502BadGateway);

        app.MapGet("/api/v1/ethereum/transactions/{chainId:long}/{txHash}", GetStatus)
            .WithTags("Ethereum")
            .RequireAuthorization()
            .WithName("GetEthereumTransferStatus")
            .WithSummary("Get the status of a submitted transfer")
            .WithDescription("Reports the receipt status of a transaction hash on a network: pending, success, or reverted (with block number and gas used once mined).")
            .Produces<TransferStatusResponse>(StatusCodes.Status200OK)
            .Produces<RefusalProblem>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        return app;
    }

    private static async Task<IResult> SendTransfer(
        string walletAddress,
        [FromBody] SendTransferRequest request,
        [FromServices] IEthereumTransactionService service,
        CancellationToken ct)
    {
        if (!TryParseRequest(request, out var valueWei, out var badRequest))
        {
            return badRequest!;
        }

        var outcome = await service.SendAsync(walletAddress, request.ChainId, request.To!, valueWei, request.Index ?? 0, ct);
        if (!outcome.Ok)
        {
            return RefusalResult(outcome.Reason);
        }

        return Results.Ok(new SendTransferResponse(outcome.TxHash!, outcome.From!, outcome.ChainId, outcome.Nonce, "submitted"));
    }

    private static async Task<IResult> PreviewTransfer(
        string walletAddress,
        [FromBody] SendTransferRequest request,
        [FromServices] IEthereumTransactionService service,
        CancellationToken ct)
    {
        if (!TryParseRequest(request, out var valueWei, out var badRequest))
        {
            return badRequest!;
        }

        var outcome = await service.PreviewAsync(walletAddress, request.ChainId, request.To!, valueWei, request.Index ?? 0, ct);
        if (!outcome.Ok)
        {
            return RefusalResult(outcome.Reason);
        }

        var p = outcome.Preparation!;
        return Results.Ok(new PreviewResponse(
            p.ChainId, p.From, p.Nonce, p.GasLimit,
            p.MaxFeePerGasWei.ToString(), p.MaxPriorityFeePerGasWei.ToString(),
            p.ValueWei.ToString(), p.EstimatedTotalCostWei.ToString()));
    }

    private static async Task<IResult> GetStatus(
        long chainId,
        string txHash,
        [FromServices] IEthereumTransactionService service,
        CancellationToken ct)
    {
        var outcome = await service.GetStatusAsync(chainId, txHash, ct);
        if (!outcome.Ok)
        {
            return RefusalResult(outcome.Reason);
        }

        return Results.Ok(new TransferStatusResponse(txHash, outcome.Status!, outcome.BlockNumber, outcome.GasUsed));
    }

    private static bool TryParseRequest(SendTransferRequest request, out BigInteger valueWei, out IResult? badRequest)
    {
        valueWei = BigInteger.Zero;
        badRequest = null;

        if (request is null || string.IsNullOrWhiteSpace(request.To) || string.IsNullOrWhiteSpace(request.ValueWei))
        {
            badRequest = Results.ValidationProblem(new Dictionary<string, string[]>
            {
                ["request"] = ["chainId, to and valueWei are required."]
            });
            return false;
        }

        if (!BigInteger.TryParse(request.ValueWei, out valueWei) || valueWei.Sign < 0)
        {
            badRequest = RefusalResult(TransferRefusalReason.InvalidAmount);
            return false;
        }

        return true;
    }

    private static IResult RefusalResult(TransferRefusalReason reason)
    {
        var status = reason switch
        {
            TransferRefusalReason.WalletNotFound => StatusCodes.Status404NotFound,
            TransferRefusalReason.RpcError
                or TransferRefusalReason.EstimateFailed
                or TransferRefusalReason.BroadcastFailed => StatusCodes.Status502BadGateway,
            _ => StatusCodes.Status400BadRequest
        };

        var code = EthereumTransactionService.ReasonCode(reason);
        return Results.Json(new RefusalProblem("Transfer refused", status, $"The transfer was refused: {code}.", code), statusCode: status);
    }
}

/// <summary>Request to send or preview a native ETH transfer. Amount is wei as a decimal string.</summary>
public sealed record SendTransferRequest(long ChainId, string? To, string? ValueWei, int? Index = 0);

/// <summary>The result of submitting a transfer.</summary>
public sealed record SendTransferResponse(string TxHash, string From, long ChainId, long Nonce, string Status);

/// <summary>The computed cost projection for an intended transfer.</summary>
public sealed record PreviewResponse(
    long ChainId, string From, long Nonce, long GasLimit,
    string MaxFeePerGasWei, string MaxPriorityFeePerGasWei, string ValueWei, string EstimatedTotalCostWei);

/// <summary>The receipt status of a submitted transfer.</summary>
public sealed record TransferStatusResponse(string TxHash, string Status, long? BlockNumber, long? GasUsed);

/// <summary>A problem-details refusal with a machine-readable reason code.</summary>
public sealed record RefusalProblem(string Title, int Status, string Detail, string Reason);
