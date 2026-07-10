// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Sorcha.ServiceClients.Evm;

/// <summary>An active ERC-1056 signing delegate (an address) — <c>veriKey</c> (assertion) or <c>sigAuth</c> (authentication).</summary>
public sealed record Erc1056Delegate(string DelegateType, string Address);

/// <summary>An active ERC-1056 published-key attribute (<c>did/pub/{algo}/{purpose}/…</c>).</summary>
public sealed record Erc1056KeyAttribute(string Algorithm, string Purpose, byte[] Key);

/// <summary>
/// The signing-relevant ERC-1056 state read for a <c>did:ethr</c> identity (Feature 179). Exactly one
/// of <see cref="RpcError"/> (configured-but-errored → resolver fails closed) or <see cref="NoHistory"/>
/// (no on-chain history / unconfigured → offline default document) may be set; otherwise the owner +
/// active delegates/attributes describe the current document.
/// </summary>
public sealed class Erc1056State
{
    /// <summary>A configured RPC call failed — the resolver MUST fail closed (never a stale default doc).</summary>
    public bool RpcError { get; init; }

    /// <summary>No on-chain history (changed==0) or the chain is unconfigured — use the offline default document.</summary>
    public bool NoHistory { get; init; }

    /// <summary>The current owner address (after rotation).</summary>
    public string OwnerAddress { get; init; } = string.Empty;

    /// <summary>Currently-valid signing delegates (newest-first, unexpired).</summary>
    public IReadOnlyList<Erc1056Delegate> Delegates { get; init; } = [];

    /// <summary>Currently-valid published key attributes (newest-first, unexpired).</summary>
    public IReadOnlyList<Erc1056KeyAttribute> Attributes { get; init; } = [];

    internal static readonly Erc1056State Error = new() { RpcError = true };
    internal static readonly Erc1056State Default = new() { NoHistory = true };
}

/// <summary>
/// Reads ERC-1056 <c>EthereumDIDRegistry</c> state over a read-only <see cref="IEvmRpcClient"/> and folds
/// the event history into the current signing state (Feature 179): <c>changed</c> → <c>identityOwner</c>
/// → walk <c>previousChange</c> collecting unexpired <c>veriKey</c>/<c>sigAuth</c> delegates and
/// <c>did/pub/*</c> key attributes. Newest-first, so a superseded/revoked entry never resurfaces.
/// </summary>
public sealed class Erc1056Registry
{
    private static readonly string ChangedSelector = AbiCodec.Selector("changed(address)");
    private static readonly string OwnerSelector = AbiCodec.Selector("identityOwner(address)");
    private static readonly string TopicOwnerChanged = AbiCodec.EventTopic("DIDOwnerChanged(address,address,uint256)");
    private static readonly string TopicDelegateChanged = AbiCodec.EventTopic("DIDDelegateChanged(address,bytes32,address,uint256,uint256)");
    private static readonly string TopicAttributeChanged = AbiCodec.EventTopic("DIDAttributeChanged(address,bytes32,bytes,uint256,uint256)");

    private static readonly HashSet<string> SigningDelegateTypes = new(StringComparer.Ordinal) { "veriKey", "sigAuth" };
    private static readonly HashSet<string> SigningAlgorithms = new(StringComparer.Ordinal) { "Secp256k1", "Ed25519" };
    private static readonly HashSet<string> SigningPurposes = new(StringComparer.Ordinal) { "veriKey", "sigAuth" };

    private readonly IEvmRpcClient _rpc;
    private readonly EvmRpcOptions _options;
    private readonly ILogger _logger;
    private readonly Func<long> _nowUnixSeconds;

    public Erc1056Registry(IEvmRpcClient rpc, IOptions<EvmRpcOptions> options, ILogger<Erc1056Registry> logger)
        : this(rpc, options.Value, logger, () => DateTimeOffset.UtcNow.ToUnixTimeSeconds())
    {
    }

    /// <summary>Test/internal constructor with an injectable clock and options value.</summary>
    internal Erc1056Registry(IEvmRpcClient rpc, EvmRpcOptions options, ILogger logger, Func<long> nowUnixSeconds)
    {
        _rpc = rpc ?? throw new ArgumentNullException(nameof(rpc));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _nowUnixSeconds = nowUnixSeconds;
    }

