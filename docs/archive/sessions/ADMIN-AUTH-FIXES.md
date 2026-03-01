# Sorcha Admin Authentication Fixes

**Date**: 2026-01-02
**Version**: Sorcha.Admin v1.0.1
**Status**: Complete ✅

## Summary

Fixed Sorcha Admin authentication issues and improved login UX by:
1. Correcting default profile configuration from "local" to "docker"
2. Hiding environment selector in an advanced options panel
3. Creating comprehensive test suite with 32 passing tests
4. Documenting browser LocalStorage structure

## Issues Fixed

### 1. Profile Name Mismatch
**Problem**: LoginDialog defaulted to non-existent "dev" profile
**Root Cause**: LoginDialog hardcoded `_selectedProfile = "dev"`, but ProfileDefaults only defined "local", "docker", and "production"
**Fix**: Changed to `_selectedProfile = "docker"` with comment

**Files Changed**:
- [LoginDialog.razor:74](../src/Apps/Sorcha.Admin/Components/Authentication/LoginDialog.razor#L74)

### 2. Wrong Default Active Profile
**Problem**: Default profile was "local" (Aspire) instead of "docker" (Docker Compose)
**Root Cause**: ProfileDefaults.DefaultActiveProfile returned "local"
**Fix**: Changed to return "docker" as default for Docker Compose deployments

**Files Changed**:
- [ProfileDefaults.cs:76](../src/Apps/Sorcha.Admin/Models/Configuration/ProfileDefaults.cs#L76)

### 3. Confusing Login UX
**Problem**: Environment selector prominently displayed, confusing for most users
**Root Cause**: Profile selector was at top of login dialog
**Fix**: Moved to collapsible "Advanced Options" panel

**Files Changed**:
- [LoginDialog.razor](../src/Apps/Sorcha.Admin/Components/Authentication/LoginDialog.razor)

## Browser LocalStorage Structure

### Configuration Key: `sorcha:config`

```json
{
  "ActiveProfile": "docker",
  "Profiles": {
    "docker": {
      "Name": "docker",
      "TenantServiceUrl": "http://localhost/api/tenant",
      "RegisterServiceUrl": "http://localhost/api/register",
      "PeerServiceUrl": "http://localhost/api/peer",
      "WalletServiceUrl": "http://localhost/api/wallet",
      "BlueprintServiceUrl": "http://localhost/api/blueprint",
      "AuthTokenUrl": "http://localhost/api/service-auth/token",
      "DefaultClientId": "sorcha-admin",
      "VerifySsl": false,
      "TimeoutSeconds": 30
    },
    "local": { ... },
    "production": { ... }
  },
  "VerboseLogging": false
}
```

### Token Cache Key: `sorcha:tokens:{profile}`

Example: `sorcha:tokens:docker`

**Value**: Base64-encoded encrypted JSON of TokenCacheEntry

```json
{
  "AccessToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "RefreshToken": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "ExpiresAt": "2026-01-03T12:00:00Z",
  "Profile": "docker",
  "Subject": "admin@sorcha.local"
}
```

**Note**: Tokens are encrypted using Web Crypto API (AES-256-GCM) before storage.

## Testing

Created comprehensive test suite with **32 tests, all passing**:

### Test Coverage

#### ConfigurationServiceTests (14 tests)
- ✅ Default configuration creation
- ✅ Profile management (get, set, list)
- ✅ Active profile switching
- ✅ LocalStorage key consistency
- ✅ Docker profile as default
- ✅ Correct authentication URLs

#### BrowserTokenCacheTests (13 tests)
- ✅ Token encryption and storage
- ✅ Token retrieval and decryption
- ✅ Expired token cleanup
- ✅ LocalStorage key format validation
- ✅ Clear all tokens functionality
- ✅ Encryption failure handling

#### ProfileDefaultsTests (5 tests)
- ✅ Docker as default active profile
- ✅ Correct authentication URLs for all profiles
- ✅ HTTP/HTTPS protocol validation
- ✅ Required field validation

### Running Tests

```bash
# Run all tests
cd tests/Sorcha.Admin.Tests
dotnet test

# With verbose output
dotnet test --logger "console;verbosity=detailed"

# With coverage
dotnet test --collect:"XPlat Code Coverage"
```

**Latest Test Run**: All 32 tests passed ✅

## Files Modified

### Source Code
1. **src/Apps/Sorcha.Admin/Models/Configuration/ProfileDefaults.cs**
   - Changed `DefaultActiveProfile` from "local" to "docker"
   - Added documentation comment

2. **src/Apps/Sorcha.Admin/Components/Authentication/LoginDialog.razor**
   - Changed default profile from "dev" to "docker"
   - Moved environment selector to collapsible "Advanced Options" panel
   - Added helper text and current profile indicator

### Tests Created
1. **tests/Sorcha.Admin.Tests/Sorcha.Admin.Tests.csproj** (NEW)
2. **tests/Sorcha.Admin.Tests/Services/ConfigurationServiceTests.cs** (NEW)
3. **tests/Sorcha.Admin.Tests/Services/BrowserTokenCacheTests.cs** (NEW)
4. **tests/Sorcha.Admin.Tests/Models/ProfileDefaultsTests.cs** (NEW)
5. **tests/Sorcha.Admin.Tests/README.md** (NEW)

## Verification Steps

1. **Clear browser cache**: Ctrl+Shift+Delete
2. **Navigate to**: http://localhost/admin/login
3. **Verify**:
   - Login dialog shows username/password fields first
   - "Advanced Options" panel collapsed by default
   - Default credentials hint visible
4. **Login with**:
   - Username: `admin@sorcha.local`
   - Password: `Dev_Pass_2025!`
5. **Expected**:
   - Authentication succeeds
   - Token stored in LocalStorage at `sorcha:tokens:docker`
   - Redirect to dashboard

### Check Browser LocalStorage

**F12 → Application → Local Storage → http://localhost**

Should see:
- `sorcha:config` - Configuration JSON (readable)
- `sorcha:tokens:docker` - Encrypted token (Base64)

### Clear LocalStorage (if needed)

```javascript
// In browser console
localStorage.removeItem('sorcha:config');
localStorage.removeItem('sorcha:tokens:docker');
// Or clear all
localStorage.clear();
```

## Regression Prevention

### Critical Tests

1. **DefaultActiveProfile_IsDocker**
   - Ensures docker remains default
   - Prevents regression to "local" or "dev"

2. **DockerProfile_HasCorrectConfiguration**
   - Validates AuthTokenUrl = `http://localhost/api/service-auth/token`
   - Prevents regression to wrong URL paths

3. **LocalStorageKey_IsConsistent**
   - Validates config key = `sorcha:config`
   - Validates token key = `sorcha:tokens:{profile}`

4. **TokenCacheKey_FollowsCorrectFormat**
   - Ensures tokens stored with correct profile name
   - Validates `sorcha:tokens:docker` format

## Known Issues

None currently identified. All tests passing.

## Future Enhancements

- [ ] Add "Remember Me" option for persistent login
- [ ] Add profile quick-switch in user menu
- [ ] Add profile creation wizard
- [ ] Add connection test before login attempt
- [ ] Add E2E authentication flow tests with Playwright

## Related Documentation

- [JWT Configuration Guide](JWT-CONFIGURATION.md)
- [Sorcha.Admin Tests README](../tests/Sorcha.Admin.Tests/README.md)
- [ProfileDefaults Source](../src/Apps/Sorcha.Admin/Models/Configuration/ProfileDefaults.cs)
- [LoginDialog Source](../src/Apps/Sorcha.Admin/Components/Authentication/LoginDialog.razor)

## Deployment

**Docker Image**: `sorcha/admin:latest`
**Build Date**: 2026-01-02
**Build Command**: `docker-compose build sorcha-admin`
**Restart Command**: `docker-compose restart sorcha-admin`

## Conclusion

All authentication issues resolved with:
- ✅ 32 comprehensive tests (100% passing)
- ✅ Improved UX with collapsible advanced options
- ✅ Correct default profile ("docker")
- ✅ Documented LocalStorage structure
- ✅ Regression prevention tests in place

**Status**: Ready for testing and deployment ✅
