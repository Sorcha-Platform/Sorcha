# AI Tool Definitions

**Feature**: 001-blueprint-chat
**Date**: 2026-02-01

## Overview

These tools are exposed to the AI assistant for manipulating blueprints. Each tool maps to operations in `Sorcha.Blueprint.Fluent`.

---

## create_blueprint

Creates a new blueprint with basic metadata.

**Parameters**:
```json
{
  "type": "object",
  "properties": {
    "title": {
      "type": "string",
      "description": "Blueprint title (3-200 characters)",
      "minLength": 3,
      "maxLength": 200
    },
    "description": {
      "type": "string",
      "description": "Blueprint description (5-2000 characters)",
      "minLength": 5,
      "maxLength": 2000
    }
  },
  "required": ["title", "description"]
}
```

**Returns**:
```json
{
  "blueprintId": "string",
  "message": "Blueprint created successfully"
}
```

**Fluent API**:
```csharp
BlueprintBuilder.Create()
    .WithTitle(title)
    .WithDescription(description)
    .BuildDraft()
```

---

## add_participant

Adds a participant (actor) to the blueprint.

**Parameters**:
```json
{
  "type": "object",
  "properties": {
    "id": {
      "type": "string",
      "description": "Unique participant identifier (e.g., 'applicant', 'reviewer')"
    },
    "name": {
      "type": "string",
      "description": "Display name for the participant"
    },
    "organisation": {
      "type": "string",
      "description": "Organization the participant belongs to"
    },
    "role": {
      "type": "string",
      "enum": ["person", "organization"],
      "description": "Whether this is an individual or an organization"
    }
  },
  "required": ["id", "name"]
}
```

**Returns**:
```json
{
  "participantId": "string",
  "participantCount": 2,
  "message": "Participant 'name' added"
}
```

**Fluent API**:
```csharp
builder.AddParticipant(id, p => p
    .Named(name)
    .FromOrganisation(organisation)
    .AsPerson() // or .AsOrganization()
)
```

---

## remove_participant

Removes a participant from the blueprint.

**Parameters**:
```json
{
  "type": "object",
  "properties": {
    "id": {
      "type": "string",
      "description": "Participant ID to remove"
    }
  },
  "required": ["id"]
}
```

**Returns**:
```json
{
  "success": true,
  "message": "Participant removed",
  "warning": "Actions referencing this participant were also removed"
}
```

**Constraints**:
- Cannot reduce below 2 participants (blueprint minimum)
- Actions referencing participant are removed

---

## add_action

Adds a workflow action (step) to the blueprint.

**Parameters**:
```json
{
  "type": "object",
  "properties": {
    "id": {
      "type": "integer",
      "description": "Action sequence number (0-based)"
    },
    "title": {
      "type": "string",
      "description": "Action title (e.g., 'Submit Application', 'Review', 'Approve')"
    },
    "description": {
      "type": "string",
      "description": "Optional action description"
    },
    "sender": {
      "type": "string",
      "description": "Participant ID who performs this action"
    },
    "isStartingAction": {
      "type": "boolean",
      "description": "Whether this action can initiate the workflow"
    },
    "dataFields": {
      "type": "array",
      "description": "Data fields to collect",
      "items": {
        "type": "object",
        "properties": {
          "name": { "type": "string" },
          "type": { "type": "string", "enum": ["string", "number", "integer", "boolean", "date", "file"] },
          "title": { "type": "string" },
          "required": { "type": "boolean" },
          "constraints": { "type": "object" }
        },
        "required": ["name", "type"]
      }
    },
    "routeToNext": {
      "type": "string",
      "description": "Participant ID for simple linear routing"
    }
  },
  "required": ["id", "title", "sender"]
}
```

**Returns**:
```json
{
  "actionId": 0,
  "message": "Action 'Submit Application' added",
  "actionCount": 2
}
```

**Fluent API**:
```csharp
builder.AddAction(id, a => a
    .WithTitle(title)
    .WithDescription(description)
    .SentBy(sender)
    .IsStartingAction()
    .RequiresData(d => d.AddString(...))
    .RouteToNext(routeToNext)
)
```

---

## update_action

Modifies an existing action.

