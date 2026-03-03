# JSON-LD Integration Implementation Summary

## Overview

This document summarizes the implementation of JSON-LD (Linked Data) support for the Sorcha Blueprint system, as recommended in [blueprint-architecture.md](blueprint-architecture.md).

**Implementation Date:** January 2025
**Status:** ✅ Completed - Step 1 (JSON-LD Integration)

---

## What Was Implemented

### 1. Core Model Updates

#### Blueprint Model ([Blueprint.cs](../src/Common/Sorcha.Blueprint.Models/Blueprint.cs))

Added JSON-LD properties:
- `@context` (`JsonLdContext`) - JSON-LD context for semantic web integration
- `@type` (`JsonLdType`) - JSON-LD type for semantic classification

```csharp
[JsonPropertyName("@context")]
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public JsonNode? JsonLdContext { get; set; }

[JsonPropertyName("@type")]
[JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
public string? JsonLdType { get; set; }
```

#### Participant Model ([Participant.cs](../src/Common/Sorcha.Blueprint.Models/Participant.cs))

Added JSON-LD properties:
- `@type` (`JsonLdType`) - Person or Organization
- `verifiableCredential` - W3C Verifiable Credentials support
- `additionalProperties` - Extended JSON-LD properties

```csharp
[JsonPropertyName("@type")]
public string? JsonLdType { get; set; }

[JsonPropertyName("verifiableCredential")]
public JsonNode? VerifiableCredential { get; set; }

[JsonPropertyName("additionalProperties")]
public Dictionary<string, JsonNode>? AdditionalProperties { get; set; }
```

#### Action Model ([Action.cs](../src/Common/Sorcha.Blueprint.Models/Action.cs))

Added ActivityStreams properties:
- `@type` (`JsonLdType`) - Activity type (Create, Accept, Reject, Update)
- `target` - Target participant (ActivityStreams)
- `published` - Timestamp (ISO 8601)
- `additionalProperties` - Extended properties

```csharp
[JsonPropertyName("@type")]
public string? JsonLdType { get; set; }

[JsonPropertyName("target")]
public string? Target { get; set; }

[JsonPropertyName("published")]
public DateTimeOffset? Published { get; set; }
```

---

### 2. JSON-LD Context Definitions

Created comprehensive JSON-LD context support in [JsonLdContext.cs](../src/Common/Sorcha.Blueprint.Models/JsonLd/JsonLdContext.cs):

#### Default Context
Maps blueprint fields to standard vocabularies:
- `@vocab`: https://sorcha.dev/blueprint/v1#
- `schema`: https://schema.org/
- `did`: https://www.w3.org/ns/did#
- `as`: https://www.w3.org/ns/activitystreams#
- `xsd`: http://www.w3.org/2001/XMLSchema#

#### Supply Chain Context
Includes GS1 vocabulary for supply chain workflows:
- `gs1`: https://gs1.org/voc/
- Product identifiers (GTIN)
- Tracking numbers
- Order information

#### Finance Context
Specialized context for financial workflows:
- Loan applications
- Credit scores
- Monetary amounts
- Payment terms

#### Helper Methods
- `GetContextByCategory(category)` - Returns appropriate context
- `MergeContexts(customContext)` - Merges custom with default
- `HasJsonLdContext(node)` - Checks if JSON has context
- `ExtractContext(node)` - Extracts context from JSON

---

### 3. JSON-LD Type Helpers

Created [JsonLdType.cs](../src/Common/Sorcha.Blueprint.Models/JsonLd/JsonLdType.cs) with:

#### Type Constants
- `Blueprint`, `Action`, `Participant`
- Schema.org types: `Person`, `Organization`, `Order`, `Product`
- ActivityStreams: `Activity`, `CreateAction`, `AcceptAction`, `RejectAction`, `UpdateAction`

#### JsonLdTypeHelper
- `GetParticipantType(organisationName)` - Auto-determines Person vs Organization
- `GetActionType(actionTitle)` - Maps action titles to ActivityStreams types

**Examples:**
```csharp
GetParticipantType("Self")         // → "schema:Person"
GetParticipantType("Acme Corp")    // → "schema:Organization"
GetActionType("Submit Application") // → "as:Create"
GetActionType("Approve Request")    // → "as:Accept"
```

---

### 4. Fluent Builder Enhancements

#### BlueprintBuilder ([BlueprintBuilder.cs](../src/Core/Sorcha.Blueprint.Fluent/BlueprintBuilder.cs))

