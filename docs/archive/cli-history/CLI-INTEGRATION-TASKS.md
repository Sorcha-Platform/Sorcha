# CLI Command Integration Tasks

**Status:** In Progress
**Created:** 2025-12-10
**Updated:** 2025-12-10

## Overview

This document tracks the integration of CLI commands with backend services. Currently, only authentication commands are fully functional. All other commands have complete System.CommandLine structure and tests but require service integration.

## Current Status

| Component | Status | Progress |
|-----------|--------|----------|
| Authentication Commands | ✅ Complete | 100% (4/4 commands) |
| Organization Commands | ❌ Stub | 0% (0/5 commands) |
| User Commands | ❌ Stub | 0% (0/5 commands) |
| Service Principal Commands | ❌ Stub | 0% (5/5 commands) |
| Wallet Commands | ❌ Stub | 0% (0/6 commands) |
| Register Commands | ❌ Stub | 0% (0/4 commands) |
| Transaction Commands | ❌ Stub | 0% (0/4 commands) |
| Peer Commands | ❌ Stub | 0% (0/5 commands) |

**Overall Progress:** 11% (4 of 36 commands working)

---

## Task Breakdown

### Phase 1: Foundation (CLI-INT-1)

#### CLI-INT-1: Register HttpClientFactory in DI Container
**Priority:** P0 (Blocker for all other commands)
**Estimate:** 30 minutes
**Dependencies:** None

**Description:**
Register the HttpClientFactory and related service clients in the DI container so they can be injected into commands.

**Acceptance Criteria:**
- [ ] HttpClientFactory registered as singleton in Program.cs
- [ ] Factory properly resolves IConfigurationService
- [ ] Build succeeds with no errors
- [ ] Existing auth tests still pass

**Implementation Steps:**
1. Update `Program.cs` ConfigureServices method:
   ```csharp
   services.AddSingleton<HttpClientFactory>();
   ```
2. Verify factory can create clients for all three services (Tenant, Register, Wallet)
3. Run tests to ensure no regression

**Files to Modify:**
- `src/Apps/Sorcha.Cli/Program.cs`

---

### Phase 2: Tenant Service Commands (CLI-INT-2, CLI-INT-3, CLI-INT-4)

#### CLI-INT-2: Wire Up OrganizationCommand with Service Integration
**Priority:** P1
**Estimate:** 2-3 hours
**Dependencies:** CLI-INT-1

**Description:**
Integrate all organization commands with the Tenant Service API using Refit clients.

**Commands to Implement:**
- `sorcha org list` - List all organizations
- `sorcha org get` - Get organization by ID
- `sorcha org create` - Create new organization
- `sorcha org update` - Update organization
- `sorcha org delete` - Delete organization

**Acceptance Criteria:**
- [ ] All 5 organization commands call real Tenant Service APIs
- [ ] Commands use authentication tokens from AuthenticationService
- [ ] Proper error handling for HTTP failures (404, 401, 500, etc.)
- [ ] Table output formatted using Spectre.Console (if available) or plain text
- [ ] JSON output mode works correctly
- [ ] Integration tests added for all commands
- [ ] All existing unit tests still pass

**Implementation Pattern:**
```csharp
public class OrgListCommand : Command
{
    private readonly HttpClientFactory _clientFactory;
    private readonly IAuthenticationService _authService;
    private readonly IConfigurationService _configService;

    public OrgListCommand(
        HttpClientFactory clientFactory,
        IAuthenticationService authService,
        IConfigurationService configService)
        : base("list", "List all organizations")
    {
        _clientFactory = clientFactory;
        _authService = authService;
        _configService = configService;

        this.SetHandler(async () =>
        {
            try
            {
                // Get active profile
                var profile = await _configService.GetActiveProfileAsync();
                var profileName = profile?.Name ?? "dev";

                // Get access token
                var token = await _authService.GetAccessTokenAsync(profileName);
                if (string.IsNullOrEmpty(token))
                {
                    ConsoleHelper.WriteError("Not authenticated. Run 'sorcha auth login' first.");
                    Environment.ExitCode = ExitCodes.AuthenticationError;
                    return;
                }

                // Create Tenant Service client
                var client = await _clientFactory.CreateTenantServiceClientAsync(profileName);

                // Call API
                var organizations = await client.ListOrganizationsAsync($"Bearer {token}");

                // Display results
                if (organizations.Count == 0)
                {
                    ConsoleHelper.WriteInfo("No organizations found.");
                    return;
                }

                ConsoleHelper.WriteSuccess($"Found {organizations.Count} organization(s):");
                foreach (var org in organizations)
                {
                    Console.WriteLine($"  {org.Id,-30} {org.Name}");
                }
            }
            catch (Refit.ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                ConsoleHelper.WriteError("Authentication failed. Token may be expired. Run 'sorcha auth login'.");
                Environment.ExitCode = ExitCodes.AuthenticationError;
            }
            catch (Refit.ApiException ex)
            {
                ConsoleHelper.WriteError($"API error: {ex.StatusCode} - {ex.Content}");
                Environment.ExitCode = ExitCodes.GeneralError;
            }
            catch (Exception ex)
            {
                ConsoleHelper.WriteError($"Failed to list organizations: {ex.Message}");
                Environment.ExitCode = ExitCodes.GeneralError;
            }
        });
    }
}
```

