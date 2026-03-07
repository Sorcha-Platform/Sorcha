// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Sorcha.Blueprint.Service.Models.Requests;
using Sorcha.Blueprint.Service.Models.Responses;
using BlueprintModel = Sorcha.Blueprint.Models.Blueprint;
using ActionModel = Sorcha.Blueprint.Models.Action;
using ParticipantModel = Sorcha.Blueprint.Models.Participant;

namespace Sorcha.Blueprint.Service.Tests.Integration;

/// <summary>
/// Integration tests for Action API endpoints (Sprint 4)
/// </summary>
public class ActionApiIntegrationTests : IClassFixture<BlueprintServiceWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly BlueprintServiceWebApplicationFactory _factory;

    public ActionApiIntegrationTests(BlueprintServiceWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    #region GET /api/actions/{wallet}/{register}/blueprints

    [Fact]
    public async Task GetAvailableBlueprints_WithPublishedBlueprints_ReturnsBlueprints()
    {
        // Arrange
        var wallet = "test-wallet-001";
        var register = "test-register-001";

        // Create and publish a blueprint via the API
        var blueprint = CreateTestBlueprint();
        var createResponse = await _client.PostAsJsonAsync("/api/blueprints", blueprint);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<BlueprintModel>();

        // Publish the blueprint
        var publishResponse = await _client.PostAsync($"/api/blueprints/{created!.Id}/publish", null);
        publishResponse.EnsureSuccessStatusCode();

        // Act
        var response = await _client.GetAsync($"/api/actions/{wallet}/{register}/blueprints");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<AvailableBlueprintsResponse>();
        result.Should().NotBeNull();
        result!.WalletAddress.Should().Be(wallet);
        result.RegisterAddress.Should().Be(register);
        result.Blueprints.Should().NotBeEmpty();
        result.Blueprints.Should().Contain(b => b.BlueprintId == created.Id);
        result.Blueprints.First().AvailableActions.Should().HaveCount(2);
    }

    [Fact]
    public async Task GetAvailableBlueprints_WithDifferentWalletRegister_ReturnsValidResponse()
    {
        // Arrange - different wallet/register than other tests
        var wallet = "test-wallet-002";
        var register = "test-register-002";

        // Act
        var response = await _client.GetAsync($"/api/actions/{wallet}/{register}/blueprints");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<AvailableBlueprintsResponse>();
        result.Should().NotBeNull();
        result!.WalletAddress.Should().Be(wallet);
        result.RegisterAddress.Should().Be(register);
        // Note: Blueprints may not be empty due to test isolation
        // (other tests may have published blueprints)
        result.Blueprints.Should().NotBeNull();
    }

    #endregion

    #region POST /api/actions (Submit Action)

    [Fact]
    public async Task SubmitAction_WithValidRequest_ReturnsActionSubmissionResponse()
    {
        // Arrange
        var blueprint = await CreateAndPublishBlueprint();

        var submissionRequest = new ActionSubmissionRequest
        {
            BlueprintId = blueprint.Id,
            ActionId = "1", // Action ID 1 = Submit Loan Application
            SenderWallet = "wallet-applicant",
            RegisterAddress = "register-001",
            PayloadData = new Dictionary<string, object>
            {
                ["loanAmount"] = 50000,
                ["purpose"] = "Home Purchase",
                ["applicantName"] = "John Doe"
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/actions", submissionRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ActionSubmissionResponse>();
        result.Should().NotBeNull();
        result!.TransactionHash.Should().NotBeNullOrEmpty();
        result.InstanceId.Should().NotBeNullOrEmpty();
        result.SerializedTransaction.Should().NotBeNullOrEmpty();
        result.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task SubmitAction_WithFiles_CreatesFileTransactions()
    {
        // Arrange
        var blueprint = await CreateAndPublishBlueprint();

        var fileContent = "This is a test file content"u8.ToArray();
        var submissionRequest = new ActionSubmissionRequest
        {
            BlueprintId = blueprint.Id,
            ActionId = "1", // Action ID 1 = Submit Loan Application
            SenderWallet = "wallet-applicant",
            RegisterAddress = "register-001",
            PayloadData = new Dictionary<string, object>
            {
                ["loanAmount"] = 50000
            },
            Files = new List<FileAttachment>
            {
                new FileAttachment
                {
                    FileName = "application.pdf",
                    ContentType = "application/pdf",
                    ContentBase64 = Convert.ToBase64String(fileContent)
                }
            }
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/actions", submissionRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ActionSubmissionResponse>();
        result.Should().NotBeNull();
        result!.FileTransactionHashes.Should().NotBeNull();
        result.FileTransactionHashes.Should().HaveCount(1);
    }

    [Fact]
    public async Task SubmitAction_WithInvalidBlueprintId_ReturnsBadRequest()
    {
        // Arrange
        var submissionRequest = new ActionSubmissionRequest
        {
            BlueprintId = "non-existent-blueprint",
            ActionId = "1",
            SenderWallet = "wallet-applicant",
            RegisterAddress = "register-001",
            PayloadData = new Dictionary<string, object>()
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/actions", submissionRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SubmitAction_WithInvalidActionId_ReturnsBadRequest()
    {
        // Arrange
        var blueprint = await CreateAndPublishBlueprint();

        var submissionRequest = new ActionSubmissionRequest
        {
            BlueprintId = blueprint.Id,
            ActionId = "999", // Non-existent action ID
            SenderWallet = "wallet-applicant",
            RegisterAddress = "register-001",
            PayloadData = new Dictionary<string, object>()
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/actions", submissionRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    #endregion

    #region GET /api/actions/{wallet}/{register} (Paginated)

    [Fact]
    public async Task GetActions_WithExistingActions_ReturnsPaginatedResults()
    {
        // Arrange
        var wallet = "wallet-test-001";
        var register = "register-test-001";
        var blueprint = await CreateAndPublishBlueprint();

        // Submit multiple actions with unique InstanceId to avoid idempotency-key collisions
        for (int i = 0; i < 5; i++)
        {
            var request = new ActionSubmissionRequest
            {
                BlueprintId = blueprint.Id,
                ActionId = "1", // Action ID 1 = Submit Loan Application
                SenderWallet = wallet,
                RegisterAddress = register,
                InstanceId = $"instance-paginated-{i}",
                PayloadData = new Dictionary<string, object>
                {
                    ["iteration"] = i
                }
            };

            var postResponse = await _client.PostAsJsonAsync("/api/actions", request);
            postResponse.EnsureSuccessStatusCode();
        }

        // Act
        var response = await _client.GetAsync($"/api/actions/{wallet}/{register}?page=1&pageSize=3");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PagedResult>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.TotalCount.Should().Be(5);
        result.Page.Should().Be(1);
        result.PageSize.Should().Be(3);
        result.TotalPages.Should().Be(2);
    }

    [Fact]
    public async Task GetActions_WithNoActions_ReturnsEmptyResult()
    {
        // Arrange
        var wallet = "wallet-empty";
        var register = "register-empty";

        // Act
        var response = await _client.GetAsync($"/api/actions/{wallet}/{register}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var json = await response.Content.ReadAsStringAsync();
        var result = JsonSerializer.Deserialize<PagedResult>(json, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        result.Should().NotBeNull();
        result!.TotalCount.Should().Be(0);
    }

    #endregion

    #region GET /api/actions/{wallet}/{register}/{tx}

    [Fact]
    public async Task GetActionDetails_WithExistingAction_ReturnsActionDetails()
    {
        // Arrange
        var wallet = "wallet-details-test";
        var register = "register-details-test";
        var blueprint = await CreateAndPublishBlueprint();

        var submissionRequest = new ActionSubmissionRequest
        {
            BlueprintId = blueprint.Id,
            ActionId = "1",
            SenderWallet = wallet,
            RegisterAddress = register,
            PayloadData = new Dictionary<string, object>
            {
                ["test"] = "value"
            }
        };

        var submitResponse = await _client.PostAsJsonAsync("/api/actions", submissionRequest);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<ActionSubmissionResponse>();

        // Act
        var response = await _client.GetAsync($"/api/actions/{wallet}/{register}/{submitted!.TransactionHash}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ActionDetailsResponse>();
        result.Should().NotBeNull();
        result!.TransactionHash.Should().Be(submitted.TransactionHash);
        result.BlueprintId.Should().Be(blueprint.Id);
        result.ActionId.Should().Be("1");
        result.SenderWallet.Should().Be(wallet);
        result.RegisterAddress.Should().Be(register);
    }

    [Fact]
    public async Task GetActionDetails_WithNonExistentAction_ReturnsNotFound()
    {
        // Arrange
        var wallet = "wallet-notfound";
        var register = "register-notfound";
        var txHash = "non-existent-hash";

        // Act
        var response = await _client.GetAsync($"/api/actions/{wallet}/{register}/{txHash}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region POST /api/actions/reject

    [Fact]
    public async Task RejectAction_WithValidRequest_ReturnsRejectionResponse()
    {
        // Arrange
        var wallet = "wallet-reject-test";
        var register = "register-reject-test";
        var blueprint = await CreateAndPublishBlueprint();

        // Submit an action first
        var submissionRequest = new ActionSubmissionRequest
        {
            BlueprintId = blueprint.Id,
            ActionId = "1",
            SenderWallet = wallet,
            RegisterAddress = register,
            PayloadData = new Dictionary<string, object>()
        };

        var submitResponse = await _client.PostAsJsonAsync("/api/actions", submissionRequest);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<ActionSubmissionResponse>();

        // Reject the action
        var rejectionRequest = new ActionRejectionRequest
        {
            TransactionHash = submitted!.TransactionHash,
            Reason = "Insufficient documentation",
            SenderWallet = "wallet-officer",
            RegisterAddress = register
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/actions/reject", rejectionRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await response.Content.ReadFromJsonAsync<ActionSubmissionResponse>();
        result.Should().NotBeNull();
        result!.TransactionHash.Should().NotBeNullOrEmpty();
        result.TransactionHash.Should().NotBe(submitted.TransactionHash);
        result.InstanceId.Should().Be(submitted.InstanceId);
    }

    [Fact]
    public async Task RejectAction_WithNonExistentTransaction_ReturnsNotFound()
    {
        // Arrange
        var rejectionRequest = new ActionRejectionRequest
        {
            TransactionHash = "non-existent-tx",
            Reason = "Test rejection",
            SenderWallet = "wallet-test",
            RegisterAddress = "register-test"
        };

        // Act
        var response = await _client.PostAsJsonAsync("/api/actions/reject", rejectionRequest);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region GET /api/files/{wallet}/{register}/{tx}/{fileId}

    [Fact]
    public async Task GetFile_WithExistingFile_ReturnsFileContent()
    {
        // Arrange
        var wallet = "wallet-file-test";
        var register = "register-file-test";
        var blueprint = await CreateAndPublishBlueprint();

        var fileContent = "Test file content for integration test"u8.ToArray();
        var submissionRequest = new ActionSubmissionRequest
        {
            BlueprintId = blueprint.Id,
            ActionId = "1",
            SenderWallet = wallet,
            RegisterAddress = register,
            PayloadData = new Dictionary<string, object>(),
            Files = new List<FileAttachment>
            {
                new FileAttachment
                {
                    FileName = "test-document.txt",
                    ContentType = "text/plain",
                    ContentBase64 = Convert.ToBase64String(fileContent)
                }
            }
        };

        var submitResponse = await _client.PostAsJsonAsync("/api/actions", submissionRequest);
        var submitted = await submitResponse.Content.ReadFromJsonAsync<ActionSubmissionResponse>();

        var fileId = submitted!.FileTransactionHashes!.First();

        // Act
        var response = await _client.GetAsync($"/api/files/{wallet}/{register}/{submitted.TransactionHash}/{fileId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var content = await response.Content.ReadAsByteArrayAsync();
        content.Should().Equal(fileContent);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/plain");
    }

    [Fact]
    public async Task GetFile_WithNonExistentFile_ReturnsNotFound()
    {
        // Arrange
        var wallet = "wallet-file-notfound";
        var register = "register-file-notfound";
        var txHash = "non-existent-tx";
        var fileId = "non-existent-file";

        // Act
        var response = await _client.GetAsync($"/api/files/{wallet}/{register}/{txHash}/{fileId}");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    #endregion

    #region Helper Methods

    private BlueprintModel CreateTestBlueprint()
    {
        return new BlueprintModel
        {
            Title = "Loan Application Blueprint",
            Description = "A blueprint for loan application processing",
            Participants = new List<ParticipantModel>
            {
                new ParticipantModel
                {
                    Id = "applicant",
                    Name = "Loan Applicant"
                },
                new ParticipantModel
                {
                    Id = "loan-officer",
                    Name = "Loan Officer"
                }
            },
            Actions = new List<ActionModel>
            {
                new ActionModel
                {
                    Id = 1,
                    Title = "Submit Loan Application",
                    Description = "Applicant submits their loan application",
                    Sender = "applicant"
                },
                new ActionModel
                {
                    Id = 2,
                    Title = "Review Application",
                    Description = "Loan officer reviews the application",
                    Sender = "loan-officer"
                }
            }
        };
    }

    private async Task<BlueprintModel> CreateAndPublishBlueprint()
    {
        var blueprint = CreateTestBlueprint();

        // Create blueprint
        var createResponse = await _client.PostAsJsonAsync("/api/blueprints", blueprint);
        createResponse.EnsureSuccessStatusCode();
        var created = await createResponse.Content.ReadFromJsonAsync<BlueprintModel>();

        // Publish blueprint
        var publishResponse = await _client.PostAsync($"/api/blueprints/{created!.Id}/publish", null);
        publishResponse.EnsureSuccessStatusCode();

        return created;
    }

    #endregion

    #region Helper DTOs for Deserialization

    private class PagedResult
    {
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    #endregion
}
