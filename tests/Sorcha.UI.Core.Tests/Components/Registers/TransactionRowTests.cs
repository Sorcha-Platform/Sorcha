// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.UI.Core.Models.Registers;
using Xunit;

namespace Sorcha.UI.Core.Tests.Components.Registers;

/// <summary>
/// Tests for TransactionViewModel computed properties used by TransactionRow.
/// Note: Full component tests require E2E testing due to MudBlazor PopoverProvider dependency.
/// </summary>
public class TransactionRowTests
{
    private static TransactionViewModel CreateTestTransaction(
        string? txId = null,
        ulong? blockNumber = 100,
        uint? actionId = null,
        string? blueprintId = null,
        DateTime? timeStamp = null)
    {
        return new TransactionViewModel
        {
            TxId = txId ?? "1234567890abcdef1234567890abcdef1234567890abcdef1234567890abcdef",
            RegisterId = "reg123456789012345678901234567890",
            SenderWallet = "5Hq3wP8d5Zr7wJ9k4Ls6mN2xC1vB0nM8",
            Signature = "sig12345678901234567890123456789012345678901234567890",
            TimeStamp = timeStamp ?? DateTime.UtcNow.AddMinutes(-5),
            DocketNumber = blockNumber,
            PayloadCount = 2,
            ActionId = actionId,
            BlueprintId = blueprintId
        };
    }

    [Fact]
    public void TransactionViewModel_TxIdTruncated_TruncatesCorrectly()
    {
        // Arrange
        var tx = CreateTestTransaction();

        // Assert
        tx.TxIdTruncated.Should().Be("12345678...");
    }

    [Fact]
    public void TransactionViewModel_SenderTruncated_TruncatesCorrectly()
    {
        // Arrange
        var tx = CreateTestTransaction();

        // Assert - Should show first 8 chars, ..., and last 4 chars
        tx.SenderTruncated.Should().Be("5Hq3wP8d...0nM8");
    }

    [Fact]
    public void TransactionViewModel_DocketNumber_IsAvailable()
    {
        // Arrange
        var tx = CreateTestTransaction(blockNumber: 42);

        // Assert
        tx.DocketNumber.Should().Be(42);
    }

    [Fact]
    public void TransactionViewModel_DocketNumber_CanBeNull()
    {
        // Arrange
        var tx = CreateTestTransaction(blockNumber: null);

        // Assert
        tx.DocketNumber.Should().BeNull();
    }

    [Fact]
    public void TransactionViewModel_TransactionType_ReturnsAction_WhenActionIdSet()
    {
        // Arrange
        var tx = CreateTestTransaction(actionId: 1);

        // Assert
        tx.TransactionType.Should().Be("Action");
    }

    [Fact]
    public void TransactionViewModel_TransactionType_ReturnsBlueprint_WhenBlueprintIdSet()
    {
        // Arrange
        var tx = CreateTestTransaction(blueprintId: "bp-123");

        // Assert
        tx.TransactionType.Should().Be("Blueprint");
    }

    [Fact]
    public void TransactionViewModel_TransactionType_ReturnsTransfer_WhenNoMetadata()
    {
        // Arrange
        var tx = CreateTestTransaction();

        // Assert
        tx.TransactionType.Should().Be("Transfer");
    }

    [Fact]
    public void TransactionViewModel_TransactionType_ReturnsParticipant_WhenMetadataTypeIs3()
    {
        // Arrange
        var tx = new TransactionViewModel
        {
            TxId = "tx123456789012345678901234567890123456789012345678901234567890123",
            RegisterId = "reg123",
            SenderWallet = "addr123",
            Signature = "sig123",
            MetadataTransactionType = 3
        };

        // Assert
        tx.TransactionType.Should().Be("Participant");
    }

    [Fact]
    public void TransactionViewModel_TransactionType_ReturnsControl_WhenMetadataTypeIs0()
    {
        // Arrange
        var tx = new TransactionViewModel
        {
            TxId = "tx123456789012345678901234567890123456789012345678901234567890123",
            RegisterId = "reg123",
            SenderWallet = "addr123",
            Signature = "sig123",
            MetadataTransactionType = 0
        };

        // Assert
        tx.TransactionType.Should().Be("Control");
    }

    [Fact]
    public void TransactionViewModel_IsRecent_ReturnsTrueForRecentTransaction()
    {
        // Arrange - Create a transaction that just happened
        var tx = CreateTestTransaction(timeStamp: DateTime.UtcNow.AddSeconds(-2));

        // Assert
        tx.IsRecent.Should().BeTrue();
    }

    [Fact]
    public void TransactionViewModel_IsRecent_ReturnsFalseForOldTransaction()
    {
        // Arrange - Create a transaction from 10 seconds ago
        var tx = CreateTestTransaction(timeStamp: DateTime.UtcNow.AddSeconds(-10));

        // Assert
        tx.IsRecent.Should().BeFalse();
    }

