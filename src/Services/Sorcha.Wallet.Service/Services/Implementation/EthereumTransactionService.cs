// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Diagnostics.Metrics;
using System.Numerics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Sorcha.ServiceClients.Evm;
using Sorcha.Wallet.Core.Domain.ValueObjects;
using Sorcha.Wallet.Core.Services.Interfaces;
using Sorcha.Wallet.Service.Configuration;
using Sorcha.Wallet.Service.Services.Interfaces;

namespace Sorcha.Wallet.Service.Services.Implementation;

/// <summary>
/// Native ETH transacting orchestrator (Feature 182, Phase 4). Enforces policy, gathers chain parameters,
/// signs via <see cref="IEthereumTransactionSigner"/>, and broadcasts via <see cref="IEvmRpcClient"/>. The
/// signer (Wallet Core) never sees the RPC client; this service (Wallet Service) never sees the private key.
/// Fail-closed throughout — nothing is broadcast unless every prior step succeeds.
/// </summary>
public sealed class EthereumTransactionService : IEthereumTransactionService
{
    private static readonly Meter Meter = new("Sorcha.Ethereum");
    private static readonly Counter<long> Submitted = Meter.CreateCounter<long>("sorcha_eth_tx_submitted_total");
    private static readonly Counter<long> Rejected = Meter.CreateCounter<long>("sorcha_eth_tx_rejected_total");
    private static readonly Counter<long> BroadcastFailed = Meter.CreateCounter<long>("sorcha_eth_tx_broadcast_failed_total");

    private readonly IEthereumIdentityService _identity;
    private readonly IEthereumTransactionSigner _signer;
    private readonly IEvmRpcClient _rpc;
    private readonly EthereumTransactionOptions _options;
    private readonly ILogger<EthereumTransactionService> _logger;

