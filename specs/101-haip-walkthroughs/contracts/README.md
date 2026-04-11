# Contracts: HAIP Walkthroughs

**Feature**: 101-haip-walkthroughs

This spec does not add HTTP endpoints. It extends the `sorcha-agent` CLI with
HAIP holder commands and creates walkthrough scripts that exercise the OpenID4VCI
issuer (097) and OpenID4VP verifier (098) infrastructure end-to-end.

---

## CLI Commands (sorcha-agent)

### `sorcha-agent haip receive`

Receives a Verifiable Credential via the OpenID4VCI pre-authorized code flow.

```
sorcha-agent haip receive --offer-uri <uri> [--key-file <path>] [--wallet-dir <dir>]
```

**Arguments**:

| Flag | Required | Default | Description |
|------|----------|---------|-------------|
| `--offer-uri` | Yes | -- | `openid-credential-offer://` URI from QR code or deep link |
| `--key-file` | No | `./wallet/holder-key.pem` | Path to the holder's private key (PEM, ES256) |
| `--wallet-dir` | No | `./wallet/` | Directory for storing received credentials |

**Behaviour**:

1. Resolves the Credential Offer from the URI
2. Exchanges the pre-authorized code at the token endpoint
3. Requests a fresh `c_nonce` from the nonce endpoint
4. Builds a key-bound JWT proof using the holder key
5. Submits the credential request to the credential endpoint
6. Stores the received SD-JWT VC to `{wallet-dir}/credentials/{vct-short-name}.sdjwt`
7. Prints a summary to stdout: credential type, issuer, claims, `cnf` binding present

**Output** (stdout):

```
Credential received:
  Type:    urn:sorcha:credential:verified-identity
  Issuer:  did:sorcha:org:sorcha1abc123
  Claims:  givenName, familyName, dateOfBirth
  Bound:   yes (ES256)
  Stored:  ./wallet/credentials/VerifiedIdentityCredential.sdjwt
```

**Exit codes**:

| Code | Condition |
|------|-----------|
| 0 | Credential received and stored successfully |
| 1 | Invalid or malformed offer URI |
| 2 | Token exchange failed (expired code, invalid grant) |
| 3 | Key proof rejected by issuer (nonce mismatch, unsupported algorithm) |
| 4 | Network error (issuer unreachable, timeout) |

---

### `sorcha-agent haip present`

Presents a Verifiable Credential via the OpenID4VP authorization response flow.

```
sorcha-agent haip present --request-uri <uri> --credential <type> --disclose <claims> [--key-file <path>] [--wallet-dir <dir>]
```

**Arguments**:

| Flag | Required | Default | Description |
|------|----------|---------|-------------|
| `--request-uri` | Yes | -- | `openid4vp://` request URI from verifier |
| `--credential` | Yes | -- | Credential type to present (short name or full VCT) |
| `--disclose` | Yes | -- | Comma-separated list of claims to selectively disclose |
| `--key-file` | No | `./wallet/holder-key.pem` | Path to the holder's private key (PEM, ES256) |
| `--wallet-dir` | No | `./wallet/` | Directory containing stored credentials |

**Behaviour**:

1. Resolves the Authorization Request from the request URI
2. Locates the matching credential in `{wallet-dir}/credentials/`
3. Validates the credential has not expired
4. Builds a VP Token with selective disclosure (only the requested claims)
5. Signs the key-binding JWT with the holder key
6. Submits the Authorization Response to the verifier's `response_uri`
7. Prints the submission result to stdout

**Output** (stdout):

```
Presentation submitted:
  Verifier:   https://verifier.example.com
  Credential: DrivingLicenceCredential
  Disclosed:  licenceNumber, category
  Result:     accepted
```

**Exit codes**:

| Code | Condition |
|------|-----------|
| 0 | Presentation accepted by verifier |
| 1 | Credential not found in wallet directory |
| 2 | Authorization request expired or invalid |
| 3 | Verification failed (signature invalid, key binding mismatch) |
| 4 | Network error (verifier unreachable, timeout) |

---

## Actor Definition Extension

Walkthrough actor definitions include a `haip` block that configures the holder
wallet for HAIP operations:

```json
{
  "name": "CitizenHolder",
  "role": "holder",
  "haip": {
    "holderKeyAlgorithm": "ES256",
    "walletDir": "./wallet"
  }
}
```

| Field | Required | Default | Description |
|-------|----------|---------|-------------|
| `holderKeyAlgorithm` | No | `ES256` | Algorithm for the holder key (`ES256`, `EdDSA`) |
| `walletDir` | No | `./wallet` | Working directory for credentials and keys |

When an actor has a `haip` block, the launcher generates a holder key at
`{walletDir}/holder-key.pem` during setup if one does not already exist.
