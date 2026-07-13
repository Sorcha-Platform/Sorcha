# Sorcha Reader (mobile)

The **verifier's** half of an in-person credential check — the other end of what the wallet does.

Wraps `src/Apps/Sorcha.Verifier.Pwa` (Blazor WASM) and the **same** `sorcha-proximity` plugin the wallet uses,
in its **BLE central** role. One plugin, two roles, one protocol.

## Why this is a separate app

`Sorcha.Verifier` is Blazor **Server**. A server-hosted app cannot reach a phone's Bluetooth radio, and a
browser cannot be a BLE central. So an in-person reader has to be a WASM host wrapped natively.

It is also **F155's long-deferred "path B" (WASM/offline verifier)**: an offline reader *is* an offline
verifier, so building one builds the other.

## It verifies with no network

That is the point, and it constrains everything: no `HttpClient` is registered, and every referenced project
must load in WASM (`scripts/check-wasm-safe.ps1` enforces this).

The consequence is stated honestly in the UI rather than hidden: the **ledger anchor cannot be checked
offline**, so it is reported as **"not checked"** — a third status, distinct from pass and from fail, which
never vetoes an otherwise-good verdict. Reporting it as a pass would tell an operator their check succeeded
when part of it never ran.

## Build

Same shape as `mobile/wallet` (see the `sorcha-app` skill):

```bash
cd mobile/reader && npm install && npx cap sync
cd android && ./gradlew assembleDebug
```

Bundle id: `app.sorcha.reader` (distinct from the wallet's `app.sorcha.wallet` — they are different apps and
may be installed on the same phone).
