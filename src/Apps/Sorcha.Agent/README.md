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
| `sorcha-agent haip receive --offer-uri <uri>` | Receive a credential via OID4VCI pre-authorized code flow |
| `sorcha-agent haip present --request-uri <uri>` | Present a credential via OID4VP direct_post |

### Options

| Option | Description |
|--------|-------------|
| `--config` | Path to actor definition JSON (required) |
| `--state` | Path to state.json for placeholder resolution |
| `--verbose` | Enable debug-level logging |
| `--quiet` | Errors only |

## HAIP Wallet Commands

The agent can act as a simulated HAIP wallet for end-to-end testing of OpenID4VCI and OpenID4VP flows. These commands are standalone (they do not use actor JSON files or the autonomous loop) and operate on a local file-based wallet directory.

### Receive a Credential (OpenID4VCI)

```bash
sorcha-agent haip receive --offer-uri <uri> --wallet-dir ./wallet
```

Executes the OID4VCI pre-authorized code flow:
1. Parses the `openid-credential-offer://` URI to extract the offer JSON
2. Fetches issuer metadata from `/.well-known/openid-credential-issuer`
3. Exchanges the pre-authorized code for an access token and `c_nonce`
4. Builds a JWT proof of possession binding the holder key to the nonce
5. Requests the credential at the credential endpoint
6. Stores the SD-JWT VC in `<wallet-dir>/credentials/<CredentialType>.sdjwt`

| Option | Required | Default | Description |
|--------|----------|---------|-------------|
| `--offer-uri` | Yes | - | OpenID4VCI Credential Offer URI |
| `--wallet-dir` | No | `./wallet` | Directory for keys and credentials |
| `--key-file` | No | `<wallet-dir>/holder-key.pem` | Path to holder key PEM file |

### Present a Credential (OpenID4VP)

```bash
sorcha-agent haip present --request-uri <uri> --credential VerifiedIdentityCredential --disclose "givenName,familyName,dateOfBirth" --wallet-dir ./wallet
```

Executes the OID4VP `direct_post` flow:
1. Loads the specified credential from the wallet
2. Fetches the authorization request object and **authenticates the verifier** (below)
3. Builds a selective disclosure presentation with only the specified claims
4. Signs a KB-JWT (Key Binding JWT) with the holder key, binding nonce and audience
5. Submits the `vp_token` via `direct_post` to the verifier's response URI

| Option | Required | Default | Description |
|--------|----------|---------|-------------|
| `--request-uri` | Yes | - | OpenID4VP Authorization Request URI |
| `--credential` | Yes | - | Credential type to present (e.g., `VerifiedIdentityCredential`) |
| `--disclose` | Yes | - | Comma-separated claim names to disclose |
| `--wallet-dir` | No | `./wallet` | Directory for keys and credentials |
| `--verifier-client-id` | No | from the request object | Expected `x509_san_dns:{host}` client_id to pin |
| `--verifier-anchor` | No | none | Trusted root (PEM or DER) the verifier chain must reach. Repeatable |
| `--require-trusted-verifier` | No | off | Refuse unless the chain reaches an anchor |
| `--allow-unverified-verifier` | No | off | Proceed even when the verifier cannot be authenticated |

#### Verifier authentication (issue #1538)

Since Feature 181 US6 the verifier signs its request object with an **X.509 certificate** and an
`x5c` chain — there is no embedded `jwk`. The agent authenticates it with the same
`RequestObjectValidator` the citizen wallet uses: ES256 verify against the `x5c` leaf, leaf SAN
dNSName matched to the `x509_san_dns:` client_id host, then a chain walk to any supplied anchor.

Because an agent has no human to render a consent decision to, it applies a policy over the
three-state verdict:

| Verdict | Behaviour |
|---------|-----------|
| Tampered signature, or SAN not matching the client_id host | **Refused always** — no flag overrides it |
| Cannot authenticate (no `x5c`, unsupported alg, unsigned body) | Refused unless `--allow-unverified-verifier` |
| Authentic, but chains to no supplied anchor | Proceeds with a warning (FR-027: absent anchors never block), or refused with `--require-trusted-verifier` |
| Authentic and chains to an anchor | Proceeds |

Without `--verifier-client-id` the expected client_id is read from the request object itself, which
proves internal consistency but **not identity** — the agent warns when it does this. Pin it, or
supply an anchor, to get a real trust decision.

> The former `--verifier-jwk-thumbprint` option is **removed**. It pinned a key in a header the
> platform no longer emits, so after US6 it could only ever refuse.

### Exit Codes (HAIP commands)

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | General error (invalid URI, missing credential, unexpected failure) |
| 2 | Authentication error (token exchange failed, request object fetch failed) |
| 3 | Credential/presentation rejected by server |
| 4 | Network or metadata error |

