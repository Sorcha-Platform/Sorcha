# CLI Demo Upgrade Plan: JSON Blueprints with JSON-e Templating

**Created:** 2025-11-19
**Status:** Planning Phase
**Purpose:** Refactor Sorcha.Cli.Demo to use JSON/YAML blueprint documents instead of Fluent API

---

## Problem Statement

The current CLI demo application (`Sorcha.Cli.Demo`) uses the Fluent API to define blueprint examples:
- `ExpenseApprovalExample.cs` uses `BlueprintBuilder.Create()...`
- `PurchaseOrderExample.cs` uses Fluent API
- `LoanApplicationExample.cs` uses Fluent API

**This violates the new architectural standard** that blueprints should be created as JSON or YAML documents, with Fluent API reserved for rare developer scenarios requiring runtime generation.

Additionally, wallet addresses are currently hardcoded in the Fluent API builders. They should be injected at runtime using JSON-e templating after wallets are created.

---

## Objectives

1. **Convert all blueprint examples from Fluent API to JSON documents**
2. **Implement JSON-e templating for runtime wallet address injection**
3. **Create a JSON blueprint loader service**
4. **Update the CLI demo to load and process JSON blueprints**
5. **Demonstrate proper blueprint creation methodology**
6. **Maintain all existing functionality** (4 participants, 4 actions, value-based routing)

---

## Architecture Changes

### Current Architecture (Incorrect)

```
ExpenseApprovalExample.cs
  └─> BlueprintBuilder.Create()
      └─> .AddParticipant("Employee")
      └─> .AddAction(0, a => ...)
      └─> .Build()
```

### Target Architecture (Correct)

```
expense-approval.json (JSON-e template)
  └─> Loaded by JsonBlueprintLoader
  └─> Processed by JsonETemplateEngine
      └─> Inject runtime context:
          {
            "walletAddresses": {
              "Employee": "0x742d35...",
              "Manager": "0x8Bb5C5...",
              ...
            }
          }
      └─> Produce Blueprint object
```

---

## Implementation Plan

### Phase 1: Add Dependencies (30 minutes)

**Task:** Add JsonE.NET NuGet package to `Sorcha.Cli.Demo.csproj`

```xml
<PackageReference Include="JsonE.NET" Version="1.0.0" />
```

**Files Modified:**
- `src/Apps/Sorcha.Cli.Demo/Sorcha.Cli.Demo.csproj`

**Acceptance Criteria:**
- [ ] JsonE.NET package added to project
- [ ] Project builds successfully
- [ ] Package version documented

---

### Phase 2: Create JSON Blueprint Templates (2 hours)

**Task:** Convert Fluent API blueprint examples to JSON-e template files

**Files to Create:**

1. `src/Apps/Sorcha.Cli.Demo/Blueprints/expense-approval.json`
2. `src/Apps/Sorcha.Cli.Demo/Blueprints/purchase-order.json`
3. `src/Apps/Sorcha.Cli.Demo/Blueprints/loan-application.json`

**Blueprint Structure Example:**

