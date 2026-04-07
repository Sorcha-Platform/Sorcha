# PayloadTests Actor Agents

Autonomous actor definitions for the PayloadTests file transfer walkthrough.

## Actors

| File | Role | Action | Special |
|------|------|--------|---------|
| sender.json | Sender Corp | Send File | File upload preAction (1KB test file) |
| receiver.json | Receiver Corp | Acknowledge Receipt | Simple boolean acknowledgement |

## Running

```powershell
# Setup first (creates orgs, wallets, register, blueprint)
pwsh walkthroughs/PayloadTests/setup.ps1

# Run with autonomous actors
pwsh walkthroughs/PayloadTests/run-agents.ps1
```

## File Upload PreAction

The sender actor uses a `preActions` hook to generate, chunk, and upload a test file before submitting the action:

```json
"preActions": [
  {
    "type": "file-upload",
    "config": {
      "fieldName": "attachment",
      "sizeBytes": 1024,
      "seed": 85
    }
  }
]
```

To use a real file instead of a generated one:

```json
"preActions": [
  {
    "type": "file-upload",
    "config": {
      "fieldName": "attachment",
      "filePath": "./files/site-photo.jpg"
    }
  }
]
```

## Cross-Machine

Remote variants (`*-remote.json`) point to `https://n1.sorcha.dev`. Copy them + `state.json` to the remote machine and set the password env vars.