**Files to Modify:**
- `src/Apps/Sorcha.Cli/Commands/OrganizationCommands.cs`
- `src/Apps/Sorcha.Cli/Program.cs` (update command registration)
- `tests/Sorcha.Cli.Tests/Commands/OrganizationCommandsTests.cs` (add integration tests)

---

#### CLI-INT-3: Wire Up UserCommand with Service Integration
**Priority:** P1
**Estimate:** 2-3 hours
**Dependencies:** CLI-INT-1

**Description:**
Integrate all user commands with the Tenant Service API.

**Commands to Implement:**
- `sorcha user list` - List users in organization
- `sorcha user get` - Get user details
- `sorcha user create` - Create new user
- `sorcha user update` - Update user
- `sorcha user delete` - Delete user

**Acceptance Criteria:**
- [ ] All 5 user commands call real Tenant Service APIs
- [ ] Commands properly handle organization ID parameter
- [ ] Authentication token used for all API calls
- [ ] Proper error handling for all failure scenarios
- [ ] Output formatting (table/json) works correctly
- [ ] Integration tests added
- [ ] All existing tests pass

**Special Considerations:**
- All user commands require `--org-id` parameter
- Consider adding a "current organization" context for future REPL mode

**Files to Modify:**
- `src/Apps/Sorcha.Cli/Commands/UserCommands.cs`
- `src/Apps/Sorcha.Cli/Program.cs`
- `tests/Sorcha.Cli.Tests/Commands/UserCommandsTests.cs`

---

#### CLI-INT-4: Wire Up ServicePrincipalCommand with Service Integration
**Priority:** P1
**Estimate:** 2-3 hours
**Dependencies:** CLI-INT-1

**Description:**
Integrate all service principal commands with the Tenant Service API.

**Commands to Implement:**
- `sorcha sp list` - List service principals in organization
- `sorcha sp get` - Get service principal details
- `sorcha sp create` - Create new service principal
- `sorcha sp delete` - Delete service principal
- `sorcha sp rotate-secret` - Rotate client secret

**Acceptance Criteria:**
- [ ] All 5 service principal commands call real APIs
- [ ] `sp create` displays client secret securely (one-time only)
- [ ] `sp rotate-secret` shows warning about invalidating old secret
- [ ] Sensitive data (client secrets) properly masked/hidden in logs
- [ ] Integration tests added
- [ ] All existing tests pass

**Security Considerations:**
- Client secrets should ONLY be displayed once during creation/rotation
- Add prominent warnings about storing secrets securely
- Consider adding optional `--output-file` to save secrets to encrypted file

**Files to Modify:**
- `src/Apps/Sorcha.Cli/Commands/ServicePrincipalCommands.cs`
- `src/Apps/Sorcha.Cli/Program.cs`
- `tests/Sorcha.Cli.Tests/Commands/ServicePrincipalCommandsTests.cs`

---

### Phase 3: Wallet Service Commands (CLI-INT-5)

#### CLI-INT-5: Wire Up WalletCommand with Service Integration
**Priority:** P1
**Estimate:** 3-4 hours
**Dependencies:** CLI-INT-1

**Description:**
Integrate all wallet commands with the Wallet Service API.

**Commands to Implement:**
- `sorcha wallet list` - List all wallets
- `sorcha wallet get` - Get wallet details
- `sorcha wallet create` - Create new HD wallet
- `sorcha wallet recover` - Recover wallet from mnemonic
- `sorcha wallet sign` - Sign data with wallet
- `sorcha wallet delete` - Delete wallet (soft delete)

