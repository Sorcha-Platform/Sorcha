# Blueprint Designer

The **Blueprint Designer** is the visual, AI-assisted way to build and publish a Sorcha workflow.
The canonical route is **`/designer/blueprint`** (the legacy `/designer` and `/designer/chat` routes
redirect here). It produces the same blueprint JSON you could author by hand — see
[Blueprint Format](blueprints/blueprint-format.md) and the `blueprint-builder` skill — so anything
the designer creates can also be written, versioned, and reviewed as a file.

## The rail: Describe → Understand → Rehearse → Go live

The designer is a four-stage shell (Feature 142). You move along the rail; you cannot **Go live**
until a **Rehearse** has passed for the exact definition you're publishing.

### 1. Describe

Build the workflow. You can converse with the **AI designer** (a streaming assistant over the
`/hubs/chat` ChatHub) to add participants, actions, data fields, routes, disclosures, and credential
requirements/issuance in plain language, and/or edit the structure directly. The AI uses the
standardised schema library — prefer `$ref`-ing the core identity components (`PersonName`,
`DateOfBirth`, `EmailAddress`, `PostalAddress`) over re-inlining them.

Key authoring rules the designer applies (and you should know):
- **One starting action** carries `isStartingAction: true`. Its sender participant is **open** —
  leave its `walletAddress` null so any submitter can be late-bound to the role. Gate it with
  `credentialRequirements` if it should be restricted.
- **Routing uses `routes[]`** (`nextActionIds`, JSON-Logic `condition`, `isDefault`); `nextActionIds: []`
  ends the workflow. (The legacy participant-condition routing is superseded.)
- **Every action declares at least one `disclosure`**; the sender always needs `/*` on their own data.
- **Form layout** is driven by schema `x-` extensions (`x-pages`, `x-sections`, `x-introduction`,
  `x-persona`, `x-file`, `x-review`).

### 2. Understand

Review the **executable definition** — the resolved, `$ref`-flattened blueprint that will actually
run. This is your chance to confirm participants, the action graph, routing, and disclosures before
testing.

### 3. Rehearse

**Dry-run the workflow without publishing.** The designer steps through the actions with sample
input, showing schema validation, computed `calculations`, the chosen `routes`, and per-participant
disclosures at each step — so you can see exactly how the workflow will behave. A successful rehearsal
records a **`RehearsalPass`** bound to a hash of the executable definition.

### 4. Go live

Publishing is **gated server-side by a `RehearsalPass` on the executable-definition hash**: you can
only publish a definition that has passed a rehearsal *of that exact definition*. Change the blueprint
after rehearsing and you must rehearse again — the hash no longer matches. On publish, the Blueprint
Service runs `PublishService.ValidateBlueprint` (structural + credential validation, see the validation
codes in [Blueprint Format](blueprints/blueprint-format.md)) and seals the published blueprint to the
register.

## After Go live

A published blueprint is immutable and versioned. Workflow **instances** are **ledger-derived
projections** (Feature 145): as participants submit actions, the validator seals them and an
`InstanceProjector` advances the instance from a signed `RoutingDecision` on the ledger — the instance
state is reconstructed from the register, not held imperatively. Create an instance via
`POST /api/instances` (mapping any pre-bound participants to wallets; open participants bind on first
submission), then participants act through their *My Actions* queue.

## Authoring as files instead

The designer is optional. Per the platform's blueprint-creation policy, blueprints can be authored
**primarily as JSON or YAML files** (Fluent API for programmatic generation). The format, validation
codes, routing, credentials, disclosures, and form extensions are all documented in
[Blueprint Format](blueprints/blueprint-format.md) and the `blueprint-builder` skill.
