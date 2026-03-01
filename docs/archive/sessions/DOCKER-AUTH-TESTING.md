# Docker Authentication Testing Guide

**Date:** 2026-01-06
**Purpose:** Test authentication flow in Docker environment

---

## Problem Identified

The Blazor WASM client was configured with wrong profile defaults:
- **Active Profile:** "Development" (pointing to https://localhost:7082)
- **Docker Profile URL:** `http://localhost:8080` (incorrect - API Gateway is on port 80)

## Fix Applied

Updated `src/Apps/Sorcha.UI/Sorcha.UI.Core/Services/Configuration/ConfigurationService.cs`:
- **Docker Profile URL:** Changed from `http://localhost:8080` to `http://localhost`
- This ensures API calls go to the API Gateway on port 80

---

## Testing Steps

### 1. Rebuild Docker Image

```powershell
# Stop running containers
docker-compose down

# Rebuild UI Web image with updated configuration
docker-compose build sorcha-ui-web

# Start all services
docker-compose up -d

# Verify services are running
docker-compose ps
```

### 2. Clear Browser Storage

The profile configuration is cached in browser localStorage. You must clear it:

**Option A: Clear All Site Data (Recommended)**
1. Open Developer Tools (F12)
2. Go to **Application** tab
3. Under **Storage**, select **Clear site data**
4. Click **Clear site data** button
5. Refresh page (F5)

**Option B: Clear Specific Keys**
1. Open Developer Tools (F12)
2. Go to **Application** tab → **Local Storage** → `http://localhost`
3. Delete these keys:
   - `sorcha:profiles`
   - `sorcha:active-profile`
   - `sorcha:ui-config`
4. Refresh page (F5)

**Option C: Incognito/Private Window**
- Open http://localhost in incognito/private browsing mode
- This starts with clean storage

### 3. Select Docker Profile

1. Navigate to http://localhost/login
2. In the **Environment** dropdown, select **Docker**
3. Verify the description shows "Docker Compose backend services (http://localhost:80)"

### 4. Test Authentication

**Credentials:**
- **Username:** `admin@sorcha.local`
- **Password:** `Dev_Pass_2025!`

**Expected Flow:**
1. Enter credentials in login form
2. Click "Sign In" button
3. Client sends POST to `http://localhost/api/service-auth/token`
4. API Gateway routes to tenant-service
5. Tenant service validates credentials
6. JWT token returned to client
7. Token cached in browser localStorage
8. User redirected to dashboard/home page

### 5. Verify Success

**Browser Console (F12):**
- Should see: `POST http://localhost/api/service-auth/token` with HTTP 200
- No CSP violations
- Token cached successfully

**Browser Application Tab:**
- Local Storage should contain `sorcha:profiles` with active profile "Docker"
- Token cache should exist under appropriate key

**UI:**
- User should be redirected after successful login
- Authentication state should show logged in user

---

## Troubleshooting

### CSP Violation Error

**Error:**
```
Connecting to 'https://localhost:7082/...' violates CSP directive: "connect-src 'self' ws: wss:"
```

**Cause:** Browser cache serving old CSP headers or wrong profile selected

**Fix:**
1. Clear browser cache (Ctrl+Shift+Delete)
2. Hard refresh (Ctrl+F5)
3. Verify "Docker" profile is selected in Environment dropdown
4. Check browser console for actual CSP header:
   ```javascript
   document.querySelector('meta[http-equiv="Content-Security-Policy"]')?.content
   ```

### Wrong Profile URL

**Error:**
```
POST https://localhost:7082/api/service-auth/token - Failed to fetch
```

**Cause:** "Development" profile is active instead of "Docker"

**Fix:**
1. Clear localStorage (see Step 2 above)
2. Refresh page
3. Select "Docker" from Environment dropdown
4. Retry login

### API Gateway Not Routing

**Error:**
```
POST http://localhost/api/service-auth/token - 404 Not Found
```

**Cause:** API Gateway doesn't have route for `/api/service-auth/**`

**Fix:**
1. Verify route exists in `src/Services/Sorcha.ApiGateway/appsettings.json`:
   ```json
   "service-auth-route": {
     "ClusterId": "tenant-cluster",
     "Match": {
       "Path": "/api/service-auth/{**catch-all}"
     }
   }
   ```
2. Restart API Gateway:
   ```bash
   docker-compose restart sorcha-api-gateway
   ```

### Tenant Service Not Responding

**Error:**
```
POST http://localhost/api/service-auth/token - 500 Internal Server Error
```

**Cause:** Tenant service not running or database not initialized

**Fix:**
1. Check tenant service logs:
   ```bash
   docker logs sorcha-tenant-service
   ```
2. Verify PostgreSQL is running:
   ```bash
   docker-compose ps postgres
   ```
3. Check if bootstrap completed:
   ```bash
   curl http://localhost/api/tenants/bootstrap
   ```

---

## Verification Commands

### Check API Gateway Routes
```bash
# View API Gateway logs
docker logs sorcha-api-gateway

# Test service-auth endpoint directly on tenant service
docker exec sorcha-tenant-service curl -X POST http://localhost:8080/api/service-auth/token \
  -H "Content-Type: application/json" \
  -d '{"username":"admin@sorcha.local","password":"Dev_Pass_2025!","grant_type":"password"}'
```

### Check Tenant Service
```bash
# View tenant service logs
docker logs sorcha-tenant-service

# Check health
curl http://localhost/api/tenant/health
```

### Check Network Connectivity
```bash
# From API Gateway to Tenant Service
docker exec sorcha-api-gateway curl http://tenant-service:8080/health

# From browser to API Gateway
curl http://localhost/api/tenant/health
```

---

## Expected API Response

**Successful Authentication:**
```json
{
  "access_token": "eyJhbGci...",
  "token_type": "Bearer",
  "expires_in": 3600,
  "refresh_token": "eyJhbGci...",
  "scope": "api"
}
```

**Failed Authentication:**
```json
{
  "error": "invalid_grant",
  "error_description": "Invalid username or password"
}
```

---

## Success Criteria

- ✅ Docker profile URL points to `http://localhost`
- ✅ Browser localStorage has "Docker" as active profile
- ✅ Login form submits to `http://localhost/api/service-auth/token`
- ✅ No CSP violations in browser console
- ✅ API Gateway routes request to tenant-service
- ✅ Token received and cached
- ✅ User redirected after login

---

**Next Steps After Successful Login:**
1. Test authenticated API calls
2. Verify token refresh mechanism
3. Test role-based access control
4. Test logout functionality
5. Document authentication flow in FINAL-TEST-SUMMARY.md
