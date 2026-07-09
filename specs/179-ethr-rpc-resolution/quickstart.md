# Quickstart: on-chain `did:ethr` verification

## For an operator — enable on-chain resolution

Add a read-only RPC endpoint per chain (server hosts only). No node — a public provider URL is fine:

```json
{
  "DidResolver": {
    "Ethr": {
      "Rpc": {
        "1": "https://mainnet.infura.io/v3/<key>",
        "11155111": "https://sepolia.infura.io/v3/<key>"
      }
      // optional: "RegistryAddress": { "1": "0x..." }  // defaults to 0xdca7…f21b
      // optional: "MaxHistoryHops": 128
    }
    // "AllowPrivateAddresses": true   // only for a local dev node
  }
}
```

- **No entry for a chain** ⇒ offline default document for that chain (Phase-2 behaviour).
- **Entry present but the provider errors** ⇒ verification **fails closed** (rejects) — it will *not*
  trust a possibly-stale offline view.

## What happens at verification (server-side)

1. A `did:ethr` ES256K credential is presented (`iss = did:ethr:{chain}:0x…`).
2. `EthrDidResolver` reads ERC-1056 over RPC: `changed` → `identityOwner` → walk `previousChange` for
   active `veriKey`/`sigAuth` delegates and `did/pub/*` keys (honouring `validTo`).
3. It builds the **current** document — recovery VMs for owner/address-delegates, key VMs for published
   keys — all in `assertionMethod`/`authentication` per purpose.
4. The existing issuer-key resolver + verify branch confirm the signature against a **currently-authorised**
   key. A rotated-away owner or expired delegate is simply absent → its signature rejects.

## The offline PWA is unchanged

The Blazor WASM wallet verifier never registers `IEvmRpcClient`, so it resolves `did:ethr` to the
offline default document with **zero** network calls — exactly as in Phase 2. The server is
authoritative for rotation.

## For a developer — run the tests

```bash
dotnet test tests/Sorcha.ServiceClients.Tests   # AbiCodec KATs, Erc1056Registry (fake RPC), EvmRpcClient SSRF, EthrDidResolver RPC path
dotnet test tests/Sorcha.Verifier.Tests          # end-to-end rotated/delegate-signed did:ethr verify
dotnet test tests/Sorcha.Blueprint.Engine.Tests  # engine-path verify
```

Build the whole solution first (stale DLLs → phantom failures). MTP ignores `--filter`;
`dotnet test <project>` runs the whole project.

## Guardrails

- **Read-only** — `eth_call` + `eth_getLogs` only; no writes, no signing, no `WalletNetworks` change.
- **Server-side only** — the WASM PWA stays offline.
- **Fail-closed** — configured-but-errored RPC rejects; never a stale default doc.
- **No new dependency** — JSON-RPC over `HttpClient`; keccak/selectors from the existing primitive.
