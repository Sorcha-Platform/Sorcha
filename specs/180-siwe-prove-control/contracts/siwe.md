# Contract: SIWE (`SiweMessage` / `SiweFormatter` / `SiweVerifier`)

**Project**: `src/Common/Sorcha.Cryptography.Secp256k1/Siwe` (pure-managed).

## `SiweFormatter.Format(SiweMessage) → string`

Emits the EIP-4361 text exactly (required + present optional fields, in spec order; EIP-55 address; no
trailing newline). `TryParse(string, out SiweMessage)` is the inverse — returns false (fail-closed) on a
missing required field or malformed datetime.

## `SiweVerifier.Verify(message, signature65, options) → SiweVerificationResult`

| Case | Result |
|---|---|
| parse fails / signature ≠ 65 bytes | `{ Valid=false, Reason }` |
| recovered address ≠ message address (case-insensitive) | `{ Valid=false }` |
| `ExpectedNonce`/`ExpectedDomain` supplied and mismatched | `{ Valid=false }` |
| `NowUtc` outside `[NotBefore, ExpirationTime]` (when present) | `{ Valid=false }` |
| all pass | `{ Valid=true, Address }` |

Never throws. `options.NowUtc` is injectable for tests.

## Test contract

- **Interop anchor:** the EIP-4361 **spec example** message (`example.com` … `0xC02aaA…756Cc2`, statement + Resources) `TryParse`s and re-`Format`s byte-identically.
- **Round-trip:** `Format`↔`TryParse` for messages with/without each optional field (scheme, statement, expiration, not-before, request-id, resources).
- **Verify accept:** a message signed by its address's key (via `Secp256k1Signer` over `Eip191`) → `Valid=true`, correct `Address`.
- **Verify reject:** tampered signature; signature by a different address; expired (`NowUtc > ExpirationTime`); not-yet-valid (`NowUtc < NotBefore`); wrong `ExpectedNonce`; wrong `ExpectedDomain`; malformed message.