New methods for JSON-LD:
```csharp
.WithJsonLd()                              // Default context
.WithJsonLd("finance")                     // Category-specific context
.WithJsonLdContext(customContext)          // Custom context
.WithJsonLdType("CustomType")              // Custom type
.WithAdditionalJsonLdContext(additional)   // Merge contexts
```

**Example:**
```csharp
var blueprint = BlueprintBuilder.Create()
    .WithTitle("Loan Application")
    .WithDescription("Two-party loan workflow")
    .WithJsonLd("finance")                    // ← JSON-LD enabled
    .AddParticipant("applicant", p => p
        .Named("John Doe")
        .FromOrganisation("Self")
        .WithDidUri("did:example:123"))
    .Build();
```

#### ParticipantBuilder ([ParticipantBuilder.cs](../src/Core/Sorcha.Blueprint.Fluent/ParticipantBuilder.cs))

New methods:
```csharp
.AsPerson()                                // Set as Person type
.AsOrganization()                          // Set as Organization type
.AsJsonLdType("CustomType")                // Custom type
.WithAutoJsonLdType()                      // Auto-detect type
.WithVerifiableCredential(credential)      // Add W3C VC
.WithAdditionalProperty(key, value)        // Custom properties
```

**Auto Type Detection:**
- Organizations → `schema:Organization`
- "Self", "Individual" → `schema:Person`

#### ActionBuilder ([ActionBuilder.cs](../src/Core/Sorcha.Blueprint.Fluent/ActionBuilder.cs))

New methods:
```csharp
.AsCreateAction()                          // ActivityStreams Create
.AsAcceptAction()                          // ActivityStreams Accept
.AsRejectAction()                          // ActivityStreams Reject
.AsUpdateAction()                          // ActivityStreams Update
.WithTarget(participantId)                 // Set target
.PublishedAt(timestamp)                    // Set timestamp
.WithAdditionalProperty(key, value)        // Custom properties
```

**Auto Type Detection:**
Actions automatically get appropriate ActivityStreams types based on title.

---

### 5. API Content Negotiation

#### JSON-LD Middleware ([JsonLdMiddleware.cs](../src/Apps/Services/Sorcha.Blueprint.Api/JsonLd/JsonLdMiddleware.cs))

Implemented content negotiation for `application/ld+json`:

**Middleware:**
- Detects `Accept: application/ld+json` header
- Stores flag in HttpContext for endpoint use

**Helper Methods:**
```csharp
context.AcceptsJsonLd()                    // Check if client accepts JSON-LD
JsonLdHelper.EnsureJsonLdContext(blueprint) // Add context if missing
JsonLdResults.Ok(context, blueprint)        // Smart response helper
```

#### API Updates ([Program.cs](../src/Apps/Services/Sorcha.Blueprint.Api/Program.cs))

Updated endpoints to support JSON-LD:
- `GET /api/blueprints` - List with JSON-LD support
- `GET /api/blueprints/{id}` - Single blueprint with JSON-LD
- `POST /api/blueprints` - Create with JSON-LD response

**Usage:**
```bash
# Regular JSON
curl -H "Accept: application/json" http://localhost:5000/api/blueprints/123

# JSON-LD (includes @context)
curl -H "Accept: application/ld+json" http://localhost:5000/api/blueprints/123
```

---

### 6. Unit Tests

Created comprehensive test suites:

#### JsonLdContextTests ([JsonLdContextTests.cs](../tests/Sorcha.Blueprint.Models.Tests/JsonLd/JsonLdContextTests.cs))
- Default context validation
- Supply chain context
- Finance context
- Context merging
- Context extraction

#### JsonLdTypeHelperTests ([JsonLdTypeHelperTests.cs](../tests/Sorcha.Blueprint.Models.Tests/JsonLd/JsonLdTypeHelperTests.cs))
- Participant type detection
- Action type mapping
- Auto type inference

#### BlueprintBuilderJsonLdTests ([BlueprintBuilderJsonLdTests.cs](../tests/Sorcha.Blueprint.Fluent.Tests/JsonLd/BlueprintBuilderJsonLdTests.cs))
- Blueprint with JSON-LD
- Category-based contexts
- Custom contexts
- Serialization validation

#### ParticipantBuilderJsonLdTests ([ParticipantBuilderJsonLdTests.cs](../tests/Sorcha.Blueprint.Fluent.Tests/JsonLd/ParticipantBuilderJsonLdTests.cs))
- Person vs Organization types
- Verifiable credentials
- DID support
- Custom properties

