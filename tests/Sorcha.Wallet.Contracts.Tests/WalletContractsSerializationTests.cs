// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
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

    /// <summary>
    /// Issue #1159 — the source-generated context existed and was test-validated, but was registered
    /// in NO host, so every Wallet contract actually (de)serialised by reflection: correct, but not
    /// trim-safe. The main casualty is the Blazor WASM client, where a Release publish trims the
    /// reflection metadata the fallback depends on.
    ///
    /// <para>It is registered on <see cref="SorchaJson"/> rather than per host. That is deliberate and
    /// slightly awkward — a generic serialisation helper naming a domain contract — but per-host
    /// chaining could not have covered the main beneficiary: <see cref="SorchaJson.Options"/> is the
    /// instance UI clients deserialise with and it is <c>MakeReadOnly()</c>, so nothing can chain a
    /// resolver into it after construction. Registering inside <c>Configure</c> reaches both that
    /// static and every host that calls it, and no host can be forgotten. Cycle-safe: both projects
    /// are dependency-free leaves.</para>
    /// </summary>
    [Fact]
    public void SorchaJsonOptions_ResolveWalletContractsThroughSourceGeneration()
    {
        Sorcha.Serialization.SorchaJson.Options.TypeInfoResolverChain
            .Should().Contain(r => r is WalletContractsJsonContext,
                "the platform options must resolve Wallet contracts via source generation, not reflection");
    }

    /// <summary>
    /// Issue #1159 — chaining a resolver must not cost every OTHER type its metadata.
    ///
    /// <para>A <see cref="JsonSerializerOptions"/> carries an implicit default reflection resolver only
    /// while its chain is untouched; inserting anything removes it. The first cut of this change
    /// inserted the Wallet context alone, which left the platform options able to resolve seven types
    /// and nothing else — <c>Sorcha.UI.ContractTests</c> went 18-red with "JsonTypeInfo metadata for
    /// type 'InboxSeverity' was not provided". This pins the fallback so the one-line version cannot
    /// come back.</para>
    /// </summary>
    [Fact]
    public void SorchaJsonOptions_StillResolveTypesOutsideTheWalletContracts()
    {
        // A type the Wallet context knows nothing about must still round-trip.
        var json = JsonSerializer.Serialize(
            new Dictionary<string, object> { ["kind"] = "unrelated", ["n"] = 42 },
            Sorcha.Serialization.SorchaJson.Options);

        json.Should().Contain("unrelated");

        Sorcha.Serialization.SorchaJson.Options.TypeInfoResolverChain
            .Should().Contain(r => r is DefaultJsonTypeInfoResolver,
                "the default reflection resolver must remain in the chain as the fallback for every "
                + "type the source-generated context does not cover");
    }

    /// <summary>
    /// Issue #1159 — the load-bearing caveat. Chaining the resolver must NOT disturb the wire names:
    /// the contracts go out camelCase, and a regression here would silently break every Wallet client.
    /// </summary>
    [Fact]
    public void SorchaJsonOptions_StillEmitCamelCaseWireNames()
    {
        var json = JsonSerializer.Serialize(SampleWallet(), Sorcha.Serialization.SorchaJson.Options);

        json.Should().Contain("\"address\"").And.Contain("\"publicKey\"").And.Contain("\"createdAt\"");
        json.Should().NotContain("\"Address\"").And.NotContain("\"PublicKey\"");

        var round = JsonSerializer.Deserialize<WalletDto>(json, Sorcha.Serialization.SorchaJson.Options);
        round!.Address.Should().Be("ws1qexample");
        round.Metadata.Should().ContainKey("k");
    }

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
