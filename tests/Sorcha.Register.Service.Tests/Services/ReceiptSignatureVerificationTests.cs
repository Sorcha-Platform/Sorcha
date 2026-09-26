// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Cryptography.Core;
using Sorcha.Cryptography.Enums;
using Sorcha.Cryptography.Utilities;
using Sorcha.Register.Models;
using Sorcha.Register.Service.Services.Implementation;
using Sorcha.Validator.Core;
using Xunit;

namespace Sorcha.Register.Service.Tests.Services;

/// <summary>
/// #1733 — the receipt and bundle verify endpoints built ReceiptValidator in proof-only mode, so a
/// validator's signature on a receipt was never checked. These use a real ED25519 key and the real
/// canonical signing data a validator signs (#1704), and prove a tampered receipt now FAILS.
/// </summary>
public class ReceiptSignatureVerificationTests
{
    private readonly CryptoModule _crypto = new();

    private static TransactionReceipt Receipt(string txId = "tx-1733") => new()
    {
        ReceiptId = "r-1",
        TransactionId = txId,
        RegisterId = "reg-1733",
        DocketNumber = 6,
        MerkleRoot = "root-6",
        InclusionProof = new MerkleInclusionProof
        {
            TransactionHash = "h", MerkleRoot = "root-6", DocketNumber = 6, ProofPath = [], LeafIndex = 0, TreeSize = 1,
        },
        Signatures = [],
        SealedAt = DateTimeOffset.FromUnixTimeMilliseconds(1_790_000_000_000),
    };

    private async Task<(byte[] PublicKey, byte[] Signature, byte[] Data)> SignedAsync(TransactionReceipt receipt)
    {
        var keys = (await _crypto.GenerateKeySetAsync(WalletNetworks.ED25519)).Value!;
        var data = ReceiptValidator.BuildReceiptSigningData(receipt);
        var signature = (await _crypto.SignAsync(data, (byte)WalletNetworks.ED25519, keys.PrivateKey.Key!)).Value!;
        return (keys.PublicKey.Key!, signature, data);
    }

    [Fact]
    public async Task AGenuineSignature_Verifies()
    {
        var (publicKey, signature, data) = await SignedAsync(Receipt());

        ReceiptSignatureVerification.Create(_crypto)(publicKey, data, signature, "ED25519").Should().BeTrue();
    }

    [Fact]
    public async Task ATamperedSignature_Fails()
    {
        var (publicKey, signature, data) = await SignedAsync(Receipt());
        signature[0] ^= 0xFF;

        ReceiptSignatureVerification.Create(_crypto)(publicKey, data, signature, "ED25519").Should().BeFalse();
    }

    [Fact]
    public async Task ASignatureOverADifferentReceipt_Fails()
    {
        var (publicKey, signature, _) = await SignedAsync(Receipt("tx-original"));
        var forged = ReceiptValidator.BuildReceiptSigningData(Receipt("tx-forged"));

        ReceiptSignatureVerification.Create(_crypto)(publicKey, forged, signature, "ED25519").Should().BeFalse();
    }

    [Fact]
    public async Task AnotherKey_Fails()
    {
        var (_, signature, data) = await SignedAsync(Receipt());
        var other = (await _crypto.GenerateKeySetAsync(WalletNetworks.ED25519)).Value!.PublicKey.Key!;

        ReceiptSignatureVerification.Create(_crypto)(other, data, signature, "ED25519").Should().BeFalse();
    }

    [Theory]
    [InlineData("NOT-AN-ALGORITHM")]
    [InlineData("")]
    public async Task AnUnknownAlgorithm_FailsClosed(string algorithm)
    {
        var (publicKey, signature, data) = await SignedAsync(Receipt());

        ReceiptSignatureVerification.Create(_crypto)(publicKey, data, signature, algorithm).Should().BeFalse();
    }

    [Fact]
    public async Task AMissingKey_FailsClosed()
    {
        // The bundle ships an empty key for a signer absent from the roster.
        var (_, signature, data) = await SignedAsync(Receipt());

        ReceiptSignatureVerification.Create(_crypto)([], data, signature, "ED25519").Should().BeFalse();
    }

    [Fact]
    public async Task TheValidator_NowRejectsATamperedReceipt_ThatProofOnlyModeAccepted()
    {
        // The counterfactual: the constructor the endpoints USED passes a tampered signature.
        var receipt = Receipt();
        var (publicKey, signature, _) = await SignedAsync(receipt);
        signature[0] ^= 0xFF;
        var signed = receipt with
        {
            Signatures = [new ReceiptSignature { ValidatorAddress = "v", SignatureValue = signature, Algorithm = "ED25519", SignedAt = DateTimeOffset.UtcNow }],
        };
        var key = Convert.ToBase64String(publicKey);
        var proof = new InclusionProofValidator(new Sorcha.Cryptography.Core.HashProvider());

        new ReceiptValidator(proof).Verify(signed, key).SignatureCheckSkipped.Should().BeTrue("proof-only mode never looks");
        new ReceiptValidator(proof, ReceiptSignatureVerification.Create(_crypto)).Verify(signed, key).SignatureValid.Should().BeFalse();
    }
}
