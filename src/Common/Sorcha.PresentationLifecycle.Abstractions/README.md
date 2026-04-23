# Sorcha.PresentationLifecycle.Abstractions

Consumer-agnostic primitive for the Sorcha Timebound Presentation Lifecycle (Feature 111). A presentation is a citizen-asserted proof of something — a credential, a signed file, a step-up authentication challenge — and its lifecycle on the register is always three events: **initiated**, **outcome** (success or decline), and optionally **abandoned** when a validity window expires without a resolution.

This package contains only the contract that external verifier consumers implement (`IPresentationConsumer`), the lifecycle event records, and the reason-code enums. It has **no HAIP-specific types, no OpenID4VP vocabulary, and no dependency on Blueprint Service internals**. That separation is deliberate: future non-HAIP consumers (file-upload-by-deadline, external-signature verification, step-up MFA) can depend on this package alone and plug into the Blueprint Service's `PresentationLifecycleService` via DI without pulling HAIP in as a transitive dependency.

Implementors see `contracts/consumer-contract.md` in the feature spec folder for the full contract invariants and an example HAIP consumer.