**Acceptance Criteria:**
- [ ] All 6 wallet commands call real Wallet Service APIs
- [ ] `wallet create` displays mnemonic phrase with security warnings
- [ ] Mnemonic phrases properly masked in logs
- [ ] `wallet sign` handles different signature algorithms
- [ ] Wallet deletion shows confirmation prompt
- [ ] Integration tests added (with mock mnemonics)
- [ ] All existing tests pass

**Security Considerations:**
- Mnemonic phrases should ONLY be displayed once during creation
- Add prominent warnings: "SAVE YOUR MNEMONIC - IT CANNOT BE RECOVERED"
- Never log or store mnemonic phrases
- Consider adding `--show-mnemonic` flag for recovery operations (disabled by default)

**Wallet Service API Notes:**
- Wallet Service is 90% complete (see docs/wallet-service-status.md)
- API endpoints are fully implemented
- Database persistence (EF Core) is in progress
- Azure Key Vault integration is planned

**Files to Modify:**
- `src/Apps/Sorcha.Cli/Commands/WalletCommands.cs`
- `src/Apps/Sorcha.Cli/Program.cs`
- `tests/Sorcha.Cli.Tests/Commands/WalletCommandsTests.cs`

---

### Phase 4: Register Service Commands (CLI-INT-6, CLI-INT-7)

#### CLI-INT-6: Wire Up RegisterCommand with Service Integration
**Priority:** P2
**Estimate:** 2 hours
**Dependencies:** CLI-INT-1

**Description:**
Integrate all register commands with the Register Service API.

**Commands to Implement:**
- `sorcha register list` - List all registers
- `sorcha register get` - Get register details
- `sorcha register create` - Create new register
- `sorcha register delete` - Delete register

**Acceptance Criteria:**
- [ ] All 4 register commands call real Register Service APIs
- [ ] Register creation requires organization ID
- [ ] Register deletion shows confirmation prompt
- [ ] Integration tests added
- [ ] All existing tests pass

**Register Service Notes:**
- Register Service is 100% complete
- MongoDB repository fully implemented
- All CRUD operations working

**Files to Modify:**
- `src/Apps/Sorcha.Cli/Commands/RegisterCommands.cs`
- `src/Apps/Sorcha.Cli/Program.cs`
- `tests/Sorcha.Cli.Tests/Commands/RegisterCommandsTests.cs`

---

#### CLI-INT-7: Wire Up TransactionCommand with Service Integration
**Priority:** P2
**Estimate:** 3 hours
**Dependencies:** CLI-INT-1, CLI-INT-5 (for wallet signing)

**Description:**
Integrate all transaction commands with the Register Service API.

**Commands to Implement:**
- `sorcha tx list` - List transactions in register
- `sorcha tx get` - Get transaction details
- `sorcha tx submit` - Submit new transaction
- `sorcha tx status` - Get transaction status

**Acceptance Criteria:**
- [ ] All 4 transaction commands call real APIs
- [ ] `tx submit` integrates with wallet for auto-signing
- [ ] Transaction payloads properly validated
- [ ] Pagination support for `tx list` (--skip, --take)
- [ ] Integration tests added
- [ ] All existing tests pass

**Transaction Service Integration:**
- Uses Register Service endpoints
- Requires wallet address for signing
- Optional `--auto-sign` flag to sign with wallet automatically
- Payloads must be valid JSON

**Files to Modify:**
- `src/Apps/Sorcha.Cli/Commands/TransactionCommands.cs`
- `src/Apps/Sorcha.Cli/Program.cs`
- `tests/Sorcha.Cli.Tests/Commands/TransactionCommandsTests.cs`

---

### Phase 5: Peer Service Commands (CLI-INT-8)

#### CLI-INT-8: Implement PeerCommand with gRPC Client
**Priority:** P3 (Future Sprint)
**Estimate:** 4-6 hours
**Dependencies:** CLI-INT-1, Peer Service gRPC definitions

**Description:**
Implement peer network commands using gRPC client instead of REST.

**Commands to Implement:**
- `sorcha peer list` - List all peers
- `sorcha peer get` - Get peer details
- `sorcha peer topology` - View network topology
- `sorcha peer stats` - Network statistics
- `sorcha peer health` - Health checks

