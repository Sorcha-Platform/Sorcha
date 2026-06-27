# Contract: Link-pending token

Stateless, signed credential returned by the social callback on the LinkRequired branch and redeemed
at link-confirm. Not an endpoint — an internal token format and signer contract.

## Mint (server-side, on social callback LinkRequired branch)

**Input**: `provider`, `subject`, `socialEmail`, `displayName?`, `targetAccountId` (Guid), now (UTC).

**Output**: opaque string token, valid ~5 minutes.

**Signer contract** (`ILinkPendingTokenService`):
```
string Mint(LinkPendingToken token);                 // serialise payload + expiry, append HMAC
bool   TryVerify(string raw, out LinkPendingToken token, out LinkPendingTokenError error);
```
- Key: `HKDF-SHA256(JwtSettings:SigningKey, info="sorcha:tenant:link-pending-hmac:v1")`, 32 bytes.
- HMAC-SHA256 covers payload **and** expiry. Constant-time compare on verify.

## Verify (server-side, at link-confirm)

| Condition | Result (`LinkPendingTokenError`) |
|-----------|----------------------------------|
| Signature mismatch / tampered | `Invalid` → 401 |
| Malformed / absent | `Invalid` → 401 |
| `ExpiresAt` in the past | `Expired` → 401 |
| Valid | `None` → proceed with decoded claims |

## Invariants
- FR-003: encodes provider, subject, social email, display name, target account id, short expiry.
- FR-004: integrity-protected; verifiable with no new persistent storage.
- FR-015: expired/tampered/malformed/absent → reject, no state change.
- Domain separation from the 2FA login token via the distinct `info` label.
