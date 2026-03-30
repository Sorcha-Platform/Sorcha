# Quickstart: Auto-Register Participant & PlatformUser Provisioning

## Validation Scenarios

### Scenario 1: Wallet Creation Auto-Links (US1)

**Setup**: Docker Compose running. Log in as any user with no existing participant record.

**Steps**:
1. Navigate to Wallets → Create Wallet
2. Fill in wallet name, select algorithm, generate mnemonic
3. Confirm mnemonic backup and submit
4. **Observe**: Wallet created successfully (existing behaviour)
5. Navigate to a workflow and subscribe to the wallet for notifications
6. **Observe**: ActionsHub subscription succeeds (previously failed with "wallet not linked")
7. Check participant page — participant record exists with the wallet linked

**Expected**: No manual participant registration or wallet linking required.

### Scenario 2: Existing Participant Gets New Wallet Linked (US1)

**Steps**:
1. As a user who already has a participant record and one wallet, create a second wallet
2. **Observe**: New wallet is auto-linked to existing participant (no duplicate participant created)
3. Token refresh includes the first wallet's address (existing behaviour — first active link)

### Scenario 3: Auto-Link Failure Doesn't Block Wallet (US1)

**Setup**: Stop the Tenant Service container while wallet creation is in progress.

**Steps**:
1. Stop Tenant Service: `docker stop sorcha-tenant-service`
2. Create a wallet (goes to Wallet Service directly)
3. **Observe**: Wallet is created successfully
4. **Observe**: Warning logged about auto-link failure
5. Restart Tenant Service, manually link the wallet if needed

### Scenario 4: Admin Provisions User in Private Org (US2)

**Steps**:
1. Log in as system administrator
2. Call the provisioning endpoint:
   ```bash
   curl -X POST http://localhost/api/platform/users \
     -H "Authorization: Bearer $ADMIN_TOKEN" \
     -H "Content-Type: application/json" \
     -d '{
       "email": "jane@ashwick.council.gov",
       "displayName": "Jane Doe",
       "organizationId": "ORG_ID_HERE",
       "role": "Member",
       "password": "SecurePassword123!",
       "skipEmailVerification": true
     }'
   ```
3. **Observe**: 201 response with user details
4. Log out, log in as jane@ashwick.council.gov with password "SecurePassword123!"
5. **Observe**: Login succeeds, user is in the correct organisation with Member role

### Scenario 5: Admin Provisions User with Existing Email (US2)

**Steps**:
1. Create user jane@ashwick.council.gov in Org A (Scenario 4)
2. Create user jane@ashwick.council.gov in Org B:
   ```bash
   curl -X POST http://localhost/api/platform/users \
     -H "Authorization: Bearer $ADMIN_TOKEN" \
     -d '{ "email": "jane@ashwick.council.gov", "organizationId": "ORG_B_ID", "role": "Admin", "displayName": "Jane Doe" }'
   ```
3. **Observe**: `isExistingPlatformUser: true` in response
4. Jane can now switch between Org A and Org B via the org switcher

### Scenario 6: Admin Resets Password (US3)

**Steps**:
1. Call the password reset endpoint:
   ```bash
   curl -X PUT http://localhost/api/platform/users/USER_ID/password \
     -H "Authorization: Bearer $ADMIN_TOKEN" \
     -d '{ "newPassword": "NewSecurePassword456!" }'
   ```
2. **Observe**: 200 response
3. Old password no longer works; new password succeeds

### Scenario 7: Non-Admin Rejected (US2/US3)

**Steps**:
1. Log in as a regular Member user
2. Call the provisioning or password reset endpoint
3. **Observe**: 403 Forbidden

## Verification Checklist

- [ ] Wallet creation auto-registers participant (new user)
- [ ] Wallet creation auto-links wallet (no challenge/verify)
- [ ] Existing participant gets new wallet linked (no duplicate)
- [ ] Token refresh includes wallet_address after auto-link
- [ ] ActionsHub subscription succeeds after auto-link
- [ ] Auto-link failure doesn't block wallet creation
- [ ] Admin can create user in private org with password
- [ ] Admin can create user with skipEmailVerification
- [ ] Existing PlatformUser reused for same email
- [ ] Admin can reset user password
- [ ] Non-admin rejected from admin endpoints
- [ ] Password policy enforced on admin-set passwords
- [ ] All existing tests pass (zero regressions)
