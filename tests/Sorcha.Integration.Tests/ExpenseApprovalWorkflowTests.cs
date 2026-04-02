// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors

using FluentAssertions;
using Sorcha.Blueprint.Engine;
using Sorcha.Blueprint.Engine.Implementation;
using Sorcha.Blueprint.Engine.Interfaces;
using Sorcha.Blueprint.Fluent;
using Sorcha.Blueprint.Models;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sorcha.Integration.Tests;

/// <summary>
/// Integration tests demonstrating a real-world expense approval workflow
/// with JSON Logic routing, participant roles, and data flow.
///
/// Workflow:
/// 1. Employee submits expense claim
/// 2. Routing logic:
///    - Amount < $100 → Instant approval
///    - Amount < $1000 → Route to Manager
///    - Amount >= $1000 → Route to Finance Director
/// 3. Manager/Finance reviews and approves/rejects
/// 4. System records final decision
/// </summary>
public class ExpenseApprovalWorkflowTests
{
    private readonly IExecutionEngine _engine;

    public ExpenseApprovalWorkflowTests()
    {
        var schemaValidator = new SchemaValidator();
        var jsonLogicEvaluator = new JsonLogicEvaluator();
        var disclosureProcessor = new DisclosureProcessor();
        var routingEngine = new RoutingEngine(jsonLogicEvaluator);
        var actionProcessor = new ActionProcessor(
            schemaValidator,
            jsonLogicEvaluator,
            disclosureProcessor,
            routingEngine
        );

        _engine = new ExecutionEngine(
            actionProcessor,
            schemaValidator,
            jsonLogicEvaluator,
            disclosureProcessor,
            routingEngine
        );
    }

    [Fact]
    public async Task ExpenseApproval_InstantApproval_AmountUnder100()
    {
        // Arrange
        WriteLine("╔═══════════════════════════════════════════════════════════╗");
        WriteLine("║   Expense Approval Workflow - Instant Approval (<$100)   ║");
        WriteLine("╚═══════════════════════════════════════════════════════════╝");
        WriteLine();

        var blueprint = CreateExpenseApprovalBlueprint();
        var expenseData = new Dictionary<string, object>
        {
            ["amount"] = 75.50,
            ["description"] = "Team lunch",
            ["category"] = "Meals",
            ["date"] = "2025-11-18"
        };

        WriteLine("📝 Expense Claim Submitted:");
        WriteLine($"   Amount: ${expenseData["amount"]}");
        WriteLine($"   Description: {expenseData["description"]}");
        WriteLine($"   Category: {expenseData["category"]}");
        WriteLine();

        // Act - Employee submits claim (Action 0)
        WriteLine("👤 Employee: Alice Johnson (alice@company.com)");
        WriteLine("   Submitting expense claim...");
        WriteLine();

        var action0 = blueprint.Actions.First(a => a.Id == 0);
        var routingResult = await _engine.DetermineRoutingAsync(
            blueprint,
            action0,
            expenseData
        );

        WriteLine($"🔀 Routing Decision: Action {routingResult.NextActionId}");

        var nextAction = blueprint.Actions.First(a => a.Id.ToString() == routingResult.NextActionId);
        WriteLine($"   → {nextAction.Title}");
        WriteLine();

        // Assert
        routingResult.NextActionId.Should().NotBeNull();
        routingResult.NextActionId.Should().Be("1", "amounts under $100 should route to instant approval");
        nextAction.Title.Should().Be("Instant Approval");

        WriteLine("✅ Result: AUTOMATICALLY APPROVED");
        WriteLine($"   Reason: Amount ${expenseData["amount"]} is under $100 threshold");
        WriteLine();
        WriteLine("════════════════════════════════════════════════════════════");
        WriteLine();
    }

