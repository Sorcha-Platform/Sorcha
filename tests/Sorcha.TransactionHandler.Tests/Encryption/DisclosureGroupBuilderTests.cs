// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;

using Sorcha.Cryptography.Enums;
using Sorcha.TransactionHandler.Encryption;
using Sorcha.TransactionHandler.Encryption.Models;

namespace Sorcha.TransactionHandler.Tests.Encryption;

/// <summary>
/// Unit tests for <see cref="DisclosureGroupBuilder"/>.
/// Verifies grouping logic that maps disclosed payloads to recipient groups
/// based on identical field sets.
/// </summary>
public class DisclosureGroupBuilderTests
{
    private readonly DisclosureGroupBuilder _sut = new();

    #region Identical fields → single group

    [Fact]
    public void BuildGroups_IdenticalFieldsAndValues_ReturnsOneGroup()
    {
        // Arrange — 3 recipients all get identical fields AND values (homogeneous disclosure)
        var disclosedPayloads = new Dictionary<string, Dictionary<string, object>>
        {
            ["ws1qalice"] = new() { ["name"] = "SharedDoc", ["amount"] = 100 },
            ["ws1qbob"] = new() { ["name"] = "SharedDoc", ["amount"] = 100 },
            ["ws1qcarol"] = new() { ["name"] = "SharedDoc", ["amount"] = 100 }
        };

        var recipients = new[]
        {
            CreateRecipient("ws1qalice"),
            CreateRecipient("ws1qbob"),
            CreateRecipient("ws1qcarol")
        };

        // Act
        var result = _sut.BuildGroups(disclosedPayloads, recipients);

        // Assert — same fields + same values = one group
        result.Should().HaveCount(1);
        result[0].Recipients.Should().HaveCount(3);
        result[0].DisclosedFields.Should().BeEquivalentTo(new[] { "amount", "name" },
            options => options.WithStrictOrdering(),
            because: "fields must be sorted alphabetically");
    }

    [Fact]
    public void BuildGroups_IdenticalFieldsDifferentValues_ReturnsSeparateGroups()
    {
        // Arrange — 3 recipients with same field names but different values (personalized disclosure)
        var disclosedPayloads = new Dictionary<string, Dictionary<string, object>>
        {
            ["ws1qalice"] = new() { ["name"] = "Alice", ["amount"] = 100 },
            ["ws1qbob"] = new() { ["name"] = "Bob", ["amount"] = 200 },
            ["ws1qcarol"] = new() { ["name"] = "Carol", ["amount"] = 300 }
        };

        var recipients = new[]
        {
            CreateRecipient("ws1qalice"),
            CreateRecipient("ws1qbob"),
            CreateRecipient("ws1qcarol")
        };

        // Act
        var result = _sut.BuildGroups(disclosedPayloads, recipients);

        // Assert — same fields but different values = separate groups (no data crossover)
        result.Should().HaveCount(3);
        result.SelectMany(g => g.Recipients).Should().HaveCount(3);
    }

    #endregion

    #region Distinct field sets → multiple groups

    [Fact]
    public void BuildGroups_DistinctFieldSets_ReturnsMultipleGroups()
    {
        // Arrange — 3 recipients with 3 different field sets
        var disclosedPayloads = new Dictionary<string, Dictionary<string, object>>
        {
            ["ws1qalice"] = new() { ["name"] = "Alice" },
            ["ws1qbob"] = new() { ["name"] = "Bob", ["ssn"] = "123-45-6789" },
            ["ws1qcarol"] = new() { ["amount"] = 500 }
        };

        var recipients = new[]
        {
            CreateRecipient("ws1qalice"),
            CreateRecipient("ws1qbob"),
            CreateRecipient("ws1qcarol")
        };

        // Act
        var result = _sut.BuildGroups(disclosedPayloads, recipients);

        // Assert
        result.Should().HaveCount(3);
        result.SelectMany(g => g.Recipients).Should().HaveCount(3);
    }

    #endregion

    #region Single recipient edge case

    [Fact]
    public void BuildGroups_SingleRecipient_ReturnsOneGroupWithOneKey()
    {
        // Arrange
        var disclosedPayloads = new Dictionary<string, Dictionary<string, object>>
        {
            ["ws1qalice"] = new() { ["name"] = "Alice", ["age"] = 30 }
        };

        var recipients = new[]
        {
            CreateRecipient("ws1qalice")
        };

        // Act
        var result = _sut.BuildGroups(disclosedPayloads, recipients);

        // Assert
        result.Should().HaveCount(1);
        result[0].Recipients.Should().HaveCount(1);
        result[0].Recipients[0].WalletAddress.Should().Be("ws1qalice");
    }

    #endregion

    #region Deterministic GroupId

    [Fact]
    public void BuildGroups_DeterministicGroupId_SameFieldsSameValues()
    {
        // Arrange — same fields + same values in different dictionary order → same GroupId
        var disclosedPayloads1 = new Dictionary<string, Dictionary<string, object>>
        {
            ["ws1qalice"] = new() { ["zebra"] = 1, ["alpha"] = 2, ["middle"] = 3 }
        };
        var disclosedPayloads2 = new Dictionary<string, Dictionary<string, object>>
        {
            ["ws1qbob"] = new() { ["alpha"] = 2, ["middle"] = 3, ["zebra"] = 1 }
        };

        var recipients1 = new[] { CreateRecipient("ws1qalice") };
        var recipients2 = new[] { CreateRecipient("ws1qbob") };

        // Act
        var result1 = _sut.BuildGroups(disclosedPayloads1, recipients1);
        var result2 = _sut.BuildGroups(disclosedPayloads2, recipients2);

        // Assert — identical fields + values → same GroupId regardless of insertion order
        result1[0].GroupId.Should().Be(result2[0].GroupId);
    }

