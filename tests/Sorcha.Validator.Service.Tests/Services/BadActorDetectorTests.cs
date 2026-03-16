// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;

namespace Sorcha.Validator.Service.Tests.Services;

public class BadActorDetectorTests
{
    private readonly Mock<IOptions<BadActorDetectorConfiguration>> _configMock;
    private readonly Mock<ILogger<BadActorDetector>> _loggerMock;
    private readonly BadActorDetector _detector;
    private readonly BadActorDetectorConfiguration _config;

    public BadActorDetectorTests()
    {
        _config = new BadActorDetectorConfiguration
        {
            RejectionCountWindow = TimeSpan.FromHours(1),
            WarningThreshold = 5,
            HighSeverityThreshold = 10,
            CriticalThreshold = 20,
            IncidentRetentionPeriod = TimeSpan.FromDays(7),
            MaxIncidentsPerValidator = 1000,
            EnableCriticalAlerts = true,
            CleanupInterval = TimeSpan.FromHours(1)
        };

        _configMock = new Mock<IOptions<BadActorDetectorConfiguration>>();
        _configMock.Setup(x => x.Value).Returns(_config);

        _loggerMock = new Mock<ILogger<BadActorDetector>>();

        _detector = new BadActorDetector(_configMock.Object, _loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_NullConfig_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new BadActorDetector(null!, _loggerMock.Object);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("config");
    }

    [Fact]
    public void Constructor_NullLogger_ThrowsArgumentNullException()
    {
        // Act
        var act = () => new BadActorDetector(_configMock.Object, null!);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("logger");
    }

    #endregion

    #region LogDocketRejection Tests

    [Fact]
    public void LogDocketRejection_ValidInput_RecordsIncident()
    {
        // Arrange
        const string registerId = "register-1";
        const string initiatorId = "validator-1";
        const string docketId = "docket-1";
        const DocketRejectionReason reason = DocketRejectionReason.InvalidMerkleRoot;

        // Act
        _detector.LogDocketRejection(registerId, initiatorId, docketId, reason);

        // Assert
        var stats = _detector.GetStats();
        stats.TotalIncidents.Should().Be(1);
        stats.IncidentsByType[BadActorIncidentType.InvalidDocketProposed].Should().Be(1);
    }

    [Fact]
    public void LogDocketRejection_NullRegisterId_ThrowsArgumentException()
    {
        // Act
        var act = () => _detector.LogDocketRejection(null!, "validator-1", "docket-1", DocketRejectionReason.InvalidMerkleRoot);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LogDocketRejection_NullInitiatorId_ThrowsArgumentException()
    {
        // Act
        var act = () => _detector.LogDocketRejection("register-1", null!, "docket-1", DocketRejectionReason.InvalidMerkleRoot);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void LogDocketRejection_NullDocketId_ThrowsArgumentException()
    {
        // Act
        var act = () => _detector.LogDocketRejection("register-1", "validator-1", null!, DocketRejectionReason.InvalidMerkleRoot);

        // Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public async Task LogDocketRejection_WithDetails_IncludesDetailsInIncident()
    {
        // Arrange
        const string registerId = "register-1";
        const string initiatorId = "validator-1";
        const string docketId = "docket-1";
        const string details = "Hash mismatch detected";

        // Act
        _detector.LogDocketRejection(registerId, initiatorId, docketId,
            DocketRejectionReason.InvalidDocketHash, details);

        // Assert
        var incidents = await _detector.GetIncidentsAsync(registerId, initiatorId);
        incidents.Should().HaveCount(1);
        incidents[0].Details.Should().Contain(details);
    }

    [Fact]
    public async Task LogDocketRejection_CriticalReason_SetsCriticalSeverity()
    {
        // Arrange - UnauthorizedInitiator is considered critical
        _detector.LogDocketRejection("register-1", "validator-1", "docket-1",
            DocketRejectionReason.UnauthorizedInitiator);

        // Act
        var incidents = await _detector.GetIncidentsAsync("register-1", "validator-1");

        // Assert
        incidents[0].Severity.Should().Be(IncidentSeverity.Critical);
    }

    #endregion

    #region LogTransactionValidationFailure Tests

    [Fact]
    public void LogTransactionValidationFailure_ValidInput_RecordsIncident()
    {
        // Arrange
        const string registerId = "register-1";
        const string senderId = "sender-1";
        const string transactionId = "tx-1";
        const string errorType = "Schema";

        // Act
        _detector.LogTransactionValidationFailure(registerId, senderId, transactionId, errorType);

        // Assert
        var stats = _detector.GetStats();
        stats.TotalIncidents.Should().Be(1);
        stats.IncidentsByType[BadActorIncidentType.InvalidTransactionSubmitted].Should().Be(1);
    }

    [Fact]
    public async Task LogTransactionValidationFailure_SetsInfoSeverity()
    {
        // Arrange & Act
        _detector.LogTransactionValidationFailure("register-1", "sender-1", "tx-1", "Schema");
        var incidents = await _detector.GetIncidentsAsync("register-1", "sender-1");

        // Assert
        incidents[0].Severity.Should().Be(IncidentSeverity.Info);
    }

    #endregion

    #region LogDoubleVote Tests

    [Fact]
    public async Task LogDoubleVote_ValidInput_RecordsHighSeverityIncident()
    {
        // Arrange
        const string registerId = "register-1";
        const string validatorId = "validator-1";
        const string docketId = "docket-1";
        const long term = 5;

        // Act
        _detector.LogDoubleVote(registerId, validatorId, docketId, term);

        // Assert
        var incidents = await _detector.GetIncidentsAsync(registerId, validatorId);
        incidents.Should().HaveCount(1);
        incidents[0].IncidentType.Should().Be(BadActorIncidentType.DoubleVoteAttempt);
        incidents[0].Severity.Should().Be(IncidentSeverity.High);
    }

    #endregion

    #region LogLeaderImpersonation Tests

    [Fact]
    public async Task LogLeaderImpersonation_ValidInput_RecordsCriticalIncident()
    {
        // Arrange
        const string registerId = "register-1";
        const string fakeLeaderId = "fake-leader";
        const string actualLeaderId = "real-leader";
        const long term = 5;

        // Act
        _detector.LogLeaderImpersonation(registerId, fakeLeaderId, actualLeaderId, term);

        // Assert
        var incidents = await _detector.GetIncidentsAsync(registerId, fakeLeaderId);
        incidents.Should().HaveCount(1);
        incidents[0].IncidentType.Should().Be(BadActorIncidentType.LeaderImpersonation);
        incidents[0].Severity.Should().Be(IncidentSeverity.Critical);
        incidents[0].Details.Should().Contain(actualLeaderId);
    }

    #endregion

    #region GetRejectionCountAsync Tests

    [Fact]
    public async Task GetRejectionCountAsync_NoIncidents_ReturnsZero()
    {
        // Act
        var count = await _detector.GetRejectionCountAsync("register-1", "validator-1", TimeSpan.FromHours(1));

        // Assert
        count.Should().Be(0);
    }

    [Fact]
    public async Task GetRejectionCountAsync_WithIncidents_ReturnsCorrectCount()
    {
        // Arrange
        _detector.LogDocketRejection("register-1", "validator-1", "docket-1", DocketRejectionReason.InvalidMerkleRoot);
        _detector.LogDocketRejection("register-1", "validator-1", "docket-2", DocketRejectionReason.InvalidDocketHash);
        _detector.LogTransactionValidationFailure("register-1", "validator-1", "tx-1", "Schema");

        // Act
        var count = await _detector.GetRejectionCountAsync("register-1", "validator-1", TimeSpan.FromHours(1));

        // Assert
        count.Should().Be(3);
    }

    [Fact]
    public async Task GetRejectionCountAsync_DifferentValidators_CountsSeparately()
    {
        // Arrange
        _detector.LogDocketRejection("register-1", "validator-1", "docket-1", DocketRejectionReason.InvalidMerkleRoot);
        _detector.LogDocketRejection("register-1", "validator-2", "docket-2", DocketRejectionReason.InvalidMerkleRoot);

        // Act
        var count1 = await _detector.GetRejectionCountAsync("register-1", "validator-1", TimeSpan.FromHours(1));
        var count2 = await _detector.GetRejectionCountAsync("register-1", "validator-2", TimeSpan.FromHours(1));

        // Assert
        count1.Should().Be(1);
        count2.Should().Be(1);
    }

    #endregion

    #region GetIncidentsAsync Tests

    [Fact]
    public async Task GetIncidentsAsync_NoIncidents_ReturnsEmptyList()
    {
        // Act
        var incidents = await _detector.GetIncidentsAsync("register-1", "validator-1");

        // Assert
        incidents.Should().BeEmpty();
    }

    [Fact]
    public async Task GetIncidentsAsync_WithIncidents_ReturnsInDescendingOrder()
    {
        // Arrange
        _detector.LogDocketRejection("register-1", "validator-1", "docket-1", DocketRejectionReason.InvalidMerkleRoot);
        await Task.Delay(10); // Small delay to ensure different timestamps
        _detector.LogDocketRejection("register-1", "validator-1", "docket-2", DocketRejectionReason.InvalidDocketHash);

        // Act
        var incidents = await _detector.GetIncidentsAsync("register-1", "validator-1");

        // Assert
        incidents.Should().HaveCount(2);
        incidents[0].DocketId.Should().Be("docket-2"); // Most recent first
        incidents[1].DocketId.Should().Be("docket-1");
    }

    [Fact]
    public async Task GetIncidentsAsync_RespectsLimit()
    {
        // Arrange
        for (int i = 0; i < 10; i++)
        {
            _detector.LogDocketRejection("register-1", "validator-1", $"docket-{i}", DocketRejectionReason.InvalidMerkleRoot);
        }

        // Act
        var incidents = await _detector.GetIncidentsAsync("register-1", "validator-1", limit: 5);

        // Assert
        incidents.Should().HaveCount(5);
    }

    #endregion

    #region ShouldFlagForReviewAsync Tests

    [Fact]
    public async Task ShouldFlagForReviewAsync_NoIncidents_ReturnsFalse()
    {
        // Act
        var result = await _detector.ShouldFlagForReviewAsync("register-1", "validator-1");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task ShouldFlagForReviewAsync_CriticalIncident_ReturnsTrue()
    {
        // Arrange
        _detector.LogLeaderImpersonation("register-1", "validator-1", "real-leader", 1);

        // Act
        var result = await _detector.ShouldFlagForReviewAsync("register-1", "validator-1");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldFlagForReviewAsync_MultipleHighSeverity_ReturnsTrue()
    {
        // Arrange
        _detector.LogDoubleVote("register-1", "validator-1", "docket-1", 1);
        _detector.LogDoubleVote("register-1", "validator-1", "docket-2", 2);

        // Act
        var result = await _detector.ShouldFlagForReviewAsync("register-1", "validator-1");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldFlagForReviewAsync_AboveWarningThreshold_ReturnsTrue()
    {
        // Arrange - Add incidents up to warning threshold
        for (int i = 0; i < _config.WarningThreshold; i++)
        {
            _detector.LogDocketRejection("register-1", "validator-1", $"docket-{i}",
                DocketRejectionReason.InvalidTransaction);
        }

        // Act
        var result = await _detector.ShouldFlagForReviewAsync("register-1", "validator-1");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public async Task ShouldFlagForReviewAsync_BelowWarningThreshold_ReturnsFalse()
    {
        // Arrange - Add incidents below warning threshold
        for (int i = 0; i < _config.WarningThreshold - 1; i++)
        {
            _detector.LogTransactionValidationFailure("register-1", "validator-1", $"tx-{i}", "Schema");
        }

        // Act
        var result = await _detector.ShouldFlagForReviewAsync("register-1", "validator-1");

        // Assert
        result.Should().BeFalse();
    }

    #endregion

    #region GetStats Tests

    [Fact]
    public void GetStats_InitialState_ReturnsZeroCounts()
    {
        // Act
        var stats = _detector.GetStats();

        // Assert
        stats.TotalIncidents.Should().Be(0);
        stats.TrackedValidators.Should().Be(0);
        stats.OldestIncident.Should().BeNull();
    }

    [Fact]
    public void GetStats_WithIncidents_ReturnsCorrectCounts()
    {
        // Arrange
        _detector.LogDocketRejection("register-1", "validator-1", "docket-1", DocketRejectionReason.InvalidMerkleRoot);
        _detector.LogDoubleVote("register-1", "validator-2", "docket-2", 1);
        _detector.LogTransactionValidationFailure("register-1", "validator-1", "tx-1", "Schema");

        // Act
        var stats = _detector.GetStats();

        // Assert
        stats.TotalIncidents.Should().Be(3);
        stats.TrackedValidators.Should().Be(2);
        stats.IncidentsByType[BadActorIncidentType.InvalidDocketProposed].Should().Be(1);
        stats.IncidentsByType[BadActorIncidentType.DoubleVoteAttempt].Should().Be(1);
        stats.IncidentsByType[BadActorIncidentType.InvalidTransactionSubmitted].Should().Be(1);
    }

    #endregion

    #region CleanupExpiredIncidents Tests

    [Fact]
    public void CleanupExpiredIncidents_NoIncidents_CompletesWithoutError()
    {
        // Act
        var act = () => _detector.CleanupExpiredIncidents();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void CleanupExpiredIncidents_WithRecentIncidents_KeepsIncidents()
    {
        // Arrange
        _detector.LogDocketRejection("register-1", "validator-1", "docket-1", DocketRejectionReason.InvalidMerkleRoot);

        // Act
        _detector.CleanupExpiredIncidents();
        var stats = _detector.GetStats();

        // Assert
        stats.TotalIncidents.Should().Be(1);
    }

    #endregion

    #region Escalation Tests

    [Fact]
    public async Task LogDocketRejection_AtCriticalThreshold_ShouldFlagForReview()
    {
        // Arrange & Act - Add incidents up to critical threshold
        for (int i = 0; i < _config.CriticalThreshold; i++)
        {
            _detector.LogDocketRejection("register-1", "validator-1", $"docket-{i}",
                DocketRejectionReason.InvalidTransaction);
        }

        // Assert - Should flag for review at this threshold
        var shouldFlag = await _detector.ShouldFlagForReviewAsync("register-1", "validator-1");
        shouldFlag.Should().BeTrue();

        // Verify incident count
        var stats = _detector.GetStats();
        stats.TotalIncidents.Should().BeGreaterThanOrEqualTo(_config.CriticalThreshold);
    }

    #endregion
}
