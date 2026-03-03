# Sorcha Blueprints

Blueprints are declarative JSON documents that define multi-party data flow workflows. They specify participants, actions, data schemas, routing logic, and disclosure rules.

## Directory Structure

```
blueprints/
├── templates/       # Production-ready starter templates
│   ├── ping-pong-template.json           # Simple two-party exchange
│   ├── approval-workflow-template.json   # Multi-step approval process
│   ├── license-approval-template.json    # License approval flow
│   ├── loan-application-template.json    # Financial loan workflow
│   ├── supply-chain-order-template.json  # Supply chain management
│   ├── work-order-template.json          # Work order lifecycle
│   └── register-governance-v1.json       # Register governance rules
└── examples/        # Domain-specific examples by industry
    ├── benefits/    # Government benefits processing
    ├── finance/     # Financial services workflows
    ├── healthcare/  # Healthcare data exchange
    └── supply-chain/# Logistics and supply chain
```

## Templates vs Examples

**Templates** are production-ready starting points. Clone one and customize it for your use case.

**Examples** demonstrate domain-specific patterns at varying complexity levels:
- `simple-*` — Minimal workflows for learning (2-3 participants, 3-5 actions)
- `moderate-*` — Real-world patterns (3-5 participants, conditional routing)
- `complex-*` — Enterprise scenarios (5+ participants, multi-stage validation)

## Blueprint Structure

Every blueprint follows this structure:

```json
{
  "title": "Workflow Name",
  "description": "What this workflow does",
  "version": "1.0.0",
  "participants": [
    { "role": "submitter", "description": "Submits data" },
    { "role": "reviewer", "description": "Reviews submissions" }
  ],
  "actions": [
    {
      "title": "Submit Data",
      "assignedTo": "submitter",
      "schema": { "type": "object", "properties": { ... } },
      "routing": { ... },
      "disclosure": { ... }
    }
  ]
}
```

## Getting Started

1. Pick a template from `templates/` or an example from `examples/`
2. Publish it to a register via the API or CLI:
   ```bash
   sorcha blueprint publish --file blueprints/templates/ping-pong-template.json
   ```
3. See [docs/blueprint-quick-start.md](../docs/getting-started/blueprint-quick-start.md) for a full tutorial

## Schema Reference

The blueprint JSON Schema is at `src/Common/blueprint.schema.json`. Validate your blueprints against it for correctness.