    [Fact]
    public async Task ExpenseApproval_ManagerReview_AmountBetween100And1000()
    {
        // Arrange
        WriteLine("╔═══════════════════════════════════════════════════════════╗");
        WriteLine("║  Expense Approval Workflow - Manager Review ($100-$1000) ║");
        WriteLine("╚═══════════════════════════════════════════════════════════╝");
        WriteLine();

        var blueprint = CreateExpenseApprovalBlueprint();
        var expenseData = new Dictionary<string, object>
        {
            ["amount"] = 450.00,
            ["description"] = "Client dinner and entertainment",
            ["category"] = "Entertainment",
            ["date"] = "2025-11-18"
        };

        WriteLine("📝 Expense Claim Submitted:");
        WriteLine($"   Amount: ${expenseData["amount"]}");
        WriteLine($"   Description: {expenseData["description"]}");
        WriteLine($"   Category: {expenseData["category"]}");
        WriteLine();

        // Act - Employee submits claim (Action 0)
        WriteLine("👤 Employee: Alice Johnson (alice@company.com)");
        WriteLine("   Submitting expense claim...");
        WriteLine();

        var action0 = blueprint.Actions.First(a => a.Id == 0);
        var routingResult = await _engine.DetermineRoutingAsync(
            blueprint,
            action0,
            expenseData
        );

        WriteLine($"🔀 Routing Decision: Action {routingResult.NextActionId}");

        var nextAction = blueprint.Actions.First(a => a.Id.ToString() == routingResult.NextActionId);
        WriteLine($"   → {nextAction.Title}");
        WriteLine($"   Assigned to: {GetParticipantName(blueprint, nextAction.Sender)}");
        WriteLine();

        // Manager reviews and approves
        var managerDecision = new Dictionary<string, object>
        {
            ["approved"] = true,
            ["comments"] = "Approved - valid client entertainment expense",
            ["reviewDate"] = "2025-11-18"
        };

        WriteLine("👔 Manager: Bob Smith (bob@company.com)");
        WriteLine($"   Decision: {((bool)managerDecision["approved"] ? "APPROVED ✅" : "REJECTED ❌")}");
        WriteLine($"   Comments: {managerDecision["comments"]}");
        WriteLine();

        // Assert
        routingResult.NextActionId.Should().NotBeNull();
        routingResult.NextActionId.Should().Be("2", "amounts $100-$1000 should route to manager");
        nextAction.Title.Should().Be("Manager Review");

        WriteLine("✅ Result: APPROVED BY MANAGER");
        WriteLine($"   Amount: ${expenseData["amount"]}");
        WriteLine($"   Final Status: Reimbursement Approved");
        WriteLine();
        WriteLine("════════════════════════════════════════════════════════════");
        WriteLine();
    }

    [Fact]
    public async Task ExpenseApproval_FinanceReview_AmountOver1000()
    {
        // Arrange
        WriteLine("╔═══════════════════════════════════════════════════════════╗");
        WriteLine("║ Expense Approval Workflow - Finance Review (>=$1000)     ║");
        WriteLine("╚═══════════════════════════════════════════════════════════╝");
        WriteLine();

        var blueprint = CreateExpenseApprovalBlueprint();
        var expenseData = new Dictionary<string, object>
        {
            ["amount"] = 2500.00,
            ["description"] = "Conference registration and travel",
            ["category"] = "Professional Development",
            ["date"] = "2025-11-18"
        };

        WriteLine("📝 Expense Claim Submitted:");
        WriteLine($"   Amount: ${expenseData["amount"]}");
        WriteLine($"   Description: {expenseData["description"]}");
        WriteLine($"   Category: {expenseData["category"]}");
        WriteLine();

        // Act - Employee submits claim (Action 0)
        WriteLine("👤 Employee: Alice Johnson (alice@company.com)");
        WriteLine("   Submitting expense claim...");
        WriteLine();

        var action0 = blueprint.Actions.First(a => a.Id == 0);
        var routingResult = await _engine.DetermineRoutingAsync(
            blueprint,
            action0,
            expenseData
        );

        WriteLine($"🔀 Routing Decision: Action {routingResult.NextActionId}");

        var nextAction = blueprint.Actions.First(a => a.Id.ToString() == routingResult.NextActionId);
        WriteLine($"   → {nextAction.Title}");
        WriteLine($"   Assigned to: {GetParticipantName(blueprint, nextAction.Sender)}");
        WriteLine();

        // Finance reviews and approves
        var financeDecision = new Dictionary<string, object>
        {
            ["approved"] = true,
            ["comments"] = "Approved - within professional development budget",
            ["reviewDate"] = "2025-11-18",
            ["budgetCode"] = "PROF-DEV-2025"
        };

        WriteLine("💰 Finance Director: Carol White (carol@company.com)");
        WriteLine($"   Decision: {((bool)financeDecision["approved"] ? "APPROVED ✅" : "REJECTED ❌")}");
        WriteLine($"   Comments: {financeDecision["comments"]}");
        WriteLine($"   Budget Code: {financeDecision["budgetCode"]}");
        WriteLine();

        // Assert
        routingResult.NextActionId.Should().NotBeNull();
        routingResult.NextActionId.Should().Be("3", "amounts >= $1000 should route to finance");
        nextAction.Title.Should().Be("Finance Review");

        WriteLine("✅ Result: APPROVED BY FINANCE");
        WriteLine($"   Amount: ${expenseData["amount"]}");
        WriteLine($"   Final Status: Reimbursement Approved");
        WriteLine();
        WriteLine("════════════════════════════════════════════════════════════");
        WriteLine();
    }

