
## Before deploying this portal

The gate component changed. `CredentialGateComponent` is retired; the pages now use
`PresentationRequestCard`, which needs `Source` and `ClaimsFetchToken` from the submission
response. **Update the deployed blueprints with the current feature set before this portal goes
live** — a blueprint published before 2026-07-28 will not carry the `presentationSource`
discriminator through the submission response, and its gate will fall back to HAIP.
