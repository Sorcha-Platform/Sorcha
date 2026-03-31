// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Register.Models;
using Sorcha.Validator.Core;
using Xunit;

namespace Sorcha.Validator.Core.Tests;

/// <summary>
/// Unit tests for RevocationValidator (T037).
/// Validates revocation payload structure and business rules.
/// </summary>
public class RevocationValidatorTests
{
    private readonly RevocationValidator _validator;

    public RevocationValidatorTests()
    {
        _validator = new RevocationValidator();
    }

    #region ValidatePayload - Valid Payloads

    [Fact]
    public void ValidatePayload_ValidPayload_ReturnsValid()
    {
        // Arrange
        var payload = CreateValidPayload();

        // Act
        var result = _validator.ValidatePayload(payload);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData(RevocationReason.Erroneous)]
    [InlineData(RevocationReason.Compromised)]
    [InlineData(RevocationReason.Expired)]
    [InlineData(RevocationReason.Withdrawn)]
    [InlineData(RevocationReason.Regulatory)]
    public void ValidatePayload_AllNonSupersededReasons_ReturnsValid(RevocationReason reason)
    {
        // Arrange
        var payload = new RevocationPayload
        {
            OriginalTxId = "tx-original-1",
            OriginalDocketNumber = 5,
            Reason = reason,
            SupersededByTxId = null
        };

        // Act
        var result = _validator.ValidatePayload(payload);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidatePayload_SupersededWithReplacementTxId_ReturnsValid()
    {
        // Arrange
        var payload = new RevocationPayload
        {
            OriginalTxId = "tx-original-1",
            OriginalDocketNumber = 5,
            Reason = RevocationReason.Superseded,
            SupersededByTxId = "tx-replacement-1"
        };

        // Act
        var result = _validator.ValidatePayload(payload);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidatePayload_WithValidMetadata_ReturnsValid()
    {
        // Arrange
        var payload = CreateValidPayload() with
        {
            Metadata = new Dictionary<string, string>
            {
                ["reason_detail"] = "Data entry error in field X",
                ["approved_by"] = "admin@org.example"
            }
        };

        // Act
        var result = _validator.ValidatePayload(payload);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region ValidatePayload - Null Payload

    [Fact]
    public void ValidatePayload_NullPayload_ReturnsInvalid()
    {
        // Act
        var result = _validator.ValidatePayload(null!);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Revocation payload is null");
        result.ErrorCode.Should().Be("VAL_REV_001");
    }

    #endregion

    #region ValidatePayload - Invalid Reason

    [Fact]
    public void ValidatePayload_InvalidReason_ReturnsError()
    {
        // Arrange
        var payload = new RevocationPayload
        {
            OriginalTxId = "tx-1",
            OriginalDocketNumber = 1,
            Reason = (RevocationReason)999 // Invalid enum value
        };

        // Act
        var result = _validator.ValidatePayload(payload);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Invalid revocation reason"));
        result.ErrorCode.Should().Be("VAL_REV_006");
    }

    #endregion

    #region ValidatePayload - SupersededByTxId Consistency

    [Fact]
    public void ValidatePayload_SupersededWithoutReplacementTxId_ReturnsError()
    {
        // Arrange: Superseded reason but no SupersededByTxId
        var payload = new RevocationPayload
        {
            OriginalTxId = "tx-1",
            OriginalDocketNumber = 1,
            Reason = RevocationReason.Superseded,
            SupersededByTxId = null
        };

        // Act
        var result = _validator.ValidatePayload(payload);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("SupersededByTxId is required when reason is Superseded");
        result.ErrorCode.Should().Be("VAL_REV_007");
    }

    [Fact]
    public void ValidatePayload_SupersededWithEmptyReplacementTxId_ReturnsError()
    {
        // Arrange
        var payload = new RevocationPayload
        {
            OriginalTxId = "tx-1",
            OriginalDocketNumber = 1,
            Reason = RevocationReason.Superseded,
            SupersededByTxId = "   " // Whitespace-only
        };

        // Act
        var result = _validator.ValidatePayload(payload);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("SupersededByTxId is required when reason is Superseded");
    }

    [Fact]
    public void ValidatePayload_NonSupersededWithReplacementTxId_ReturnsError()
    {
        // Arrange: Non-Superseded reason but has SupersededByTxId
        var payload = new RevocationPayload
        {
            OriginalTxId = "tx-1",
            OriginalDocketNumber = 1,
            Reason = RevocationReason.Erroneous,
            SupersededByTxId = "tx-replacement-1"
        };

        // Act
        var result = _validator.ValidatePayload(payload);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("SupersededByTxId must be null when reason is not Superseded");
        result.ErrorCode.Should().Be("VAL_REV_007");
    }

    #endregion

    #region ValidatePayload - Required Fields

    [Fact]
    public void ValidatePayload_EmptyOriginalTxId_ReturnsError()
    {
        // Arrange
        var payload = new RevocationPayload
        {
            OriginalTxId = "",
            OriginalDocketNumber = 1,
            Reason = RevocationReason.Erroneous
        };

        // Act
        var result = _validator.ValidatePayload(payload);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("OriginalTxId is required");
    }

    [Fact]
    public void ValidatePayload_NegativeDocketNumber_ReturnsError()
    {
        // Arrange
        var payload = new RevocationPayload
        {
            OriginalTxId = "tx-1",
            OriginalDocketNumber = -1,
            Reason = RevocationReason.Erroneous
        };

        // Act
        var result = _validator.ValidatePayload(payload);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("OriginalDocketNumber must be non-negative");
    }

    #endregion

    #region ValidatePayload - Metadata Constraints

    [Fact]
    public void ValidatePayload_MetadataExceedsTenEntries_ReturnsError()
    {
        // Arrange
        var metadata = new Dictionary<string, string>();
        for (int i = 0; i < 11; i++)
        {
            metadata[$"key{i}"] = $"value{i}";
        }

        var payload = CreateValidPayload() with { Metadata = metadata };

        // Act
        var result = _validator.ValidatePayload(payload);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain("Metadata cannot have more than 10 entries");
    }

    [Fact]
    public void ValidatePayload_MetadataKeyExceeds50Chars_ReturnsError()
    {
        // Arrange
        var longKey = new string('k', 51);
        var payload = CreateValidPayload() with
        {
            Metadata = new Dictionary<string, string> { [longKey] = "value" }
        };

        // Act
        var result = _validator.ValidatePayload(payload);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("exceeds 50 characters"));
    }

    [Fact]
    public void ValidatePayload_MetadataValueExceeds500Chars_ReturnsError()
    {
        // Arrange
        var longValue = new string('v', 501);
        var payload = CreateValidPayload() with
        {
            Metadata = new Dictionary<string, string> { ["key"] = longValue }
        };

        // Act
        var result = _validator.ValidatePayload(payload);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("exceeds 500 characters"));
    }

    [Fact]
    public void ValidatePayload_MetadataAtExactLimits_ReturnsValid()
    {
        // Arrange: Exactly 10 entries, key=50 chars, value=500 chars
        var metadata = new Dictionary<string, string>();
        for (int i = 0; i < 10; i++)
        {
            var key = new string('k', 50 - i.ToString().Length) + i;
            metadata[key] = new string('v', 500);
        }

        var payload = CreateValidPayload() with { Metadata = metadata };

        // Act
        var result = _validator.ValidatePayload(payload);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region Helpers

    private static RevocationPayload CreateValidPayload()
    {
        return new RevocationPayload
        {
            OriginalTxId = "tx-original-1",
            OriginalDocketNumber = 5,
            Reason = RevocationReason.Erroneous
        };
    }

    #endregion
}