    [Fact]
    public async Task ExpenseApproval_CompleteWorkflow_WithDataDisclosure()
    {
        // Arrange
        WriteLine("╔═══════════════════════════════════════════════════════════╗");
        WriteLine("║  Complete Expense Workflow - Data Disclosure Demo        ║");
        WriteLine("╚═══════════════════════════════════════════════════════════╝");
        WriteLine();

        var blueprint = CreateExpenseApprovalBlueprint();

        // Scenario: Manager reviews expense with sensitive data
        var expenseData = new Dictionary<string, object>
        {
            ["amount"] = 750.00,
            ["description"] = "Client meeting - Project Acquisition",
            ["category"] = "Business Development",
            ["date"] = "2025-11-18",
            ["employeeSSN"] = "***-**-1234",  // Sensitive data
            ["employeeSalary"] = 85000         // Sensitive data
        };

        WriteLine("📝 Employee Data (All Fields):");
        WriteLine($"   Amount: ${expenseData["amount"]}");
        WriteLine($"   Description: {expenseData["description"]}");
        WriteLine($"   Category: {expenseData["category"]}");
        WriteLine($"   Employee SSN: {expenseData["employeeSSN"]} (SENSITIVE)");
        WriteLine($"   Employee Salary: ${expenseData["employeeSalary"]} (SENSITIVE)");
        WriteLine();

        var action0 = blueprint.Actions.First(a => a.Id == 0);
        var disclosure = action0.Disclosures.First();

        WriteLine("🔒 Disclosure Rules Applied:");
        WriteLine($"   Recipient: Manager");
        WriteLine($"   Fields Disclosed: amount, description, category, date");
        WriteLine($"   Fields Hidden: employeeSSN, employeeSalary");
        WriteLine();

        // Act - Process disclosure
        var disclosureResults = _engine.ApplyDisclosures(
            expenseData,
            action0
        );

        WriteLine("👔 Manager View (After Disclosure):");
        var managerData = disclosureResults.First().DisclosedData;
        foreach (var kvp in managerData)
        {
            WriteLine($"   {kvp.Key}: {kvp.Value}");
        }
        WriteLine();

        // Assert
        disclosureResults.Should().NotBeNull();
        disclosureResults.Should().NotBeEmpty();
        managerData.Should().ContainKey("amount");
        managerData.Should().ContainKey("description");
        managerData.Should().NotContainKey("employeeSSN", "sensitive data should be hidden");
        managerData.Should().NotContainKey("employeeSalary", "sensitive data should be hidden");

        WriteLine("✅ Data Disclosure Successful");
        WriteLine("   Sensitive employee data protected from manager view");
        WriteLine();
        WriteLine("════════════════════════════════════════════════════════════");
        WriteLine();
    }

    #region Helper Methods

