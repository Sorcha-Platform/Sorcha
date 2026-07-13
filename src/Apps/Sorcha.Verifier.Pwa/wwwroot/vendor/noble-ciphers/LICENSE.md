# @noble/ciphers

Pinned vendor copy of [`@noble/ciphers`](https://github.com/paulmillr/noble-ciphers)
version **1.2.1** (ESM build), fetched from `https://unpkg.com/@noble/ciphers@1.2.1/esm/`.

Files in this directory are unmodified library output and are licensed under
the upstream MIT licence — see
<https://github.com/paulmillr/noble-ciphers/blob/main/LICENSE>.

Only the dependency closure needed for `xchacha20poly1305` is vendored:
`chacha.js`, `_arx.js`, `_poly1305.js`, `utils.js`, `_assert.js`. The ESM
relative imports between these files resolve natively in the browser — no
build step.

Used by `wwwroot/js/xchacha-bridge.js` (Feature 114 T056) to encrypt the
IndexedDB credential cache at rest with IETF XChaCha20-Poly1305.

We vendor a pinned copy rather than depending on a CDN at runtime so that:

- Builds are deterministic and offline-installable.
- The PWA's Content-Security-Policy stays on `script-src 'self'` with no
  remote allowlist.
- Service-worker pre-cache picks up the assets like every other PWA bundle.

Upgrading: replace the files with a newer pinned version (keeping the same
dependency closure), run the wallet's test suite, and update this file's
version line.
