# Walkthrough Patterns Reference

## Blueprint Template Structure

```json
{
  "title": "Workflow Name",
  "description": "What this workflow does",
  "hasCycles": false,
  "instanceReference": {
    "prefix": "WF",
    "components": [{ "field": "/fieldName", "transform": "FirstWord", "chars": 3 }]
  },
  "participants": [
    {
      "id": "role-id",
      "name": "Display Name",
      "description": "Role description",
      "walletAddress": "WALLET_PLACEHOLDER_role-id"
    }
  ],
  "actions": [
    {
      "id": 1,
      "name": "Action Name",
      "description": "What this action does",
      "sender": "role-id",
      "isStartingAction": true,
      "requiredPriorActions": [],
      "payloadSchema": {
        "type": "object",
        "properties": { ... },
        "required": [ ... ]
      },
      "disclosureRules": [
        { "participantId": "role-id", "visibleFields": ["/*"] }
      ],
      "routes": [
        {
          "id": "route-name",
          "nextActionIds": [2],
          "isDefault": true
        }
      ]
    }
  ]
}
```

## Conditional Routing

```json
"routes": [
  {
    "id": "high-risk",
    "nextActionIds": [4],
    "condition": { ">=": [{ "var": "riskScore" }, 7] },
    "description": "High risk → environmental review"
  },
  {
    "id": "low-risk",
    "nextActionIds": [5],
    "isDefault": true,
    "description": "Low risk → skip to building control"
  }
]
```

## Credential Issuance

```json
"credentialIssuanceConfig": {
  "credentialType": "PlanningPermissionCredential",
  "recipientParticipantId": "self-builder",
  "expiryDuration": "P1095D",
  "claimMappings": [
    { "claimName": "permitReference", "sourceField": "/permitReference" }
  ],
  "disclosableClaims": ["permitReference", "siteAddress"]
}
```

## Credential Requirement

```json
"credentialRequirements": [
  {
    "credentialType": "PlanningPermissionCredential",
    "requiredClaims": ["permitReference", "siteAddress"],
    "revocationCheckPolicy": "FailClosed",
    "purpose": "Valid planning permission required"
  }
]
```

## JSON Logic Calculations

```json
"calculations": [
  {
    "outputField": "riskScore",
    "expression": { "+": [
      { "var": "soilRiskFactor" },
      { "var": "waterTableRiskFactor" }
    ]},
    "description": "Foundation risk score"
  }
]
```

## Dispute/Loop Routes

```json
"routes": [
  {
    "id": "dispute",
    "nextActionIds": [5],
    "condition": { "==": [{ "var": "decision" }, "dispute"] },
    "description": "Disputed — return to prior action"
  },
  {
    "id": "approved",
    "nextActionIds": [],
    "isDefault": true,
    "description": "Approved — terminal"
  }
]
```

Blueprint must set `"hasCycles": true` for loop routes.

## Actor Rule Matching

Rules evaluate top-to-bottom, first match wins:

```json
"rules": [
  {
    "actionName": "Review",
    "condition": { ">": [{ "var": "payload.cost" }, 500000] },
    "decision": "reject",
    "payload": { "reason": "Over threshold" }
  },
  {
    "actionName": "Review",
    "decision": "approve",
    "payload": { "notes": "Approved" }
  }
]
```

- Condition `null`/absent = always true (catch-all)
- `var` references: `payload.*` (previous action), `action.name`, `action.index`
- No match = skip (action stays in inbox for next poll)

## State.json Key Paths

### Per-Role Model (ConstructionPermit, TradeFinance)
```
{{registerId}}                      → state.registerId
{{roles.role-name.email}}           → state.roles["role-name"].email
{{roles.role-name.password}}        → (use $env: instead)
{{roles.role-name.organizationId}}  → state.roles["role-name"].organizationId
{{roles.role-name.walletAddress}}   → state.roles["role-name"].walletAddress
```

### Single-Admin Model (SelfBuildHouse)
```
{{organizationId}}                  → state.organizationId
{{wallets.role-name}}               → state.wallets["role-name"]
{{planningRegisterId}}              → state.planningRegisterId
```

### Multi-Register Model (TradeFinance)
```
{{registers.register-name.id}}      → state.registers["register-name"].id
{{blueprints.blueprint-name.id}}    → state.blueprints["blueprint-name"].id
```
