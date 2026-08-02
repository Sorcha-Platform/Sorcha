# `/temp` — runtime scratch, never committed

Everything written here is ignored by git. The folder itself is tracked (via its own `.gitignore`
and this file) so it exists on a fresh clone and tooling can rely on it without a `mkdir` dance.

## What belongs here

Anything a script, ceremony, walkthrough or demo produces **at runtime**:

- generated key material and mnemonics (e.g. `genesis-validator-key.json`)
- state files, deploy outputs, scratch configs
- logs, dumps, one-off exports

## What does NOT belong here

Artefacts that are **inputs** to the build or are meant to be committed. The genesis ceremony is the
clearest illustration of the split — one command writes both:

| Output | Goes to | Why |
|---|---|---|
| `system-register-genesis.json` | `src/Common/Sorcha.Register.Models/Resources/` | A committed artefact — the embedded dev trust anchor. |
| `genesis-validator-key.json` | **`/temp/`** | Private key material. Import it into the first validator, then destroy it. |

## Why this exists

A `genesis-validator-key.json.bak-pre471` — a file whose mnemonic controls **every key derived from
the genesis wallet** — sat untracked in the repo root for weeks. `.gitignore` carried the exact
string `genesis-validator-key.json`, which does not match a `.bak-pre471` suffix, so one `git add -A`
would have published it.

Two independent things now prevent a repeat, and both matter:

1. **This directory.** Producers write scratch here rather than to the repo root, so there is nothing
   at root to catch in the first place.
2. **Pattern-based ignores at the root.** `.gitignore` matches `genesis-validator-key*` — the whole
   family, not one exact filename — so a key that lands somewhere unexpected, or picks up a suffix,
   is still excluded. Belt and braces: the convention can be forgotten, the pattern cannot.

## If you are writing tooling

Write runtime output here, not to the repo root and not next to source. Resolve the path by walking
up for the repo marker rather than assuming the current working directory — a CLI installed as a
global tool can be invoked from anywhere, and should fall back to the working directory when it is
not running inside a checkout.

See also: the per-tool convention for walkthroughs and demos, which keeps their own state inside
`walkthroughs/<name>/` and `demos/<name>/`. That convention still applies; `/temp` is the default for
anything without a natural home of its own.
