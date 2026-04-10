# CLI Contract: system-register Commands

## sorcha system-register create

**Purpose**: Generate a pre-signed system register genesis block offline.

```
sorcha system-register create [options]

Options:
  --network-id <name>     Network identifier (default: "sorcha-local")
  --output <path>         Genesis file output path (default: src/Common/Sorcha.Register.Models/Resources/system-register-genesis.json)
  --algorithm <algo>      Signing algorithm (default: "ED25519")
  -q, --quiet             Suppress output
  -o, --output-format     Output format: table, json (default: table)

Outputs:
  1. Genesis file at --output path
  2. genesis-validator-key.json in current working directory

Exit Codes:
  0  Success
  1  General error
  4  Validation error (invalid algorithm, bad output path)
```

**Console Output (table format)**:
```
Genesis ceremony completed.

  Network ID:     sorcha-prod
  Register ID:    aebf26362e079087571ac0932d4db973
  Algorithm:      ED25519
  Fingerprint:    a3b1c9d4e5f6...
  Genesis File:   src/Common/Sorcha.Register.Models/Resources/system-register-genesis.json
  Validator Key:  ./genesis-validator-key.json

  WARNING: Store genesis-validator-key.json securely or destroy it after
  importing into the first validator. It is not needed for normal operation.
```

## sorcha system-register verify

**Purpose**: Verify a genesis file's signatures and display its contents.

```
sorcha system-register verify <genesis-file> [options]

Arguments:
  <genesis-file>          Path to genesis JSON file

Options:
  -q, --quiet             Suppress output (exit code only)
  -o, --output-format     Output format: table, json (default: table)

Exit Codes:
  0  All signatures valid
  1  Signature verification failed
  4  File not found or invalid format
```

**Console Output (table format, success)**:
```
Genesis file verified.

  Network ID:     sorcha-prod
  Register ID:    aebf26362e079087571ac0932d4db973
  Version:        1
  Algorithm:      ED25519
  Fingerprint:    a3b1c9d4e5f6...
  Signed At:      2026-04-10T14:30:00Z

  Validator Roster:
    #1  a1b2c3d4...  ED25519  Active  sorcha:docket-signing

  Signatures:     ALL VALID
```

**Console Output (failure)**:
```
Genesis file verification FAILED.

  Network ID:     sorcha-prod
  Register ID:    aebf26362e079087571ac0932d4db973

  FAILURE: Control record signature is invalid.
  Expected signer:  a3b1c9d4e5f6...
  Payload hash:     deadbeef1234...
```

## sorcha system-register import-validator-key

**Purpose**: Import genesis validator private key into the local Wallet Service.

```
sorcha system-register import-validator-key [options]

Options:
  --key <path>            Path to genesis-validator-key.json (required)
  -p, --profile <name>    CLI profile (default: active profile)
  -q, --quiet             Suppress output

Exit Codes:
  0  Key imported successfully (or already exists)
  1  General error
  4  Invalid key file
  7  Network error (Wallet Service unreachable)
```

**Console Output (success)**:
```
Validator key imported.

  Wallet Address:   s1abc123def456...
  Algorithm:        ED25519
  Network ID:       sorcha-prod
  Fingerprint:      a3b1c9d4e5f6...

  The local validator can now seal genesis dockets for this network.
```