    private static Sorcha.Blueprint.Models.Blueprint CreateExpenseApprovalBlueprint()
    {
        // Define participants
        var employee = new Participant
        {
            Id = "employee",
            Name = "Alice Johnson",
            Organisation = "Acme Corporation",
            WalletAddress = "employee-wallet-addr"
        };

        var manager = new Participant
        {
            Id = "manager",
            Name = "Bob Smith",
            Organisation = "Acme Corporation",
            WalletAddress = "manager-wallet-addr"
        };

        var finance = new Participant
        {
            Id = "finance",
            Name = "Carol White",
            Organisation = "Acme Corporation",
            WalletAddress = "finance-wallet-addr"
        };

        var system = new Participant
        {
            Id = "system",
            Name = "Expense System",
            Organisation = "Acme Corporation",
            WalletAddress = "system-wallet-addr"
        };

        // Define expense claim schema
        var expenseSchema = JsonDocument.Parse("""
        {
            "type": "object",
            "properties": {
                "amount": { "type": "number", "minimum": 0 },
                "description": { "type": "string", "minLength": 1 },
                "category": { "type": "string" },
                "date": { "type": "string", "format": "date" }
            },
            "required": ["amount", "description", "category", "date"]
        }
        """);

        // Action 0: Employee submits expense claim
        var submitClaim = new Sorcha.Blueprint.Models.Action
        {
            Id = 0,
            Title = "Submit Expense Claim",
            Description = "Employee submits an expense claim for reimbursement",
            Sender = "employee",
            DataSchemas = new[] { expenseSchema },
            Disclosures = new[]
            {
                new Disclosure("manager", new List<string> { "/amount", "/description", "/category", "/date" }),
                new Disclosure("finance", new List<string> { "/amount", "/description", "/category", "/date" })
            },
            // Routing logic via Routes:
            // if amount < 100 then 1 (instant approval)
            // else if amount < 1000 then 2 (manager review)
            // else 3 (finance review - default)
            Routes = new[]
            {
                new Route
                {
                    Id = "instant-approval",
                    NextActionIds = new[] { 1 },
                    Condition = JsonNode.Parse("""{ "<": [{ "var": "amount" }, 100] }"""),
                    Description = "Amount under $100 - instant approval"
                },
                new Route
                {
                    Id = "manager-review",
                    NextActionIds = new[] { 2 },
                    Condition = JsonNode.Parse("""{ "<": [{ "var": "amount" }, 1000] }"""),
                    Description = "Amount $100-$999 - manager review"
                },
                new Route
                {
                    Id = "finance-review",
                    NextActionIds = new[] { 3 },
                    IsDefault = true,
                    Description = "Amount >= $1000 - finance review"
                }
            }
        };

        // Action 1: Instant approval (< $100)
        var instantApproval = new Sorcha.Blueprint.Models.Action
        {
            Id = 1,
            Title = "Instant Approval",
            Description = "System automatically approves low-value expenses",
            Sender = "system",
            Disclosures = new[]
            {
                new Disclosure("employee", new List<string> { "/amount", "/description", "/approved" })
            },
            Condition = JsonNode.Parse("{ \"==\": [0, 0] }") // Terminal action
        };

        // Action 2: Manager review ($100 - $1000)
        var managerReview = new Sorcha.Blueprint.Models.Action
        {
            Id = 2,
            Title = "Manager Review",
            Description = "Manager reviews and approves/rejects expense",
            Sender = "manager",
            DataSchemas = new[]
            {
                JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "approved": { "type": "boolean" },
                        "comments": { "type": "string" }
                    },
                    "required": ["approved"]
                }
                """)
            },
            Disclosures = new[]
            {
                new Disclosure("employee", new List<string> { "/amount", "/description", "/approved", "/comments" }),
                new Disclosure("finance", new List<string> { "/amount", "/description", "/approved", "/comments" })
            },
            Condition = JsonNode.Parse("{ \"==\": [0, 0] }") // Terminal action
        };

        // Action 3: Finance review (>= $1000)
        var financeReview = new Sorcha.Blueprint.Models.Action
        {
            Id = 3,
            Title = "Finance Review",
            Description = "Finance director reviews and approves/rejects high-value expense",
            Sender = "finance",
            DataSchemas = new[]
            {
                JsonDocument.Parse("""
                {
                    "type": "object",
                    "properties": {
                        "approved": { "type": "boolean" },
                        "comments": { "type": "string" },
                        "budgetCode": { "type": "string" }
                    },
                    "required": ["approved", "budgetCode"]
                }
                """)
            },
            Disclosures = new[]
            {
                new Disclosure("employee", new List<string> { "/amount", "/description", "/approved", "/comments", "/budgetCode" }),
                new Disclosure("manager", new List<string> { "/amount", "/description", "/approved", "/comments", "/budgetCode" })
            },
            Condition = JsonNode.Parse("{ \"==\": [0, 0] }") // Terminal action
        };

        return new Sorcha.Blueprint.Models.Blueprint
        {
            Id = Guid.NewGuid().ToString(),
            Title = "Expense Approval Workflow",
            Description = "Multi-level expense approval based on amount thresholds",
            Version = 1,
            Participants = new List<Participant> { employee, manager, finance, system },
            Actions = new List<Sorcha.Blueprint.Models.Action> { submitClaim, instantApproval, managerReview, financeReview }
        };
    }

    private static string GetParticipantName(Sorcha.Blueprint.Models.Blueprint blueprint, string participantId)
    {
        var participant = blueprint.Participants.FirstOrDefault(p => p.Id == participantId);
        return participant != null ? $"{participant.Name} ({participant.Id})" : participantId;
    }

    private static void WriteLine(string message = "")
    {
        Console.WriteLine(message);
    }

    #endregion
}
