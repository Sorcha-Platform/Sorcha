// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Fluent;
using Sorcha.Blueprint.Schemas.Client;

namespace Sorcha.Integration.Tests;

public class BlueprintEndToEndTests
{
    [Fact]
    public void CreateBlueprintWithFluentAPI_ShouldSucceed()
    {
        // Arrange & Act
        var blueprint = BlueprintBuilder.Create()
            .WithTitle("Purchase Order Workflow")
            .WithDescription("A complete purchase order workflow with two parties")
            .WithMetadata("author", "Integration Test")
            .WithMetadata("category", "Finance")
            .AddParticipant("buyer", p => p
                .Named("Buyer Organization")
                .FromOrganisation("ORG-BUYER-001"))
            .AddParticipant("seller", p => p
                .Named("Seller Organization")
                .FromOrganisation("ORG-SELLER-001"))
            .AddAction(0, a => a
                .WithTitle("Submit Purchase Order")
                .WithDescription("Buyer submits a purchase order")
                .SentBy("buyer")
                .RequiresData(d => d
                    .AddString("itemName", f => f.IsRequired().WithTitle("Name of the item"))
                    .AddInteger("quantity", f => f.IsRequired().WithTitle("Quantity to order"))
                    .AddNumber("unitPrice", f => f.IsRequired().WithTitle("Price per unit")))
                .Disclose("seller", d => d
                    .Field("/itemName")
                    .Field("/quantity")
                    .Field("/unitPrice"))
                .Calculate("totalPrice", c => c
                    .Multiply("quantity", "unitPrice"))
                .RouteToNext("seller"))
            .AddAction(1, a => a
                .WithTitle("Accept Order")
                .WithDescription("Seller accepts the order")
                .SentBy("seller"))
            .Build();

        // Assert
        blueprint.Should().NotBeNull();
        blueprint.Title.Should().Be("Purchase Order Workflow");
        blueprint.Participants.Should().HaveCount(2);
        blueprint.Actions.Should().HaveCount(2);

        var firstAction = blueprint.Actions[0];
        firstAction.Title.Should().Be("Submit Purchase Order");
        firstAction.Sender.Should().Be("buyer");
        firstAction.DataSchemas.Should().HaveCount(1);
        firstAction.Disclosures.Should().HaveCount(1);
        firstAction.Calculations.Should().ContainKey("totalPrice");

        var secondAction = blueprint.Actions[1];
        secondAction.Title.Should().Be("Accept Order");
        secondAction.Sender.Should().Be("seller");
    }

    [Fact]
    public async Task SchemaLibrary_ShouldLoadBuiltInSchemas()
    {
        // Arrange
        var schemaLibrary = new SchemaLibraryService();

        // Act
        var schemas = await schemaLibrary.GetAllSchemasAsync();

        // Assert
        schemas.Should().NotBeNull();
        schemas.Should().NotBeEmpty();
        schemas.Should().Contain(s => s.Metadata.Id == "https://sorcha.dev/schemas/person/v1");
        schemas.Should().Contain(s => s.Metadata.Id == "https://sorcha.dev/schemas/address/v1");
        schemas.Should().Contain(s => s.Metadata.Id == "https://sorcha.dev/schemas/document/v1");
        schemas.Should().Contain(s => s.Metadata.Id == "https://sorcha.dev/schemas/payment/v1");
    }

    [Fact]
    public async Task SchemaLibrary_SearchAndRetrieve_ShouldWork()
    {
        // Arrange
        var schemaLibrary = new SchemaLibraryService();

        // Act
        var searchResults = await schemaLibrary.SearchAsync("person");
        var personSchema = await schemaLibrary.GetSchemaByIdAsync("https://sorcha.dev/schemas/person/v1");

        // Assert
        searchResults.Should().NotBeEmpty();
        searchResults.Should().Contain(s => s.Metadata.Id == "https://sorcha.dev/schemas/person/v1");

        personSchema.Should().NotBeNull();
        personSchema!.Metadata.Title.Should().Contain("Person");
        personSchema.Schema.Should().NotBeNull();
    }

    [Fact]
    public void CreateMultiStepBlueprint_WithConditionalRouting_ShouldSucceed()
    {
        // Arrange & Act
        var blueprint = BlueprintBuilder.Create()
            .WithTitle("Loan Application Workflow")
            .WithDescription("Multi-step loan application with conditional approval")
            .AddParticipant("applicant", p => p.Named("Loan Applicant"))
            .AddParticipant("processor", p => p.Named("Loan Processor"))
            .AddParticipant("approver", p => p.Named("Loan Approver"))
            .AddAction(0, a => a
                .WithTitle("Submit Application")
                .SentBy("applicant")
                .RequiresData(d => d
                    .AddNumber("loanAmount")
                    .AddInteger("creditScore"))
                .Disclose("processor", d => d
                    .AllFields())
                .RouteToNext("processor"))
            .AddAction(1, a => a
                .WithTitle("Process Application")
                .SentBy("processor")
                .RouteConditionally(c => c
                    .When(cb => cb.GreaterThan("creditScore", 700))
                    .ThenRoute("approver")
                    .ElseRoute("applicant")))
            .AddAction(2, a => a
                .WithTitle("Approve Loan")
                .SentBy("approver"))
            .Build();

        // Assert
        blueprint.Should().NotBeNull();
        blueprint.Participants.Should().HaveCount(3);
        blueprint.Actions.Should().HaveCount(3);
        blueprint.Actions[1].Condition.Should().NotBeNull();
    }

    [Fact]
    public void CreateBlueprintWithComplexDisclosures_ShouldSucceed()
    {
        // Arrange & Act
        var blueprint = BlueprintBuilder.Create()
            .WithTitle("Healthcare Record Sharing")
            .WithDescription("Selective disclosure of healthcare records")
            .AddParticipant("patient", p => p.Named("Patient"))
            .AddParticipant("doctor", p => p.Named("Primary Doctor"))
            .AddParticipant("specialist", p => p.Named("Specialist"))
            .AddParticipant("insurance", p => p.Named("Insurance Company"))
            .AddAction(0, a => a
                .WithTitle("Share Medical Records")
                .SentBy("patient")
                .RequiresData(d => d
                    .AddString("patientName")
                    .AddString("diagnosis")
                    .AddString("treatment")
                    .AddNumber("cost"))
                .Disclose("doctor", d => d.AllFields())
                .Disclose("specialist", d => d
                    .Field("/diagnosis")
                    .Field("/treatment"))
                .Disclose("insurance", d => d
                    .Field("/diagnosis")
                    .Field("/cost"))
                .RouteToNext("doctor"))
            .Build();

        // Assert
        blueprint.Should().NotBeNull();
        blueprint.Participants.Should().HaveCount(4);
        blueprint.Actions[0].Disclosures.Should().HaveCount(3);

        var doctorDisclosure = blueprint.Actions[0].Disclosures.First(d => d.ParticipantAddress == "doctor");
        doctorDisclosure.DataPointers.Should().Contain("/*");

        var specialistDisclosure = blueprint.Actions[0].Disclosures.First(d => d.ParticipantAddress == "specialist");
        specialistDisclosure.DataPointers.Should().HaveCount(2);

        var insuranceDisclosure = blueprint.Actions[0].Disclosures.First(d => d.ParticipantAddress == "insurance");
        insuranceDisclosure.DataPointers.Should().Contain("/cost");
    }
}