    [Fact]
    public void BuildGroups_DeterministicGroupId_SameFieldsDifferentValues()
    {
        // Arrange — same fields but different values → different GroupId
        var disclosedPayloads1 = new Dictionary<string, Dictionary<string, object>>
        {
            ["ws1qalice"] = new() { ["zebra"] = 1, ["alpha"] = 2, ["middle"] = 3 }
        };
        var disclosedPayloads2 = new Dictionary<string, Dictionary<string, object>>
        {
            ["ws1qbob"] = new() { ["alpha"] = 10, ["middle"] = 20, ["zebra"] = 30 }
        };

        var recipients1 = new[] { CreateRecipient("ws1qalice") };
        var recipients2 = new[] { CreateRecipient("ws1qbob") };

        // Act
        var result1 = _sut.BuildGroups(disclosedPayloads1, recipients1);
        var result2 = _sut.BuildGroups(disclosedPayloads2, recipients2);

        // Assert — same fields but different values → different GroupId
        result1[0].GroupId.Should().NotBe(result2[0].GroupId);
    }

    #endregion

    #region Mixed groups

    [Fact]
    public void BuildGroups_MixedGroups()
    {
        // Arrange — 5 recipients across 2 distinct field sets
        // Group A: ["amount", "name"] with identical values → 3 members
        // Group B: ["name", "ssn"] with identical values   → 2 members
        var disclosedPayloads = new Dictionary<string, Dictionary<string, object>>
        {
            ["ws1q1"] = new() { ["name"] = "SharedReport", ["amount"] = 100 },
            ["ws1q2"] = new() { ["name"] = "SharedReport", ["amount"] = 100 },
            ["ws1q3"] = new() { ["name"] = "SharedReport", ["amount"] = 100 },
            ["ws1q4"] = new() { ["name"] = "Confidential", ["ssn"] = "000-00-0000" },
            ["ws1q5"] = new() { ["name"] = "Confidential", ["ssn"] = "000-00-0000" }
        };

        var recipients = new[]
        {
            CreateRecipient("ws1q1"),
            CreateRecipient("ws1q2"),
            CreateRecipient("ws1q3"),
            CreateRecipient("ws1q4"),
            CreateRecipient("ws1q5")
        };

        // Act
        var result = _sut.BuildGroups(disclosedPayloads, recipients);

        // Assert — 2 groups: same fields + same values within each group
        result.Should().HaveCount(2);

        var groupA = result.First(g => g.DisclosedFields.Contains("amount"));
        var groupB = result.First(g => g.DisclosedFields.Contains("ssn"));

        groupA.Recipients.Should().HaveCount(3);
        groupB.Recipients.Should().HaveCount(2);

        groupA.DisclosedFields.Should().BeEquivalentTo(new[] { "amount", "name" },
            options => options.WithStrictOrdering());
        groupB.DisclosedFields.Should().BeEquivalentTo(new[] { "name", "ssn" },
            options => options.WithStrictOrdering());
    }

    #endregion

    #region Empty payloads

    [Fact]
    public void BuildGroups_EmptyPayloads_ReturnsEmpty()
    {
        // Arrange
        var disclosedPayloads = new Dictionary<string, Dictionary<string, object>>();
        var recipients = new[] { CreateRecipient("ws1qalice") };

        // Act
        var result = _sut.BuildGroups(disclosedPayloads, recipients);

        // Assert
        result.Should().BeEmpty();
    }

    #endregion

    #region Wallet with no RecipientInfo is skipped

    [Fact]
    public void BuildGroups_WalletWithNoRecipientInfo_Skipped()
    {
        // Arrange — wallet "ws1qorphan" is in disclosedPayloads but NOT in recipients
        var disclosedPayloads = new Dictionary<string, Dictionary<string, object>>
        {
            ["ws1qalice"] = new() { ["name"] = "Alice" },
            ["ws1qorphan"] = new() { ["name"] = "Orphan", ["secret"] = "hidden" }
        };

        var recipients = new[]
        {
            CreateRecipient("ws1qalice")
            // ws1qorphan intentionally omitted
        };

        // Act
        var result = _sut.BuildGroups(disclosedPayloads, recipients);

        // Assert — only Alice's group appears; orphan is skipped
        result.Should().HaveCount(1);
        result[0].Recipients.Should().HaveCount(1);
        result[0].Recipients[0].WalletAddress.Should().Be("ws1qalice");
    }

    #endregion

    #region GroupId is SHA-256 hex

    [Fact]
    public void BuildGroups_GroupIdIsSha256Hex()
    {
        // Arrange
        var disclosedPayloads = new Dictionary<string, Dictionary<string, object>>
        {
            ["ws1qalice"] = new() { ["name"] = "Alice", ["age"] = 30 }
        };

        var recipients = new[] { CreateRecipient("ws1qalice") };

        // Act
        var result = _sut.BuildGroups(disclosedPayloads, recipients);

        // Assert — GroupId should be 64-char lowercase hex (SHA-256 output)
        var groupId = result[0].GroupId;
        groupId.Should().HaveLength(64, because: "SHA-256 produces 32 bytes = 64 hex chars");
        groupId.Should().MatchRegex("^[0-9a-f]{64}$",
            because: "GroupId must be lowercase hexadecimal");
    }

    #endregion

    #region Helpers

    private static RecipientInfo CreateRecipient(string walletAddress)
    {
        return new RecipientInfo
        {
            WalletAddress = walletAddress,
            PublicKey = new byte[] { 0x04, 0x01, 0x02, 0x03 },
            Algorithm = WalletNetworks.ED25519,
            Source = KeySource.External
        };
    }

    #endregion
}