    [Fact]
    public void TransactionViewModel_TimeStampFormatted_ShowsJustNow()
    {
        // Arrange
        var tx = CreateTestTransaction(timeStamp: DateTime.UtcNow.AddSeconds(-30));

        // Assert
        tx.TimeStampFormatted.Should().Be("just now");
    }

    [Fact]
    public void TransactionViewModel_TimeStampFormatted_ShowsMinutesAgo()
    {
        // Arrange
        var tx = CreateTestTransaction(timeStamp: DateTime.UtcNow.AddMinutes(-5));

        // Assert
        tx.TimeStampFormatted.Should().Be("5m ago");
    }

    [Fact]
    public void TransactionViewModel_TimeStampFormatted_ShowsTimeForToday()
    {
        // A transaction earlier the same day (>1h ago) shows HH:mm:ss.
        // The clock is injected so this is deterministic — the previous version
        // arranged DateTime.UtcNow.AddHours(-2) and was flaky in the first 2h of
        // a UTC day, when "2 hours ago" fell on the previous calendar day and the
        // formatter switched to the date format.
        var now = new DateTime(2026, 1, 15, 14, 0, 0, DateTimeKind.Utc);
        var earlierToday = new DateTime(2026, 1, 15, 9, 30, 15, DateTimeKind.Utc);

        var result = TransactionViewModel.GetFormattedTime(earlierToday, now);

        result.Should().Be("09:30:15");
    }

    [Fact]
    public void TransactionViewModel_TimeStampFormatted_ShowsDateForPreviousDay()
    {
        // Regression for the midnight flake: a transaction >1h ago but on the
        // previous calendar day shows the absolute date, not HH:mm:ss. This is
        // exactly the state that intermittently broke the "today" test.
        var now = new DateTime(2026, 1, 15, 1, 0, 0, DateTimeKind.Utc);          // 01:00 UTC
        var lateYesterday = new DateTime(2026, 1, 14, 23, 0, 0, DateTimeKind.Utc); // 2h earlier, prev day

        var result = TransactionViewModel.GetFormattedTime(lateYesterday, now);

        // Date branch ("MMM dd, HH:mm") — culture-independent shape, and explicitly NOT the time-only format.
        result.Should().MatchRegex(@"^\w{3} \d{2}, \d{2}:\d{2}$");
        result.Should().NotMatchRegex(@"\d{2}:\d{2}:\d{2}");
    }

    [Theory]
    [InlineData(-30, "just now")]
    [InlineData(-120, "2m ago")]
    [InlineData(-300, "5m ago")]
    [InlineData(-3540, "59m ago")]
    public void TransactionViewModel_TimeStampFormatted_FormatsRelativeTimeCorrectly(int secondsAgo, string expected)
    {
        // Arrange
        var tx = CreateTestTransaction(timeStamp: DateTime.UtcNow.AddSeconds(secondsAgo));

        // Assert
        tx.TimeStampFormatted.Should().Be(expected);
    }

    [Fact]
    public void TransactionViewModel_ActionId_PrioritizedOverBlueprintId()
    {
        // Arrange - Both ActionId and BlueprintId set
        var tx = new TransactionViewModel
        {
            TxId = "tx123456789012345678901234567890123456789012345678901234567890123",
            RegisterId = "reg123",
            SenderWallet = "addr123",
            Signature = "sig123",
            ActionId = 1,
            BlueprintId = "bp-123"
        };

        // Assert - ActionId should take priority
        tx.TransactionType.Should().Be("Action");
    }

    [Fact]
    public void TransactionViewModel_DidUri_FormatsCorrectly()
    {
        // Arrange
        var tx = new TransactionViewModel
        {
            TxId = "abcdef123456",
            RegisterId = "my-register",
            SenderWallet = "addr123",
            Signature = "sig123"
        };

        // Assert
        tx.DidUri.Should().Be("did:sorcha:register:my-register/tx/abcdef123456");
    }

    [Fact]
    public void TransactionViewModel_RequiredProperties_CanBeSet()
    {
        // Arrange & Act
        var tx = new TransactionViewModel
        {
            TxId = "txid123",
            RegisterId = "regid456",
            SenderWallet = "wallet789",
            Signature = "signature000"
        };

        // Assert
        tx.TxId.Should().Be("txid123");
        tx.RegisterId.Should().Be("regid456");
        tx.SenderWallet.Should().Be("wallet789");
        tx.Signature.Should().Be("signature000");
    }

    [Fact]
    public void TransactionViewModel_OptionalProperties_HaveDefaults()
    {
        // Arrange
        var tx = new TransactionViewModel
        {
            TxId = "tx",
            RegisterId = "reg",
            SenderWallet = "wallet",
            Signature = "sig"
        };

        // Assert
        tx.RecipientsWallets.Should().BeEmpty();
        tx.DocketNumber.Should().BeNull();
        tx.PayloadCount.Should().Be(0);
        tx.PrevTxId.Should().BeNull();
        tx.Version.Should().Be(1);
        tx.BlueprintId.Should().BeNull();
        tx.InstanceId.Should().BeNull();
        tx.ActionId.Should().BeNull();
    }
}
