// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.UI.Core.Models.Registers;
using Sorcha.UI.Core.Services;
using Xunit;

namespace Sorcha.UI.Core.Tests.Services;

public class GraphLayoutServiceTests
{
    private readonly GraphLayoutService _sut = new();

    private static TransactionGraphNode MakeNode(string txId, string prevTxId, DateTime? timestamp = null)
    {
        return new TransactionGraphNode
        {
            TxId = txId,
            PrevTxId = prevTxId,
            SenderWallet = "wallet123456789",
            TimeStamp = timestamp ?? DateTime.UtcNow
        };
    }

    [Fact]
    public void ComputeLayout_SingleChain_AssignsIncrementingRanks()
    {
        // Arrange: genesis → A → B
        var genesis = MakeNode("genesis1", "", new DateTime(2026, 1, 1));
        var nodeA = MakeNode("aaaa1111", "genesis1", new DateTime(2026, 1, 2));
        var nodeB = MakeNode("bbbb2222", "aaaa1111", new DateTime(2026, 1, 3));

        // Act
        var (nodes, edges) = _sut.ComputeLayout([genesis, nodeA, nodeB]);

        // Assert
        genesis.Rank.Should().Be(0);
        nodeA.Rank.Should().Be(1);
        nodeB.Rank.Should().Be(2);

        edges.Should().HaveCount(2);
        edges.Should().Contain(e => e.SourceTxId == "genesis1" && e.TargetTxId == "aaaa1111");
        edges.Should().Contain(e => e.SourceTxId == "aaaa1111" && e.TargetTxId == "bbbb2222");
    }

    [Fact]
    public void ComputeLayout_ForkedChain_AssignsCorrectRanks()
    {
        // Arrange: genesis → child1, genesis → child2
        var genesis = MakeNode("genesis1", "", new DateTime(2026, 1, 1));
        var child1 = MakeNode("child111", "genesis1", new DateTime(2026, 1, 2));
        var child2 = MakeNode("child222", "genesis1", new DateTime(2026, 1, 3));

        // Act
        var (nodes, edges) = _sut.ComputeLayout([genesis, child1, child2]);

        // Assert
        genesis.Rank.Should().Be(0);
        child1.Rank.Should().Be(1);
        child2.Rank.Should().Be(1);

        edges.Should().HaveCount(2);
    }

    [Fact]
    public void ComputeLayout_MultipleGenesis_AllRankZero()
    {
        // Arrange
        var gen1 = MakeNode("gen11111", "", new DateTime(2026, 1, 1));
        var gen2 = MakeNode("gen22222", "0000000000000000", new DateTime(2026, 1, 2));

        // Act
        _sut.ComputeLayout([gen1, gen2]);

        // Assert
        gen1.Rank.Should().Be(0);
        gen2.Rank.Should().Be(0);
    }

    [Fact]
    public void ComputeLayout_EmptyInput_ReturnsEmpty()
    {
        // Act
        var (nodes, edges) = _sut.ComputeLayout([]);

        // Assert
        nodes.Should().BeEmpty();
        edges.Should().BeEmpty();
    }

    [Fact]
    public void ComputeLayout_LeftToRight_XIncreasesByRank()
    {
        // Arrange
        var genesis = MakeNode("genesis1", "", new DateTime(2026, 1, 1));
        var nodeA = MakeNode("aaaa1111", "genesis1", new DateTime(2026, 1, 2));
        var nodeB = MakeNode("bbbb2222", "aaaa1111", new DateTime(2026, 1, 3));

        // Act
        _sut.ComputeLayout([genesis, nodeA, nodeB], leftToRight: true);

        // Assert — X should increase with rank
        nodeA.X.Should().BeGreaterThan(genesis.X);
        nodeB.X.Should().BeGreaterThan(nodeA.X);

        // Y should be 0 for all (single node per rank)
        genesis.Y.Should().Be(0);
        nodeA.Y.Should().Be(0);
        nodeB.Y.Should().Be(0);
    }

