# Contract: Reactions (idempotent, role-gated side effects)

A `ReactionDispatcher` subscribes to `docket:confirmed` on every node and performs side effects for sealed actions that declare them. Pure projection (state) and side effects (reactions) are separate.

## Trigger
A sealed action transaction whose blueprint action declares a side effect:
- `credentialIssuanceConfig` → `CredentialMint` (issuer side) + `CredentialDeliver`/detect (recipient side)
- notification / inbox config → `Notification` / `InboxWrite`

## Entitlement
A node performs a reaction only if it **locally hosts the responsible wallet**:
- `CredentialMint`: the node hosting the **issuer** participant's wallet (the action sender's wallet).
- `CredentialDeliver` / inbound detect: the node hosting the **recipient** wallet.
- `Notification`/`InboxWrite`: the node hosting the target wallet/user.

Entitlement is probed via `IWalletServiceClient.GetWalletAsync` (the existing pattern). Non-entitled nodes no-op (FR-017).

## Idempotency
- Keyed on `(sealedTxId, reactionKind)` via `Sorcha.AtomicCache` SET-NX (the F114/F128 single-use pattern).
- First claim performs the effect; replays / restarts / duplicate seals find the claim and no-op (FR-016, SC-004).
- At-least-once semantics with idempotent effect; durable-outbox delivery is out of scope.

## Credential issuance specifics
- Moves out of `ActionExecutionService`'s inline submit path entirely.
- Mints using the existing issuance machinery (holder-key binding, encrypt-to-recipient), but triggered by the sealed Action tx, not by the submit call.
- Cross-node delivery is unchanged in mechanism (encrypted credential tx → replicates → recipient node's inbound detection is itself a reaction on the same idempotency key).

## Observability
OTel on a `Sorcha.Blueprint.Reactions` meter: `reaction_dispatched_total{kind,outcome}`, `reaction_idempotent_skip_total{kind}`, `reaction_entitlement_skip_total{kind}`.
