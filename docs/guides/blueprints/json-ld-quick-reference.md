# JSON-LD Quick Reference Guide

## Enable JSON-LD in Blueprints

### Basic Usage

```csharp
var blueprint = BlueprintBuilder.Create()
    .WithTitle("My Workflow")
    .WithDescription("Description here")
    .WithJsonLd()                        // ← Add this line
    .AddParticipant(...)
    .AddAction(...)
    .Build();
```

### Category-Specific Context

```csharp
.WithJsonLd("finance")        // Finance/loan workflows
.WithJsonLd("supply-chain")   // Supply chain/logistics
```

### Custom Context

```csharp
var customContext = JsonNode.Parse(@"{""myField"": ""https://example.com/vocab#myField""}");
.WithJsonLdContext(customContext)
```

---

## Participants

### As Person

```csharp
.AddParticipant("alice", p => p
    .Named("Alice Smith")
    .FromOrganisation("Self")
    .WithDidUri("did:example:alice")
    .AsPerson())                      // ← Explicit Person type
```

### As Organization

```csharp
.AddParticipant("bank", p => p
    .Named("Community Bank")
    .FromOrganisation("Bank Corp")
    .WithDidUri("did:example:bank")
    .AsOrganization())                // ← Explicit Organization type
```

### Auto Type Detection

```csharp
// Automatically becomes Person (org is "Self")
.AddParticipant("user", p => p
    .Named("John")
    .FromOrganisation("Self"))

// Automatically becomes Organization
.AddParticipant("company", p => p
    .Named("Acme")
    .FromOrganisation("Acme Corp"))
```

### With Verifiable Credential

```csharp
var credential = JsonNode.Parse(@"{
  ""@context"": ""https://www.w3.org/2018/credentials/v1"",
  ""type"": [""VerifiableCredential""],
  ""issuer"": ""did:example:issuer"",
  ""credentialSubject"": {
    ""id"": ""did:example:alice"",
    ""role"": ""Manager""
  }
}");

.AddParticipant("manager", p => p
    .Named("Alice")
    .WithVerifiableCredential(credential))
```

---

## Actions

### ActivityStreams Types

```csharp
.AddAction(0, a => a
    .WithTitle("Submit Application")
    .SentBy("applicant")
    .AsCreateAction())              // ← Create action

.AddAction(1, a => a
    .WithTitle("Approve Request")
    .SentBy("approver")
    .AsAcceptAction())              // ← Accept action

.AddAction(2, a => a
    .WithTitle("Reject Application")
    .SentBy("approver")
    .AsRejectAction())              // ← Reject action

.AddAction(3, a => a
    .WithTitle("Update Profile")
    .SentBy("user")
    .AsUpdateAction())              // ← Update action
```

### Auto Type Detection

Actions get types based on title keywords:
- "Submit", "Create", "Apply" → `as:Create`
- "Approve", "Accept", "Endorse" → `as:Accept`
- "Reject", "Deny", "Decline" → `as:Reject`
- "Update", "Modify", "Edit" → `as:Update`
- Others → `as:Activity`

### With Target and Timestamp

```csharp
.AddAction(0, a => a
    .WithTitle("Send Request")
    .SentBy("requester")
    .WithTarget("approver")              // ← ActivityStreams target
    .PublishedAt(DateTimeOffset.UtcNow)) // ← Timestamp
```

---

## API Usage

### Request JSON-LD

```bash
# Standard JSON
curl http://localhost:5000/api/blueprints/123

# JSON-LD (with @context)
curl -H "Accept: application/ld+json" http://localhost:5000/api/blueprints/123
```

### Check Response

JSON-LD responses include:
```json
{
  "@context": { ... },
  "@type": "Blueprint",
  "id": "123",
  ...
}
```

---

## Complete Example

```csharp
var loanBlueprint = BlueprintBuilder.Create()
    .WithTitle("Loan Application Workflow")
    .WithDescription("Two-party loan application process")
    .WithJsonLd("finance")                           // ← Finance context

    .AddParticipant("applicant", p => p
        .Named("John Doe")
        .FromOrganisation("Self")
        .WithDidUri("did:example:applicant")
        .WithWallet("0x1234567890abcdef")
        .AsPerson())                                 // ← Person

    .AddParticipant("bank", p => p
        .Named("Community Bank")
        .FromOrganisation("Bank Corp")
        .WithDidUri("did:example:bank")
        .AsOrganization())                           // ← Organization

    .AddAction(0, a => a
        .WithTitle("Submit Loan Application")
        .WithDescription("Applicant submits application")
        .SentBy("applicant")
        .WithTarget("bank")
        .AsCreateAction()                            // ← Create
        .PublishedAt(DateTimeOffset.UtcNow)
        .RequiresData(d => d
            .AddNumber("amount", f => f
                .WithTitle("Loan Amount")
                .WithMinimum(1000)
                .IsRequired())))

    .AddAction(1, a => a
        .WithTitle("Review and Decide")
        .SentBy("bank")
        .WithTarget("applicant")
        .AsAcceptAction()                            // ← Accept/Reject
        .RequiresData(d => d
            .AddString("decision", f => f
                .WithEnum(new[] { "approved", "rejected" })
                .IsRequired())))

    .Build();
```

---

## JSON-LD Context Types

### Default
```csharp
.WithJsonLd()
```
Maps to: schema.org, W3C DID, ActivityStreams

### Finance
```csharp
.WithJsonLd("finance")
```
Adds: LoanApplication, creditScore, loanAmount

### Supply Chain
```csharp
.WithJsonLd("supply-chain")
```
Adds: GS1 vocabulary, Order, Product, tracking

---

## Common Patterns

### DID-Based Participants

```csharp
.AddParticipant("user", p => p
    .Named("Alice Smith")
    .WithDidUri("did:example:alice")
    .WithWallet("0x..."))
```

### Multi-Step Approval

```csharp
.AddAction(0, a => a.AsCreateAction())
.AddAction(1, a => a.AsAcceptAction())   // Or .AsRejectAction()
.AddAction(2, a => a.AsUpdateAction())
```

### Custom Properties

```csharp
.WithAdditionalProperty("customField",
    JsonNode.Parse(@"{""value"": ""data""}"))
```

---

## Testing

### Check JSON-LD Output

```csharp
var json = JsonSerializer.Serialize(blueprint, new JsonSerializerOptions
{
    WriteIndented = true
});

Assert.Contains("\"@context\"", json);
Assert.Contains("\"@type\"", json);
```

### Validate Context

```csharp
Assert.NotNull(blueprint.JsonLdContext);
Assert.Equal(JsonLdTypes.Blueprint, blueprint.JsonLdType);
```

---

## Troubleshooting

### Context Not Appearing
- Ensure `.WithJsonLd()` was called
- Check serialization settings don't exclude null values

### Wrong Participant Type
- Use `.AsPerson()` or `.AsOrganization()` explicitly
- Check organization name isn't "Self" or "Individual"

### Action Type Not Set
- Use explicit `.AsCreateAction()`, etc.
- Or let auto-detection work from title keywords

---

## Resources

- [Full Documentation](blueprint-architecture.md)
- [Implementation Summary](json-ld-implementation-summary.md)
- [W3C JSON-LD Spec](https://www.w3.org/TR/json-ld11/)
- [ActivityStreams](https://www.w3.org/TR/activitystreams-core/)