```json
{
  "id": "expense-approval-demo",
  "title": "Expense Approval Workflow",
  "description": "Multi-step expense approval with conditional routing based on amount",
  "version": 1,
  "participants": [
    {
      "id": "Employee",
      "name": "Employee",
      "organisation": "ACME Corp",
      "walletAddress": {"$eval": "walletAddresses.Employee"}
    },
    {
      "id": "Manager",
      "name": "Manager",
      "organisation": "ACME Corp",
      "walletAddress": {"$eval": "walletAddresses.Manager"}
    },
    {
      "id": "Finance",
      "name": "Finance Department",
      "organisation": "ACME Corp",
      "walletAddress": {"$eval": "walletAddresses.Finance"}
    },
    {
      "id": "CFO",
      "name": "Chief Financial Officer",
      "organisation": "ACME Corp",
      "walletAddress": {"$eval": "walletAddresses.CFO"}
    }
  ],
  "actions": [
    {
      "id": 0,
      "title": "Submit Expense Report",
      "description": "Employee submits an expense report for approval",
      "sender": "Employee",
      "dataSchemas": [
        {
          "type": "object",
          "properties": {
            "employeeName": {
              "type": "string",
              "title": "Employee Name"
            },
            "amount": {
              "type": "number",
              "title": "Amount",
              "minimum": 0.01
            },
            "category": {
              "type": "string",
              "title": "Category"
            },
            "description": {
              "type": "string",
              "title": "Description"
            },
            "date": {
              "type": "string",
              "format": "date",
              "title": "Expense Date"
            }
          },
          "required": ["employeeName", "amount", "category", "description", "date"]
        }
      ],
      "disclosures": [
        {
          "participantAddress": "Manager",
          "dataPointers": ["/*"]
        },
        {
          "participantAddress": "Finance",
          "dataPointers": ["/*"]
        },
        {
          "participantAddress": "CFO",
          "dataPointers": ["/*"]
        }
      ],
      "calculations": {},
      "condition": {
        "if": [
          {">=": [{"var": "amount"}, 5000]},
          "CFO",
          "Manager"
        ]
      }
    },
    {
      "id": 1,
      "title": "Manager Review",
      "description": "Manager reviews and approves expenses under $5,000",
      "sender": "Manager",
      "dataSchemas": [
        {
          "type": "object",
          "properties": {
            "approved": {
              "type": "boolean",
              "title": "Approved"
            },
            "comments": {
              "type": "string",
              "title": "Manager Comments"
            }
          },
          "required": ["approved"]
        }
      ],
      "disclosures": [
        {
          "participantAddress": "Employee",
          "dataPointers": ["/approved", "/comments"]
        },
        {
          "participantAddress": "Finance",
          "dataPointers": ["/*"]
        }
      ],
      "condition": {
        "if": [
          {"==": [{"var": "approved"}, true]},
          "Finance",
          "__Complete__"
        ]
      }
    },
    {
      "id": 2,
      "title": "CFO Approval",
      "description": "CFO approves expenses $5,000 and above",
      "sender": "CFO",
      "dataSchemas": [
        {
          "type": "object",
          "properties": {
            "approved": {
              "type": "boolean",
              "title": "Approved"
            },
            "comments": {
              "type": "string",
              "title": "CFO Comments"
            }
          },
          "required": ["approved"]
        }
      ],
      "disclosures": [
        {
          "participantAddress": "Employee",
          "dataPointers": ["/approved", "/comments"]
        },
        {
          "participantAddress": "Finance",
          "dataPointers": ["/*"]
        }
      ],
      "condition": {
        "if": [
          {"==": [{"var": "approved"}, true]},
          "Finance",
          "__Complete__"
        ]
      }
    },
    {
      "id": 3,
      "title": "Finance Processing",
      "description": "Finance processes approved expense",
      "sender": "Finance",
      "dataSchemas": [
        {
          "type": "object",
          "properties": {
            "processed": {
              "type": "boolean",
              "title": "Processed"
            },
            "paymentMethod": {
              "type": "string",
              "title": "Payment Method"
            },
            "transactionId": {
              "type": "string",
              "title": "Transaction ID"
            }
          },
          "required": ["processed", "paymentMethod"]
        }
      ],
      "disclosures": [
        {
          "participantAddress": "Employee",
          "dataPointers": ["/processed", "/paymentMethod", "/transactionId"]
        }
      ]
    }
  ]
}
```

**Acceptance Criteria:**
- [ ] Three JSON blueprint files created
- [ ] All use JSON-e templating for `walletAddress` fields
- [ ] Blueprints match current Fluent API functionality
- [ ] JSON is valid and well-formatted
- [ ] Blueprints validate against the JSON Schema

---

### Phase 3: Implement JSON Blueprint Loader (1.5 hours)

**Task:** Create a service to load JSON blueprint files and deserialize them

**Files to Create:**
- `src/Apps/Sorcha.Cli.Demo/Services/JsonBlueprintLoader.cs`

**Implementation:**

