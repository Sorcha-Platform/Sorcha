// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Sorcha.Wallet.Contracts.Models;
using Sorcha.Wallet.Contracts.Serialization;

namespace Sorcha.Wallet.Contracts.Tests;

/// <summary>
/// Wire-compatibility guard for the consolidated Wallet contracts. Every contract type must
/// round-trip through the source-generated <see cref="WalletContractsJsonContext"/> and serialize
/// with the platform's camelCase property names — the property the clean-break retirement depends on.
/// </summary>
public class WalletContractsSerializationTests
{
    // Use the context's own baked options (camelCase + source-gen metadata) — the self-contained usage.
    // Hosts that instead chain the resolver into their existing options inherit those options' camelCase.
    private static readonly JsonSerializerOptions Options = WalletContractsJsonContext.Default.Options;

    private static WalletDto SampleWallet() => new()
    {
        Address = "ws1qexample",
        Name = "Primary",
        PublicKey = "0xabc",
        Algorithm = "ED25519",
        Status = "Active",
        Owner = "user-1",
        Tenant = "tenant-1",
        CreatedAt = new DateTime(2026, 7, 12, 10, 0, 0, DateTimeKind.Utc),
        UpdatedAt = new DateTime(2026, 7, 12, 11, 0, 0, DateTimeKind.Utc),
        SigningMode = "KmsResident",
        KmsKeyId = "kms-key-1",
        Metadata = new Dictionary<string, string> { ["k"] = "v" },
    };

    private static WalletAddressDto SampleAddress() => new()
    {
        Id = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        ParentWalletAddress = "ws1qexample",
        Address = "ws1qchild",
        PublicKey = "cHVia2V5",
        DerivationPath = "m/44'/0'/0'/0/5",
        Index = 5,
        Account = 0,
        IsChange = false,
        Label = "salary",
        Notes = "note",
        Tags = "tag1,tag2",
        IsUsed = true,
        CreatedAt = new DateTime(2026, 7, 12, 10, 0, 0, DateTimeKind.Utc),
        FirstUsedAt = new DateTime(2026, 7, 12, 10, 30, 0, DateTimeKind.Utc),
        LastUsedAt = new DateTime(2026, 7, 12, 11, 0, 0, DateTimeKind.Utc),
        Metadata = new Dictionary<string, string> { ["m"] = "1" },
    };

    [Fact]
    public void WalletDto_RoundTrips_ThroughSourceGenContext()
    {
        var original = SampleWallet();

        var json = JsonSerializer.Serialize(original, Options);
        var back = JsonSerializer.Deserialize<WalletDto>(json, Options);

        back.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void WalletDto_Serializes_WithCamelCaseNames()
    {
        var json = JsonSerializer.Serialize(SampleWallet(), Options);

        json.Should().Contain("\"address\"")
            .And.Contain("\"signingMode\"")
            .And.Contain("\"kmsKeyId\"");
        json.Should().NotContain("\"Address\"");
    }

    [Fact]
    public void WalletAddressDto_RoundTrips_ThroughSourceGenContext()
    {
        var original = SampleAddress();

        var json = JsonSerializer.Serialize(original, Options);
        var back = JsonSerializer.Deserialize<WalletAddressDto>(json, Options);

        back.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void AddressListResponse_RoundTrips_WithNestedAddresses()
    {
        var original = new AddressListResponse
        {
            WalletAddress = "ws1qexample",
            Addresses = [SampleAddress()],
            TotalCount = 100,
            Page = 1,
            PageSize = 50,
        };

        var json = JsonSerializer.Serialize(original, Options);
        var back = JsonSerializer.Deserialize<AddressListResponse>(json, Options);

        back.Should().BeEquivalentTo(original, o => o.Excluding(x => x.HasMore));
    }

    [Fact]
    public void CreateWalletRequest_RoundTrips_ThroughSourceGenContext()
    {
        var original = new CreateWalletRequest
        {
            Name = "Primary",
            Algorithm = "ED25519",
            WordCount = 24,
            Passphrase = "pp",
            PqcAlgorithm = "ML-DSA-65",
            EnableHybrid = true,
            SigningMode = "Local",
            Tags = new Dictionary<string, string> { ["t"] = "1" },
        };

        var json = JsonSerializer.Serialize(original, Options);
        var back = JsonSerializer.Deserialize<CreateWalletRequest>(json, Options);

        back.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void CreateWalletResponse_RoundTrips_WithNestedWallet()
    {
        var original = new CreateWalletResponse
        {
            Wallet = SampleWallet(),
            MnemonicWords = ["abandon", "ability", "able"],
            PqcWalletAddress = "ws2qexample",
            PqcAlgorithm = "ML-DSA-65",
        };

        var json = JsonSerializer.Serialize(original, Options);
        var back = JsonSerializer.Deserialize<CreateWalletResponse>(json, Options);

        back.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void SignTransactionRequest_RoundTrips_WithHybridFields()
    {
        var original = new SignTransactionRequest
        {
            TransactionData = "ZGF0YQ==",
            DerivationPath = "sorcha:register-attestation",
            IsPreHashed = true,
            HybridMode = true,
            PqcWalletAddress = "ws2qexample",
        };

        var json = JsonSerializer.Serialize(original, Options);
        var back = JsonSerializer.Deserialize<SignTransactionRequest>(json, Options);

        back.Should().BeEquivalentTo(original);
        json.Should().Contain("\"hybridMode\"").And.Contain("\"pqcWalletAddress\"");
    }

    [Fact]
    public void SignTransactionResponse_RoundTrips_ThroughSourceGenContext()
    {
        var original = new SignTransactionResponse
        {
            Signature = "c2ln",
            SignedBy = "ws1qexample",
            SignedAt = new DateTime(2026, 7, 12, 12, 0, 0, DateTimeKind.Utc),
            PublicKey = "cHVi",
        };

        var json = JsonSerializer.Serialize(original, Options);
        var back = JsonSerializer.Deserialize<SignTransactionResponse>(json, Options);

        back.Should().BeEquivalentTo(original);
    }
}