### Wallet Directory Structure

The HAIP commands use a flat file-based wallet:

```
wallet/
├── holder-key.pem               # ES256 (P-256) private key — auto-generated on first use
├── holder-key.jwk.json           # Public JWK for reference
└── credentials/
    ├── VerifiedIdentityCredential.sdjwt
    └── DrivingLicenceCredential.sdjwt
```

### Holder Key

The holder key is an ECDSA P-256 (ES256) key pair used for:
- **JWT proof of possession** during credential issuance (binds the credential to this key via `cnf`)
- **KB-JWT signing** during credential presentation (proves the presenter holds the key)

The key is generated on first use and persisted as a PEM file. Subsequent `receive` and `present` invocations reuse the same key. Both walkthroughs in `walkthroughs/HaipIdentityAttestation/` and `walkthroughs/HaipDrivingLicence/` share the same wallet directory and holder key.

### Important Notes

- **IssuerUrl resolution**: The issuer URL embedded in the credential offer must be host-resolvable (typically `http://127.0.0.1` when running against Docker). The agent runs on the host machine and must reach the issuer's metadata, token, and credential endpoints directly.
- **Issuer JWK in header**: In dev/walkthrough mode, the issuer's public JWK is embedded in the JWS header. Production deployments use `x5c` certificate chains.
- **No authentication required for HAIP commands**: Unlike the `run` and `validate` commands, the HAIP commands do not authenticate against Sorcha. They interact directly with the OID4VCI/OID4VP endpoints using the pre-authorized code or presentation request URI.

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
- **Open-Starting Watch** — Optional blueprint-scoped watch so an agent playing a Feature 103 open participant can START a workflow (issue #1446)
- **Pluggable Decision Engines** — Rules (JSON Logic) or AI (Claude) with schema validation
- **File Upload Pre-Actions** — Chunked encrypted file submission (up to 40MB)
- **Resilient Execution** — Polly retry/circuit-breaker, SignalR auto-reconnect, JWT auto-refresh
- **Audit Logging** — JSONL append-only trail of all decisions and submissions
- **Persona Mode** — Optional autonomous initiator loop alongside reactive inbox (Feature 110)
- **Cross-Platform** — Runs on Windows, macOS, and Linux

## Open-Starting Watch (issue #1446)

An agent that plays a **Feature 103 open (late-bound) participant** cannot be reached through its
inbox, and that is not a bug in the inbox: until somebody submits the starting action, the open
participant is bound to no wallet, so the action is nobody's assigned work. `GET /api/actions/pending`
therefore never carries it — for this agent or for anyone else.

Opt in to a second, blueprint-scoped watch:

```jsonc
"inbox": {
  "signalR": { "enabled": true },
  "polling": { "enabled": true, "intervalSeconds": 15 },
  "openStarting": {
    "enabled": true,
    "blueprintId": "{{blueprintId}}",   // REQUIRED — validated
    "intervalSeconds": 15               // optional; falls back to polling.intervalSeconds
  }
}
```

It polls `GET /api/actions/open-starting?blueprintId=…&registerId=…` and feeds the results through
the same `CompositeInboxListener`, so rules match on `actionName` exactly as they do for assigned
work, and the existing dedupe prevents a second submission before the late-bind folds.

`enabled` with no `blueprintId` fails `sorcha-agent validate`: the endpoint refuses an unscoped query,
and an agent that quietly watched nothing would look identical to the defect this closes. Scoping to
one blueprint is also what stops an agent starting arbitrary workflows.

`walkthroughs/PropertyInspection/actors/tenant.json` is the worked example — the actor whose run this
defect blocked.

## Persona Mode

A **persona** is an optional JSON file that lets an agent initiate a workflow instead of only reacting to its inbox. Enabled by adding a `personaFile` field to the actor definition:

```jsonc
{
  "actor": { "name": "procurement-mgr", ... },
  "personaFile": "../personas/procurement-mgr-kickoff.persona.json",
  "mode": "rules",
  "rules": [ ... ]
}
```

The persona file declares a trigger (`once` in v1), a target (blueprint + instance + action index), and a payload template with substitution tokens (`${now}`, `${uuid}`, `${counter}`, `${random.int|decimal|choice}`). The persona loop runs alongside the reactive inbox loop using the same wallet, auth, and HTTP client — agents without `personaFile` are unaffected.

Typical use is unblocking multi-agent walkthroughs that would otherwise hang because the first action has no prior transaction to populate any agent's inbox. See [`specs/110-agent-persona-mode/quickstart.md`](../../../specs/110-agent-persona-mode/quickstart.md) for the full guide.

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
