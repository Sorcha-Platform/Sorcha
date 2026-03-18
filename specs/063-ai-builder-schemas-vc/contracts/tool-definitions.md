# AI Tool Contracts: Blueprint Builder Enhancement

**Branch**: `063-ai-builder-schemas-vc` | **Date**: 2026-03-18

These are the 5 new tool definitions to add to `BlueprintToolExecutor`. Each tool is defined as a `ToolDefinition` with name, description, and JSON Schema input.

## Tool 1: search_schemas

**Purpose**: Query the standardised schema library by name, category, or keyword.

```json
{
  "name": "search_schemas",
  "description": "Search the standardised schema library for reusable data schemas. Returns matching schema summaries including identifier, title, category, description, and field count. Use this to find appropriate schemas before applying them with use_standard_schema.",
  "input_schema": {
    "type": "object",
    "properties": {
      "query": {
        "type": "string",
        "description": "Search term to match against schema names, descriptions, tags, and keywords"
      },
      "category": {
        "type": "string",
        "enum": ["people-identity", "financial", "documents-evidence", "compliance-governance", "supply-chain", "healthcare", "credentials"],
        "description": "Optional category filter"
      }
    },
    "required": ["query"]
  }
}
```

**Response Format**:
```json
{
  "results": [
    {
      "identifier": "uk-address",
      "title": "UK Address",
      "category": "people-identity",
      "description": "Standard UK postal address with postcode validation",
      "fieldCount": 5,
      "fieldNames": ["addressLine1", "addressLine2", "city", "county", "postcode"],
      "tags": ["address", "uk", "postal"]
    }
  ],
  "totalCount": 1,
  "message": "Found 1 schema matching 'address'"
}
```

---

## Tool 2: use_standard_schema

**Purpose**: Apply a standardised schema's fields to an action's data definition, including form layout.

```json
{
  "name": "use_standard_schema",
  "description": "Apply a standardised schema to a blueprint action's data definition. This imports all fields, types, constraints, and form layout from the schema. Call search_schemas first to find the schema identifier.",
  "input_schema": {
    "type": "object",
    "properties": {
      "schemaId": {
        "type": "string",
        "description": "Schema identifier (e.g., 'uk-address', 'payment-details')"
      },
      "actionId": {
        "type": "integer",
        "description": "Action ID to apply the schema to"
      },
      "merge": {
        "type": "boolean",
        "description": "If true, merge with existing fields. If false, replace existing schema. Default: true"
      }
    },
    "required": ["schemaId", "actionId"]
  }
}
```

**Response Format**:
```json
{
  "message": "Applied 'UK Address' schema to action 'Submit Application'",
  "schemaId": "uk-address",
  "actionId": 0,
  "fieldsAdded": ["addressLine1", "addressLine2", "city", "county", "postcode"],
  "disclosureRecommendation": "Full address is generally needed by all recipients. Consider restricting to postcode only for summary views.",
  "sensitiveFields": [],
  "blueprintChanged": true
}
```

---

## Tool 3: require_credential

**Purpose**: Add a Verified Credential requirement to a blueprint action.

```json
{
  "name": "require_credential",
  "description": "Add a Verified Credential requirement to a blueprint action. The participant performing this action must present a valid credential of the specified type before the action can be executed. Use schemas from the 'credentials' category to reference known credential types.",
  "input_schema": {
    "type": "object",
    "properties": {
      "actionId": {
        "type": "integer",
        "description": "Action ID to add the credential requirement to"
      },
      "credentialType": {
        "type": "string",
        "description": "Type of credential required (e.g., 'TrainingCertificate', 'ProfessionalLicense', 'ProductPassport')"
      },
      "acceptedIssuers": {
        "type": "array",
        "items": { "type": "string" },
        "description": "List of trusted issuer DIDs or addresses. Empty array means any issuer is accepted."
      },
      "requiredClaims": {
        "type": "array",
        "description": "Claims that must be present in the credential",
        "items": {
          "type": "object",
          "properties": {
            "claimName": { "type": "string", "description": "Claim name to require" },
            "expectedValue": { "description": "Optional expected value (null means any value accepted)" }
          },
          "required": ["claimName"]
        }
      },
      "revocationPolicy": {
        "type": "string",
        "enum": ["FailClosed", "FailOpen"],
        "description": "What happens if revocation status cannot be checked. FailClosed (default): reject. FailOpen: accept."
      },
      "description": {
        "type": "string",
        "description": "Human-readable description of why this credential is required"
      }
    },
    "required": ["actionId", "credentialType"]
  }
}
```

