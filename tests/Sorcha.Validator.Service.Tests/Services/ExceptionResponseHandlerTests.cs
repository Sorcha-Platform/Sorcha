// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Sorcha.Validator.Service.Configuration;
using Sorcha.Validator.Service.Models;
using Sorcha.Validator.Service.Services;
using Sorcha.Validator.Service.Services.Interfaces;
using Xunit;
using Sorcha.Register.Models;

namespace Sorcha.Validator.Service.Tests.Services;

public class ExceptionResponseHandlerTests
{
    private readonly Mock<IOptions<ValidatorConfiguration>> _configMock;
    private readonly Mock<ILogger<ExceptionResponseHandler>> _loggerMock;
    private readonly ExceptionResponseHandler _handler;
    private readonly ValidatorConfiguration _config;

    public ExceptionResponseHandlerTests()
    {
        _config = new ValidatorConfiguration
        {
            ValidatorId = "test-validator-001",
            SystemWalletAddress = "test-wallet-address"
        };

        _configMock = new Mock<IOptions<ValidatorConfiguration>>();
        _configMock.Setup(c => c.Value).Returns(_config);

        _loggerMock = new Mock<ILogger<ExceptionResponseHandler>>();

        _handler = new ExceptionResponseHandler(
            _configMock.Object,
            _loggerMock.Object);
    }

    #region Constructor Tests

    [Fact]
    public void Constructor_WithNullConfig_ThrowsArgumentNullException()
    {
        var act = () => new ExceptionResponseHandler(
            null!,
            _loggerMock.Object);

        act.Should().Throw<ArgumentNullException>().WithParameterName("config");
    }

