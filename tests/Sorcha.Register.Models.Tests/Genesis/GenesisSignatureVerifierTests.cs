// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Sorcha.Register.Models.Constants;
using Sorcha.Register.Models.Genesis;

namespace Sorcha.Register.Models.Tests.Genesis;

public class GenesisSignatureVerifierTests
{
    [Fact]
    public void ComputeGenesisTxId_IsDeterministic()
    {
        var txId1 = GenesisSignatureVerifier.ComputeGenesisTxId();
        var txId2 = GenesisSignatureVerifier.ComputeGenesisTxId();

        txId1.Should().Be(txId2);
    }

    [Fact]
    public void ComputeGenesisTxId_MatchesExpectedFormat()
    {
        var txId = GenesisSignatureVerifier.ComputeGenesisTxId();

        txId.Should().MatchRegex("^[0-9a-f]{64}$");
    }

    [Fact]
    public void ComputeGenesisTxId_DerivedFromSystemRegisterId()
    {
        var input = $"genesis-{SystemRegisterConstants.SystemRegisterId}";
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();

        var txId = GenesisSignatureVerifier.ComputeGenesisTxId();

        txId.Should().Be(expected);
    }

    [Fact]
    public void ExtractVerificationData_ReturnsCorrectHash()
    {
        var genesis = CreateValidGenesis();

        var data = GenesisSignatureVerifier.ExtractVerificationData(genesis);

        var tx = genesis.GenesisTransaction;
        var expectedInput = $"{tx.TxId}:{tx.PayloadHash}";
        var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedInput));

        data.SignedDataHash.Should().BeEquivalentTo(expectedHash);
        data.Algorithm.Should().Be("ED25519");
        data.Signature.Should().HaveCount(64);
        data.PublicKey.Should().HaveCount(32);
    }

    [Fact]
    public void ExtractVerificationData_ThrowsOnNull()
    {
        var act = () => GenesisSignatureVerifier.ExtractVerificationData(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ValidateStructure_ValidGenesis_ReturnsNoErrors()
    {
        var genesis = CreateValidGenesis();

        var errors = GenesisSignatureVerifier.ValidateStructure(genesis);

        errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateStructure_WrongVersion_ReturnsError()
    {
        var genesis = CreateValidGenesis();
        genesis.Version = 99;

        var errors = GenesisSignatureVerifier.ValidateStructure(genesis);

        errors.Should().Contain(e => e.Contains("Unsupported version"));
    }

    [Fact]
    public void ValidateStructure_EmptyNetworkId_ReturnsError()
    {
        var genesis = CreateValidGenesis();
        genesis.NetworkId = "";

        var errors = GenesisSignatureVerifier.ValidateStructure(genesis);

        errors.Should().Contain(e => e.Contains("NetworkId"));
    }

    [Fact]
    public void ValidateStructure_WrongTxId_ReturnsError()
    {
        var genesis = CreateValidGenesis();
        genesis.GenesisTransaction.TxId = "wrong-tx-id";

        var errors = GenesisSignatureVerifier.ValidateStructure(genesis);

        errors.Should().Contain(e => e.Contains("TxId mismatch"));
    }

    [Fact]
    public void ValidateStructure_PayloadHashMismatch_ReturnsError()
    {
        var genesis = CreateValidGenesis();
        genesis.GenesisTransaction.PayloadHash = "0000000000000000000000000000000000000000000000000000000000000000";

        var errors = GenesisSignatureVerifier.ValidateStructure(genesis);

        errors.Should().Contain(e => e.Contains("PayloadHash mismatch"));
    }

    [Fact]
    public void ValidateStructure_FingerprintMismatch_ReturnsError()
    {
        var genesis = CreateValidGenesis();
        genesis.GenesisPublicKeyFingerprint = "00000000000000000000000000000000";

        var errors = GenesisSignatureVerifier.ValidateStructure(genesis);

        errors.Should().Contain(e => e.Contains("Fingerprint mismatch"));
    }

    [Fact]
    public void MatchesFingerprint_MatchingKey_ReturnsTrue()
    {
        var publicKey = new byte[32];
        Array.Fill(publicKey, (byte)0xAB);
        var fingerprint = GenesisFileLoader.ComputeFingerprint(publicKey);

        GenesisSignatureVerifier.MatchesFingerprint(publicKey, fingerprint).Should().BeTrue();
    }

    [Fact]
    public void MatchesFingerprint_DifferentKey_ReturnsFalse()
    {
        var publicKey = new byte[32];
        Array.Fill(publicKey, (byte)0xAB);

        GenesisSignatureVerifier.MatchesFingerprint(publicKey, "00000000000000000000000000000000").Should().BeFalse();
    }

    private static SystemRegisterGenesis CreateValidGenesis()
    {
        var publicKey = new byte[32];
        Array.Fill(publicKey, (byte)0x01);

        var payload = Encoding.UTF8.GetBytes("{\"registerId\":\"test\"}");
        var payloadBase64 = Convert.ToBase64String(payload);
        var payloadHash = Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

        return new SystemRegisterGenesis
        {
            Version = 1,
            NetworkId = "test-network",
            GenesisTransaction = new GenesisTransactionData
            {
                TxId = GenesisSignatureVerifier.ComputeGenesisTxId(),
                Payload = payloadBase64,
                PayloadHash = payloadHash,
                Signature = new GenesisSignature
                {
                    PublicKey = Convert.ToBase64String(publicKey),
                    SignatureValue = Convert.ToBase64String(new byte[64]),
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            },
            ValidatorRoster = new ValidatorRoster
            {
                Validators =
                [
                    new ValidatorRosterEntry
                    {
                        ValidatorId = "test-validator",
                        PublicKey = Convert.ToBase64String(publicKey),
                        Algorithm = SignatureAlgorithm.ED25519,
                        DerivationContext = "sorcha:docket-signing",
                        Status = ValidatorKeyStatus.Active,
                        AuthorizedAt = DateTimeOffset.UtcNow
                    }
                ],
                RequiredSignatures = 1,
                Version = 1
            },
            GenesisPublicKeyFingerprint = GenesisFileLoader.ComputeFingerprint(publicKey)
        };
    }
}