```csharp
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Sorcha.Blueprint.Models;
using Microsoft.Extensions.Logging;

namespace Sorcha.Cli.Demo.Services;

/// <summary>
/// Loads blueprint definitions from JSON files
/// </summary>
public class JsonBlueprintLoader
{
    private readonly ILogger<JsonBlueprintLoader> _logger;
    private readonly string _blueprintsPath;
    private readonly JsonSerializerOptions _jsonOptions;

    public JsonBlueprintLoader(ILogger<JsonBlueprintLoader> logger)
    {
        _logger = logger;
        _blueprintsPath = Path.Combine(AppContext.BaseDirectory, "Blueprints");

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        _logger.LogInformation("Blueprint loader initialized. Path: {Path}", _blueprintsPath);
    }

    /// <summary>
    /// Loads a blueprint from a JSON file (template, not yet processed)
    /// </summary>
    public async Task<string> LoadBlueprintTemplateAsync(string fileName)
    {
        var filePath = Path.Combine(_blueprintsPath, fileName);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException($"Blueprint file not found: {filePath}");
        }

        var jsonContent = await File.ReadAllTextAsync(filePath);
        _logger.LogInformation("Loaded blueprint template from {FileName}", fileName);

        return jsonContent;
    }

    /// <summary>
    /// Parses a JSON string into a Blueprint object
    /// </summary>
    public Blueprint ParseBlueprint(string json)
    {
        try
        {
            var blueprint = JsonSerializer.Deserialize<Blueprint>(json, _jsonOptions);

            if (blueprint == null)
            {
                throw new InvalidOperationException("Failed to deserialize blueprint");
            }

            _logger.LogInformation("Parsed blueprint: {Title}", blueprint.Title);
            return blueprint;
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "Failed to parse blueprint JSON");
            throw;
        }
    }

    /// <summary>
    /// Gets the full path to the blueprints directory
    /// </summary>
    public string GetBlueprintsPath() => _blueprintsPath;
}
```

**Acceptance Criteria:**
- [ ] Service loads JSON files from `Blueprints/` directory
- [ ] Proper error handling for missing files
- [ ] Logging for debugging
- [ ] Unit tests for loader service

---

### Phase 4: Implement JSON-e Template Processor (2 hours)

**Task:** Create a service to process JSON-e templates and inject runtime values

**Files to Create:**
- `src/Apps/Sorcha.Cli.Demo/Services/JsonETemplateEngine.cs`

**Implementation:**

```csharp
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using JsonE;

namespace Sorcha.Cli.Demo.Services;

/// <summary>
/// Processes JSON-e templates with runtime context
/// </summary>
public class JsonETemplateEngine
{
    private readonly ILogger<JsonETemplateEngine> _logger;

    public JsonETemplateEngine(ILogger<JsonETemplateEngine> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Processes a JSON-e template with the provided context
    /// </summary>
    /// <param name="templateJson">JSON-e template string</param>
    /// <param name="context">Runtime context values</param>
    /// <returns>Processed JSON string with values injected</returns>
    public string ProcessTemplate(string templateJson, Dictionary<string, object> context)
    {
        try
        {
            // Parse template JSON
            var templateDoc = JsonDocument.Parse(templateJson);

            // Convert context to JsonElement
            var contextJson = JsonSerializer.Serialize(context);
            var contextDoc = JsonDocument.Parse(contextJson);

            // Process the template using JSON-e
            var result = JsonE.Render(templateDoc.RootElement, contextDoc.RootElement);

            // Serialize result back to JSON string
            var resultJson = JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            _logger.LogInformation("Successfully processed JSON-e template");
            return resultJson;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to process JSON-e template");
            throw;
        }
    }

    /// <summary>
    /// Creates a wallet address context from participant wallets
    /// </summary>
    public Dictionary<string, object> CreateWalletContext(Dictionary<string, string> participantWallets)
    {
        return new Dictionary<string, object>
        {
            ["walletAddresses"] = participantWallets
        };
    }
}
```

**Acceptance Criteria:**
- [ ] Service processes JSON-e templates correctly
- [ ] Wallet addresses injected at runtime
- [ ] Error handling for malformed templates
- [ ] Logging for debugging
- [ ] Unit tests for template processing

---

### Phase 5: Update Blueprint Example Interface (1 hour)

**Task:** Modify `IBlueprintExample` and implementations to load from JSON

**Files to Modify:**
- `src/Apps/Sorcha.Cli.Demo/Program.cs` (interface definition)

**Files to Create:**
- `src/Apps/Sorcha.Cli.Demo/Examples/JsonBlueprintExample.cs`

**Files to Delete:**
- `src/Apps/Sorcha.Cli.Demo/Examples/ExpenseApprovalExample.cs`
- `src/Apps/Sorcha.Cli.Demo/Examples/PurchaseOrderExample.cs`
- `src/Apps/Sorcha.Cli.Demo/Examples/LoanApplicationExample.cs`

**New Interface Design:**