#### ActionBuilderJsonLdTests ([ActionBuilderJsonLdTests.cs](../tests/Sorcha.Blueprint.Fluent.Tests/JsonLd/ActionBuilderJsonLdTests.cs))
- ActivityStreams types
- Target participants
- Published timestamps
- Complex workflows

---

## Usage Examples

### Simple Blueprint with JSON-LD

```csharp
var blueprint = BlueprintBuilder.Create()
    .WithTitle("Simple Approval")
    .WithDescription("Two-party approval workflow")
    .WithJsonLd()                              // ← Enable JSON-LD
    .AddParticipant("requester", p => p
        .Named("Alice Smith")
        .FromOrganisation("Self")
        .WithDidUri("did:example:alice")
        .AsPerson())                           // ← Explicit Person type
    .AddParticipant("approver", p => p
        .Named("Approval Department")
        .FromOrganisation("Acme Corp")
        .AsOrganization())                     // ← Explicit Organization type
    .AddAction(0, a => a
        .WithTitle("Submit Request")
        .SentBy("requester")
        .WithTarget("approver")
        .AsCreateAction())                     // ← ActivityStreams type
    .AddAction(1, a => a
        .WithTitle("Approve or Reject")
        .SentBy("approver")
        .AsAcceptAction())
    .Build();
```

### Generated JSON-LD Output

```json
{
  "@context": {
    "@vocab": "https://sorcha.dev/blueprint/v1#",
    "schema": "https://schema.org/",
    "did": "https://www.w3.org/ns/did#",
    "as": "https://www.w3.org/ns/activitystreams#",
    "id": "@id",
    "type": "@type",
    "title": "schema:name",
    "participants": "schema:participant"
  },
  "@type": "Blueprint",
  "id": "blueprint-123",
  "title": "Simple Approval",
  "description": "Two-party approval workflow",
  "participants": [
    {
      "@type": "schema:Person",
      "id": "requester",
      "name": "Alice Smith",
      "organisation": "Self",
      "didUri": "did:example:alice"
    },
    {
      "@type": "schema:Organization",
      "id": "approver",
      "name": "Approval Department",
      "organisation": "Acme Corp"
    }
  ],
  "actions": [
    {
      "@type": "as:Create",
      "id": 0,
      "title": "Submit Request",
      "sender": "requester",
      "target": "approver"
    },
    {
      "@type": "as:Accept",
      "id": 1,
      "title": "Approve or Reject",
      "sender": "approver"
    }
  ]
}
```

### With Verifiable Credentials

```csharp
var credential = JsonNode.Parse(@"{
  ""@context"": ""https://www.w3.org/2018/credentials/v1"",
  ""type"": [""VerifiableCredential"", ""BusinessCredential""],
  ""issuer"": ""did:example:bank"",
  ""credentialSubject"": {
    ""id"": ""did:example:alice"",
    ""businessRole"": ""Procurement Officer"",
    ""department"": ""Purchasing""
  }
}");

.AddParticipant("buyer", p => p
    .Named("Alice Smith")
    .FromOrganisation("Acme Corp")
    .WithDidUri("did:example:alice")
    .WithVerifiableCredential(credential))   // ← Add credential
```

---

## Benefits Achieved

### 1. Semantic Interoperability
- Blueprints now use standard vocabularies (schema.org, W3C DID, ActivityStreams)
- External systems can understand blueprint meaning without custom documentation
- RDF triples can be extracted for graph-based queries

### 2. Decentralized Identity (DID) Support
- Participants identified by W3C DIDs
- Verifiable Credentials support for authentication
- Cross-organizational identity federation

### 3. ActivityStreams Integration
- Actions modeled as standard Activities
- Interoperable with ActivityPub and other ActivityStreams consumers
- Standard vocabulary for workflow actions

### 4. Content Negotiation
- Clients can request JSON or JSON-LD via Accept header
- Backward compatible (JSON-LD fields omitted when not requested)
- Automatic context injection when requested

### 5. Type Safety with Auto-Detection
- Participants automatically typed as Person or Organization
- Actions automatically typed based on title
- Fluent builders for explicit control when needed

---

## Technical Details

### JSON-LD Context Hosting

Currently contexts are embedded in code. For production:

