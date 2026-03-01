# Hybrid Authentication Architecture

## Overview

Sorcha.Admin now uses a **Blazor Web App (Hybrid)** architecture with seamless authentication across **InteractiveServer** and **InteractiveWebAssembly** render modes.

## Authentication Flow

### Architecture Diagram

```
┌──────────────────────────────────────────────────────────────┐
│  ANONYMOUS USER - Server Rendered (No WASM Download)         │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  1. User visits homepage (/)                                 │
│     → Render Mode: InteractiveServer                         │
│     → Payload: ~400 KB                                       │
│     → Auth State: Anonymous (ClaimsPrincipal.Empty)          │
│                                                              │
│  2. User navigates to /login                                 │
│     → Still InteractiveServer                                │
│     → LoginDialog component shown                            │
│                                                              │
│  3. User enters credentials and clicks "Sign In"             │
│     → AuthenticationService.LoginAsync() called              │
│     → POST /api/service-auth/token (password grant)          │
│     → Receives JWT access token                              │
│     → CustomAuthenticationStateProvider updated              │
│     → Auth state changed notification triggered              │
│                                                              │
└──────────────────────────────────────────────────────────────┘

┌──────────────────────────────────────────────────────────────┐
│  AUTHENTICATED USER - Navigates to WASM Page                 │
├──────────────────────────────────────────────────────────────┤
│                                                              │
│  4. User clicks "Create Blueprint" → /designer               │
│     → Render Mode: InteractiveWebAssembly (TRIGGERS WASM!)  │
│     → Download starts: ~1.8 MB                               │
│                                                              │
│  5. WASM Download Process:                                   │
│     ┌────────────────────────────────────────────────┐      │
│     │  Server Pre-Render Phase                       │      │
│     ├────────────────────────────────────────────────┤      │
│     │  • AuthenticationStateSerialization runs       │      │
│     │  • Gets auth state from server:                │      │
│     │    - CustomAuthenticationStateProvider         │      │
│     │  • Extracts UserInfo (ID, name, email, roles)  │      │
│     │  • Persists to PersistentComponentState        │      │
│     │  • Serialized as JSON in page payload          │      │
│     └────────────────────────────────────────────────┘      │
│                                                              │
│  6. WASM Initializes on Client:                              │
│     ┌────────────────────────────────────────────────┐      │
│     │  Client-Side WebAssembly Boot                  │      │
│     ├────────────────────────────────────────────────┤      │
│     │  • Sorcha.Admin.Client/Program.cs runs         │      │
│     │  • PersistentAuthenticationStateProvider       │      │
│     │    registered as AuthenticationStateProvider   │      │
│     │  • Reads UserInfo from PersistentComponentState│      │
│     │  • Reconstructs ClaimsPrincipal on client      │      │
│     │  • ✅ User authenticated in WASM!              │      │
│     └────────────────────────────────────────────────┘      │
│                                                              │
│  7. Subsequent WASM Pages (/my-workflows, /my-wallet):       │
│     → WASM already downloaded                                │
│     → Auth state already available                           │
│     → Instant navigation!                                    │
│                                                              │
└──────────────────────────────────────────────────────────────┘
```

## Component Architecture

### Server-Side Components

#### 1. **CustomAuthenticationStateProvider** (Server)
- **Location:** `Services/Authentication/CustomAuthenticationStateProvider.cs`
- **Purpose:** Manages authentication state for **InteractiveServer** pages
- **How it works:**
  - Calls `IAuthenticationService.GetAccessTokenAsync()`
  - Parses JWT token to extract claims
  - Returns `AuthenticationState` with `ClaimsPrincipal`
- **Used by:** Homepage, Login, Admin pages, Dev pages

#### 2. **AuthenticationStateSerialization Component**
- **Location:** `Components/Authentication/AuthenticationStateSerialization.razor`
- **Purpose:** Captures server auth state and persists it for WASM
- **How it works:**
  - Subscribes to `PersistentComponentState.RegisterOnPersisting()`
  - When transitioning to WASM page:
    1. Gets current `AuthenticationState` from server
    2. Extracts claims into `UserInfo` DTO
    3. Serializes to JSON
    4. Embeds in page for client to read
- **Lifecycle:** Runs during server pre-render, before WASM takes over

#### 3. **UserInfo DTO**
- **Purpose:** Serializable representation of auth state
- **Fields:**
  ```csharp
  public class UserInfo
  {
      public required string UserId { get; set; }
      public required string UserName { get; set; }
      public required string Email { get; set; }
      public string[] Roles { get; set; }
      public Dictionary<string, string> AdditionalClaims { get; set; }
  }
  ```
- **Shared between server and client**

### Client-Side Components (WASM)

#### 4. **PersistentAuthenticationStateProvider** (Client)
- **Location:** `Sorcha.Admin.Client/Services/Authentication/`
- **Purpose:** Reads persisted auth state in WASM environment
- **How it works:**
  - Constructor receives `PersistentComponentState`
  - Tries to read `UserInfo` from persistent state
  - If found:
    - Reconstructs `ClaimsPrincipal` from UserInfo
    - Returns authenticated state
  - If not found:
    - Returns anonymous state
- **Used by:** All WASM pages (Designer, Blueprints, My* pages)

## Authentication State Transfer Sequence

