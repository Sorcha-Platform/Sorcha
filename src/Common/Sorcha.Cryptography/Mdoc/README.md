# Sorcha.Cryptography.Mdoc (feature 135)

ISO/IEC 18013-5 `mso_mdoc` credential primitives, online path only (OpenID4VP / ISO 18013-7).
Built on BCL `System.Formats.Cbor` + `System.Security.Cryptography.Cose` — no third-party CBOR/COSE.

Implementation lands in **US2** (verification) and **US3** (issuance):

- `Cbor/` — tag-24 wrapping helpers + deterministic encoding.
- `Cose/CoseX5Chain.cs` — x5chain (COSE header label 33) encode/decode (no named BCL constant).
- `MobileSecurityObject.cs`, `IssuerSigned.cs`, `DeviceResponse.cs`, `SessionTranscript.cs` — wire models.
- `MdocService.cs` — issue / present / verify.

Parallels `../SdJwt/`. See `specs/135-eudi-credential-format-trust/data-model.md` §3 for the wire structures (tag-24 wrapping is load-bearing).
