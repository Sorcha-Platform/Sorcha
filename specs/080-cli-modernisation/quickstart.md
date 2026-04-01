# Quickstart: 080 — CLI Modernisation

## What This Feature Does

Modernises the Sorcha CLI from a basic tool to a polished, production-ready command-line interface with consistent output, real-time event streaming, full API coverage, bulk operations, and MCP/AI integration.

## Key Changes

### New Files (~15)
- `Commands/EventWatchCommand.cs` — SignalR event streaming
- `Commands/HealthCommand.cs` — Aggregated health check
- `Commands/InvitationCommands.cs` — Register invitation management
- `Commands/AuditCommands.cs` — Audit log querying
- `Commands/VerifyCommands.cs` — Receipt/bundle/proof verification
- `Commands/PlatformCommands.cs` — Platform management
- `Commands/CompletionCommand.cs` — Shell completion scripts
- `Commands/HelpCommand.cs` — Getting-started guide
- `Commands/YamlOutputFormatter.cs` — YAML output format
- `Commands/MachineReadableFormatter.cs` — JSON envelope wrapper
- `Services/IInvitationServiceClient.cs` — Refit: invitation endpoints
- `Services/IAuditServiceClient.cs` — Refit: audit endpoints
- `Services/IVerificationServiceClient.cs` — Refit: verification endpoints
- `Services/EventStreamService.cs` — SignalR client wrapper
- `Branding/SorchaBanner.cs` — ASCII art and version display

### Modified Files (~30+)
- `Program.cs` — Add new commands, banner, global options
- All 20 command files — Consistent formatting, examples, pagination
- All 9 Refit client interfaces — Update to match current APIs
- All 3 existing formatter files — Fixes and consistency
- `Sorcha.Cli.csproj` — New dependencies (SignalR.Client, YamlDotNet)

## Testing Approach

```bash
# 1. Branding
sorcha --help          # Verify ASCII banner + profile status
sorcha version         # Verify banner + version info

# 2. Output consistency
sorcha register list --output json | python -m json.tool   # Valid JSON
sorcha register list --output csv > test.csv               # Valid CSV
sorcha register list --output yaml                         # Valid YAML
sorcha register list --machine-readable | jq .status       # Envelope

# 3. Event streaming
sorcha events watch --register <id> --output json          # JSON lines
# Submit transaction on register, verify event appears

# 4. Health
sorcha health                        # All services
sorcha health --service register     # Single service

# 5. Pagination
sorcha register list --page 1 --page-size 5

# 6. Bulk ops
sorcha wallet create-batch --count 3 --algorithm ED25519

# 7. Config
sorcha config view
sorcha config validate

# 8. Completion
sorcha completion bash > /tmp/test.sh && source /tmp/test.sh
```