```csharp
/// <summary>
/// Interface for blueprint examples loaded from JSON files
/// </summary>
public interface IBlueprintExample
{
    string Name { get; }
    string Description { get; }
    string FileName { get; } // New: JSON file name
    string[] GetParticipants();
    Task<Blueprint> GetBlueprintAsync(Dictionary<string, string> participantWallets); // Modified: async with wallet context
}
```

**New Implementation:**

```csharp
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Sorcha.Blueprint.Models;
using Sorcha.Cli.Demo.Services;

namespace Sorcha.Cli.Demo.Examples;

/// <summary>
/// Blueprint example loaded from JSON file with JSON-e templating
/// </summary>
public class JsonBlueprintExample : IBlueprintExample
{
    private readonly string _fileName;
    private readonly string _name;
    private readonly string _description;
    private readonly string[] _participants;
    private readonly JsonBlueprintLoader _loader;
    private readonly JsonETemplateEngine _templateEngine;

    public JsonBlueprintExample(
        string fileName,
        string name,
        string description,
        string[] participants,
        JsonBlueprintLoader loader,
        JsonETemplateEngine templateEngine)
    {
        _fileName = fileName;
        _name = name;
        _description = description;
        _participants = participants;
        _loader = loader;
        _templateEngine = templateEngine;
    }

    public string Name => _name;
    public string Description => _description;
    public string FileName => _fileName;

    public string[] GetParticipants() => _participants;

    public async Task<Blueprint> GetBlueprintAsync(Dictionary<string, string> participantWallets)
    {
        // Load JSON-e template
        var templateJson = await _loader.LoadBlueprintTemplateAsync(_fileName);

        // Create runtime context with wallet addresses
        var context = _templateEngine.CreateWalletContext(participantWallets);

        // Process template to inject runtime values
        var processedJson = _templateEngine.ProcessTemplate(templateJson, context);

        // Parse into Blueprint object
        var blueprint = _loader.ParseBlueprint(processedJson);

        return blueprint;
    }
}
```

**Acceptance Criteria:**
- [ ] Interface updated to support async and wallet context
- [ ] JsonBlueprintExample implementation complete
- [ ] Old Fluent API examples removed
- [ ] Unit tests for JsonBlueprintExample

---

### Phase 6: Update Service Registration (30 minutes)

**Task:** Update DI container to register new services

**Files to Modify:**
- `src/Apps/Sorcha.Cli.Demo/Program.cs`

**Changes:**

```csharp
// Add JSON blueprint services
services.AddSingleton<JsonBlueprintLoader>();
services.AddSingleton<JsonETemplateEngine>();

// Register blueprint examples (now using JSON files)
services.AddSingleton<IBlueprintExample>(sp => new JsonBlueprintExample(
    fileName: "expense-approval.json",
    name: "Expense Approval Workflow",
    description: "Multi-step expense approval with conditional routing based on amount",
    participants: new[] { "Employee", "Manager", "Finance", "CFO" },
    loader: sp.GetRequiredService<JsonBlueprintLoader>(),
    templateEngine: sp.GetRequiredService<JsonETemplateEngine>()
));

services.AddSingleton<IBlueprintExample>(sp => new JsonBlueprintExample(
    fileName: "purchase-order.json",
    name: "Purchase Order Processing",
    description: "Purchase order workflow with supplier and shipping coordination",
    participants: new[] { "Buyer", "Supplier", "Shipping", "Finance" },
    loader: sp.GetRequiredService<JsonBlueprintLoader>(),
    templateEngine: sp.GetRequiredService<JsonETemplateEngine>()
));

services.AddSingleton<IBlueprintExample>(sp => new JsonBlueprintExample(
    fileName: "loan-application.json",
    name: "Loan Application Process",
    description: "Loan application with credit check and conditional approval routing",
    participants: new[] { "Applicant", "LoanOfficer", "CreditBureau", "Underwriter" },
    loader: sp.GetRequiredService<JsonBlueprintLoader>(),
    templateEngine: sp.GetRequiredService<JsonETemplateEngine>()
));
```

**Acceptance Criteria:**
- [ ] New services registered in DI container
- [ ] Blueprint examples registered with proper metadata
- [ ] Application builds successfully

---

### Phase 7: Update Blueprint Executor (30 minutes)

**Task:** Modify `BlueprintExecutor` to handle async blueprint loading