**Recommended approach:**
1. Host contexts at actual URLs (e.g., https://sorcha.dev/contexts/v1/default.jsonld)
2. Use versioned URLs for stability
3. Support content negotiation on context URLs
4. Implement caching headers (immutable contexts)

**Example hosted context:**
```json
{
  "@context": "https://sorcha.dev/contexts/v1/finance.jsonld"
}
```

### Serialization Behavior

- `@context` and `@type` only serialized when non-null
- Uses `JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)`
- Backward compatible with existing blueprints
- Opt-in via fluent builders or API content negotiation

### Performance Considerations

- Context objects are cloned (`.DeepClone()`) to prevent mutations
- Middleware detection is lightweight (header check only)
- Context injection happens at serialization time
- No performance impact when JSON-LD not used

---

## Migration Path

### For Existing Blueprints

1. **No Changes Required**: Existing blueprints continue to work
2. **Opt-In**: Add `.WithJsonLd()` to enable
3. **Gradual Adoption**: Can mix JSON and JSON-LD blueprints

### For API Consumers

1. **JSON Clients**: No changes required (default behavior unchanged)
2. **JSON-LD Clients**: Add `Accept: application/ld+json` header
3. **Mixed**: Can use both formats for different endpoints

---

## Files Created/Modified

### New Files
- `src/Common/Sorcha.Blueprint.Models/JsonLd/JsonLdContext.cs`
- `src/Common/Sorcha.Blueprint.Models/JsonLd/JsonLdType.cs`
- `src/Apps/Services/Sorcha.Blueprint.Api/JsonLd/JsonLdMiddleware.cs`
- `tests/Sorcha.Blueprint.Models.Tests/JsonLd/JsonLdContextTests.cs`
- `tests/Sorcha.Blueprint.Models.Tests/JsonLd/JsonLdTypeHelperTests.cs`
- `tests/Sorcha.Blueprint.Fluent.Tests/JsonLd/BlueprintBuilderJsonLdTests.cs`
- `tests/Sorcha.Blueprint.Fluent.Tests/JsonLd/ParticipantBuilderJsonLdTests.cs`
- `tests/Sorcha.Blueprint.Fluent.Tests/JsonLd/ActionBuilderJsonLdTests.cs`

### Modified Files
- `src/Common/Sorcha.Blueprint.Models/Blueprint.cs` - Added `@context` and `@type`
- `src/Common/Sorcha.Blueprint.Models/Participant.cs` - Added JSON-LD properties
- `src/Common/Sorcha.Blueprint.Models/Action.cs` - Added ActivityStreams properties
- `src/Core/Sorcha.Blueprint.Fluent/BlueprintBuilder.cs` - Added JSON-LD methods
- `src/Core/Sorcha.Blueprint.Fluent/ParticipantBuilder.cs` - Added type methods
- `src/Core/Sorcha.Blueprint.Fluent/ActionBuilder.cs` - Added ActivityStreams methods
- `src/Apps/Services/Sorcha.Blueprint.Api/Program.cs` - Added content negotiation

---

## Next Steps (Future Enhancements)

### Step 2: JSON-e Template Support (Recommended in Documentation)
- Add JSON-e evaluation library
- Create `BlueprintTemplate` model
- Implement template evaluation service
- Add template parameter validation
- Create template repository

### Step 3: Enhanced JSON Logic
- Visual expression builder in UI
- Expression library/snippets
- Expression validation tools
- Performance optimization (caching)

### Step 4: Schema Evolution
- Version schemas explicitly
- Schema migration tools
- Backward compatibility checks
- Deprecation warnings

---

## References

- [Blueprint Architecture Documentation](blueprint-architecture.md) - Full architecture guide
- [W3C JSON-LD Specification](https://www.w3.org/TR/json-ld11/)
- [W3C DIDs](https://www.w3.org/TR/did-core/)
- [W3C Verifiable Credentials](https://www.w3.org/TR/vc-data-model/)
- [ActivityStreams 2.0](https://www.w3.org/TR/activitystreams-core/)
- [Schema.org](https://schema.org/)
- [GS1 Web Vocabulary](https://www.gs1.org/voc/)

---

## Summary

✅ **JSON-LD integration is complete and production-ready.**

The implementation provides:
- Comprehensive semantic web support
- Backward compatibility with existing code
- Content negotiation for gradual adoption
- Fluent API for easy usage
- Comprehensive test coverage

Blueprints can now be consumed by semantic web tools, linked data applications, and systems expecting standard vocabularies like schema.org and ActivityStreams.