    [Fact]
    public void Constructor_WithNullLogger_ThrowsArgumentNullException()
    {
        var act = () => new ExceptionResponseHandler(
            _configMock.Object,
            null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("logger");
    }

    #endregion

    #region CreateResponse Tests

    [Fact]
    public void CreateResponse_WithValidInput_CreatesResponse()
    {
        // Arrange
        var transaction = CreateTestTransaction();
        var validationResult = ValidationEngineResult.Failure(
            transaction.TransactionId,
            transaction.RegisterId,
            TimeSpan.FromMilliseconds(50),
            new ValidationEngineError
            {
                Code = "VAL_001",
                Message = "Test error message",
                Category = ValidationErrorCategory.Schema,
                IsFatal = true
            });

        // Act
        var response = _handler.CreateResponse(validationResult, transaction);

        // Assert
        response.Should().NotBeNull();
        response.ExceptionId.Should().StartWith("exc_");
        response.TransactionId.Should().Be(transaction.TransactionId);
        response.RegisterId.Should().Be(transaction.RegisterId);
        response.BlueprintId.Should().Be(transaction.BlueprintId);
        response.Code.Should().Be(ExceptionCode.SchemaViolation);
        response.Summary.Should().Be("Test error message");
        response.Details.Should().HaveCount(1);
        response.ValidatorId.Should().Be(_config.ValidatorId);
    }

    [Fact]
    public void CreateResponse_WithValidResult_ThrowsArgumentException()
    {
        // Arrange
        var transaction = CreateTestTransaction();
        var validationResult = ValidationEngineResult.Success(
            transaction.TransactionId,
            transaction.RegisterId,
            TimeSpan.FromMilliseconds(50));

        // Act
        var act = () => _handler.CreateResponse(validationResult, transaction);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("Cannot create exception response for valid transaction*");
    }

    [Fact]
    public void CreateResponse_WithNullValidationResult_ThrowsArgumentNullException()
    {
        var transaction = CreateTestTransaction();

        var act = () => _handler.CreateResponse(null!, transaction);

        act.Should().Throw<ArgumentNullException>().WithParameterName("validationResult");
    }

    [Fact]
    public void CreateResponse_WithNullTransaction_ThrowsArgumentNullException()
    {
        var validationResult = ValidationEngineResult.Failure(
            "tx-1", "reg-1", TimeSpan.Zero,
            new ValidationEngineError { Code = "ERR", Message = "Error", Category = ValidationErrorCategory.Internal });

        var act = () => _handler.CreateResponse(validationResult, null!);

        act.Should().Throw<ArgumentNullException>().WithParameterName("originalTransaction");
    }

    [Fact]
    public void CreateResponse_WithStructureErrors_ReturnsInvalidStructureCode()
    {
        // Arrange
        var transaction = CreateTestTransaction();
        var validationResult = ValidationEngineResult.Failure(
            transaction.TransactionId,
            transaction.RegisterId,
            TimeSpan.FromMilliseconds(50),
            new ValidationEngineError
            {
                Code = "VAL_STRUCT_001",
                Message = "Missing required field",
                Category = ValidationErrorCategory.Structure,
                Field = "TransactionId"
            });

        // Act
        var response = _handler.CreateResponse(validationResult, transaction);

        // Assert
        response.Code.Should().Be(ExceptionCode.InvalidStructure);
    }

    [Fact]
    public void CreateResponse_WithCryptographicErrors_ReturnsCryptographicFailureCode()
    {
        // Arrange
        var transaction = CreateTestTransaction();
        var validationResult = ValidationEngineResult.Failure(
            transaction.TransactionId,
            transaction.RegisterId,
            TimeSpan.FromMilliseconds(50),
            new ValidationEngineError
            {
                Code = "VAL_SIG_001",
                Message = "Invalid signature",
                Category = ValidationErrorCategory.Cryptographic
            });

        // Act
        var response = _handler.CreateResponse(validationResult, transaction);

        // Assert
        response.Code.Should().Be(ExceptionCode.CryptographicFailure);
    }

    [Fact]
    public void CreateResponse_WithChainErrors_ReturnsChainViolationCode()
    {
        // Arrange
        var transaction = CreateTestTransaction();
        var validationResult = ValidationEngineResult.Failure(
            transaction.TransactionId,
            transaction.RegisterId,
            TimeSpan.FromMilliseconds(50),
            new ValidationEngineError
            {
                Code = "VAL_CHAIN_001",
                Message = "Broken chain",
                Category = ValidationErrorCategory.Chain
            });

        // Act
        var response = _handler.CreateResponse(validationResult, transaction);

        // Assert
        response.Code.Should().Be(ExceptionCode.ChainViolation);
    }

    [Fact]
    public void CreateResponse_WithBlueprintErrors_ReturnsBlueprintErrorCode()
    {
        // Arrange
        var transaction = CreateTestTransaction();
        var validationResult = ValidationEngineResult.Failure(
            transaction.TransactionId,
            transaction.RegisterId,
            TimeSpan.FromMilliseconds(50),
            new ValidationEngineError
            {
                Code = "VAL_BP_001",
                Message = "Blueprint not found",
                Category = ValidationErrorCategory.Blueprint
            });

        // Act
        var response = _handler.CreateResponse(validationResult, transaction);

        // Assert
        response.Code.Should().Be(ExceptionCode.BlueprintError);
    }

    [Fact]
    public void CreateResponse_WithPermissionErrors_ReturnsPermissionDeniedCode()
    {
        // Arrange
        var transaction = CreateTestTransaction();
        var validationResult = ValidationEngineResult.Failure(
            transaction.TransactionId,
            transaction.RegisterId,
            TimeSpan.FromMilliseconds(50),
            new ValidationEngineError
            {
                Code = "VAL_PERM_001",
                Message = "Not authorized",
                Category = ValidationErrorCategory.Permission
            });

        // Act
        var response = _handler.CreateResponse(validationResult, transaction);

        // Assert
        response.Code.Should().Be(ExceptionCode.PermissionDenied);
    }

    [Fact]
    public void CreateResponse_WithTimingErrors_ReturnsTimingViolationCode()
    {
        // Arrange
        var transaction = CreateTestTransaction();
        var validationResult = ValidationEngineResult.Failure(
            transaction.TransactionId,
            transaction.RegisterId,
            TimeSpan.FromMilliseconds(50),
            new ValidationEngineError
            {
                Code = "VAL_TIME_001",
                Message = "Transaction expired",
                Category = ValidationErrorCategory.Timing
            });

        // Act
        var response = _handler.CreateResponse(validationResult, transaction);

        // Assert
        response.Code.Should().Be(ExceptionCode.TimingViolation);
    }

    [Fact]
    public void CreateResponse_WithMultipleCategoryErrors_ReturnsMultipleFailuresCode()
    {
        // Arrange
        var transaction = CreateTestTransaction();
        var validationResult = ValidationEngineResult.Failure(
            transaction.TransactionId,
            transaction.RegisterId,
            TimeSpan.FromMilliseconds(50),
            new ValidationEngineError { Code = "ERR1", Message = "Error 1", Category = ValidationErrorCategory.Schema },
            new ValidationEngineError { Code = "ERR2", Message = "Error 2", Category = ValidationErrorCategory.Chain });

        // Act
        var response = _handler.CreateResponse(validationResult, transaction);

        // Assert
        response.Code.Should().Be(ExceptionCode.MultipleFailures);
        response.Details.Should().HaveCount(2);
    }

    [Fact]
    public void CreateResponse_IncludesRemediationAdvice()
    {
        // Arrange
        var transaction = CreateTestTransaction();
        var validationResult = ValidationEngineResult.Failure(
            transaction.TransactionId,
            transaction.RegisterId,
            TimeSpan.FromMilliseconds(50),
            new ValidationEngineError
            {
                Code = "VAL_SIG_001",
                Message = "Invalid signature",
                Category = ValidationErrorCategory.Cryptographic
            });

        // Act
        var response = _handler.CreateResponse(validationResult, transaction);

        // Assert
        response.Details[0].Remediation.Should().NotBeNullOrEmpty();
        response.Details[0].Remediation.Should().Contain("wallet");
    }

    #endregion

    #region CreateResponses Tests

    [Fact]
    public void CreateResponses_WithMatchingCounts_CreatesResponses()
    {
        // Arrange
        var tx1 = CreateTestTransaction("tx-1");
        var tx2 = CreateTestTransaction("tx-2");

        var results = new List<ValidationEngineResult>
        {
            ValidationEngineResult.Failure(tx1.TransactionId, tx1.RegisterId, TimeSpan.Zero,
                new ValidationEngineError { Code = "E1", Message = "Error 1", Category = ValidationErrorCategory.Schema }),
            ValidationEngineResult.Success(tx2.TransactionId, tx2.RegisterId, TimeSpan.Zero)
        };

        var transactions = new List<Transaction> { tx1, tx2 };

        // Act
        var responses = _handler.CreateResponses(results, transactions);

        // Assert
        responses.Should().HaveCount(1); // Only failed transaction
        responses[0].TransactionId.Should().Be("tx-1");
    }

    [Fact]
    public void CreateResponses_WithMismatchedCounts_ThrowsArgumentException()
    {
        var results = new List<ValidationEngineResult>
        {
            ValidationEngineResult.Failure("tx-1", "reg-1", TimeSpan.Zero,
                new ValidationEngineError { Code = "E1", Message = "Error 1", Category = ValidationErrorCategory.Schema })
        };

        var transactions = new List<Transaction>
        {
            CreateTestTransaction("tx-1"),
            CreateTestTransaction("tx-2")
        };

        var act = () => _handler.CreateResponses(results, transactions);

        act.Should().Throw<ArgumentException>()
            .WithMessage("*same count*");
    }

    #endregion

    #region DeliverViaSignalRAsync Tests

    [Fact]
    public async Task DeliverViaSignalRAsync_WithoutHub_ReturnsFalse()
    {
        // Arrange
        var response = CreateTestExceptionResponse();

        // Act
        var result = await _handler.DeliverViaSignalRAsync(response);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public async Task DeliverViaSignalRAsync_WithNullResponse_ThrowsArgumentNullException()
    {
        var act = async () => await _handler.DeliverViaSignalRAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("response");
    }

    #endregion

    #region RecordToAuditLogAsync Tests

    [Fact]
    public async Task RecordToAuditLogAsync_LogsException()
    {
        // Arrange
        var response = CreateTestExceptionResponse();

        // Act
        await _handler.RecordToAuditLogAsync(response);

        // Assert - Verify logging occurred
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => v.ToString()!.Contains(response.ExceptionId)),
                null,
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Fact]
    public async Task RecordToAuditLogAsync_WithNullResponse_ThrowsArgumentNullException()
    {
        var act = async () => await _handler.RecordToAuditLogAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().WithParameterName("response");
    }

    #endregion

    #region GetStats Tests

    [Fact]
    public void GetStats_InitialState_ReturnsZeroCounts()
    {
        // Act
        var stats = _handler.GetStats();

        // Assert
        stats.TotalCreated.Should().Be(0);
        stats.TotalDelivered.Should().Be(0);
        stats.ByCode.Should().BeEmpty();
        stats.ByDeliveryMethod.Should().BeEmpty();
    }

    [Fact]
    public void GetStats_AfterCreatingResponses_ReturnsCorrectCounts()
    {
        // Arrange
        var transaction = CreateTestTransaction();
        var result = ValidationEngineResult.Failure(
            transaction.TransactionId,
            transaction.RegisterId,
            TimeSpan.Zero,
            new ValidationEngineError { Code = "ERR", Message = "Error", Category = ValidationErrorCategory.Schema });

        // Act
        _handler.CreateResponse(result, transaction);
        _handler.CreateResponse(result, transaction);
        var stats = _handler.GetStats();

        // Assert
        stats.TotalCreated.Should().Be(2);
        stats.ByCode.Should().ContainKey(ExceptionCode.SchemaViolation);
        stats.ByCode[ExceptionCode.SchemaViolation].Should().Be(2);
    }

    [Fact]
    public async Task GetStats_AfterRecordingToAuditLog_TracksDelivery()
    {
        // Arrange
        var response = CreateTestExceptionResponse();

        // Act
        await _handler.RecordToAuditLogAsync(response);
        var stats = _handler.GetStats();

        // Assert
        stats.TotalDelivered.Should().Be(1);
        stats.ByDeliveryMethod.Should().ContainKey(ExceptionDeliveryMethod.AuditLogOnly);
    }

    #endregion

    #region Summary Generation Tests

    [Fact]
    public void CreateResponse_WithSingleError_UsesSingleErrorMessage()
    {
        // Arrange
        var transaction = CreateTestTransaction();
        var result = ValidationEngineResult.Failure(
            transaction.TransactionId,
            transaction.RegisterId,
            TimeSpan.Zero,
            new ValidationEngineError
            {
                Code = "ERR",
                Message = "Specific error message",
                Category = ValidationErrorCategory.Schema
            });

        // Act
        var response = _handler.CreateResponse(result, transaction);

        // Assert
        response.Summary.Should().Be("Specific error message");
    }

    [Fact]
    public void CreateResponse_WithMultipleSameCategoryErrors_GeneratesCategorySummary()
    {
        // Arrange
        var transaction = CreateTestTransaction();
        var result = ValidationEngineResult.Failure(
            transaction.TransactionId,
            transaction.RegisterId,
            TimeSpan.Zero,
            new ValidationEngineError { Code = "E1", Message = "Error 1", Category = ValidationErrorCategory.Schema },
            new ValidationEngineError { Code = "E2", Message = "Error 2", Category = ValidationErrorCategory.Schema });

        // Act
        var response = _handler.CreateResponse(result, transaction);

        // Assert
        response.Summary.Should().Contain("2").And.Contain("Schema");
    }

    [Fact]
    public void CreateResponse_WithFatalError_PrioritizesFatalMessage()
    {
        // Arrange
        var transaction = CreateTestTransaction();
        var result = ValidationEngineResult.Failure(
            transaction.TransactionId,
            transaction.RegisterId,
            TimeSpan.Zero,
            new ValidationEngineError { Code = "E1", Message = "Minor issue", Category = ValidationErrorCategory.Schema, IsFatal = false },
            new ValidationEngineError { Code = "E2", Message = "Critical failure", Category = ValidationErrorCategory.Cryptographic, IsFatal = true });

        // Act
        var response = _handler.CreateResponse(result, transaction);

        // Assert
        response.Summary.Should().Contain("Critical failure");
    }

    #endregion

    #region Helper Methods

    private static Transaction CreateTestTransaction(string? txId = null)
    {
        return new Transaction
        {
            TransactionId = txId ?? "tx-123",
            RegisterId = "reg-456",
            BlueprintId = "bp-789",
            ActionId = "1",
            Payload = JsonSerializer.Deserialize<JsonElement>("{}"),
            PayloadHash = "abc123",
            CreatedAt = DateTimeOffset.UtcNow,
            Signatures =
            [
                new RegisterSignature
                {
                    PublicKey = new byte[] { 1, 2, 3 },
                    SignatureValue = new byte[] { 4, 5, 6 },
                    Algorithm = "ED25519",
                    SignedAt = DateTimeOffset.UtcNow
                }
            ]
        };
    }

    private static ExceptionResponse CreateTestExceptionResponse()
    {
        return new ExceptionResponse
        {
            ExceptionId = "exc_test123",
            TransactionId = "tx-123",
            RegisterId = "reg-456",
            BlueprintId = "bp-789",
            Code = ExceptionCode.SchemaViolation,
            Summary = "Test exception",
            Details =
            [
                new ExceptionDetail
                {
                    Code = "ERR",
                    Message = "Test error",
                    Category = ValidationErrorCategory.Schema
                }
            ]
        };
    }

    #endregion
}