**Files to Modify:**
- `src/Apps/Sorcha.Cli.Demo/Services/BlueprintExecutor.cs`

**Changes:**

```csharp
public async Task ExecuteAsync(IBlueprintExample example, DemoContext context, ConsoleRenderer renderer)
{
    context.Reset();

    // Load blueprint with runtime wallet addresses
    context.CurrentBlueprint = await example.GetBlueprintAsync(context.ParticipantWallets);

    var blueprint = context.CurrentBlueprint;
    // ... rest of execution logic remains the same
}
```

**Acceptance Criteria:**
- [ ] Executor calls async GetBlueprintAsync()
- [ ] Wallet addresses passed to blueprint loader
- [ ] Existing functionality maintained

---

### Phase 8: Update Program.cs Main Loop (30 minutes)

**Task:** Update main application loop to work with new async blueprint loading

**Files to Modify:**
- `src/Apps/Sorcha.Cli.Demo/Program.cs`

**Changes:**

```csharp
static async Task RunBlueprintDemo(
    IBlueprintExample example,
    DemoContext context,
    ConsoleRenderer renderer,
    IServiceProvider serviceProvider)
{
    try
    {
        AnsiConsole.Clear();

        // Show blueprint overview
        renderer.ShowBlueprintOverview(example);

        // Initialize wallets for participants
        var walletService = serviceProvider.GetRequiredService<WalletDemoService>();
        await walletService.EnsureParticipantWalletsAsync(example.GetParticipants(), context);

        // Show wallet assignments
        renderer.ShowWalletAssignments(example.GetParticipants(), context);

        if (!AnsiConsole.Confirm("Ready to execute blueprint?"))
            return;

        // Execute blueprint (now loads JSON with wallet addresses injected)
        var executor = serviceProvider.GetRequiredService<BlueprintExecutor>();
        await executor.ExecuteAsync(example, context, renderer);

        // Show transaction chain summary
        renderer.ShowTransactionChainSummary(context);

        AnsiConsole.WriteLine();
        AnsiConsole.Markup("[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }
    catch (Exception ex)
    {
        AnsiConsole.WriteException(ex);
        AnsiConsole.Markup("\n[dim]Press any key to continue...[/]");
        Console.ReadKey(true);
    }
}
```

**Acceptance Criteria:**
- [ ] Main loop updated for async operations
- [ ] No breaking changes to user experience
- [ ] Error handling maintained

---

### Phase 9: Update Project File Structure (15 minutes)

**Task:** Ensure JSON blueprint files are copied to output directory

**Files to Modify:**
- `src/Apps/Sorcha.Cli.Demo/Sorcha.Cli.Demo.csproj`

**Changes:**

```xml
<ItemGroup>
  <Content Include="Blueprints\*.json">
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </Content>
</ItemGroup>
```

**Acceptance Criteria:**
- [ ] Blueprint JSON files copied to output directory
- [ ] Files accessible at runtime
- [ ] Build process updated

---

### Phase 10: Testing and Validation (2 hours)

**Task:** Comprehensive testing of refactored CLI demo

**Test Scenarios:**

1. **Blueprint Loading**
   - [ ] Expense approval blueprint loads correctly
   - [ ] Purchase order blueprint loads correctly
   - [ ] Loan application blueprint loads correctly
   - [ ] Invalid JSON files produce clear error messages

2. **JSON-e Template Processing**
   - [ ] Wallet addresses correctly injected
   - [ ] All participant references resolved
   - [ ] Template processing errors handled gracefully

3. **Workflow Execution**
   - [ ] Expense approval workflow executes (amount < $5000 → Manager)
   - [ ] Expense approval workflow executes (amount >= $5000 → CFO)
   - [ ] Purchase order workflow executes with routing
   - [ ] Loan application workflow executes with credit score routing

4. **Data Validation**
   - [ ] JSON Schema validation works
   - [ ] Required fields enforced
   - [ ] Data types validated

5. **UI and UX**
   - [ ] Menu displays correctly
   - [ ] Blueprint selection works
   - [ ] Action prompts display correctly
   - [ ] Results display properly

**Acceptance Criteria:**
- [ ] All test scenarios pass
- [ ] No regression in existing functionality
- [ ] Performance is acceptable

---

## File Structure After Upgrade