**Acceptance Criteria:**
- [ ] gRPC client generated from Peer Service .proto files
- [ ] All 5 peer commands use gRPC client
- [ ] Proper error handling for gRPC failures
- [ ] Network topology visualization (tree/graph format)
- [ ] Integration tests added
- [ ] All existing tests pass

**Peer Service Notes:**
- Peer Service is 65% complete
- gRPC protocol definitions in progress
- Network topology algorithms implemented
- This is lower priority - can remain stub until Peer Service is production-ready

**Additional Work Required:**
- [ ] Add Grpc.Net.Client package
- [ ] Generate gRPC client from .proto files
- [ ] Create IPeerServiceClient interface
- [ ] Update HttpClientFactory to support gRPC clients
- [ ] Add gRPC-specific error handling

**Files to Modify:**
- `src/Apps/Sorcha.Cli/Commands/PeerCommands.cs`
- `src/Apps/Sorcha.Cli/Services/IPeerServiceClient.cs` (new)
- `src/Apps/Sorcha.Cli/Services/HttpClientFactory.cs` (add gRPC support)
- `src/Apps/Sorcha.Cli/Program.cs`
- `tests/Sorcha.Cli.Tests/Commands/PeerCommandsTests.cs`

---

### Phase 6: Testing & Documentation (CLI-INT-9)

#### CLI-INT-9: Add Integration Tests for All Wired Commands
**Priority:** P1
**Estimate:** 4-6 hours
**Dependencies:** CLI-INT-2, CLI-INT-3, CLI-INT-4, CLI-INT-5, CLI-INT-6, CLI-INT-7

**Description:**
Add comprehensive integration tests for all service-integrated commands.

**Test Coverage Required:**
- [ ] Organization commands (5 tests minimum)
- [ ] User commands (5 tests minimum)
- [ ] Service Principal commands (5 tests minimum)
- [ ] Wallet commands (6 tests minimum)
- [ ] Register commands (4 tests minimum)
- [ ] Transaction commands (4 tests minimum)

**Test Patterns:**
1. **Command Structure Tests** (already exist)
   - Verify command names, descriptions, options
2. **Mock Service Tests** (new)
   - Test command logic with mocked HTTP responses
   - Use TestHttpMessageHandler for API responses
3. **Error Handling Tests** (new)
   - Test 401 Unauthorized responses
   - Test 404 Not Found responses
   - Test 500 Server Error responses
   - Test network timeouts
   - Test invalid input validation

**Example Integration Test:**
```csharp
[Fact]
public async Task OrgListCommand_ShouldCallTenantServiceAPI_WithAuthToken()
{
    // Arrange
    var tokenResponse = new TokenResponse
    {
        AccessToken = "test-token",
        ExpiresIn = 3600
    };
    _httpHandler.SetResponse(HttpStatusCode.OK, tokenResponse);

    await _authService.LoginAsync(new LoginRequest
    {
        Username = "test",
        Password = "test"
    }, "dev");

    var organizations = new List<Organization>
    {
        new() { Id = "org-1", Name = "Org 1" },
        new() { Id = "org-2", Name = "Org 2" }
    };
    _httpHandler.SetResponse(HttpStatusCode.OK, organizations);

    var rootCommand = new RootCommand();
    var clientFactory = new HttpClientFactory(_configService);
    rootCommand.AddCommand(new OrganizationCommand(clientFactory, _authService, _configService));

    // Act
    var exitCode = await rootCommand.InvokeAsync("org list");

    // Assert
    exitCode.Should().Be(0);
    // Verify HTTP request was made with Bearer token
}
```

**Files to Modify:**
- `tests/Sorcha.Cli.Tests/Commands/*CommandsTests.cs` (all command test files)
- `tests/Sorcha.Cli.Tests/Utilities/TestHttpMessageHandler.cs` (enhance if needed)

---

## Configuration Changes

### CLI-INT-10: Add Local Docker Profile to Configuration ✅
**Status:** Complete
**Completed:** 2025-12-10

**Changes Made:**
- ✅ Added "local" profile to ConfigurationService.cs
- ✅ Updated config.template.json with local profile
- ✅ Local profile uses HTTP on ports 5080-5083 (Docker container ports)

