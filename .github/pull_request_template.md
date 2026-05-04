## Summary
<!-- One paragraph describing the change. Reference any related issue or spec task. -->

## Test plan
<!-- Checklist of how this PR was verified. -->

- [ ]
- [ ]

## Standards & discoverability

<!-- Spec 117 (AI Discoverability) — these checks keep the machine-readable surface accurate. -->

- [ ] **`STANDARDS.md` reviewed and updated** for any change touching a path listed in its Components column, or any change to a standards-related claim (BIP32/39/44, ML-DSA/ML-KEM, OpenID4VC, HAIP 1.0, W3C VCDM, IETF Token Status List, DID, OAuth 2.0, etc.).
- [ ] **`last_updated` bumped** on changed `docs/` files that carry YAML frontmatter (`docs/architecture.md`, `docs/openid4vc-haip-integration.md`, `docs/applicability.md`, `docs/security-model.md`).
- [ ] **`llms.txt` reviewed** if a new standard or capability was added to the platform, or an existing one's status changed.
- [ ] **OpenAPI metadata** (`WithName` / `WithSummary` / `WithDescription` / `WithTags` plus property `[Description]` or XML `<summary>`) added for any new endpoint or DTO property.

If any of the above is N/A, tick the box and note "N/A" inline.