    public EthereumTransactionService(
        IEthereumIdentityService identity,
        IEthereumTransactionSigner signer,
        IEvmRpcClient rpc,
        IOptions<EthereumTransactionOptions> options,
        ILogger<EthereumTransactionService> logger)
    {
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<PreviewOutcome> PreviewAsync(string walletAddress, long chainId, string to, BigInteger valueWei, int index, CancellationToken ct = default)
    {
        var (prep, reason) = await PrepareAsync(walletAddress, chainId, to, valueWei, index, ct).ConfigureAwait(false);
        if (prep is null)
        {
            Reject(reason, chainId);
            return PreviewOutcome.Refused(reason);
        }

        return new PreviewOutcome(true, TransferRefusalReason.None, prep);
    }

    /// <inheritdoc />
    public async Task<SendOutcome> SendAsync(string walletAddress, long chainId, string to, BigInteger valueWei, int index, CancellationToken ct = default)
    {
        var (prep, reason) = await PrepareAsync(walletAddress, chainId, to, valueWei, index, ct).ConfigureAwait(false);
        if (prep is null)
        {
            Reject(reason, chainId);
            return SendOutcome.Refused(reason);
        }

        SignedEthereumTransaction signed;
        try
        {
            var request = new EthereumTransactionRequest(
                chainId, to, valueWei, prep.Nonce, prep.GasLimit, prep.MaxFeePerGasWei, prep.MaxPriorityFeePerGasWei);
            signed = await _signer.SignTransactionAsync(walletAddress, request, index, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ethereum transfer signing failed for wallet {Wallet} on chain {ChainId}", walletAddress, chainId);
            Reject(TransferRefusalReason.InvalidAddress, chainId);
            return SendOutcome.Refused(TransferRefusalReason.InvalidAddress);
        }

        var broadcast = await _rpc.SendRawTransactionAsync(chainId, signed.RawTxHex, ct).ConfigureAwait(false);
        if (broadcast.Outcome != EvmRpcOutcome.Ok || string.IsNullOrEmpty(broadcast.TxHash))
        {
            _logger.LogError("Ethereum broadcast failed for wallet {Wallet} on chain {ChainId} ({Outcome})", walletAddress, chainId, broadcast.Outcome);
            BroadcastFailed.Add(1, new KeyValuePair<string, object?>("chainId", chainId));
            return SendOutcome.Refused(TransferRefusalReason.BroadcastFailed);
        }

        Submitted.Add(1, new KeyValuePair<string, object?>("chainId", chainId));
        _logger.LogInformation("Ethereum transfer submitted: wallet {Wallet} chain {ChainId} nonce {Nonce} tx {TxHash}",
            walletAddress, chainId, prep.Nonce, broadcast.TxHash);
        return new SendOutcome(true, TransferRefusalReason.None, broadcast.TxHash, prep.From, chainId, prep.Nonce);
    }

    /// <inheritdoc />
    public async Task<StatusOutcome> GetStatusAsync(long chainId, string txHash, CancellationToken ct = default)
    {
        if (!_options.EnabledChainIds.Contains(chainId))
        {
            return StatusOutcome.Refused(TransferRefusalReason.ChainNotEnabled);
        }

        var receipt = await _rpc.GetTransactionReceiptAsync(chainId, txHash, ct).ConfigureAwait(false);
        return receipt.Outcome switch
        {
            EvmRpcOutcome.Ok when receipt.Receipt is null => new StatusOutcome(true, TransferRefusalReason.None, "pending", null, null),
            EvmRpcOutcome.Ok => new StatusOutcome(true, TransferRefusalReason.None,
                receipt.Receipt!.Success ? "success" : "reverted", receipt.Receipt.BlockNumber, receipt.Receipt.GasUsed),
            _ => StatusOutcome.Refused(TransferRefusalReason.RpcError)
        };
    }

    /// <summary>Policy gate + chain-parameter gathering shared by send and preview.</summary>
    private async Task<(TransferPreparation? Prep, TransferRefusalReason Reason)> PrepareAsync(
        string walletAddress, long chainId, string to, BigInteger valueWei, int index, CancellationToken ct)
    {
        // --- policy (validate before any network call) ---
        if (!_options.EnabledChainIds.Contains(chainId))
            return (null, TransferRefusalReason.ChainNotEnabled);
        if (!EthereumTransactionOptions.IsKnownTestnet(chainId) && !_options.AllowMainnet)
            return (null, TransferRefusalReason.MainnetNotAllowed);
        if (valueWei.Sign < 0)
            return (null, TransferRefusalReason.InvalidAmount);
        if (_options.MaxValue > 0 && valueWei > _options.MaxValue)
            return (null, TransferRefusalReason.ValueOverCap);
        if (!IsValidAddress(to))
            return (null, TransferRefusalReason.InvalidAddress);

        string from;
        try
        {
            from = await _identity.GetAddressAsync(walletAddress, index, ct).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            return (null, TransferRefusalReason.WalletNotFound);
        }

        // --- live chain parameters ---
        var nonce = await _rpc.GetTransactionCountAsync(chainId, from, ct).ConfigureAwait(false);
        if (nonce.Outcome != EvmRpcOutcome.Ok)
            return (null, TransferRefusalReason.RpcError);

        var baseFee = await _rpc.GetBaseFeePerGasAsync(chainId, ct).ConfigureAwait(false);
        if (baseFee.Outcome != EvmRpcOutcome.Ok)
            return (null, TransferRefusalReason.RpcError);

        // Priority fee: use the node suggestion, else fall back to the configured default (not a failure).
        var priorityResult = await _rpc.GetMaxPriorityFeePerGasAsync(chainId, ct).ConfigureAwait(false);
        var priorityFee = priorityResult.Outcome == EvmRpcOutcome.Ok ? priorityResult.Value : _options.DefaultPriorityFee;

        var maxFee = baseFee.Value * 2 + priorityFee;
        if (_options.MaxFeePerGas > 0 && maxFee > _options.MaxFeePerGas)
            return (null, TransferRefusalReason.FeeOverCeiling);

        var gas = await _rpc.EstimateGasAsync(chainId, from, to, ToHexQuantity(valueWei), "0x", ct).ConfigureAwait(false);
        if (gas.Outcome != EvmRpcOutcome.Ok)
            return (null, TransferRefusalReason.EstimateFailed);

        var gasLimit = (long)gas.Value;
        var totalCost = valueWei + gas.Value * maxFee;

        return (new TransferPreparation(
            chainId, from, (long)nonce.Value, gasLimit, maxFee, priorityFee, valueWei, totalCost), TransferRefusalReason.None);
    }

    private void Reject(TransferRefusalReason reason, long chainId) =>
        Rejected.Add(1,
            new KeyValuePair<string, object?>("reason", ReasonCode(reason)),
            new KeyValuePair<string, object?>("chainId", chainId));

    private static bool IsValidAddress(string to)
    {
        if (string.IsNullOrWhiteSpace(to)) return false;
        var hex = to.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? to[2..] : to;
        return hex.Length == 40 && hex.All(Uri.IsHexDigit);
    }

    private static string ToHexQuantity(BigInteger value)
    {
        if (value.IsZero) return "0x0";
        var hex = value.ToString("x");
        return "0x" + hex.TrimStart('0');
    }

    /// <summary>The machine-readable refusal code shared with the API contract.</summary>
    public static string ReasonCode(TransferRefusalReason reason) => reason switch
    {
        TransferRefusalReason.ChainNotEnabled => "chain-not-enabled",
        TransferRefusalReason.MainnetNotAllowed => "mainnet-not-allowed",
        TransferRefusalReason.ValueOverCap => "value-over-cap",
        TransferRefusalReason.FeeOverCeiling => "fee-over-ceiling",
        TransferRefusalReason.InvalidAddress => "invalid-address",
        TransferRefusalReason.InvalidAmount => "invalid-amount",
        TransferRefusalReason.RpcError => "rpc-error",
        TransferRefusalReason.EstimateFailed => "estimate-failed",
        TransferRefusalReason.BroadcastFailed => "broadcast-failed",
        TransferRefusalReason.WalletNotFound => "wallet-not-found",
        _ => "none"
    };
}
