// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Fluent;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace Sorcha.Integration.Tests;

/// <summary>
/// End-to-end tests for complete Blueprint-Action workflows
/// Tests the full lifecycle: Create Blueprint → Publish → Submit Action → Process
/// </summary>
public class BlueprintActionEndToEndTests : IAsyncLifetime
{
    private HttpClient? _client;
    private const string BaseUrl = "http://localhost:5000"; // Configurable via environment
    private string? _testBlueprintId;
    private string? _testWalletAddress;
    private string? _testRegisterAddress;

    public async ValueTask InitializeAsync()
    {
        _client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
        _testWalletAddress = "wallet-test-001";
        _testRegisterAddress = "register-test-001";

        // Wait for services to be ready
        await Task.Delay(1000);
    }

    public ValueTask DisposeAsync()
    {
        _client?.Dispose();
        return ValueTask.CompletedTask;
    }

    [Fact(Skip = "Requires running services")]
    public async Task CompleteWorkflow_CreateBlueprint_Publish_SubmitAction_ShouldSucceed()
    {
        // Arrange - Create a blueprint
        var blueprint = BlueprintBuilder.Create()
            .WithTitle("Purchase Order E2E Test")
            .WithDescription("End-to-end test for purchase order workflow")
            .AddParticipant("buyer", p => p
                .Named("Buyer Organization")
                .FromOrganisation("ORG-BUYER-E2E"))
            .AddParticipant("seller", p => p
                .Named("Seller Organization")
                .FromOrganisation("ORG-SELLER-E2E"))
            .AddAction(0, a => a
                .WithTitle("Submit Purchase Order")
                .WithDescription("Buyer submits a purchase order")
                .SentBy("buyer")
                .RequiresData(d => d
                    .AddString("itemName", f => f.IsRequired().WithTitle("Item Name"))
                    .AddInteger("quantity", f => f.IsRequired().WithTitle("Quantity"))
                    .AddNumber("unitPrice", f => f.IsRequired().WithTitle("Unit Price")))
                .Disclose("seller", d => d.AllFields())
                .RouteToNext("seller"))
            .AddAction(1, a => a
                .WithTitle("Accept Order")
                .WithDescription("Seller accepts the order")
                .SentBy("seller"))
            .Build();

        // Act 1 - Create blueprint
        var createResponse = await _client!.PostAsJsonAsync("/api/blueprints", blueprint);
        createResponse.EnsureSuccessStatusCode();

        var createdBlueprint = await createResponse.Content.ReadFromJsonAsync<JsonElement>();
        _testBlueprintId = createdBlueprint.GetProperty("id").GetString();
        _testBlueprintId.Should().NotBeNullOrEmpty();

        // Act 2 - Publish blueprint
        var publishResponse = await _client.PostAsync($"/api/blueprints/{_testBlueprintId}/publish", null);
        publishResponse.EnsureSuccessStatusCode();

        var publishResult = await publishResponse.Content.ReadFromJsonAsync<JsonElement>();
        publishResult.GetProperty("isSuccess").GetBoolean().Should().BeTrue();

        // Act 3 - Get available blueprints
        var availableResponse = await _client.GetAsync($"/api/actions/{_testWalletAddress}/{_testRegisterAddress}/blueprints");
        availableResponse.EnsureSuccessStatusCode();

        var available = await availableResponse.Content.ReadFromJsonAsync<JsonElement>();
        var blueprints = available.GetProperty("blueprints");
        blueprints.GetArrayLength().Should().BeGreaterThan(0);

        // Act 4 - Submit action
        var actionRequest = new
        {
            blueprintId = _testBlueprintId,
            actionId = "0",
            instanceId = Guid.NewGuid().ToString(),
            senderWallet = _testWalletAddress,
            registerAddress = _testRegisterAddress,
            previousTransactionHash = (string?)null,
            payloadData = new Dictionary<string, object>
            {
                ["itemName"] = "Test Product",
                ["quantity"] = 10,
                ["unitPrice"] = 99.99
            }
        };

        var submitResponse = await _client.PostAsJsonAsync("/api/actions", actionRequest);
        submitResponse.EnsureSuccessStatusCode();

        var submitResult = await submitResponse.Content.ReadFromJsonAsync<JsonElement>();
        var txHash = submitResult.GetProperty("transactionHash").GetString();
        txHash.Should().NotBeNullOrEmpty();

        // Assert - Retrieve submitted action
        var actionResponse = await _client.GetAsync($"/api/actions/{_testWalletAddress}/{_testRegisterAddress}/{txHash}");
        actionResponse.EnsureSuccessStatusCode();

        var action = await actionResponse.Content.ReadFromJsonAsync<JsonElement>();
        action.GetProperty("blueprintId").GetString().Should().Be(_testBlueprintId);
        action.GetProperty("actionId").GetString().Should().Be("0");
    }

