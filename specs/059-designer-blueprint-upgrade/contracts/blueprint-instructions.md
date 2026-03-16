# API Contract: Blueprint Instructions

## Blueprint Model Extension

The Blueprint JSON model gains an optional `instructions` property:

```json
{
  "id": "bp-001",
  "title": "Approval Workflow",
  "description": "...",
  "versionMajor": 2,
  "versionMinor": 1,
  "instructions": {
    "overview": "## Purpose\nThis workflow manages multi-tier approvals...",
    "locale": "en-GB",
    "actionInstructions": {
      "0": "Fill in the request form. All fields marked required must be completed.",
      "1": "Review the submission for completeness and compliance."
    },
    "participantInstructions": {
      "applicant": "You are submitting a request for approval.",
      "reviewer": "Check the request against compliance guidelines before approving."
    },
    "instructionSets": [
      {
        "locale": "fr-FR",
        "source": "did:sorcha:instructions/bp-001/fr",
        "version": "1.0"
      }
    ],
    "governanceRoles": {
      "reviewer": "did:sorcha:participant:reviewer-001",
      "publisher": "did:sorcha:participant:publisher-001"
    }
  },
  "participants": [...],
  "actions": [
    {
      "id": 0,
      "title": "Submit Request",
      "instructions": "Complete all required fields and attach supporting documentation.",
      "form": {
        "type": "Layout",
        "elements": [
          {
            "type": "TextLine",
            "scope": "/requestType",
            "title": "Request Type",
            "instructions": "Select the category that best matches your request."
          }
        ]
      }
    }
  ]
}
```

## Instruction Rendering Priority

When rendering field-level help text in the form UI:

1. **Explicit**: `Control.Instructions` (if non-null/non-empty) — highest priority
2. **Schema fallback**: Bound data schema property `description` (parsed from `Action.DataSchemas` via `Control.Scope`) — automatic
3. **None**: No help text displayed

## Instruction Export Format

`GET /api/blueprints/{id}/instructions/export?locale={locale}`

Response:
```json
{
  "blueprintId": "bp-001",
  "locale": "en-GB",
  "exportedAt": "2026-03-16T14:30:00Z",
  "strings": {
    "blueprint.overview": "## Purpose\nThis workflow manages...",
    "action.0.instructions": "Fill in the request form...",
    "action.1.instructions": "Review the submission...",
    "control.action0./requestType.instructions": "Select the category...",
    "participant.applicant.instructions": "You are submitting..."
  }
}
```

## Instruction Import

`POST /api/blueprints/{id}/instructions/import`

Request body: Same format as export, with target `locale` field.

Behaviour:
- If locale matches primary locale: updates inline instruction text
- If locale is different: creates/updates an InstructionSet entry
