# qr-scanner

Pinned vendor copy of [`nimiq/qr-scanner`](https://github.com/nimiq/qr-scanner)
version **1.4.2**, fetched from `https://unpkg.com/qr-scanner@1.4.2/`.

Files in this directory are unmodified library output and are licensed under
the upstream library's MIT licence — see
<https://github.com/nimiq/qr-scanner/blob/master/LICENSE>.

We vendor a pinned copy rather than depending on a CDN at runtime so that:

- Builds are deterministic and offline-installable.
- The PWA's Content-Security-Policy can keep `script-src 'self'` with no
  remote allowlist.
- Service-worker pre-cache picks up the assets like every other PWA bundle.

Upgrading: replace both files with a newer pinned version, run the wallet's
Playwright test suite, and update this file's version line.
