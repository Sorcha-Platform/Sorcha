# Quickstart: Agent Persona Mode

A practical, copy-pasteable walkthrough for adding a persona to a Sorcha agent. Read the spec for intent; this is the how.

## 1. Add a persona file

Create a JSON file next to the actor config, typically in `walkthroughs/<Name>/personas/`:

```jsonc
// walkthroughs/TradeFinance/personas/procurement-mgr-kickoff.persona.json
{
  "name": "procurement-mgr-kickoff",
  "target": {
    "blueprintId": "{{blueprints.procurement-to-pay.id}}",
    "instanceId":  "{{instances.procurement-to-pay.id}}",
    "actionName":  "Raise Purchase Order"
  },
  "trigger": { "kind": "once", "delaySeconds": 2 },
  "payloadTemplate": {
    "poReference":          "PO-CAIRN-2026-00142",
    "projectName":          "Aviemore Heights Phase 2",
    "siteAddress":          "Plot 7-12, Craig na Gower Avenue, Aviemore, PH22 1RW",
    "paymentTerms":         "Net 30",
    "requiredDeliveryDate": "2026-04-15"
  }
}
```

`{{blueprints.*}}` and `{{instances.*}}` placeholders are resolved from `state.json` by the same `VariableResolver` that already handles actor-config placeholders, so the values you declare in `run-agents.ps1` / `setup.ps1` flow through unchanged.

## 2. Point the actor at it

Add one field to the existing actor config:

```jsonc
// walkthroughs/TradeFinance/actors/procurement-mgr.json
{
  "actor": { "name": "procurement-mgr", ... },
  "connection": { ... },
  "inbox": { "signalR": { "enabled": true }, "polling": { ... } },
  "personaFile": "../personas/procurement-mgr-kickoff.persona.json",   // <── new
  "mode": "rules",
  "rules": [ ... ]
}
```

Path is relative to the actor file. Absolute paths also work.

## 3. Run the walkthrough

No changes to `run-agents.ps1`. Launch as today:

```powershell
pwsh walkthroughs/TradeFinance/setup.ps1
pwsh walkthroughs/TradeFinance/run-agents.ps1
```

Expected log lines from `procurement-mgr`:

```text
[12:03:11] Actor "procurement-mgr" starting...
[12:03:12] Authenticated
[12:03:12] SignalR enabled
[12:03:12] Polling enabled (20s interval)
[12:03:12] Actor "procurement-mgr" started
[12:03:12] Persona "procurement-mgr-kickoff" loaded (trigger: once, delay: 2s)
[12:03:14] Persona fire #1 → blueprint=<id>, action="Raise Purchase Order"
[12:03:14] Persona fire #1 → Submitted (412 ms)
[12:03:14] Persona "procurement-mgr-kickoff" completed
```

Other agents (site-mgr, sales-mgr, etc.) react to the now-live workflow through their normal inbox flow — no persona needed on them.

## 4. Recurring scenario-data persona

For populating a register with varied data, swap the trigger and templates:

```jsonc
{
  "name": "invoice-generator",
  "target": {
    "blueprintId": "{{blueprints.invoice.id}}",
    "instanceId":  "{{instances.invoice.id}}",
    "actionName":  "Raise Invoice"
  },
  "trigger": {
    "kind": "interval",
    "everySeconds": 30,
    "maxIterations": 20
  },
  "payloadTemplate": {
    "invoiceNumber": "INV-${counter}",
    "invoiceId":     "${uuid}",
    "issuedAt":      "${now}",
    "amount":        "${random.decimal(100, 9999, 2)}",
    "currency":      "${random.choice([\"EUR\",\"GBP\",\"USD\"])}"
  }
}
```

This fires 20 times, 30 s apart (~10 minutes total), producing 20 register instances with varying amounts and currencies. `"${counter}"` and `"${random.decimal(...)}"` resolve to a JSON number in the submitted payload (not a string) because they are the entire string value.

## 5. Troubleshooting

| Symptom | Likely cause |
|---------|--------------|
| Agent logs `Persona load failed: unknown token '${randm.int(...)}'` | Typo — correct to `${random.int(...)}`. |
| Persona fires but the submission logs `validation rejected`. | Payload doesn't match the blueprint's action schema. Fix the template — the persona doesn't inspect blueprint schemas at load time. |
| Persona never fires. | Check the actor file resolves `personaFile` to an existing path (logged at agent start). |
| Recurring persona stops after 1 fire. | `maxIterations: 1` or `until` already in the past — check the persona file. |
| Reactive inbox behaviour regresses when a persona is present. | File a bug against this feature — FR-011 says this must not happen. |

## 6. What this does NOT do (v1)

- No `cron` triggers, no event triggers, no register-state thresholds.
- No templating loops or conditionals. If you need those, generate the persona file from a script.
- No persistence across restarts. Killing and relaunching the agent resets `${counter}` to 1 and re-fires a `once` persona.
- No de-duplication. If two agents in the same manifest both point at one-shot personas targeting the same starting action, both fire and you get two instances. This is intentional — de-duping is the scenario author's responsibility.
