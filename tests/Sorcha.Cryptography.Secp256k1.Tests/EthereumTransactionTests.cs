// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Numerics;

namespace Sorcha.Cryptography.Secp256k1.Tests;

/// <summary>
/// EIP-1559 (type-2) transaction encoding tests (Feature 182, Phase 4). Anchored two ways without an
/// external tool: (1) a byte-exact signing-payload vector hand-derived from the RLP spec (pins field
/// order + scalar encoding + the empty access list); (2) a sign→recover→address round-trip using the
/// well-known private-key-=-1 identity (pins the signing hash, signature, and <c>yParity = v−27</c>).
/// </summary>
public class EthereumTransactionTests
{
    // Private key = 1 → address 0x7E5F4552091A69125d5DfCb7b8C2659029395Bdf (see EthereumAddressTests).
    private static readonly byte[] PrivateKeyOne = BuildKey(1);
    private const string AddressForKeyOne = "0x7E5F4552091A69125d5DfCb7b8C2659029395Bdf";

    private static byte[] BuildKey(byte last)
    {
        var k = new byte[32];
        k[31] = last;
        return k;
    }

    private static EthereumTransaction SampleTransfer() => new(
        chainId: 1,
        nonce: 0,
        maxPriorityFeePerGas: 1,
        maxFeePerGas: 2,
        gasLimit: 21000,
        to: Convert.FromHexString("1122334455667788990011223344556677889900"),
        value: 100,
        data: ReadOnlySpan<byte>.Empty);

    [Fact]
    public void BuildSigningPayload_KnownFields_MatchesHandDerivedRlpVector()
    {
        // 0x02 ‖ rlp([1, 0, 1, 2, 21000, <20-byte to>, 100, "", []]).
        // Payload = 31 bytes → list prefix 0xdf. Derived byte-for-byte from the RLP spec.
        var expected =
            "02df0180010282520894" +
            "1122334455667788990011223344556677889900" +
            "6480c0";

        Convert.ToHexStringLower(SampleTransfer().BuildSigningPayload()).Should().Be(expected);
    }

    [Fact]
    public void SignRecoverRoundTrip_RecoversTheSignerAddress()
    {
        var tx = SampleTransfer();
        var digest = tx.SigningHash();

        var sig = Secp256k1Signer.SignRecoverable(digest, PrivateKeyOne);
        sig.Length.Should().Be(65);
        sig[64].Should().BeOneOf((byte)27, (byte)28);

        var r = new Org.BouncyCastle.Math.BigInteger(1, sig[..32]);
        var s = new Org.BouncyCastle.Math.BigInteger(1, sig[32..64]);
        var recovered = Secp256k1Recovery.RecoverFromDigest(digest, r, s, sig[64] - 27);

        recovered.Should().NotBeNull();
        EthereumAddress.FromPublicKey(recovered!).Should().Be(AddressForKeyOne);
    }

    [Fact]
    public void AssembleSigned_ProducesType2RawTxAndKeccakHash()
    {
        var tx = SampleTransfer();
        var sig = Secp256k1Signer.SignRecoverable(tx.SigningHash(), PrivateKeyOne);

        var signed = tx.AssembleSigned(sig);

        signed.RawTransactionHex.Should().StartWith("0x02");
        signed.TransactionHash.Should().MatchRegex("^0x[0-9a-f]{64}$");
        // tx hash is keccak of the raw tx bytes.
        var raw = Convert.FromHexString(signed.RawTransactionHex[2..]);
        signed.TransactionHash.Should().Be("0x" + Convert.ToHexStringLower(Keccak256.ComputeHash(raw)));
    }

    [Fact]
    public void AssembleSigned_BadSignatureLength_Throws() =>
        ((Action)(() => SampleTransfer().AssembleSigned(new byte[64])))
            .Should().Throw<ArgumentException>();

    [Fact]
    public void Constructor_Non20ByteRecipient_Throws() =>
        ((Action)(() => new EthereumTransaction(1, 0, 1, 2, 21000, new byte[19], 100, ReadOnlySpan<byte>.Empty)))
            .Should().Throw<ArgumentException>();
}
