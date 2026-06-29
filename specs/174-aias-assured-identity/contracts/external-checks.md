# Contract: Rules-engine external-check hook (Sorcha.Agent)

The one new code surface in M1. Lives under `src/Apps/Sorcha.Agent/Decision/Checks/`. Keeps the
decision declarative: checks produce **facts**, JSON-Logic rules decide.

## `IExternalCheck`

```csharp
/// <summary>
/// A single pre-decision check that inspects the action payload and produces one or more named
/// boolean facts (optionally with a human detail string) for the rules context.
/// </summary>
public interface IExternalCheck
{
    /// <summary>Stable fact key (e.g. "postcodeExists", "profane").</summary>
    string Name { get; }

    /// <summary>
    /// Evaluate against the action payload. MUST NOT throw on a normal "false" outcome; network
    /// faults degrade per the check's own fallback policy (see PostcodeExistsCheck offline mode).
    /// </summary>
    Task<ExternalCheckResult> EvaluateAsync(IReadOnlyDictionary<string, object?> payload, CancellationToken ct);
}

/// <param name="Name">Fact key.</param>
/// <param name="Value">Boolean result merged at /checks/{Name}.</param>
/// <param name="Detail">Optional human string merged at /checks/{Name}Detail (for rejection copy).</param>
public sealed record ExternalCheckResult(string Name, bool Value, string? Detail = null);
```

## `ExternalCheckRunner`

```csharp
/// <summary>
/// Runs the configured checks and merges their results into a "checks" sub-object on the rules
/// context BEFORE RulesDecisionEngine evaluates JSON Logic. Checks run concurrently; an individual
/// check that faults unexpectedly resolves to a safe default (its Value=false) and is logged — it
/// never crashes the decision.
/// </summary>
public sealed class ExternalCheckRunner
{
    Task<IReadOnlyDictionary<string, object?>> RunAsync(
        IReadOnlyDictionary<string, object?> payload, CancellationToken ct);
    // returns { "postcodeExists": true, "postcodeExistsDetail": "...", "profane": false, ... }
}
```

## Integration point

`RulesDecisionEngine` (`src/Apps/Sorcha.Agent/Decision/RulesDecisionEngine.cs`) gains a pre-step:
before building the JSON-Logic data context for the matching action, it calls
`ExternalCheckRunner.RunAsync(payload)` and adds the result under the `checks` key. The existing
first-match-wins rule evaluation is unchanged — rules reference `{ "var": "checks.postcodeExists" }`
etc. The runner + checks are only invoked for actions that have checks configured (config-driven),
so non-AIAS agents are unaffected.

## Concrete checks

| Check | Type | Behaviour |
|-------|------|-----------|
| `EmailVerifiedCheck` | email-verified | Reads the applicant's verified-email signal from the payload/context. |
| `field-present` (generic) | field-present | True iff the configured JSON-Pointer field is present & non-empty (used for `photoPresent`). |
| `PostcodeExistsCheck` | uk-postcode | Calls **postcodes.io**; on a network fault (or `offlineMode: "always"`) resolves against the bundled fixture. `Detail` carries the queried postcode for the rejection reason. |
| `ProfanityCheck` | profanity | Scans the configured free-text fields against the bundled wordlist; `Value=true` if any match. |

## Test contract (xUnit, `tests/Sorcha.Agent.Tests/Decision/Checks/`)

- Each check: true/false cases against representative payloads.
- `PostcodeExistsCheck`: live-shape (mocked HTTP) returns true for a known postcode, false for a
  nonsense one; **offline fallback** returns the fixture result when the HTTP call throws.
- `ExternalCheckRunner`: merges multiple checks into the `checks` object; a faulting check resolves
  to `false` and does not throw.
- `RulesDecisionEngine` (extended): given facts, the AIAS rules approve the clean case and reject
  each dodgy case with the expected reason — exercising the data-model rule table.
