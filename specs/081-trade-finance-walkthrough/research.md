# Research: Trade Finance Walkthrough

## Decision 1: Blueprint Template Structure

**Decision**: Follow the exact JSON envelope pattern from ConstructionPermit and SelfBuildHouse.

**Rationale**: The existing blueprint template structure is well-established across two walkthroughs. The top-level wrapper has `id`, `title`, `description`, `version`, `category`, `tags`, `author`, `published`, `template`, `parameterSchema`, `defaultParameters` (null), and `examples`. Participant wallet addresses exist only in `parameterSchema` and `examples` — never in `participants` or `disclosures`. All internal references use logical participant IDs.

**Alternatives considered**: Creating a simplified blueprint format was rejected because `Publish-SorchaBlueprint` in the shared module expects this exact structure.

## Decision 2: MCP Multi-Connection Pattern

**Decision**: Use distinct MCP server key names per participant role (e.g., `sorcha-procurement-mgr`, `sorcha-sales-mgr`).

**Rationale**: Claude Code supports multiple MCP servers with unique key names in `settings.json` or `.mcp.json`. Each spawns a separate `Sorcha.McpServer` process with its own JWT token. Tools are namespaced as `mcp__<server-key>__<tool-name>` (e.g., `mcp__sorcha-procurement-mgr__sorcha_inbox_list`), preventing collisions between instances. The Sorcha MCP Server has no built-in prefix configuration — the `sorcha_` prefix is hardcoded in `[McpServerTool]` attributes — so server-key namespacing is the correct disambiguation mechanism.

**Alternatives considered**: Modifying the MCP Server to support configurable tool prefixes was rejected as unnecessary scope expansion.

## Decision 3: Scenario Data Structure

**Decision**: Follow the SelfBuildHouse pattern with separate sub-objects per blueprint.

**Rationale**: The trade finance walkthrough has two blueprints on two registers, matching SelfBuildHouse's architecture. Scenario files will have `procurement` and `finance` sub-objects (keyed by action ID string) instead of SelfBuildHouse's `planning` and `warrant`. Top-level fields include expected paths per blueprint, expected calculated values, and rejection metadata.

**Alternatives considered**: The ConstructionPermit flat `actions` object was rejected because it doesn't support the cross-register pattern.

## Decision 4: Blueprint File Location

**Decision**: Place blueprint files directly in the walkthrough root directory (not a `blueprints/` subdirectory).

**Rationale**: Both ConstructionPermit and SelfBuildHouse place their template JSON files directly in the walkthrough folder, not in a subdirectory. The design spec proposed a `blueprints/` subdirectory, but following existing convention is more consistent.

**Alternatives considered**: The `blueprints/` subdirectory from the design spec. Changed to match existing conventions, reducing confusion for anyone familiar with the codebase.

## Decision 5: Config.json Structure

**Decision**: Follow the SelfBuildHouse config pattern with `registers` (array) and `templates` (array).

**Rationale**: SelfBuildHouse already handles the two-register, two-blueprint pattern. The config will declare 4 organisations, 2 registers, and reference both template files and all 3 scenario files.

## Decision 6: Agent Prompt Delivery

**Decision**: Agent prompts are Markdown files in `prompts/` that operators paste into Claude Code sessions or reference via `--prompt-file`.

**Rationale**: Claude Code supports loading system prompts from files. The setup wizard generates the MCP config snippets; the operator configures them in their Claude Code settings, then starts a new session with the agent prompt. This keeps the walkthrough self-contained without requiring custom tooling.

**Alternatives considered**: Embedding prompts in a launcher script was rejected because it would require the walkthrough to manage Claude Code session lifecycle, which is outside its scope.

## Decision 7: Manifest vs Config

**Decision**: Merge the proposed `manifest.json` into an extended `config.json` rather than maintaining two separate files.

**Rationale**: The existing `config.json` already contains organisations and template references. Adding participants, wallets, registers, and scenario metadata to it avoids a second file that largely duplicates the same information. The setup wizard reads `config.json` as its manifest.

**Alternatives considered**: Separate `manifest.json` as proposed in the design spec. Merged because the config already serves this role in existing walkthroughs.