**Profile Configuration:**
```json
"local": {
  "name": "local",
  "tenantServiceUrl": "http://localhost:5080",
  "registerServiceUrl": "http://localhost:5081",
  "peerServiceUrl": "http://localhost:5082",
  "walletServiceUrl": "http://localhost:5083",
  "authTokenUrl": "http://localhost:5080/api/service-auth/token",
  "defaultClientId": "sorcha-cli",
  "verifySsl": false,
  "timeoutSeconds": 30
}
```

**Usage:**
```bash
# Use local Docker profile
sorcha auth login --profile local
sorcha org list --profile local
```

---

## Implementation Order

**Recommended Sprint Order:**

### Sprint 1: Foundation & Tenant Service (Week 1)
- [ ] CLI-INT-1: Register HttpClientFactory (Day 1)
- [ ] CLI-INT-2: Wire up OrganizationCommand (Day 2-3)
- [ ] CLI-INT-3: Wire up UserCommand (Day 3-4)
- [ ] CLI-INT-4: Wire up ServicePrincipalCommand (Day 4-5)

**Deliverable:** Tenant Service commands fully functional

### Sprint 2: Wallet & Register Services (Week 2)
- [ ] CLI-INT-5: Wire up WalletCommand (Day 1-2)
- [ ] CLI-INT-6: Wire up RegisterCommand (Day 3)
- [ ] CLI-INT-7: Wire up TransactionCommand (Day 4-5)

**Deliverable:** Wallet and Register commands fully functional

### Sprint 3: Testing & Polish (Week 3)
- [ ] CLI-INT-9: Add integration tests (Day 1-3)
- [ ] Documentation updates (Day 4)
- [ ] End-to-end testing (Day 5)

**Deliverable:** Production-ready CLI with full test coverage

### Future Sprint: Peer Service (TBD)
- [ ] CLI-INT-8: Implement PeerCommand with gRPC (when Peer Service is ready)

---

## Testing Checklist

Before marking any task as complete:

- [ ] Build succeeds with no warnings or errors
- [ ] All existing unit tests pass
- [ ] New integration tests added and passing
- [ ] Manual testing performed with real backend services
- [ ] Error scenarios tested (401, 404, 500, timeouts)
- [ ] Output formatting tested (table and JSON modes)
- [ ] Documentation updated (README.md, command help text)
- [ ] Code reviewed for security issues (no secrets in logs, proper input validation)

---

## Dependencies & Blockers

| Task | Blocked By | Notes |
|------|-----------|-------|
| CLI-INT-2 | CLI-INT-1 | Need HttpClientFactory in DI |
| CLI-INT-3 | CLI-INT-1 | Need HttpClientFactory in DI |
| CLI-INT-4 | CLI-INT-1 | Need HttpClientFactory in DI |
| CLI-INT-5 | CLI-INT-1 | Need HttpClientFactory in DI |
| CLI-INT-6 | CLI-INT-1 | Need HttpClientFactory in DI |
| CLI-INT-7 | CLI-INT-1, CLI-INT-5 | Need HttpClientFactory + Wallet integration for auto-sign |
| CLI-INT-8 | Peer Service .proto files | Waiting for Peer Service gRPC definitions |
| CLI-INT-9 | CLI-INT-2 through CLI-INT-7 | Need commands implemented first |

---

## Success Criteria

The CLI integration is complete when:

1. ✅ All 40 commands have real service integration (not stubs)
2. ✅ All commands properly use authentication tokens
3. ✅ Error handling covers all common failure scenarios
4. ✅ Output formatting works for both table and JSON modes
5. ✅ Integration test coverage ≥ 80%
6. ✅ End-to-end workflows tested (create org → create user → create wallet → submit transaction)
7. ✅ Documentation updated with working examples
8. ✅ CLI can be used for production administration tasks

---

## Notes

- **Authentication Pattern:** All commands should check for valid auth token first, fail fast with clear error message if not authenticated
- **Error Messages:** Use ConsoleHelper for colored output (success=green, error=red, warning=yellow, info=cyan)
- **Retry Logic:** HttpClientFactory already has Polly retry policies configured
- **SSL Verification:** Disabled for dev/local profiles, enabled for staging/production
- **Timeouts:** Default 30 seconds, configurable per profile
- **Output Modes:** Support --output table (default), json, csv
- **Profile Selection:** Use --profile flag or active profile from config

---

**Last Updated:** 2025-12-10
**Next Review:** After Sprint 1 completion