**Parameters**:
```json
{
  "type": "object",
  "properties": {
    "id": {
      "type": "integer",
      "description": "Action ID to update"
    },
    "title": { "type": "string" },
    "description": { "type": "string" },
    "sender": { "type": "string" },
    "isStartingAction": { "type": "boolean" },
    "dataFields": { "type": "array" },
    "routeToNext": { "type": "string" }
  },
  "required": ["id"]
}
```

**Returns**:
```json
{
  "actionId": 0,
  "message": "Action updated",
  "fieldsChanged": ["title", "sender"]
}
```

**Notes**:
- Only provided fields are updated
- Rebuilds action with merged properties

---

## set_disclosure

Configures which data fields a participant can see.

**Parameters**:
```json
{
  "type": "object",
  "properties": {
    "actionId": {
      "type": "integer",
      "description": "Action ID where disclosure applies"
    },
    "participantId": {
      "type": "string",
      "description": "Participant who receives the disclosure"
    },
    "fields": {
      "type": "array",
      "description": "JSON Pointer paths to disclosed fields",
      "items": { "type": "string" },
      "examples": [["/applicantName", "/loanAmount"], ["/*"]]
    }
  },
  "required": ["actionId", "participantId", "fields"]
}
```

**Returns**:
```json
{
  "message": "Disclosure configured for reviewer",
  "fieldsDisclosed": 3
}
```

**Fluent API**:
```csharp
actionBuilder.Disclose(participantId, disc => disc
    .Fields(fields)
    // or .AllFields() for "/*"
)
```

---

## add_routing

Adds conditional routing to an action.

**Parameters**:
```json
{
  "type": "object",
  "properties": {
    "actionId": {
      "type": "integer",
      "description": "Action ID to add routing to"
    },
    "conditions": {
      "type": "array",
      "description": "Routing conditions",
      "items": {
        "type": "object",
        "properties": {
          "field": { "type": "string", "description": "Field to evaluate" },
          "operator": { "type": "string", "enum": ["equals", "notEquals", "greaterThan", "lessThan", "contains"] },
          "value": { "description": "Value to compare against" },
          "routeTo": { "type": "string", "description": "Participant ID if condition matches" }
        },
        "required": ["field", "operator", "value", "routeTo"]
      }
    },
    "defaultRoute": {
      "type": "string",
      "description": "Participant ID for default/else case"
    }
  },
  "required": ["actionId", "conditions"]
}
```

**Returns**:
```json
{
  "message": "Conditional routing added",
  "routeCount": 2
}
```

**Fluent API**:
```csharp
actionBuilder.RouteConditionally(cond => cond
    .When(j => j.Equals(field, value))
    .ThenRoute(routeTo)
    .ElseRoute(defaultRoute)
)
```

---

## validate_blueprint

Validates the current blueprint and returns any errors.

**Parameters**:
```json
{
  "type": "object",
  "properties": {},
  "required": []
}
```

**Returns**:
```json
{
  "isValid": false,
  "errors": [
    {
      "code": "MIN_PARTICIPANTS",
      "message": "Blueprint requires at least 2 participants",
      "location": "participants"
    }
  ],
  "warnings": [
    {
      "code": "NO_STARTING_ACTION",
      "message": "No action is marked as a starting action",
      "location": "actions"
    }
  ]
}
```

**Error Codes**:
- `MIN_PARTICIPANTS`: Fewer than 2 participants
- `MIN_ACTIONS`: No actions defined
- `MISSING_SENDER`: Action has no sender
- `INVALID_PARTICIPANT_REF`: Action references non-existent participant
- `ORPHAN_ACTION`: Action not reachable from any starting action
- `CIRCULAR_ROUTE`: Routing creates infinite loop

---

## System Prompt Context

The AI receives this context about available tools:

```
You are a blueprint design assistant. You help users create workflow blueprints for the Sorcha decentralised register platform.

Available tools:
- create_blueprint: Start a new blueprint (required first step)
- add_participant: Add people or organizations to the workflow (minimum 2 required)
- remove_participant: Remove an actor from the workflow
- add_action: Add workflow steps (what each participant does)
- update_action: Modify an existing action
- set_disclosure: Control who can see what data
- add_routing: Add decision points (if/then logic)
- validate_blueprint: Check if the blueprint is complete and valid

Blueprint rules:
- Every blueprint needs at least 2 participants
- Every blueprint needs at least 1 action
- Every action needs a sender (who does it)
- At least one action must be a starting action
- Use disclosure to control data privacy between participants

Always validate the blueprint after making changes to show users any issues.
```