```
src/Apps/Sorcha.Cli.Demo/
├── Blueprints/                              [NEW]
│   ├── expense-approval.json                [NEW]
│   ├── purchase-order.json                  [NEW]
│   └── loan-application.json                [NEW]
├── Examples/
│   ├── JsonBlueprintExample.cs              [NEW]
│   ├── ExpenseApprovalExample.cs            [DELETED]
│   ├── PurchaseOrderExample.cs              [DELETED]
│   └── LoanApplicationExample.cs            [DELETED]
├── Services/
│   ├── BlueprintExecutor.cs                 [MODIFIED]
│   ├── WalletDemoService.cs                 [UNCHANGED]
│   ├── JsonBlueprintLoader.cs               [NEW]
│   └── JsonETemplateEngine.cs               [NEW]
├── Utilities/
│   ├── ConsoleRenderer.cs                   [UNCHANGED]
│   ├── DemoContext.cs                       [UNCHANGED]
│   └── LocalStorageManager.cs               [UNCHANGED]
├── Program.cs                               [MODIFIED]
└── Sorcha.Cli.Demo.csproj                   [MODIFIED]
```

---

## Dependencies

### NuGet Packages to Add

- **JsonE.NET** - JSON-e templating engine for C#

### Existing Dependencies (No Changes)

- Sorcha.Blueprint.Models
- Sorcha.Blueprint.Engine
- Sorcha.Wallet.Core
- Sorcha.Cryptography
- Spectre.Console
- Microsoft.Extensions.DependencyInjection
- Microsoft.Extensions.Logging.Console

---

## Risk Assessment

### Low Risk
- ✅ JSON blueprint files are static and testable
- ✅ JSON-e is a well-documented standard
- ✅ Changes are isolated to CLI demo (no library changes)

### Medium Risk
- ⚠️ JsonE.NET library maturity/stability (mitigation: test thoroughly)
- ⚠️ JSON deserialization edge cases (mitigation: comprehensive tests)

### High Risk
- None identified

---

## Rollback Plan

If the upgrade encounters blocking issues:

1. Revert changes to `Program.cs`, `BlueprintExecutor.cs`
2. Restore deleted Fluent API example files from git history
3. Remove JsonE.NET package reference
4. Delete new services (JsonBlueprintLoader, JsonETemplateEngine)
5. Delete Blueprints/ directory

**Rollback Complexity:** Low (changes are isolated)

---

## Success Criteria

- [ ] All three blueprints load from JSON files
- [ ] JSON-e templating correctly injects wallet addresses at runtime
- [ ] CLI demo executes workflows identically to Fluent API version
- [ ] All original features maintained (routing, validation, disclosure, etc.)
- [ ] Code follows new architectural standards
- [ ] Documentation updated
- [ ] Tests pass (unit + integration)

---

## Timeline Estimate

| Phase | Estimated Time | Dependencies |
|-------|----------------|--------------|
| 1. Add Dependencies | 30 min | None |
| 2. Create JSON Templates | 2 hours | Phase 1 |
| 3. Blueprint Loader | 1.5 hours | Phase 1 |
| 4. JSON-e Engine | 2 hours | Phase 1 |
| 5. Update Interface | 1 hour | Phase 2, 3, 4 |
| 6. Service Registration | 30 min | Phase 5 |
| 7. Update Executor | 30 min | Phase 5 |
| 8. Update Main Loop | 30 min | Phase 7 |
| 9. Project Structure | 15 min | Phase 2 |
| 10. Testing | 2 hours | All phases |

**Total Estimated Time:** ~11 hours

---

## Next Steps

1. **Review and approve this plan**
2. **Begin Phase 1: Add JsonE.NET dependency**
3. **Create first JSON blueprint (expense-approval.json) as proof of concept**
4. **Implement JsonBlueprintLoader and JsonETemplateEngine**
5. **Test end-to-end with one blueprint before converting others**

---

## References

- **JSON-e Specification:** https://json-e.js.org/
- **JsonE.NET NuGet:** https://www.nuget.org/packages/JsonE.NET/
- **Blueprint Format Documentation:** `docs/blueprint-format.md`
- **Constitution:** `.specify/constitution.md`
- **CLAUDE.md Guidelines:** `CLAUDE.md`

---

**Document Status:** Ready for Implementation
**Approval Required:** Yes
**Estimated Completion:** 1-2 development days
