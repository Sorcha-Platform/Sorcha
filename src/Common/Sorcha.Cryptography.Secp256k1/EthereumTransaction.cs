// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Numerics;

namespace Sorcha.Cryptography.Secp256k1;

/// <summary>The signed, broadcast-ready product of <see cref="EthereumTransaction.AssembleSigned"/>.</summary>
/// <param name="RawTransactionHex">The <c>0x02…</c> signed raw transaction for <c>eth_sendRawTransaction</c>.</param>
/// <param name="TransactionHash">The <c>0x…</c> keccak-256 hash of the signed raw transaction.</param>
public sealed record SignedTransaction(string RawTransactionHex, string TransactionHash);

/// <summary>
/// An EIP-1559 (type-2 / EIP-2718) Ethereum transaction (Feature 182, Phase 4). Builds the signing payload
/// and assembles the signed raw transaction using the pure-managed <see cref="Rlp"/> encoder and
/// <see cref="Keccak256"/>. Native-transfer scope: <see cref="Data"/> is empty and the access list is empty.
/// Pure-managed; WASM-safe by construction, but wired server-side only (value-moving custody).
/// </summary>
public sealed class EthereumTransaction
{
    private const byte Type2 = 0x02;

    /// <summary>Target chain id.</summary>
    public long ChainId { get; }

    /// <summary>Sender account nonce.</summary>
    public long Nonce { get; }

    /// <summary>EIP-1559 max priority fee per gas (the tip), in wei.</summary>
    public BigInteger MaxPriorityFeePerGas { get; }

    /// <summary>EIP-1559 max fee per gas, in wei.</summary>
    public BigInteger MaxFeePerGas { get; }

    /// <summary>Gas limit.</summary>
    public long GasLimit { get; }

    /// <summary>Recipient — exactly 20 bytes.</summary>
    public byte[] To { get; }

    /// <summary>Transfer amount, in wei.</summary>
    public BigInteger Value { get; }

    /// <summary>Call data — empty for a native transfer this phase.</summary>
    public byte[] Data { get; }

    /// <summary>Construct a fully-specified type-2 transaction.</summary>
    /// <exception cref="ArgumentException">A field is out of range (e.g. a non-20-byte recipient).</exception>
    public EthereumTransaction(
        long chainId, long nonce, BigInteger maxPriorityFeePerGas, BigInteger maxFeePerGas,
        long gasLimit, ReadOnlySpan<byte> to, BigInteger value, ReadOnlySpan<byte> data)
    {
        if (chainId <= 0) throw new ArgumentException("chainId must be positive.", nameof(chainId));
        if (nonce < 0) throw new ArgumentException("nonce must be non-negative.", nameof(nonce));
        if (gasLimit <= 0) throw new ArgumentException("gasLimit must be positive.", nameof(gasLimit));
        if (to.Length != 20) throw new ArgumentException("Recipient must be 20 bytes.", nameof(to));
        if (maxPriorityFeePerGas.Sign < 0 || maxFeePerGas.Sign < 0 || value.Sign < 0)
            throw new ArgumentException("Fees and value must be non-negative.");

        ChainId = chainId;
        Nonce = nonce;
        MaxPriorityFeePerGas = maxPriorityFeePerGas;
        MaxFeePerGas = maxFeePerGas;
        GasLimit = gasLimit;
        To = to.ToArray();
        Value = value;
        Data = data.ToArray();
    }

    /// <summary>
    /// The EIP-1559 signing payload: <c>0x02 ‖ rlp([chainId, nonce, maxPriorityFeePerGas, maxFeePerGas,
    /// gasLimit, to, value, data, accessList=[]])</c>. Its keccak-256 hash (<see cref="SigningHash"/>) is
    /// what the secp256k1 key signs.
    /// </summary>
    public byte[] BuildSigningPayload()
    {
        var list = Rlp.EncodeList(
            Rlp.EncodeScalar(ChainId),
            Rlp.EncodeScalar(Nonce),
            Rlp.EncodeScalar(MaxPriorityFeePerGas),
            Rlp.EncodeScalar(MaxFeePerGas),
            Rlp.EncodeScalar(GasLimit),
            Rlp.EncodeString(To),
            Rlp.EncodeScalar(Value),
            Rlp.EncodeString(Data),
            Rlp.EncodeList()); // empty access list → 0xc0

        return Prepend(Type2, list);
    }

    /// <summary>The keccak-256 hash of the signing payload — the 32-byte digest the signer signs.</summary>
    public byte[] SigningHash() => Keccak256.ComputeHash(BuildSigningPayload());

    /// <summary>
    /// Assemble the signed raw transaction from a 65-byte recoverable signature (<c>r‖s‖v</c>, as produced
    /// by <see cref="Secp256k1Signer.SignRecoverable"/> with <c>v ∈ {27,28}</c>). Typed transactions carry
    /// the signature parity, so <c>yParity = v − 27</c>. Emits
    /// <c>0x02 ‖ rlp([…the 9 fields…, yParity, r, s])</c> and its keccak-256 hash.
    /// </summary>
    /// <exception cref="ArgumentException">The signature is not 65 bytes or <c>v</c> is not 27/28.</exception>
    public SignedTransaction AssembleSigned(ReadOnlySpan<byte> recoverableSignature)
    {
        if (recoverableSignature.Length != 65)
        {
            throw new ArgumentException("Recoverable signature must be 65 bytes (r‖s‖v).", nameof(recoverableSignature));
        }

        var r = new BigInteger(recoverableSignature[..32], isUnsigned: true, isBigEndian: true);
        var s = new BigInteger(recoverableSignature[32..64], isUnsigned: true, isBigEndian: true);
        var v = recoverableSignature[64];
        if (v is not (27 or 28))
        {
            throw new ArgumentException("Recovery id v must be 27 or 28.", nameof(recoverableSignature));
        }

        var yParity = v - 27;

        var list = Rlp.EncodeList(
            Rlp.EncodeScalar(ChainId),
            Rlp.EncodeScalar(Nonce),
            Rlp.EncodeScalar(MaxPriorityFeePerGas),
            Rlp.EncodeScalar(MaxFeePerGas),
            Rlp.EncodeScalar(GasLimit),
            Rlp.EncodeString(To),
            Rlp.EncodeScalar(Value),
            Rlp.EncodeString(Data),
            Rlp.EncodeList(),
            Rlp.EncodeScalar(yParity),
            Rlp.EncodeScalar(r),
            Rlp.EncodeScalar(s));

        var raw = Prepend(Type2, list);
        var hash = Keccak256.ComputeHash(raw);
        return new SignedTransaction("0x" + Convert.ToHexStringLower(raw), "0x" + Convert.ToHexStringLower(hash));
    }

    private static byte[] Prepend(byte prefix, byte[] rest)
    {
        var result = new byte[rest.Length + 1];
        result[0] = prefix;
        Array.Copy(rest, 0, result, 1, rest.Length);
        return result;
    }
}
