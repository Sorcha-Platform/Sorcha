# Research: 080 — CLI Modernisation and Feature Completion

## Decision 1: Output Formatting Stack

**Decision**: Use existing Spectre.Console (v0.54.0) for table output, System.Text.Json for JSON, existing CsvOutputFormatter for CSV, add YamlDotNet (v16.3.0, already centrally defined) for YAML.

**Rationale**: All dependencies already available. Spectre.Console is already used by TableOutputFormatter. YamlDotNet is in Directory.Packages.props (used by McpServer, UI, Blueprint Service).

**Alternatives**: Could use ConsoleTables or manual formatting — rejected since Spectre.Console is already there and more capable.

## Decision 2: Event Streaming

**Decision**: Add Microsoft.AspNetCore.SignalR.Client (v10.0.5, already in solution) to Sorcha.Cli. Connect to existing `/hubs/register` and `/hubs/events` SignalR endpoints.

**Rationale**: Hub infrastructure exists. Client library is already used by Sorcha.UI.Core. No new server-side work needed.

**Alternatives**: Could use gRPC streaming — rejected since SignalR hubs already exist with the right events.

## Decision 3: Retry/Resilience

**Decision**: Use Microsoft.Extensions.Http.Polly (v10.0.5, already referenced) for HTTP retry policies. Configure on the Refit HttpClient registrations.

**Rationale**: Package already in .csproj. Standard Polly retry with exponential backoff for transient HTTP errors (5xx, timeout, network).

## Decision 4: YAML Output

**Decision**: Add YamlDotNet to CLI .csproj (version from central packages). Create YamlOutputFormatter alongside existing formatters.

**Rationale**: Already centrally managed. Simple serializer call. Minimal effort.

## Decision 5: Machine-Readable Schema

**Decision**: `--machine-readable` wraps all output in a standard envelope: `{"status": "success|error", "command": "register list", "data": [...], "errors": [], "timestamp": "ISO8601"}`.

**Rationale**: Consistent schema enables MCP tools to parse any command output without command-specific parsing.

## Decision 6: Shell Completion

**Decision**: Use System.CommandLine's built-in `dotnet-suggest` completion support. Add `sorcha completion <shell>` that outputs the appropriate script.

**Rationale**: System.CommandLine 2.x has native completion support. Minimal custom code needed.

## Decision 7: ASCII Art Banner

**Decision**: Static ASCII art stored as an embedded resource or string constant. Displayed via Spectre.Console markup for colour support. Keep it compact (5-7 lines).

**Rationale**: Spectre.Console already handles ANSI colour. Static text avoids runtime generation complexity.
