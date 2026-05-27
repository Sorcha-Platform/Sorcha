# mdoc test vectors (feature 135)

Holds conformance fixtures for the `mso_mdoc` codec and verification tests:

- `pid-device-response.b64` — a conformant EUDI PID `DeviceResponse` (base64url CBOR) for the OpenID4VP online flow. **(to be added in US2 / T036–T038)**
- `sdjwt-vc-parity.txt` — an equivalent SD-JWT VC presentation used by the cross-format parity tests.

Vectors are committed verbatim; the tag-24 outer bytes are load-bearing for digest/signature checks (see `data-model.md` §3 / R8) — do not re-encode.
