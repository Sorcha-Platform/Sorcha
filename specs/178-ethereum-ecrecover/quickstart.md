# Quickstart: verifying an address-form Ethereum issuer credential

## For a blueprint author

Trust a `did:pkh` or address-form `did:ethr` issuer exactly like any other DID — list it in the
credential requirement's allow-list. No new configuration surface:

```json
{
  "type": "EthereumIssuedAttestation",
  "trustPolicy": {
    "sources": [
      { "kind": "did-allowlist", "allowedIssuers": [
        "did:pkh:eip155:1:0xAb5801a7D398351b8bE11C439e05C5B3259aeC9B",
        "did:ethr:0x1234567890abcdef1234567890abcdef12345678"
      ] }
    ]
  },
  "revocationCheckPolicy": "FailClosed"
}
```

To accept a signature-valid credential from an **unlisted** address-form issuer at reduced assurance
(off by default), set the Phase-1 opt-in on the requirement's trust policy:
`"warnOnUnlistedVerifiedIssuer": true`.

## What happens at verification (offline)

1. The credential is an SD-JWT/JWT signed `alg: "ES256K"`, `iss: did:pkh:eip155:1:0x…` (or address-form
   `did:ethr`).
2. The issuer DID resolves — `PkhDidResolver` / `EthrDidResolver` — to an
   `EcdsaSecp256k1RecoveryMethod2020` verification method carrying a `blockchainAccountId`, no key.
3. The ES256K verify branch recovers the signer's public key from the signature, derives its EIP-55
   address, and matches it to the DID's address. Match ⇒ signature verified. No network call.
4. Trust maps the verified signature to **Pass** (allow-listed) / **Warn** (unlisted + opt-in) /
   **Reject** (unlisted default, or any recovery/DID failure).

## For a developer — verify the primitive locally

```bash
dotnet test tests/Sorcha.Cryptography.Secp256k1.Tests   # ecrecover KATs (recid 0 & 1), VerifyByAddress
dotnet test tests/Sorcha.ServiceClients.Http.Tests       # PkhDidResolver / EthrDidResolver
dotnet test tests/Sorcha.Verifier.Tests                  # engine VerifyEs256k address branch
dotnet test tests/Sorcha.Blueprint.Engine.Tests          # DidX5cIssuerKeyResolver + format-handler
```

Build the whole solution before testing (stale DLLs → phantom failures). MTP ignores `--filter`;
`dotnet test <project>` runs the whole project.

## Guardrails

- **Offline only** — any resolver that would need a chain read returns `null` (reject). No RPC in Phase 2.
- **Verification-only** — no signing capability is exposed; no `WalletNetworks` change.
- **No new dependency** — BouncyCastle (already present) does the recovery.
- **Fail-closed preserved** — unlisted issuer with the opt-in unset always rejects.
