// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Validator.Core.Models;
using Sorcha.Validator.Core.Validators;
using Xunit;
using Sorcha.Register.Models;

namespace Sorcha.Validator.Core.Tests.Validators;

/// <summary>
/// Unit tests for ConsensusValidator
/// Tests cover >95% code coverage as required by project standards
/// </summary>
public class ConsensusValidatorTests
{
    private readonly ConsensusValidator _validator;

    public ConsensusValidatorTests()
    {
        _validator = new ConsensusValidator();
    }

    #region ValidateVoteStructure Tests

    [Fact]
    public void ValidateVoteStructure_WithValidVote_ReturnsSuccess()
    {
        // Arrange
        var docketHash = "docket-hash-123";
        var vote = CreateValidVote(VoteDecision.Approve);

        // Act
        var result = _validator.ValidateVoteStructure(vote, docketHash);

        // Assert
        result.Should().NotBeNull();
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("", "CV_001")]
    [InlineData(null, "CV_001")]
    [InlineData("   ", "CV_001")]
    public void ValidateVoteStructure_WithInvalidValidatorId_ReturnsError(string? validatorId, string expectedCode)
    {
        // Arrange
        var docketHash = "docket-hash-123";
        var vote = CreateValidVote(VoteDecision.Approve) with { ValidatorId = validatorId! };

        // Act
        var result = _validator.ValidateVoteStructure(vote, docketHash);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == expectedCode);
        result.Errors[0].Field.Should().Be(nameof(vote.ValidatorId));
    }

    [Theory]
    [InlineData("", "CV_002")]
    [InlineData(null, "CV_002")]
    [InlineData("   ", "CV_002")]
    public void ValidateVoteStructure_WithInvalidDocketHash_ReturnsError(string? voteDocketHash, string expectedCode)
    {
        // Arrange
        var docketHash = "docket-hash-123";
        var vote = CreateValidVote(VoteDecision.Approve) with { DocketHash = voteDocketHash! };

        // Act
        var result = _validator.ValidateVoteStructure(vote, docketHash);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == expectedCode);
    }

    [Fact]
    public void ValidateVoteStructure_WithMismatchedDocketHash_ReturnsError()
    {
        // Arrange
        var expectedHash = "docket-hash-123";
        var vote = CreateValidVote(VoteDecision.Approve) with { DocketHash = "wrong-hash" };

        // Act
        var result = _validator.ValidateVoteStructure(vote, expectedHash);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "CV_003");
        result.Errors[0].Message.Should().Contain("mismatch");
        result.Errors[0].Message.Should().Contain(expectedHash);
        result.Errors[0].Message.Should().Contain("wrong-hash");
    }

    [Theory]
    [InlineData((VoteDecision)999)]
    [InlineData((VoteDecision)(-1))]
    [InlineData((VoteDecision)0)]
    public void ValidateVoteStructure_WithInvalidDecision_ReturnsError(VoteDecision invalidDecision)
    {
        // Arrange
        var docketHash = "docket-hash-123";
        var vote = CreateValidVote(VoteDecision.Approve) with { Decision = invalidDecision };

        // Act
        var result = _validator.ValidateVoteStructure(vote, docketHash);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "CV_004");
        result.Errors[0].Message.Should().Contain("Invalid vote decision");
    }

    [Theory]
    [InlineData("", "CV_005")]
    [InlineData(null, "CV_005")]
    [InlineData("   ", "CV_005")]
    public void ValidateVoteStructure_WithInvalidSignature_ReturnsError(string? signature, string expectedCode)
    {
        // Arrange
        var docketHash = "docket-hash-123";
        var vote = CreateValidVote(VoteDecision.Approve) with { Signature = signature! };

        // Act
        var result = _validator.ValidateVoteStructure(vote, docketHash);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == expectedCode);
    }

    [Fact]
    public void ValidateVoteStructure_WithFutureTimestamp_ReturnsError()
    {
        // Arrange
        var docketHash = "docket-hash-123";
        var futureTime = DateTimeOffset.UtcNow.AddMinutes(10);
        var vote = CreateValidVote(VoteDecision.Approve) with { VotedAt = futureTime };

        // Act
        var result = _validator.ValidateVoteStructure(vote, docketHash);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "CV_006");
        result.Errors[0].Message.Should().Contain("future");
    }

    [Fact]
    public void ValidateVoteStructure_WithTimestampWithin5MinuteSkew_ReturnsSuccess()
    {
        // Arrange
        var docketHash = "docket-hash-123";
        var nearFutureTime = DateTimeOffset.UtcNow.AddMinutes(4);
        var vote = CreateValidVote(VoteDecision.Approve) with { VotedAt = nearFutureTime };

        // Act
        var result = _validator.ValidateVoteStructure(vote, docketHash);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateVoteStructure_WithRejectVoteAndNoReason_ReturnsError()
    {
        // Arrange
        var docketHash = "docket-hash-123";
        var vote = CreateValidVote(VoteDecision.Reject) with { RejectionReason = null };

        // Act
        var result = _validator.ValidateVoteStructure(vote, docketHash);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "CV_007");
        result.Errors[0].Message.Should().Contain("Rejection reason is required");
    }

    [Fact]
    public void ValidateVoteStructure_WithRejectVoteAndReason_ReturnsSuccess()
    {
        // Arrange
        var docketHash = "docket-hash-123";
        var vote = CreateValidVote(VoteDecision.Reject) with { RejectionReason = "Invalid transactions" };

        // Act
        var result = _validator.ValidateVoteStructure(vote, docketHash);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(VoteDecision.Approve)]
    [InlineData(VoteDecision.Abstain)]
    public void ValidateVoteStructure_WithApproveOrAbstainWithoutReason_ReturnsSuccess(VoteDecision decision)
    {
        // Arrange
        var docketHash = "docket-hash-123";
        var vote = CreateValidVote(decision) with { RejectionReason = null };

        // Act
        var result = _validator.ValidateVoteStructure(vote, docketHash);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void ValidateVoteStructure_WithMultipleErrors_ReturnsAllErrors()
    {
        // Arrange
        var docketHash = "docket-hash-123";
        var vote = new ConsensusVoteData
        {
            ValidatorId = "",
            DocketHash = "",
            Decision = (VoteDecision)999,
            VotedAt = DateTimeOffset.UtcNow.AddMinutes(10),
            Signature = "",
            RejectionReason = null
        };

        // Act
        var result = _validator.ValidateVoteStructure(vote, docketHash);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCountGreaterThan(3);
        result.Errors.Should().Contain(e => e.Code == "CV_001"); // ValidatorId
        result.Errors.Should().Contain(e => e.Code == "CV_002"); // DocketHash
        result.Errors.Should().Contain(e => e.Code == "CV_004"); // Decision
        result.Errors.Should().Contain(e => e.Code == "CV_005"); // Signature
        result.Errors.Should().Contain(e => e.Code == "CV_006"); // Timestamp
    }

    #endregion

    #region ValidateQuorum Tests

    [Theory]
    [InlineData(7, 10, 0.66)]  // 70% > 66%
    [InlineData(8, 10, 0.75)]  // 80% > 75%
    [InlineData(10, 10, 0.90)] // 100% > 90%
    [InlineData(3, 4, 0.66)]   // 75% > 66%
    public void ValidateQuorum_WithSufficientApprovals_ReturnsSuccess(
        int approvalCount, int totalValidators, double threshold)
    {
        // Act
        var result = _validator.ValidateQuorum(approvalCount, totalValidators, threshold);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Metadata.Should().NotBeNull();
        result.Metadata.Should().ContainKey("ApprovalPercentage");
        result.Metadata.Should().ContainKey("ApprovalCount");
    }

    [Theory]
    [InlineData(5, 10, 0.50)] // 50% = 50% (not greater)
    [InlineData(6, 10, 0.70)] // 60% < 70%
    [InlineData(2, 10, 0.50)] // 20% < 50%
    [InlineData(0, 10, 0.10)] // 0% < 10%
    public void ValidateQuorum_WithInsufficientApprovals_ReturnsError(
        int approvalCount, int totalValidators, double threshold)
    {
        // Act
        var result = _validator.ValidateQuorum(approvalCount, totalValidators, threshold);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "CV_012");
        result.Errors[0].Message.Should().Contain("Quorum not met");
        result.Errors[0].Message.Should().Contain(approvalCount.ToString());
        result.Errors[0].Message.Should().Contain(totalValidators.ToString());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    public void ValidateQuorum_WithNegativeApprovalCount_ReturnsError(int approvalCount)
    {
        // Act
        var result = _validator.ValidateQuorum(approvalCount, 10, 0.5);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "CV_008");
        result.Errors[0].Message.Should().Contain("cannot be negative");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    public void ValidateQuorum_WithInvalidTotalValidators_ReturnsError(int totalValidators)
    {
        // Act
        var result = _validator.ValidateQuorum(5, totalValidators, 0.5);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "CV_009");
        result.Errors[0].Message.Should().Contain("greater than zero");
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    [InlineData(2.0)]
    [InlineData(-1.0)]
    public void ValidateQuorum_WithInvalidThreshold_ReturnsError(double threshold)
    {
        // Act
        var result = _validator.ValidateQuorum(5, 10, threshold);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "CV_010");
        result.Errors[0].Message.Should().Contain("between 0.0 and 1.0");
    }

    [Fact]
    public void ValidateQuorum_WithApprovalsExceedingTotal_ReturnsError()
    {
        // Act
        var result = _validator.ValidateQuorum(15, 10, 0.5);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "CV_011");
        result.Errors[0].Message.Should().Contain("cannot exceed total validators");
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void ValidateQuorum_WithValidThresholdBoundaries_Works(double threshold)
    {
        // Act
        var result = _validator.ValidateQuorum(8, 10, threshold);

        // Assert - 80% should be valid for 0.0 and 0.5, but invalid for 1.0
        if (threshold == 1.0)
        {
            result.IsValid.Should().BeFalse();
        }
        else
        {
            result.IsValid.Should().BeTrue();
        }
    }

    [Fact]
    public void ValidateQuorum_WithMultipleErrors_ReturnsAllErrors()
    {
        // Act
        var result = _validator.ValidateQuorum(-5, -10, 2.0);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().HaveCount(4); // Actually 4 errors: negative, invalid total, invalid threshold, exceeds total
        result.Errors.Should().Contain(e => e.Code == "CV_008"); // Negative approval
        result.Errors.Should().Contain(e => e.Code == "CV_009"); // Invalid total
        result.Errors.Should().Contain(e => e.Code == "CV_010"); // Invalid threshold
        result.Errors.Should().Contain(e => e.Code == "CV_011"); // Exceeds total
    }

    #endregion

    #region ValidateVoteCollection Tests

    [Fact]
    public void ValidateVoteCollection_WithValidVotes_ReturnsSuccess()
    {
        // Arrange
        var docketHash = "docket-hash-123";
        var votes = new List<ConsensusVoteData>
        {
            CreateValidVote(VoteDecision.Approve) with { ValidatorId = "validator-1" },
            CreateValidVote(VoteDecision.Approve) with { ValidatorId = "validator-2" },
            CreateValidVote(VoteDecision.Reject) with { ValidatorId = "validator-3", RejectionReason = "Bad tx" }
        };

        // Act
        var result = _validator.ValidateVoteCollection(votes, docketHash);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ValidateVoteCollection_WithEmptyCollection_ReturnsError()
    {
        // Arrange
        var docketHash = "docket-hash-123";
        var votes = new List<ConsensusVoteData>();

        // Act
        var result = _validator.ValidateVoteCollection(votes, docketHash);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "CV_013");
        result.Errors[0].Message.Should().Contain("cannot be empty");
    }

    [Fact]
    public void ValidateVoteCollection_WithDuplicateValidators_ReturnsError()
    {
        // Arrange
        var docketHash = "docket-hash-123";
        var votes = new List<ConsensusVoteData>
        {
            CreateValidVote(VoteDecision.Approve) with { ValidatorId = "validator-1" },
            CreateValidVote(VoteDecision.Approve) with { ValidatorId = "validator-1" }, // Duplicate
            CreateValidVote(VoteDecision.Reject) with { ValidatorId = "validator-2", RejectionReason = "Bad" }
        };

        // Act
        var result = _validator.ValidateVoteCollection(votes, docketHash);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "CV_014");
        result.Errors[0].Message.Should().Contain("Duplicate votes");
        result.Errors[0].Message.Should().Contain("validator-1");
    }

    [Fact]
    public void ValidateVoteCollection_WithMultipleDuplicates_ReportsAll()
    {
        // Arrange
        var docketHash = "docket-hash-123";
        var votes = new List<ConsensusVoteData>
        {
            CreateValidVote(VoteDecision.Approve) with { ValidatorId = "validator-1" },
            CreateValidVote(VoteDecision.Approve) with { ValidatorId = "validator-1" }, // Duplicate
            CreateValidVote(VoteDecision.Reject) with { ValidatorId = "validator-2", RejectionReason = "Bad" },
            CreateValidVote(VoteDecision.Approve) with { ValidatorId = "validator-2" }  // Duplicate
        };

        // Act
        var result = _validator.ValidateVoteCollection(votes, docketHash);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "CV_014");
        result.Errors[0].Message.Should().Contain("validator-1");
        result.Errors[0].Message.Should().Contain("validator-2");
    }

    [Fact]
    public void ValidateVoteCollection_WithInvalidVotes_ReturnsVoteSpecificErrors()
    {
        // Arrange
        var docketHash = "docket-hash-123";
        var votes = new List<ConsensusVoteData>
        {
            CreateValidVote(VoteDecision.Approve) with { ValidatorId = "" }, // Invalid
            CreateValidVote(VoteDecision.Approve) with { ValidatorId = "validator-2" } // Valid
        };

        // Act
        var result = _validator.ValidateVoteCollection(votes, docketHash);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Message.Contains("Vote[0]"));
        result.Errors.Should().Contain(e => e.Code == "CV_001");
    }

    [Fact]
    public void ValidateVoteCollection_WithCaseInsensitiveValidatorIds_DetectsDuplicates()
    {
        // Arrange
        var docketHash = "docket-hash-123";
        var votes = new List<ConsensusVoteData>
        {
            CreateValidVote(VoteDecision.Approve) with { ValidatorId = "Validator-1" },
            CreateValidVote(VoteDecision.Approve) with { ValidatorId = "validator-1" } // Case different
        };

        // Act
        var result = _validator.ValidateVoteCollection(votes, docketHash);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "CV_014");
    }

    #endregion

    #region CheckConsensusAchievement Tests

    [Theory]
    [InlineData(7, 10, 0.66)] // 70% > 66%
    [InlineData(8, 10, 0.75)] // 80% > 75%
    [InlineData(10, 10, 0.50)] // 100% > 50%
    [InlineData(6, 10, 0.50)] // 60% > 50%
    public void CheckConsensusAchievement_WithSufficientApprovals_ReturnsSuccess(
        int approvalCount, int totalValidators, double threshold)
    {
        // Arrange
        var votes = CreateVotes(approvalCount, totalValidators - approvalCount, 0);

        // Act
        var result = _validator.CheckConsensusAchievement(votes, totalValidators, threshold);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Metadata.Should().NotBeNull();
        result.Metadata.Should().ContainKey("ConsensusAchieved");
        result.Metadata!["ConsensusAchieved"].Should().Be(true);
        result.Metadata.Should().ContainKey("ApprovalCount");
        result.Metadata["ApprovalCount"].Should().Be(approvalCount);
        result.Metadata.Should().ContainKey("ApprovalPercentage");
    }

    [Theory]
    [InlineData(5, 10, 0.50)] // 50% = 50% (not greater)
    [InlineData(6, 10, 0.70)] // 60% < 70%
    [InlineData(0, 10, 0.10)] // 0% < 10%
    public void CheckConsensusAchievement_WithInsufficientApprovals_ReturnsFailure(
        int approvalCount, int totalValidators, double threshold)
    {
        // Arrange
        var votes = CreateVotes(approvalCount, totalValidators - approvalCount, 0);

        // Act
        var result = _validator.CheckConsensusAchievement(votes, totalValidators, threshold);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "CV_017");
        result.Errors[0].Message.Should().Contain("Consensus not achieved");
        result.Metadata.Should().NotBeNull();
        result.Metadata!["ConsensusAchieved"].Should().Be(false);
        result.Metadata.Should().ContainKey("ApprovalCount");
        result.Metadata.Should().ContainKey("RejectCount");
        result.Metadata.Should().ContainKey("AbstainCount");
    }

    [Fact]
    public void CheckConsensusAchievement_WithZeroValidators_ReturnsError()
    {
        // Arrange
        var votes = new List<ConsensusVoteData>();

        // Act
        var result = _validator.CheckConsensusAchievement(votes, 0, 0.5);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "CV_015");
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void CheckConsensusAchievement_WithInvalidThreshold_ReturnsError(double threshold)
    {
        // Arrange
        var votes = CreateVotes(5, 5, 0);

        // Act
        var result = _validator.CheckConsensusAchievement(votes, 10, threshold);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.Code == "CV_016");
    }

    [Fact]
    public void CheckConsensusAchievement_CountsVotesByDecision()
    {
        // Arrange
        var votes = CreateVotes(7, 2, 1); // 7 approve, 2 reject, 1 abstain

        // Act
        var result = _validator.CheckConsensusAchievement(votes, 10, 0.66);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Metadata!["ApprovalCount"].Should().Be(7);
        result.Metadata["RejectCount"].Should().Be(2);
        result.Metadata["AbstainCount"].Should().Be(1);
    }

    [Fact]
    public void CheckConsensusAchievement_WithAllApprovals_ReturnsSuccess()
    {
        // Arrange
        var votes = CreateVotes(10, 0, 0); // All approve

        // Act
        var result = _validator.CheckConsensusAchievement(votes, 10, 0.90);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Metadata!["ApprovalPercentage"].Should().Be(1.0);
    }

    [Fact]
    public void CheckConsensusAchievement_WithAllRejections_ReturnsFailure()
    {
        // Arrange
        var votes = CreateVotes(0, 10, 0); // All reject

        // Act
        var result = _validator.CheckConsensusAchievement(votes, 10, 0.50);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Metadata!["ApprovalCount"].Should().Be(0);
        result.Metadata["RejectCount"].Should().Be(10);
    }

    [Fact]
    public void CheckConsensusAchievement_WithAbstentions_IgnoresThemInCalculation()
    {
        // Arrange
        var votes = CreateVotes(6, 0, 4); // 6 approve, 4 abstain

        // Act - 6/10 = 60%
        var result = _validator.CheckConsensusAchievement(votes, 10, 0.50);

        // Assert
        result.IsValid.Should().BeTrue(); // 60% > 50%
        result.Metadata!["ApprovalCount"].Should().Be(6);
        result.Metadata["AbstainCount"].Should().Be(4);
    }

    [Fact]
    public void CheckConsensusAchievement_WithEdgeThreshold_RequiresStrictlyGreater()
    {
        // Arrange - Exactly at threshold
        var votes = CreateVotes(5, 5, 0); // 50%

        // Act
        var result = _validator.CheckConsensusAchievement(votes, 10, 0.50);

        // Assert - Must be GREATER than threshold, not equal
        result.IsValid.Should().BeFalse();
        result.Errors[0].Message.Should().Contain(">50.00%"); // Showing "greater than" symbol with percentage (deterministic, culture-invariant)
    }

    #endregion

    #region Helper Methods

    /// <summary>
    /// Creates a valid consensus vote for testing
    /// </summary>
    private static ConsensusVoteData CreateValidVote(VoteDecision decision, string? rejectionReason = null)
    {
        return new ConsensusVoteData
        {
            ValidatorId = "validator-" + Guid.NewGuid().ToString().Substring(0, 8),
            DocketHash = "docket-hash-123",
            Decision = decision,
            VotedAt = DateTimeOffset.UtcNow.AddMinutes(-1),
            Signature = "signature-" + Guid.NewGuid().ToString(),
            RejectionReason = decision == VoteDecision.Reject ? (rejectionReason ?? "Invalid") : null
        };
    }

    /// <summary>
    /// Creates a collection of votes with specified counts
    /// </summary>
    private static List<ConsensusVoteData> CreateVotes(int approvalCount, int rejectCount, int abstainCount)
    {
        var votes = new List<ConsensusVoteData>();

        for (int i = 0; i < approvalCount; i++)
        {
            votes.Add(CreateValidVote(VoteDecision.Approve) with { ValidatorId = $"validator-approve-{i}" });
        }

        for (int i = 0; i < rejectCount; i++)
        {
            votes.Add(CreateValidVote(VoteDecision.Reject, "Rejection reason") with { ValidatorId = $"validator-reject-{i}" });
        }

        for (int i = 0; i < abstainCount; i++)
        {
            votes.Add(CreateValidVote(VoteDecision.Abstain) with { ValidatorId = $"validator-abstain-{i}" });
        }

        return votes;
    }

    #endregion
}
