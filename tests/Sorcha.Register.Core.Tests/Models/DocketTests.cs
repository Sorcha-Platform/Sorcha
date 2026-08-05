// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Models;
using Sorcha.Register.Models.Enums;
using System.ComponentModel.DataAnnotations;
using Xunit;

namespace Sorcha.Register.Core.Tests.Models;

public class DocketTests
{
    [Fact]
    public void Docket_DefaultConstructor_ShouldSetDefaultValues()
    {
        // Act
        var docket = new DocketHeader();

        // Assert
        docket.Id.Should().Be(0ul);
        docket.RegisterId.Should().BeEmpty();
        docket.PreviousHash.Should().BeEmpty();
        docket.Hash.Should().BeEmpty();
        docket.TransactionIds.Should().BeEmpty();
        docket.TimeStamp.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
        docket.State.Should().Be(DocketState.Init);
        docket.MetaData.Should().BeNull();
        docket.Votes.Should().BeNull();
    }

    [Fact]
    public void Docket_WithValidProperties_ShouldCreateSuccessfully()
    {
        // Arrange
        var registerId = Guid.NewGuid().ToString("N");
        var previousHash = "previous-hash";
        var hash = "current-hash";
        var txIds = new List<string> { "tx1", "tx2", "tx3" };
        var timestamp = DateTime.UtcNow.AddHours(-1);

        // Act
        var docket = new DocketHeader
        {
            Id = 5,
            RegisterId = registerId,
            PreviousHash = previousHash,
            Hash = hash,
            TransactionIds = txIds,
            TimeStamp = timestamp,
            State = DocketState.Sealed,
            Votes = "vote-data"
        };

        // Assert
        docket.Id.Should().Be(5ul);
        docket.RegisterId.Should().Be(registerId);
        docket.PreviousHash.Should().Be(previousHash);
        docket.Hash.Should().Be(hash);
        docket.TransactionIds.Should().HaveCount(3);
        docket.TransactionIds.Should().ContainInOrder("tx1", "tx2", "tx3");
        docket.TimeStamp.Should().Be(timestamp);
        docket.State.Should().Be(DocketState.Sealed);
        docket.Votes.Should().Be("vote-data");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Docket_WithInvalidRegisterId_ShouldFailValidation(string? invalidRegisterId)
    {
        // Arrange
        var docket = new DocketHeader
        {
            RegisterId = invalidRegisterId!,
            Hash = "some-hash"
        };

        // Act
        var validationResults = ValidateModel(docket);

        // Assert
        validationResults.Should().ContainSingle(v => v.MemberNames.Contains("RegisterId"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Docket_WithInvalidHash_ShouldFailValidation(string? invalidHash)
    {
        // Arrange
        var docket = new DocketHeader
        {
            RegisterId = Guid.NewGuid().ToString("N"),
            Hash = invalidHash!
        };

        // Act
        var validationResults = ValidateModel(docket);

        // Assert
        validationResults.Should().ContainSingle(v => v.MemberNames.Contains("Hash"));
    }

    [Theory]
    [InlineData(DocketState.Init)]
    [InlineData(DocketState.Proposed)]
    [InlineData(DocketState.Accepted)]
    [InlineData(DocketState.Rejected)]
    [InlineData(DocketState.Sealed)]
    public void Docket_WithAllStateValues_ShouldBeValid(DocketState state)
    {
        // Arrange
        var docket = new DocketHeader
        {
            RegisterId = Guid.NewGuid().ToString("N"),
            Hash = "valid-hash",
            State = state
        };

        // Act
        var validationResults = ValidateModel(docket);

        // Assert
        validationResults.Should().BeEmpty();
    }

    [Fact]
    public void Docket_TransactionIds_ShouldBeModifiable()
    {
        // Arrange
        var docket = new DocketHeader
        {
            RegisterId = Guid.NewGuid().ToString("N"),
            Hash = "hash123"
        };

        // Act
        docket.TransactionIds.Add("tx1");
        docket.TransactionIds.Add("tx2");
        docket.TransactionIds.Add("tx3");

        // Assert
        docket.TransactionIds.Should().HaveCount(3);
        docket.TransactionIds.Should().Contain("tx1");
        docket.TransactionIds.Should().Contain("tx2");
        docket.TransactionIds.Should().Contain("tx3");
    }

    [Fact]
    public void Docket_WithMetaData_ShouldStoreCorrectly()
    {
        // Arrange
        var metadata = new TransactionMetaData
        {
            RegisterId = "reg123",
            BlueprintId = "blueprint456"
        };

        var docket = new DocketHeader
        {
            RegisterId = Guid.NewGuid().ToString("N"),
            Hash = "hash123",
            MetaData = metadata
        };

        // Act & Assert
        docket.MetaData.Should().NotBeNull();
        docket.MetaData!.RegisterId.Should().Be("reg123");
        docket.MetaData.BlueprintId.Should().Be("blueprint456");
    }

    [Fact]
    public void Docket_IdProperty_ShouldAcceptUInt64Values()
    {
        // Arrange
        var docket = new DocketHeader
        {
            RegisterId = Guid.NewGuid().ToString("N"),
            Hash = "hash123"
        };

        // Act & Assert
        docket.Id = 0;
        docket.Id.Should().Be(0ul);

        docket.Id = 1000;
        docket.Id.Should().Be(1000ul);

        docket.Id = ulong.MaxValue;
        docket.Id.Should().Be(ulong.MaxValue);
    }

    [Fact]
    public void Docket_EmptyPreviousHash_ShouldBeValidForGenesisBlock()
    {
        // Arrange - Genesis block should have empty previous hash
        var docket = new DocketHeader
        {
            Id = 1,
            RegisterId = Guid.NewGuid().ToString("N"),
            PreviousHash = string.Empty,
            Hash = "genesis-hash",
            State = DocketState.Sealed
        };

        // Act
        var validationResults = ValidateModel(docket);

        // Assert
        validationResults.Should().BeEmpty();
        docket.PreviousHash.Should().BeEmpty();
    }

    [Fact]
    public void Docket_WithManyTransactions_ShouldHandleCorrectly()
    {
        // Arrange
        var docket = new DocketHeader
        {
            RegisterId = Guid.NewGuid().ToString("N"),
            Hash = "hash123"
        };

        var txIds = Enumerable.Range(1, 1000)
            .Select(i => $"tx-{i:D5}")
            .ToList();

        // Act
        docket.TransactionIds = txIds;

        // Assert
        docket.TransactionIds.Should().HaveCount(1000);
        docket.TransactionIds.First().Should().Be("tx-00001");
        docket.TransactionIds.Last().Should().Be("tx-01000");
    }

    [Fact]
    public void Docket_FullyPopulated_ShouldPassValidation()
    {
        // Arrange
        var docket = new DocketHeader
        {
            Id = 42,
            RegisterId = Guid.NewGuid().ToString("N"),
            PreviousHash = "0123456789abcdef",
            Hash = "fedcba9876543210",
            TransactionIds = new List<string> { "tx1", "tx2", "tx3" },
            TimeStamp = DateTime.UtcNow.AddMinutes(-5),
            State = DocketState.Sealed,
            MetaData = new TransactionMetaData { RegisterId = "reg123" },
            Votes = "{\"vote\": \"data\"}"
        };

        // Act
        var validationResults = ValidateModel(docket);

        // Assert
        validationResults.Should().BeEmpty();
    }

    private static IList<ValidationResult> ValidateModel(object model)
    {
        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(model, null, null);
        Validator.TryValidateObject(model, validationContext, validationResults, true);
        return validationResults;
    }
}
