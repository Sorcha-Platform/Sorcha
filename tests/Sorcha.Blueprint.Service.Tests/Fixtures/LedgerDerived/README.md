# Ledger-Derived Instances — canonical fixtures (Feature 145)

Canonical sealed-docket / sealed-transaction streams the projection tests fold to assert
determinism, order-independence, idempotency, and rebuild parity (US1/US4, SC-001/SC-003).

Each fixture is a deterministic stream of sealed action transactions (each carrying a
`RoutingDecision` on its clear `TransactionMetaData`) that the `InstanceProjector` folds into
an instance projection. Tests feed the same stream in varied order / with a mid-stream
restart and assert identical resulting state.

Conventions:
- Fixtures are built in-code via the shared `LedgerDerivedFixtures` helper where possible;
  JSON fixtures here are for cross-node parity goldens that must be byte-stable.
- Keep fixtures small and intention-revealing (single-branch, parallel-branch, terminal).