    [Fact]
    public void ComputeLayout_TopToBottom_YIncreasesByRank()
    {
        // Arrange
        var genesis = MakeNode("genesis1", "", new DateTime(2026, 1, 1));
        var nodeA = MakeNode("aaaa1111", "genesis1", new DateTime(2026, 1, 2));
        var nodeB = MakeNode("bbbb2222", "aaaa1111", new DateTime(2026, 1, 3));

        // Act
        _sut.ComputeLayout([genesis, nodeA, nodeB], leftToRight: false);

        // Assert — Y should increase with rank
        nodeA.Y.Should().BeGreaterThan(genesis.Y);
        nodeB.Y.Should().BeGreaterThan(nodeA.Y);

        // X should be 0 for all (single node per rank)
        genesis.X.Should().Be(0);
        nodeA.X.Should().Be(0);
        nodeB.X.Should().Be(0);
    }

    [Fact]
    public void HighlightChain_SelectsCorrectAncestors()
    {
        // Arrange: genesis → A → B → C
        var genesis = MakeNode("genesis1", "", new DateTime(2026, 1, 1));
        var nodeA = MakeNode("aaaa1111", "genesis1", new DateTime(2026, 1, 2));
        var nodeB = MakeNode("bbbb2222", "aaaa1111", new DateTime(2026, 1, 3));
        var nodeC = MakeNode("cccc3333", "bbbb2222", new DateTime(2026, 1, 4));

        var (nodes, edges) = _sut.ComputeLayout([genesis, nodeA, nodeB, nodeC]);

        // Act — select the last node
        _sut.HighlightChain(nodes, edges, "cccc3333");

        // Assert — all 4 nodes should be highlighted
        genesis.IsHighlighted.Should().BeTrue();
        nodeA.IsHighlighted.Should().BeTrue();
        nodeB.IsHighlighted.Should().BeTrue();
        nodeC.IsHighlighted.Should().BeTrue();

        // All 3 edges should be highlighted
        edges.Where(e => e.IsHighlighted).Should().HaveCount(3);
    }

    [Fact]
    public void HighlightChain_ForkDoesNotHighlightSiblings()
    {
        // Arrange: genesis → child1, genesis → child2
        var genesis = MakeNode("genesis1", "", new DateTime(2026, 1, 1));
        var child1 = MakeNode("child111", "genesis1", new DateTime(2026, 1, 2));
        var child2 = MakeNode("child222", "genesis1", new DateTime(2026, 1, 3));

        var (nodes, edges) = _sut.ComputeLayout([genesis, child1, child2]);

        // Act — select child1
        _sut.HighlightChain(nodes, edges, "child111");

        // Assert
        genesis.IsHighlighted.Should().BeTrue();
        child1.IsHighlighted.Should().BeTrue();
        child2.IsHighlighted.Should().BeFalse();

        edges.Where(e => e.IsHighlighted).Should().HaveCount(1);
        edges.Single(e => e.IsHighlighted).TargetTxId.Should().Be("child111");
    }

    [Fact]
    public void ClearHighlights_ResetsAll()
    {
        // Arrange
        var genesis = MakeNode("genesis1", "", new DateTime(2026, 1, 1));
        var nodeA = MakeNode("aaaa1111", "genesis1", new DateTime(2026, 1, 2));

        var (nodes, edges) = _sut.ComputeLayout([genesis, nodeA]);
        _sut.HighlightChain(nodes, edges, "aaaa1111");

        // Verify highlights are set
        nodes.Any(n => n.IsHighlighted).Should().BeTrue();

        // Act
        _sut.ClearHighlights(nodes, edges);

        // Assert
        nodes.Should().AllSatisfy(n => n.IsHighlighted.Should().BeFalse());
        edges.Should().AllSatisfy(e => e.IsHighlighted.Should().BeFalse());
    }
}