**Response Format**:
```json
{
  "message": "Added credential requirement 'TrainingCertificate' to action 'Submit Application'",
  "actionId": 0,
  "credentialType": "TrainingCertificate",
  "acceptedIssuers": ["did:example:training-provider"],
  "requiredClaims": [{"claimName": "courseCompleted"}],
  "revocationPolicy": "FailClosed",
  "blueprintChanged": true
}
```

---

## Tool 4: issue_credential

**Purpose**: Configure an action to issue a Verified Credential upon completion.

```json
{
  "name": "issue_credential",
  "description": "Configure a blueprint action to issue a Verified Credential when executed. The credential is signed by the action sender's wallet and delivered to the specified recipient. Use this for actions that produce certifications, approvals, or attestations.",
  "input_schema": {
    "type": "object",
    "properties": {
      "actionId": {
        "type": "integer",
        "description": "Action ID that issues the credential"
      },
      "credentialType": {
        "type": "string",
        "description": "Type of credential to issue (e.g., 'TrainingCompletionCertificate', 'ApprovalAttestation')"
      },
      "claimMappings": {
        "type": "array",
        "description": "Map action data fields to credential claims",
        "items": {
          "type": "object",
          "properties": {
            "claimName": { "type": "string", "description": "Claim name in the issued credential" },
            "sourceField": { "type": "string", "description": "JSON Pointer to action data field (e.g., '/applicantName', '/courseTitle')" }
          },
          "required": ["claimName", "sourceField"]
        }
      },
      "recipientParticipantId": {
        "type": "string",
        "description": "Participant ID who receives the credential"
      },
      "expiryDuration": {
        "type": "string",
        "description": "ISO 8601 duration for credential validity (e.g., 'P365D' for 1 year, 'P2Y' for 2 years)"
      },
      "usagePolicy": {
        "type": "string",
        "enum": ["Reusable", "SingleUse", "LimitedUse"],
        "description": "How many times the credential can be presented. Default: Reusable"
      }
    },
    "required": ["actionId", "credentialType", "claimMappings", "recipientParticipantId"]
  }
}
```

**Response Format**:
```json
{
  "message": "Configured action 'Complete Training' to issue 'TrainingCompletionCertificate' to 'trainee'",
  "actionId": 2,
  "credentialType": "TrainingCompletionCertificate",
  "claimCount": 4,
  "recipientParticipantId": "trainee",
  "expiryDuration": "P2Y",
  "usagePolicy": "Reusable",
  "blueprintChanged": true
}
```

---

## Tool 5: search_templates

**Purpose**: Query the blueprint template catalogue by name, category, or keyword.

```json
{
  "name": "search_templates",
  "description": "Search the blueprint template catalogue for existing templates that match the user's workflow needs. Returns template summaries including title, category, and description. If a good match exists, suggest using it as a starting point rather than building from scratch.",
  "input_schema": {
    "type": "object",
    "properties": {
      "query": {
        "type": "string",
        "description": "Search term to match against template titles, descriptions, categories, and tags"
      },
      "category": {
        "type": "string",
        "description": "Optional category filter (e.g., 'approval', 'finance', 'demo', 'system')"
      }
    },
    "required": ["query"]
  }
}
```

**Response Format**:
```json
{
  "results": [
    {
      "id": "approval-workflow-001",
      "title": "Multi-Tier Approval Workflow",
      "category": "approval",
      "description": "Flexible multi-tier approval workflow with configurable approval stages",
      "version": 1,
      "participantCount": 5,
      "actionCount": 6
    }
  ],
  "totalCount": 1,
  "message": "Found 1 template matching 'approval'"
}
```
