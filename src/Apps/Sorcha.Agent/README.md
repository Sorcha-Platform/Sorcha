# Sorcha Agent - Autonomous Actor for Decentralised Workflows

**Version:** 1.0.0
**Status:** Production Ready

The Sorcha Agent is a cross-platform CLI tool that runs as an autonomous actor in [Sorcha](https://github.com/sorcha-platform/sorcha) decentralised register workflows. It listens for pending actions, makes decisions using pluggable engines (rules or AI), and submits responses — enabling fully automated multi-participant workflow execution.

## Installation

```bash
# Install as a global tool
dotnet tool install --global Sorcha.Agent

# Verify installation
sorcha-agent --version
```

## Quick Start

### 1. Create an Actor Definition

Actor definitions are JSON files that configure identity, inbox discovery, and decision-making:

```json
{
  "actor": {
    "name": "approver",
    "description": "Automated approval agent"
  },
  "connection": {
    "gatewayUrl": "http://localhost",
    "registerId": "your-register-id",
    "credentials": {
      "email": "$env:AGENT_EMAIL",
      "password": "$env:AGENT_PASSWORD",
      "organizationId": "your-org-id"
    },
    "walletAddress": "your-wallet-address"
  },
  "inbox": {
    "signalR": { "enabled": true },
    "polling": { "enabled": true, "intervalSeconds": 30 }
  },
  "mode": "rules",
  "rules": [
    {
      "actionName": "Review Request",
      "condition": { "==": [true, true] },
      "decision": "approve",
      "payload": {
        "approved": true,
        "notes": "Auto-approved by agent"
      }
    }
  ]
}
```

### 2. Validate Configuration

```bash
sorcha-agent validate --config actor.json
```

This checks JSON structure, variable resolution, authentication, and SignalR connectivity.

### 3. Run the Agent

```bash
sorcha-agent run --config actor.json
```

The agent will authenticate, connect to the inbox, and begin processing actions autonomously.

## Commands

| Command | Description |
|---------|-------------|
| `sorcha-agent run --config <path>` | Start the autonomous actor loop |
| `sorcha-agent validate --config <path>` | Pre-flight configuration checks |

### Options

| Option | Description |
|--------|-------------|
| `--config` | Path to actor definition JSON (required) |
| `--state` | Path to state.json for placeholder resolution |
| `--verbose` | Enable debug-level logging |
| `--quiet` | Errors only |

## Decision Engines

### Rules Engine (JSON Logic)

Deterministic rule evaluation using [JSON Logic](https://jsonlogic.com/) syntax. Rules are evaluated top-to-bottom; first match wins.

```json
{
  "mode": "rules",
  "rules": [
    {
      "actionName": "Cost Approval",
      "condition": { ">": [{ "var": "payload.cost" }, 500000] },
      "decision": "reject",
      "payload": { "reason": "Exceeds budget threshold" }
    },
    {
      "actionName": "Cost Approval",
      "condition": { "==": [true, true] },
      "decision": "approve",
      "payload": { "approved": true }
    }
  ]
}
```

### AI Engine (Claude)

LLM-powered decisions using a persona prompt and action context:

```json
{
  "mode": "ai",
  "ai": {
    "provider": "anthropic",
    "model": "claude-sonnet-4-6",
    "temperature": 0.3,
    "personaFile": "./persona.md",
    "apiKeyEnvVar": "ANTHROPIC_API_KEY"
  }
}
```

## Variable Resolution

- **`$env:VAR_NAME`** — Resolved from environment variables (recommended for secrets)
- **`{{placeholder}}`** — Resolved from a `state.json` file (useful for dynamic IDs from setup scripts)

## Pre-Actions

Hooks that execute before payload submission, e.g. file uploads:

```json
{
  "preActions": [
    {
      "type": "file-upload",
      "config": {
        "fieldName": "document",
        "filePath": "./report.pdf",
        "fileName": "report.pdf",
        "contentType": "application/pdf"
      }
    }
  ]
}
```

## Features

- **Dual Inbox Discovery** — Real-time SignalR + configurable HTTP polling with automatic deduplication
- **Pluggable Decision Engines** — Rules (JSON Logic) or AI (Claude) with schema validation
- **File Upload Pre-Actions** — Chunked encrypted file submission (up to 40MB)
- **Resilient Execution** — Polly retry/circuit-breaker, SignalR auto-reconnect, JWT auto-refresh
- **Audit Logging** — JSONL append-only trail of all decisions and submissions
- **Cross-Platform** — Runs on Windows, macOS, and Linux

## Exit Codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | General error |
| 2 | Authentication error |
| 4 | Validation error |
| 6 | Configuration error |
| 7 | Network error |
| 8 | Service error |

## Requirements

- .NET 10 runtime
- Access to a running Sorcha platform instance
- Valid user credentials with wallet access

## License

[MIT](https://github.com/sorcha-platform/sorcha/blob/master/LICENSE)