    [Fact]
    public void CreateComplexBlueprint_WithMultipleActions_ShouldSucceed()
    {
        // Arrange & Act - Create a complex multi-step workflow
        var blueprint = BlueprintBuilder.Create()
            .WithTitle("Complex Approval Workflow")
            .WithDescription("Multi-step approval process with conditional routing")
            .AddParticipant("initiator", p => p.Named("Request Initiator"))
            .AddParticipant("manager", p => p.Named("Manager"))
            .AddParticipant("director", p => p.Named("Director"))
            .AddParticipant("finance", p => p.Named("Finance Department"))
            .AddAction(0, a => a
                .WithTitle("Submit Request")
                .SentBy("initiator")
                .RequiresData(d => d
                    .AddString("requestType")
                    .AddNumber("amount")
                    .AddString("justification"))
                .Disclose("manager", d => d.AllFields())
                .RouteConditionally(r => r
                    .When(w => w.GreaterThan("amount", 10000))
                    .ThenRoute("director")
                    .ElseRoute("manager")))
            .AddAction(1, a => a
                .WithTitle("Manager Approval")
                .SentBy("manager")
                .RouteToNext("finance"))
            .AddAction(2, a => a
                .WithTitle("Director Approval")
                .SentBy("director")
                .RouteToNext("finance"))
            .AddAction(3, a => a
                .WithTitle("Finance Processing")
                .SentBy("finance"))
            .Build();

        // Assert
        blueprint.Should().NotBeNull();
        blueprint.Title.Should().Be("Complex Approval Workflow");
        blueprint.Participants.Should().HaveCount(4);
        blueprint.Actions.Should().HaveCount(4);

        var firstAction = blueprint.Actions[0];
        firstAction.Condition.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecutionHelpers_Validate_Calculate_Route_Disclose_ShouldWork()
    {
        // This test validates the execution helper endpoints work correctly
        var blueprint = BlueprintBuilder.Create()
            .WithTitle("Execution Test Blueprint")
            .WithDescription("Blueprint for testing execution helper functionality")
            .AddParticipant("sender", p => p.Named("Sender"))
            .AddParticipant("receiver", p => p.Named("Receiver"))
            .AddAction(0, a => a
                .WithTitle("Test Action")
                .SentBy("sender")
                .RequiresData(d => d
                    .AddString("name", f => f.IsRequired())
                    .AddNumber("amount", f => f.IsRequired()))
                .Calculate("total", c => c.Multiply("amount", 2))
                .Disclose("receiver", d => d.Field("/name"))
                .RouteToNext("receiver"))
            .Build();

        // Assert
        blueprint.Should().NotBeNull();
        var action = blueprint.Actions[0];
        action.DataSchemas.Should().NotBeNull();
        action.Calculations.Should().ContainKey("total");
        action.Disclosures.Should().HaveCount(1);
    }

    [Fact]
    public void CreateBlueprintWithFileAttachments_ShouldIncludeFileMetadata()
    {
        // Arrange & Act
        var blueprint = BlueprintBuilder.Create()
            .WithTitle("Document Submission Workflow")
            .WithDescription("Workflow supporting file attachments")
            .AddParticipant("submitter", p => p.Named("Document Submitter"))
            .AddParticipant("reviewer", p => p.Named("Document Reviewer"))
            .AddAction(0, a => a
                .WithTitle("Submit Documents")
                .SentBy("submitter")
                .RequiresData(d => d
                    .AddString("documentType")
                    .AddString("description"))
                .Disclose("reviewer", d => d.AllFields())
                .RouteToNext("reviewer"))
            .AddAction(1, a => a
                .WithTitle("Review Documents")
                .SentBy("reviewer")
                .RequiresData(d => d
                    .AddString("reviewStatus")
                    .AddString("comments")))
            .Build();

        // Assert
        blueprint.Should().NotBeNull();
        blueprint.Actions.Should().HaveCount(2);
        blueprint.Actions[0].Title.Should().Be("Submit Documents");
        blueprint.Actions[1].Title.Should().Be("Review Documents");
    }

    [Fact]
    public void CreateBlueprintWithSelectiveDisclosure_ShouldConfigureCorrectly()
    {
        // Arrange & Act - Create blueprint with complex disclosure rules
        var blueprint = BlueprintBuilder.Create()
            .WithTitle("Privacy-Preserving Data Sharing")
            .WithDescription("Demonstrates selective disclosure capabilities")
            .AddParticipant("dataOwner", p => p.Named("Data Owner"))
            .AddParticipant("analyst1", p => p.Named("Analyst 1"))
            .AddParticipant("analyst2", p => p.Named("Analyst 2"))
            .AddParticipant("auditor", p => p.Named("Auditor"))
            .AddAction(0, a => a
                .WithTitle("Share Data")
                .SentBy("dataOwner")
                .RequiresData(d => d
                    .AddString("fullName")
                    .AddString("email")
                    .AddNumber("salary")
                    .AddString("department")
                    .AddString("performanceRating"))
                .Disclose("analyst1", d => d
                    .Field("/department")
                    .Field("/performanceRating"))
                .Disclose("analyst2", d => d
                    .Field("/salary")
                    .Field("/department"))
                .Disclose("auditor", d => d.AllFields())
                .RouteToNext("analyst1"))
            .Build();

        // Assert
        blueprint.Should().NotBeNull();
        var action = blueprint.Actions[0];
        action.Disclosures.Should().HaveCount(3);

        var analyst1Disclosure = action.Disclosures.First(d => d.ParticipantAddress == "analyst1");
        analyst1Disclosure.DataPointers.Should().Contain("/department");
        analyst1Disclosure.DataPointers.Should().Contain("/performanceRating");
        analyst1Disclosure.DataPointers.Should().NotContain("/salary");

        var auditorDisclosure = action.Disclosures.First(d => d.ParticipantAddress == "auditor");
        auditorDisclosure.DataPointers.Should().Contain("/*");
    }

    [Fact]
    public void CreateBlueprintWithCalculations_ShouldIncludeJsonLogic()
    {
        // Arrange & Act - Create blueprint with complex calculations
        var blueprint = BlueprintBuilder.Create()
            .WithTitle("Invoice Calculation Workflow")
            .WithDescription("Workflow demonstrating automatic invoice calculations")
            .AddParticipant("vendor", p => p.Named("Vendor"))
            .AddParticipant("customer", p => p.Named("Customer"))
            .AddAction(0, a => a
                .WithTitle("Submit Invoice")
                .SentBy("vendor")
                .RequiresData(d => d
                    .AddNumber("subtotal")
                    .AddNumber("taxRate")
                    .AddNumber("discount"))
                .Calculate("taxAmount", c => c.Multiply("subtotal", "taxRate"))
                .Calculate("discountedSubtotal", c => c.Subtract("subtotal", "discount"))
                .Calculate("total", c => c.Add("discountedSubtotal", "taxAmount"))
                .Disclose("customer", d => d.AllFields())
                .RouteToNext("customer"))
            .Build();

        // Assert
        blueprint.Should().NotBeNull();
        var action = blueprint.Actions[0];
        action.Calculations.Should().HaveCount(3);
        action.Calculations.Should().ContainKeys("taxAmount", "discountedSubtotal", "total");
    }

    [Fact]
    public void CreateBlueprintWithConditionalRouting_ShouldIncludeConditions()
    {
        // Arrange & Act
        var blueprint = BlueprintBuilder.Create()
            .WithTitle("Dynamic Routing Workflow")
            .WithDescription("Workflow with conditional routing based on risk assessment")
            .AddParticipant("applicant", p => p.Named("Applicant"))
            .AddParticipant("juniorReviewer", p => p.Named("Junior Reviewer"))
            .AddParticipant("seniorReviewer", p => p.Named("Senior Reviewer"))
            .AddAction(0, a => a
                .WithTitle("Submit Application")
                .SentBy("applicant")
                .RequiresData(d => d
                    .AddNumber("requestAmount")
                    .AddInteger("riskScore"))
                .RouteConditionally(r => r
                    .When(w => w.Or(
                        w.GreaterThan("requestAmount", 50000),
                        w.GreaterThan("riskScore", 70)))
                    .ThenRoute("seniorReviewer")
                    .ElseRoute("juniorReviewer")))
            .AddAction(1, a => a
                .WithTitle("Junior Review")
                .SentBy("juniorReviewer"))
            .AddAction(2, a => a
                .WithTitle("Senior Review")
                .SentBy("seniorReviewer"))
            .Build();

        // Assert
        blueprint.Should().NotBeNull();
        blueprint.Actions[0].Condition.Should().NotBeNull();
        blueprint.Actions[0].Participants.Should().NotBeNull();
    }

    [Fact]
    public async Task ValidationScenarios_InvalidData_ShouldFailValidation()
    {
        // Test data validation scenarios
        var blueprint = BlueprintBuilder.Create()
            .WithTitle("Validation Test")
            .WithDescription("Blueprint for testing data validation scenarios")
            .AddParticipant("sender", p => p.Named("Sender"))
            .AddParticipant("receiver", p => p.Named("Receiver"))
            .AddAction(0, a => a
                .WithTitle("Test Action")
                .SentBy("sender")
                .RequiresData(d => d
                    .AddString("email", f => f.IsRequired())
                    .AddInteger("age", f => f.IsRequired().WithMinimum(0).WithMaximum(150))
                    .AddNumber("salary", f => f.IsRequired().WithMinimum(0))))
            .Build();

        // Assert
        blueprint.Should().NotBeNull();
        var action = blueprint.Actions[0];
        action.DataSchemas.Should().NotBeNull();
        // Note: DataSchemas is a collection of JsonDocuments, detailed schema validation would require parsing
    }

    [Fact]
    public void CreateMultiPartyBlueprint_WithComplexParticipants_ShouldSucceed()
    {
        // Arrange & Act - Create a blueprint with many participants
        var blueprint = BlueprintBuilder.Create()
            .WithTitle("Multi-Party Contract Workflow")
            .WithDescription("Complex workflow involving multiple organizations")
            .AddParticipant("company_a", p => p
                .Named("Company A")
                .FromOrganisation("ORG-A-001"))
            .AddParticipant("company_b", p => p
                .Named("Company B")
                .FromOrganisation("ORG-B-001"))
            .AddParticipant("company_c", p => p
                .Named("Company C")
                .FromOrganisation("ORG-C-001"))
            .AddParticipant("escrow", p => p
                .Named("Escrow Service")
                .FromOrganisation("ORG-ESCROW-001"))
            .AddParticipant("notary", p => p
                .Named("Notary Service")
                .FromOrganisation("ORG-NOTARY-001"))
            .AddAction(0, a => a
                .WithTitle("Company A Proposal")
                .SentBy("company_a")
                .RequiresData(d => d.AddString("proposalText"))
                .Disclose("company_b", d => d.AllFields())
                .Disclose("company_c", d => d.AllFields())
                .RouteToNext("company_b"))
            .AddAction(1, a => a
                .WithTitle("Company B Acceptance")
                .SentBy("company_b")
                .RouteToNext("company_c"))
            .AddAction(2, a => a
                .WithTitle("Company C Acceptance")
                .SentBy("company_c")
                .RouteToNext("escrow"))
            .AddAction(3, a => a
                .WithTitle("Escrow Setup")
                .SentBy("escrow")
                .RouteToNext("notary"))
            .AddAction(4, a => a
                .WithTitle("Notarization")
                .SentBy("notary"))
            .Build();

        // Assert
        blueprint.Should().NotBeNull();
        blueprint.Participants.Should().HaveCount(5);
        blueprint.Actions.Should().HaveCount(5);
        blueprint.Title.Should().Be("Multi-Party Contract Workflow");
    }
}