```
Server Pre-Render → Serialize → JSON in Page → Client Reads → Authenticated

  [Server]                [Browser]              [WASM]
     │                        │                     │
     │  1. Pre-render page    │                     │
     │─────────────────────>  │                     │
     │                        │                     │
     │  2. Get auth state     │                     │
     │     (JWT claims)       │                     │
     │                        │                     │
     │  3. Serialize UserInfo │                     │
     │     to JSON            │                     │
     │                        │                     │
     │  4. Embed in page      │                     │
     │─────────────────────>  │                     │
     │                        │                     │
     │                        │  5. WASM boots      │
     │                        │──────────────────>  │
     │                        │                     │
     │                        │  6. Read UserInfo   │
     │                        │     from state      │
     │                        │                     │
     │                        │  7. Create Claims   │
     │                        │     ✅ Authenticated│
```

## Key Benefits

1. **🚀 Fast Anonymous Access**
   - Homepage loads in <1s (400 KB)
   - No WASM download for anonymous users

2. **🔐 Seamless Authentication**
   - Login on server (InteractiveServer)
   - Auth state automatically transfers to WASM
   - No re-login required

3. **💾 Lazy WASM Loading**
   - WASM only downloads when user accesses:
     - /designer
     - /my-* pages
     - /blueprints, /schemas, /templates
   - Download happens AFTER authentication

4. **🔄 Stateless Client**
   - WASM reads auth state from server
   - No client-side token storage initially
   - Can use LocalStorage for offline later

## Page Render Mode Strategy

| Page Path | Render Mode | Auth Required | WASM Download |
|-----------|-------------|---------------|---------------|
| / (Homepage) | Server | No | ❌ |
| /login | Server | No | ❌ |
| /help | Server | No | ❌ |
| /admin/* | Server | Yes | ❌ |
| /dev/* | Server | Yes | ❌ |
| **WASM BOUNDARY** | | | |
| /designer | **WASM** | Yes | ✅ **TRIGGERS** |
| /blueprints | WASM | Yes | (already loaded) |
| /my-* | WASM | Yes | (already loaded) |
| /schemas | WASM | Yes | (already loaded) |
| /templates | WASM | Yes | (already loaded) |

## Security Considerations

### ✅ Secure
- JWT tokens validated on server
- Claims extracted server-side
- Only user identity transferred to client (no secrets)
- WASM auth state is read-only (cannot modify server state)

### ⚠️ Important Notes
- Client receives auth state as JSON (visible in page source)
- Do NOT include sensitive data in `AdditionalClaims`
- For API calls from WASM, implement separate token endpoint
- Consider token refresh for long-running WASM sessions

## Future Enhancements

### Planned (Phase 3)
1. **Token Endpoint for WASM**
   - Endpoint: `/api/auth/wasm-token`
   - Returns JWT for WASM pages to call APIs
   - Short-lived tokens (15 min)
   - Refresh token flow

2. **Offline Token Storage**
   - Store JWT in `LocalStorage` (encrypted)
   - For offline blueprint editing
   - Sync when back online

3. **Passkey Support**
   - WebAuthn integration
   - Server-side credential verification
   - Same persistent state transfer

## Testing Authentication Flow

### Test Scenario 1: Anonymous User
```bash
# Expected: No WASM download
1. Navigate to http://localhost:5000/
2. Observe: Page loads quickly (~400 KB)
3. Network tab: No dotnet.wasm download
4. Check claims: Anonymous (empty)
```

### Test Scenario 2: Login → WASM
```bash
# Expected: WASM downloads after login
1. Navigate to http://localhost:5000/login
2. Enter credentials and sign in
3. Click "Create Blueprint" button
4. Observe:
   - WASM download starts (~1.8 MB)
   - Designer page loads
   - User authenticated in WASM
5. Network tab: dotnet.wasm downloaded
6. Check claims: UserId, UserName, Email, Roles populated
```

### Test Scenario 3: Direct WASM Navigation (Authenticated)
```bash
# Expected: Redirect to login if not authenticated
1. Direct navigate to http://localhost:5000/designer (not logged in)
2. Observe: Redirected to /login
3. After login, navigate to /designer
4. Observe: WASM downloads, designer loads authenticated
```

## Troubleshooting

### Issue: User not authenticated in WASM
**Symptoms:** Claims are empty in WASM pages

**Possible Causes:**
1. `AuthenticationStateSerialization` not included in `Routes.razor`
2. `PersistentComponentState` not registered
3. Server auth state not available during pre-render

**Solution:**
- Check `Routes.razor` includes `<AuthenticationStateSerialization />`
- Verify server-side auth is working first
- Check browser console for serialization errors

### Issue: WASM downloads on anonymous access
**Symptoms:** WASM downloads for homepage

**Possible Causes:**
1. Homepage has `@rendermode InteractiveWebAssembly` (should be Server)
2. Component used on homepage requires WASM

**Solution:**
- Verify `Pages/Index.razor` has `@rendermode InteractiveServer`
- Check all components on homepage are Server-compatible

## Implementation Checklist

- [x] Create `PersistentAuthenticationStateProvider` (Server)
- [x] Create `PersistentAuthenticationStateProvider` (Client)
- [x] Create `AuthenticationStateSerialization` component
- [x] Add `UserInfo` DTO
- [x] Register server-side `AuthenticationStateProvider`
- [x] Register client-side `AuthenticationStateProvider`
- [x] Add `AuthenticationStateSerialization` to `Routes.razor`
- [x] Configure render modes on all pages
- [x] Test build succeeds
- [ ] Test authentication flow (Server → WASM)
- [ ] Test lazy WASM loading
- [ ] Add WASM download progress indicator
- [ ] Implement token endpoint for API calls
- [ ] Add offline token storage

## Related Documentation

- [Hybrid Render Modes](https://learn.microsoft.com/aspnet/core/blazor/components/render-modes)
- [Authentication in Blazor](https://learn.microsoft.com/aspnet/core/blazor/security/)
- [PersistentComponentState](https://learn.microsoft.com/aspnet/core/blazor/components/prerender)
