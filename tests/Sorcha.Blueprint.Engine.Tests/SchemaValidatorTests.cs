// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Text.Json.Nodes;
using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Engine.Interfaces;

namespace Sorcha.Blueprint.Engine.Tests;

/// <summary>
/// Unit tests for SchemaValidator.
/// </summary>
public class SchemaValidatorTests
{
    private readonly ISchemaValidator _validator;

    public SchemaValidatorTests()
    {
        _validator = new SchemaValidator();
    }

    [Fact]
    public async Task ValidateAsync_ValidData_ReturnsValid()
    {
        // Arrange
        var schema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "name": { "type": "string" },
                "age": { "type": "number" }
            },
            "required": ["name"]
        }
        """);

        var data = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["age"] = 30
        };

        // Act
        var result = await _validator.ValidateAsync(data, schema!);

        // Assert
        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_SorchaHolderKeyField_ValidatesAsPass()
    {
        // Feature 137 (T019 lock-in): the action-data validator must tolerate an object field
        // declared with format "sorcha-holder-key" and an x-holder-key extension — unknown formats
        // pass under RequireFormatValidation and x- keywords are ignored as annotations, exactly
        // like the existing x-file / file-reference fields on the same blueprint.
        var schema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "name": { "type": "string" },
                "holderKeys": {
                    "type": "object",
                    "format": "sorcha-holder-key",
                    "x-holder-key": { "required": true }
                }
            },
            "required": ["name", "holderKeys"]
        }
        """);

        var data = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["holderKeys"] = new Dictionary<string, object>
            {
                ["holderJwk"] = new Dictionary<string, object>
                {
                    ["kty"] = "EC", ["crv"] = "P-256", ["x"] = "AAA", ["y"] = "BBB"
                },
                ["encryptionPublicKey"] = "QkFTRTY0",
                ["algorithm"] = "ED25519"
            }
        };

        var result = await _validator.ValidateAsync(data, schema!);

        result.IsValid.Should().BeTrue(
            "holder-key fields must validate as pass; errors: {0}",
            string.Join(" | ", result.Errors.Select(e => $"{e.InstanceLocation}:{e.Message}")));
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_FileReferenceFieldWithObjectValue_ValidatesAsPass()
    {
        // A field declared { "type": "string", "format": "file-reference" } carries an OBJECT at
        // runtime (the FileReference plus, for F107 portrait capture, a tokenImageBase64 sibling),
        // never a plain string. The "type" constraint must be relaxed for file-reference fields or
        // the object value spuriously fails ("Value is object but should be string"). Structural
        // integrity is enforced separately by FileReferenceValidator. Regression for the AIAS
        // portrait-seal bug (Feature 174): a portrait-bearing Action 1 was rejected by the validator
        // (VAL_SCHEMA_004) and therefore never sealed.
        var schema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "name": { "type": "string" },
                "portrait": {
                    "type": "string",
                    "format": "file-reference",
                    "x-file": { "accept": ["image/jpeg"], "embedAs": "image-token-jpeg-240x320" }
                }
            },
            "required": ["name"]
        }
        """);

        var data = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["portrait"] = new Dictionary<string, object>
            {
                ["tokenImageBase64"] = "/9j/4AAQSkZJRg=="
            }
        };

        var result = await _validator.ValidateAsync(data, schema!);

        result.IsValid.Should().BeTrue(
            "file-reference fields carry an object value and must validate as pass; errors: {0}",
            string.Join(" | ", result.Errors.Select(e => $"{e.InstanceLocation}:{e.Message}")));
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_MissingRequiredField_ReturnsInvalid()
    {
        // Arrange
        var schema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "name": { "type": "string" },
                "email": { "type": "string" }
            },
            "required": ["name", "email"]
        }
        """);

        var data = new Dictionary<string, object>
        {
            ["name"] = "Alice"
            // email is missing
        };

        // Act
        var result = await _validator.ValidateAsync(data, schema!);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
        result.Errors.Should().Contain(e => e.Keyword == "required");
    }

    [Fact]
    public async Task ValidateAsync_WrongType_ReturnsInvalid()
    {
        // Arrange
        var schema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "age": { "type": "number" }
            }
        }
        """);

        var data = new Dictionary<string, object>
        {
            ["age"] = "thirty" // string instead of number
        };

        // Act
        var result = await _validator.ValidateAsync(data, schema!);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
        result.Errors.Should().Contain(e => e.Keyword == "type");
    }

    [Fact]
    public async Task ValidateAsync_NestedObjects_ValidatesDeep()
    {
        // Arrange
        var schema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "user": {
                    "type": "object",
                    "properties": {
                        "name": { "type": "string" },
                        "address": {
                            "type": "object",
                            "properties": {
                                "city": { "type": "string" },
                                "zipCode": { "type": "string", "pattern": "^[0-9]{5}$" }
                            },
                            "required": ["city", "zipCode"]
                        }
                    },
                    "required": ["name", "address"]
                }
            },
            "required": ["user"]
        }
        """);

        var data = new Dictionary<string, object>
        {
            ["user"] = new Dictionary<string, object>
            {
                ["name"] = "Alice",
                ["address"] = new Dictionary<string, object>
                {
                    ["city"] = "Springfield",
                    ["zipCode"] = "12345"
                }
            }
        };

        // Act
        var result = await _validator.ValidateAsync(data, schema!);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_NestedObjectInvalidPattern_ReturnsInvalid()
    {
        // Arrange
        var schema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "user": {
                    "type": "object",
                    "properties": {
                        "address": {
                            "type": "object",
                            "properties": {
                                "zipCode": { "type": "string", "pattern": "^[0-9]{5}$" }
                            }
                        }
                    }
                }
            }
        }
        """);

        var data = new Dictionary<string, object>
        {
            ["user"] = new Dictionary<string, object>
            {
                ["address"] = new Dictionary<string, object>
                {
                    ["zipCode"] = "ABC123" // invalid pattern
                }
            }
        };

        // Act
        var result = await _validator.ValidateAsync(data, schema!);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
        result.Errors.Should().Contain(e => e.Keyword == "pattern");
        result.Errors.Should().Contain(e => e.InstanceLocation.Contains("zipCode"));
    }

    [Fact]
    public async Task ValidateAsync_Arrays_ValidatesItems()
    {
        // Arrange
        var schema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "tags": {
                    "type": "array",
                    "items": { "type": "string" },
                    "minItems": 1,
                    "maxItems": 5
                }
            }
        }
        """);

        var data = new Dictionary<string, object>
        {
            ["tags"] = new List<string> { "tag1", "tag2", "tag3" }
        };

        // Act
        var result = await _validator.ValidateAsync(data, schema!);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ArrayItemWrongType_ReturnsInvalid()
    {
        // Arrange
        var schema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "scores": {
                    "type": "array",
                    "items": { "type": "number" }
                }
            }
        }
        """);

        var data = new Dictionary<string, object>
        {
            ["scores"] = new List<object> { 100, 95, "eighty" } // string in number array
        };

        // Act
        var result = await _validator.ValidateAsync(data, schema!);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().NotBeEmpty();
    }

    [Fact]
    public async Task ValidateAsync_MinimumConstraint_Validates()
    {
        // Arrange
        var schema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "age": { 
                    "type": "number",
                    "minimum": 18
                }
            }
        }
        """);

        var validData = new Dictionary<string, object> { ["age"] = 25 };
        var invalidData = new Dictionary<string, object> { ["age"] = 15 };

        // Act
        var validResult = await _validator.ValidateAsync(validData, schema!);
        var invalidResult = await _validator.ValidateAsync(invalidData, schema!);

        // Assert
        validResult.IsValid.Should().BeTrue();
        invalidResult.IsValid.Should().BeFalse();
        invalidResult.Errors.Should().Contain(e => e.Keyword == "minimum");
    }

    [Fact]
    public async Task ValidateAsync_StringLength_Validates()
    {
        // Arrange
        var schema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "username": { 
                    "type": "string",
                    "minLength": 3,
                    "maxLength": 20
                }
            }
        }
        """);

        var validData = new Dictionary<string, object> { ["username"] = "alice" };
        var tooShort = new Dictionary<string, object> { ["username"] = "ab" };
        var tooLong = new Dictionary<string, object> { ["username"] = "abcdefghijklmnopqrstuvwxyz" };

        // Act
        var validResult = await _validator.ValidateAsync(validData, schema!);
        var shortResult = await _validator.ValidateAsync(tooShort, schema!);
        var longResult = await _validator.ValidateAsync(tooLong, schema!);

        // Assert
        validResult.IsValid.Should().BeTrue();
        shortResult.IsValid.Should().BeFalse();
        shortResult.Errors.Should().Contain(e => e.Keyword == "minLength");
        longResult.IsValid.Should().BeFalse();
        longResult.Errors.Should().Contain(e => e.Keyword == "maxLength");
    }

    [Fact]
    public async Task ValidateAsync_EmailFormat_Validates()
    {
        // Arrange
        var schema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "email": { 
                    "type": "string",
                    "format": "email"
                }
            }
        }
        """);

        var validData = new Dictionary<string, object> { ["email"] = "alice@example.com" };
        var invalidData = new Dictionary<string, object> { ["email"] = "not-an-email" };

        // Act
        var validResult = await _validator.ValidateAsync(validData, schema!);
        var invalidResult = await _validator.ValidateAsync(invalidData, schema!);

        // Assert
        validResult.IsValid.Should().BeTrue();
        invalidResult.IsValid.Should().BeFalse();
        invalidResult.Errors.Should().Contain(e => e.Keyword == "format");
    }

    [Fact]
    public async Task ValidateAsync_EmptyData_ValidatesAsEmptyObject()
    {
        // Arrange
        var schema = JsonNode.Parse("""
        {
            "type": "object"
        }
        """);

        var data = new Dictionary<string, object>();

        // Act
        var result = await _validator.ValidateAsync(data, schema!);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ComplexSchema_ValidatesCorrectly()
    {
        // Arrange
        var schema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "orderId": { "type": "string" },
                "customer": {
                    "type": "object",
                    "properties": {
                        "name": { "type": "string" },
                        "email": { "type": "string", "format": "email" }
                    },
                    "required": ["name", "email"]
                },
                "items": {
                    "type": "array",
                    "items": {
                        "type": "object",
                        "properties": {
                            "productId": { "type": "string" },
                            "quantity": { "type": "number", "minimum": 1 },
                            "price": { "type": "number", "minimum": 0 }
                        },
                        "required": ["productId", "quantity", "price"]
                    },
                    "minItems": 1
                },
                "total": { "type": "number", "minimum": 0 }
            },
            "required": ["orderId", "customer", "items", "total"]
        }
        """);

        var data = new Dictionary<string, object>
        {
            ["orderId"] = "ORD-12345",
            ["customer"] = new Dictionary<string, object>
            {
                ["name"] = "Alice Johnson",
                ["email"] = "alice@example.com"
            },
            ["items"] = new List<object>
            {
                new Dictionary<string, object>
                {
                    ["productId"] = "PROD-001",
                    ["quantity"] = 2,
                    ["price"] = 29.99
                },
                new Dictionary<string, object>
                {
                    ["productId"] = "PROD-002",
                    ["quantity"] = 1,
                    ["price"] = 49.99
                }
            },
            ["total"] = 109.97
        };

        // Act
        var result = await _validator.ValidateAsync(data, schema!);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_NullData_ThrowsArgumentNullException()
    {
        // Arrange
        var schema = JsonNode.Parse("""{ "type": "object" }""");

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _validator.ValidateAsync(null!, schema!)
        );
    }

    [Fact]
    public async Task ValidateAsync_NullSchema_ThrowsArgumentNullException()
    {
        // Arrange
        var data = new Dictionary<string, object>();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _validator.ValidateAsync(data, null!)
        );
    }

    #region 4.1 Blueprint DataSchemas Tests

    [Fact]
    public async Task ValidateAsync_BlueprintWithEmbeddedDataSchemas_ValidatesCorrectly()
    {
        // Arrange
        var blueprintSchema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "person": {
                    "type": "object",
                    "properties": {
                        "name": { "type": "string" },
                        "age": { "type": "number", "minimum": 0 }
                    },
                    "required": ["name"]
                }
            }
        }
        """);

        var data = new Dictionary<string, object>
        {
            ["person"] = new Dictionary<string, object>
            {
                ["name"] = "Alice",
                ["age"] = 30
            }
        };

        // Act
        var result = await _validator.ValidateAsync(data, blueprintSchema!);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_BlueprintDataSchemasReferencedByAction_ValidatesCorrectly()
    {
        // Arrange - Action data must match Blueprint.DataSchemas
        var actionSchema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "applicationData": {
                    "type": "object",
                    "properties": {
                        "applicantName": { "type": "string" },
                        "applicantEmail": { "type": "string", "format": "email" },
                        "requestedAmount": { "type": "number", "minimum": 1 }
                    },
                    "required": ["applicantName", "applicantEmail", "requestedAmount"]
                }
            },
            "required": ["applicationData"]
        }
        """);

        var actionData = new Dictionary<string, object>
        {
            ["applicationData"] = new Dictionary<string, object>
            {
                ["applicantName"] = "Bob Smith",
                ["applicantEmail"] = "bob@example.com",
                ["requestedAmount"] = 50000
            }
        };

        // Act
        var result = await _validator.ValidateAsync(actionData, actionSchema!);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_InvalidJSONSchemaInBlueprint_FailsValidation()
    {
        // Arrange - Schema with invalid JSON Schema syntax
        var invalidSchema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "field": {
                    "type": "invalid-type"
                }
            }
        }
        """);

        var data = new Dictionary<string, object>
        {
            ["field"] = "value"
        };

        // Act
        var result = await _validator.ValidateAsync(data, invalidSchema!);

        // Assert - SchemaValidator should handle invalid schema types
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task ValidateAsync_EmptyDataSchemasArray_AllowsAnyData()
    {
        // Arrange - Empty schema = no validation constraints
        var emptySchema = JsonNode.Parse("""
        {
            "type": "object"
        }
        """);

        var anyData = new Dictionary<string, object>
        {
            ["anything"] = "goes",
            ["number"] = 123,
            ["nested"] = new Dictionary<string, object>
            {
                ["value"] = true
            }
        };

        // Act
        var result = await _validator.ValidateAsync(anyData, emptySchema!);

        // Assert
        result.IsValid.Should().BeTrue("Empty schema should allow any object");
    }

    #endregion

    #region 4.2 Action DataSchemas Tests

    [Fact]
    public async Task ValidateAsync_ActionWithValidDataSchemas_PassesValidation()
    {
        // Arrange - Action schema with specific structure
        var actionSchema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "actionType": { "type": "string", "enum": ["submit", "approve", "reject"] },
                "comments": { "type": "string", "maxLength": 500 },
                "timestamp": { "type": "string", "format": "date-time" }
            },
            "required": ["actionType"]
        }
        """);

        var actionData = new Dictionary<string, object>
        {
            ["actionType"] = "approve",
            ["comments"] = "Looks good!",
            ["timestamp"] = "2025-11-23T10:30:00Z"
        };

        // Act
        var result = await _validator.ValidateAsync(actionData, actionSchema!);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ActionDataSchemaDraft2020_12_Validates()
    {
        // Arrange - JSON Schema Draft 2020-12 features
        var modernSchema = JsonNode.Parse("""
        {
            "$schema": "https://json-schema.org/draft/2020-12/schema",
            "type": "object",
            "properties": {
                "name": { "type": "string", "minLength": 1 },
                "email": { "type": "string", "format": "email" }
            },
            "required": ["name", "email"]
        }
        """);

        var validData = new Dictionary<string, object>
        {
            ["name"] = "Alice",
            ["email"] = "alice@example.com"
        };

        // Act
        var result = await _validator.ValidateAsync(validData, modernSchema!);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_ActionDataMustValidateAgainstSchema_FailsOnMismatch()
    {
        // Arrange
        var actionSchema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "amount": { "type": "number", "minimum": 0, "maximum": 10000 },
                "currency": { "type": "string", "enum": ["USD", "EUR", "GBP"] }
            },
            "required": ["amount", "currency"]
        }
        """);

        var invalidData = new Dictionary<string, object>
        {
            ["amount"] = 15000, // exceeds maximum
            ["currency"] = "JPY" // not in enum
        };

        // Act
        var result = await _validator.ValidateAsync(invalidData, actionSchema!);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Keyword == "maximum");
        result.Errors.Should().Contain(e => e.Keyword == "enum");
    }

    [Fact]
    public async Task ValidateAsync_MultipleDataSchemasInAction_ValidatesAll()
    {
        // Arrange - Complex action with multiple schema validations
        var complexSchema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "personalInfo": {
                    "type": "object",
                    "properties": {
                        "firstName": { "type": "string" },
                        "lastName": { "type": "string" }
                    },
                    "required": ["firstName", "lastName"]
                },
                "contactInfo": {
                    "type": "object",
                    "properties": {
                        "email": { "type": "string", "format": "email" },
                        "phone": { "type": "string", "pattern": "^[0-9]{10}$" }
                    },
                    "required": ["email"]
                },
                "metadata": {
                    "type": "object",
                    "properties": {
                        "submittedAt": { "type": "string", "format": "date-time" },
                        "version": { "type": "number" }
                    }
                }
            },
            "required": ["personalInfo", "contactInfo"]
        }
        """);

        var data = new Dictionary<string, object>
        {
            ["personalInfo"] = new Dictionary<string, object>
            {
                ["firstName"] = "John",
                ["lastName"] = "Doe"
            },
            ["contactInfo"] = new Dictionary<string, object>
            {
                ["email"] = "john@example.com",
                ["phone"] = "5551234567"
            },
            ["metadata"] = new Dictionary<string, object>
            {
                ["submittedAt"] = "2025-11-23T10:30:00Z",
                ["version"] = 1
            }
        };

        // Act
        var result = await _validator.ValidateAsync(data, complexSchema!);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    #endregion

    #region 4.3 PreviousData Validation Tests

    [Fact]
    public async Task ValidateAsync_PreviousDataMatchesPreviousActionSchema_Passes()
    {
        // Arrange - Previous action's schema
        var previousActionSchema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "requestId": { "type": "string" },
                "status": { "type": "string", "enum": ["pending", "approved", "rejected"] }
            },
            "required": ["requestId", "status"]
        }
        """);

        // Current action's previousData must match previous action's output
        var previousData = new Dictionary<string, object>
        {
            ["requestId"] = "REQ-12345",
            ["status"] = "pending"
        };

        // Act
        var result = await _validator.ValidateAsync(previousData, previousActionSchema!);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_FirstActionNullPreviousData_IsValid()
    {
        // Arrange - First action (ID 0) can have null/empty previousData
        var actionSchema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "initialData": { "type": "string" }
            }
        }
        """);

        // First action starts the workflow - no previous data
        var initialData = new Dictionary<string, object>
        {
            ["initialData"] = "Starting workflow"
        };

        // Act
        var result = await _validator.ValidateAsync(initialData, actionSchema!);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public async Task ValidateAsync_NonFirstActionWithNullPreviousData_ValidatesBasedOnWorkflow()
    {
        // Arrange - Some workflows allow null previousData for branching
        var actionSchema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "actionData": { "type": "string" }
            }
        }
        """);

        // Branching action might not have previousData from primary chain
        var branchData = new Dictionary<string, object>
        {
            ["actionData"] = "Branch workflow"
        };

        // Act
        var result = await _validator.ValidateAsync(branchData, actionSchema!);

        // Assert
        result.IsValid.Should().BeTrue("Branching workflows may have independent data");
    }

    [Fact]
    public async Task ValidateAsync_PreviousDataSchemaMismatch_FailsValidation()
    {
        // Arrange - Previous action expected different schema
        var previousActionSchema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "approvalCode": { "type": "string", "pattern": "^[A-Z]{4}-[0-9]{4}$" },
                "approvedBy": { "type": "string" }
            },
            "required": ["approvalCode", "approvedBy"]
        }
        """);

        // Current action's previousData doesn't match
        var mismatchedData = new Dictionary<string, object>
        {
            ["approvalCode"] = "invalid-format", // wrong pattern
            ["approvedBy"] = "Alice"
        };

        // Act
        var result = await _validator.ValidateAsync(mismatchedData, previousActionSchema!);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Keyword == "pattern");
    }

    [Fact]
    public async Task ValidateAsync_PreviousDataMissingRequiredField_FailsValidation()
    {
        // Arrange
        var previousSchema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "transactionId": { "type": "string" },
                "amount": { "type": "number" },
                "signature": { "type": "string" }
            },
            "required": ["transactionId", "amount", "signature"]
        }
        """);

        var incompleteData = new Dictionary<string, object>
        {
            ["transactionId"] = "TX-001",
            ["amount"] = 100
            // missing required "signature"
        };

        // Act
        var result = await _validator.ValidateAsync(incompleteData, previousSchema!);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Keyword == "required");
        result.Errors.Should().Contain(e => e.Message.Contains("signature"));
    }

    [Fact]
    public async Task ValidateAsync_ChainedActionsDataFlow_ValidatesCorrectly()
    {
        // Arrange - Simulating data flowing through action chain
        var action1Schema = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "step1Data": { "type": "string" }
            },
            "required": ["step1Data"]
        }
        """);

        var action2ExpectsPreviousData = JsonNode.Parse("""
        {
            "type": "object",
            "properties": {
                "step1Data": { "type": "string" },
                "step2Data": { "type": "string" }
            },
            "required": ["step1Data", "step2Data"]
        }
        """);

        // Action 1 output
        var action1Output = new Dictionary<string, object>
        {
            ["step1Data"] = "Data from step 1"
        };

        // Action 2 receives action1Output as previousData + adds new data
        var action2Data = new Dictionary<string, object>
        {
            ["step1Data"] = "Data from step 1", // from previousData
            ["step2Data"] = "Data from step 2"  // new data
        };

        // Act
        var action1Result = await _validator.ValidateAsync(action1Output, action1Schema!);
        var action2Result = await _validator.ValidateAsync(action2Data, action2ExpectsPreviousData!);

        // Assert
        action1Result.IsValid.Should().BeTrue();
        action2Result.IsValid.Should().BeTrue();
    }

    #endregion
}
