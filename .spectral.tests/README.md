# Spectral rule fixtures (spec 117 T012)

Each fixture is a tiny synthetic OpenAPI document that exercises **one** rule
in `.spectral.yaml`. Run a rule's fixtures locally with:

```bash
npx spectral lint .spectral.tests/<fixture>.yaml --ruleset .spectral.yaml
```

Two fixtures per rule:
- `<rule>-pass.yaml` — must lint clean (exit 0).
- `<rule>-fail.yaml` — must lint with the named rule firing (exit 1).

The CI workflow `.github/workflows/ai-discoverability-check.yml` invokes
the orchestrator at `scripts/check-discoverability.sh` which runs Spectral
against the live `/.well-known/openapi.json`. These fixtures are unit tests
for the ruleset itself, not for the served document.

## Fixtures present

| Rule | Pass | Fail |
|---|---|---|
| `operationId-pascalcase` | ✓ | ✓ |
| `description-required-on-properties` | ✓ | ✓ |
| `info-x-mcp-server-required` | ✓ | ✓ |
| `info-x-standards-required` | ✓ | ✓ |
| `no-marketing-adjectives` | ✓ | ✓ |
| `info-title-required` | (covered by base `spectral:oas`) | — |
| `info-contact-url-required` | (covered by base `spectral:oas`) | — |
| `examples-required-on-credential-issuance` | (covered by integration tests, requires path-specific fixture) | — |

The two `info-*-required` rules and the credential-issuance rule are
exercised by the integration tests in
`tests/Sorcha.Gateway.Integration.Tests/OpenApiWellKnownTests.cs` — adding
synthetic fixtures here would duplicate that coverage.
