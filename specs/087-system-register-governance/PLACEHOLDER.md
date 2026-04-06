# 087 - System Register Governance

**Status**: Not yet specced (depends on 086-validator-key-roster)  
**Created**: 2026-04-06  
**Predecessor**: 086-validator-key-roster

## Summary

Transform the Sorcha System Register from per-node independent copies into a singleton canonical register synced across the entire network, with a curated platform-level validator roster.

## Key Decisions Recorded

1. **Singleton canonical register**: One authoritative System Register for the network, synced by all nodes. Nodes do NOT create their own independent copies once connected.

2. **Bootstrap fallback**: Each node starts with a minimal baseline System Register so it can operate if orphaned from the network (private system mode). When it connects to the network for the first time, it syncs the canonical version and replaces/merges its local bootstrap copy.

3. **Curated validator roster**: The System Register's validator roster is managed at the platform level — only authorized Sorcha platform operators, not every node that joins. Initially small (1-3 validators), expandable via governance proposals.

4. **Read vs write separation**: All nodes sync (read) the System Register. Only roster-authorized validators can propose dockets (write). This is enforced by the validator key roster from 086.

5. **Content carried**: System blueprints (governance, control), platform feature flags, upgrade metadata, blueprint governance records. New features and system upgrades propagate to all nodes via System Register sync.

6. **New node flow**: Start → connect to seeds → discover System Register via advertisements → sync as read-only subscriber → get blueprints/config → operational. No local System Register creation.

7. **Orphaned node flow**: Start → no network → bootstrap minimal System Register locally → operate in private mode → when network becomes available, sync canonical version.

## Depends On

- **086-validator-key-roster**: Provides the validator roster mechanism, external validator support (FR-013/014/015), and threshold signing schema.
- **Working peer sync**: Docket and transaction relay via peer network must be functional (fixes from current session).

## Open Questions (for speccing)

- What is the minimal baseline System Register content for an orphaned node? Just the governance control blueprint?
- How does merge/replacement work when an orphaned node reconnects — does the canonical version overwrite, or is there conflict resolution?
- Should the System Register use a well-known register ID (deterministic) so all nodes can reference it without discovery?
- How are the initial platform validators bootstrapped — hard-coded in config, or discovered from seed nodes?