    /// <summary>Read the current signing state for a <c>did:ethr</c> identity address on a chain.</summary>
    public async Task<Erc1056State> ReadAsync(long chainId, string identity, CancellationToken ct = default)
    {
        var registry = _options.RegistryFor(chainId);
        var addrArg = Convert.ToHexStringLower(AbiCodec.EncodeAddress(identity));

        // 1. changed(identity) — last-change block; 0 (or unconfigured) → offline default document.
        var changedCall = await _rpc.CallAsync(chainId, registry, ChangedSelector + addrArg, ct).ConfigureAwait(false);
        if (changedCall.Outcome == EvmRpcOutcome.NotConfigured)
        {
            return Erc1056State.Default;
        }

        if (changedCall.Outcome != EvmRpcOutcome.Ok || !TryHexBytes(changedCall.Value, out var changedWord))
        {
            return Erc1056State.Error;
        }

        var lastBlock = (long)AbiCodec.DecodeUInt(changedWord);
        if (lastBlock == 0)
        {
            return Erc1056State.Default;
        }

        // 2. identityOwner(identity) — the current owner (authoritative for rotation).
        var ownerCall = await _rpc.CallAsync(chainId, registry, OwnerSelector + addrArg, ct).ConfigureAwait(false);
        if (ownerCall.Outcome != EvmRpcOutcome.Ok || !TryHexBytes(ownerCall.Value, out var ownerWord) || ownerWord.Length < 32)
        {
            return Erc1056State.Error;
        }

        var owner = AbiCodec.DecodeAddress(ownerWord);

        // 3. Walk previousChange from the last-change block, newest-first.
        var now = _nowUnixSeconds();
        var identityTopic = AbiCodec.Pad32Topic(identity);
        var delegates = new List<Erc1056Delegate>();
        var attributes = new List<Erc1056KeyAttribute>();
        var seenDelegates = new HashSet<string>(StringComparer.Ordinal);
        var seenAttributes = new HashSet<string>(StringComparer.Ordinal);

        var block = lastBlock;
        var hops = 0;
        while (block != 0)
        {
            if (++hops > _options.MaxHistoryHops)
            {
                _logger.LogWarning("did:ethr history for {Identity} on chain {ChainId} exceeded MaxHistoryHops; failing closed", identity, chainId);
                return Erc1056State.Error;
            }

            var logs = await _rpc.GetLogsAsync(chainId, registry, [null, identityTopic], block, ct).ConfigureAwait(false);
            if (logs.Outcome != EvmRpcOutcome.Ok)
            {
                return Erc1056State.Error;
            }

            long previous = 0;
            foreach (var log in logs.Logs!)
            {
                var topic0 = log.Topics.Count > 0 ? log.Topics[0] : string.Empty;
                if (!TryHexBytes(log.Data, out var data))
                {
                    continue;
                }

                try
                {
                    if (string.Equals(topic0, TopicDelegateChanged, StringComparison.OrdinalIgnoreCase))
                    {
                        var type = AbiCodec.DecodeBytes32Ascii(AbiCodec.Word(data, 0));
                        var delegateAddr = AbiCodec.DecodeAddress(AbiCodec.Word(data, 1));
                        var validTo = (long)AbiCodec.DecodeUInt(AbiCodec.Word(data, 2));
                        previous = (long)AbiCodec.DecodeUInt(AbiCodec.Word(data, 3));

                        var key = $"{type}:{delegateAddr}";
                        if (seenDelegates.Add(key) && validTo >= now && SigningDelegateTypes.Contains(type))
                        {
                            delegates.Add(new Erc1056Delegate(type, delegateAddr));
                        }
                    }
                    else if (string.Equals(topic0, TopicAttributeChanged, StringComparison.OrdinalIgnoreCase))
                    {
                        var name = AbiCodec.DecodeBytes32Ascii(AbiCodec.Word(data, 0));
                        var value = AbiCodec.DecodeDynamicBytes(data, 1);
                        var validTo = (long)AbiCodec.DecodeUInt(AbiCodec.Word(data, 2));
                        previous = (long)AbiCodec.DecodeUInt(AbiCodec.Word(data, 3));

                        var key = $"{name}:{Convert.ToHexStringLower(value)}";
                        if (seenAttributes.Add(key) && validTo >= now
                            && TryParseKeyAttribute(name, value, out var attr))
                        {
                            attributes.Add(attr!);
                        }
                    }
                    else if (string.Equals(topic0, TopicOwnerChanged, StringComparison.OrdinalIgnoreCase))
                    {
                        // Owner already resolved via identityOwner; just follow the chain.
                        previous = (long)AbiCodec.DecodeUInt(AbiCodec.Word(data, 1));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to decode an ERC-1056 log for {Identity}; skipping", identity);
                }
            }

            block = previous;
        }

        return new Erc1056State
        {
            OwnerAddress = owner,
            Delegates = delegates,
            Attributes = attributes
        };
    }

    private static bool TryParseKeyAttribute(string name, byte[] value, out Erc1056KeyAttribute? attr)
    {
        attr = null;

        // did/pub/{algo}/{purpose}/{encoding}. The on-chain `value` is the raw key bytes; the encoding
        // segment is an output-representation hint we don't need (we build a JWK from the raw key).
        var parts = name.Split('/');
        if (parts.Length < 4 || parts[0] != "did" || parts[1] != "pub")
        {
            return false; // service entries (did/svc/*) and malformed names are ignored
        }

        var algo = parts[2];
        var purpose = parts[3];
        if (!SigningAlgorithms.Contains(algo) || !SigningPurposes.Contains(purpose) || value.Length == 0)
        {
            return false; // enc / X25519 / unknown / empty are out of scope for signing
        }

        attr = new Erc1056KeyAttribute(algo, purpose, value);
        return true;
    }

    private static bool TryHexBytes(string? hex, out byte[] bytes)
    {
        bytes = [];
        if (string.IsNullOrEmpty(hex))
        {
            return false;
        }

        var h = hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? hex[2..] : hex;
        if (h.Length % 2 != 0)
        {
            return false;
        }

        try
        {
            bytes = Convert.FromHexString(h);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
