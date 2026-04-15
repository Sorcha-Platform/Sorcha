# Sorcha Admin Integration Walkthrough

**Purpose:** Integrate Sorcha.Admin (Blazor WASM) into Docker deployment behind API Gateway at `/admin` path.

**Date Created:** 2026-01-02
**Status:** ✅ Complete
**Prerequisites:** Docker Desktop, .NET 10 SDK, Completed BlueprintStorageBasic walkthrough

---

## Overview

This walkthrough demonstrates:
1. Dockerizing the Sorcha.Admin Blazor WebAssembly application
2. Serving it via nginx at the `/admin` subpath
3. Routing traffic through API Gateway using YARP
4. Configuring authentication to work with backend services
5. Testing the complete integration

---

## What Changed

### Application Configuration

**ProfileDefaults.cs** - Updated docker profile:
```csharp
["docker"] = new Profile
{
    TenantServiceUrl = "http://localhost/api/tenant",      // ✅ Fixed
    RegisterServiceUrl = "http://localhost/api/register",   // ✅ Fixed
    PeerServiceUrl = "http://localhost/api/peer",          // ✅ Fixed
    WalletServiceUrl = "http://localhost/api/wallet",      // ✅ Fixed
    BlueprintServiceUrl = "http://localhost/api/blueprint", // ✅ Fixed
    AuthTokenUrl = "http://localhost/api/service-auth/token", // ✅ Fixed
    ...
}
```

**index.html** - Base path updated:
```html
<base href="/admin/" />  <!-- Changed from "/" -->
```

### Docker Configuration

**Dockerfile** - Multi-stage build:
- Build stage: Compile Blazor WASM with .NET SDK
- Runtime stage: Serve static files with nginx:alpine
- Files served from `/usr/share/nginx/html/admin`

**nginx.conf** - Subpath configuration:
- Serves admin UI from `/admin` location
- Handles SPA routing (all routes → index.html)
- Correct MIME types for .wasm, .dll, .dat, .blat files
- Health check endpoint at `/health`

**docker-compose.yml** - New service:
```yaml
sorcha-admin:
  image: sorcha/admin:latest
  container_name: sorcha-admin
  networks:
    - sorcha-network
  healthcheck:
    test: ["CMD", "wget", "--spider", "http://localhost/health"]
```

### API Gateway Routing

**appsettings.json** - YARP configuration:
```json
"admin-route": {
  "ClusterId": "admin-cluster",
  "Match": {
    "Path": "/admin/{**catch-all}"
  }
},
"admin-cluster": {
  "Destinations": {
    "destination1": {
      "Address": "http://sorcha-admin:80"
    }
  }
}
```

---

## Architecture

```
┌─────────────────────────────────────────────────────────┐
│              Browser (http://localhost)                  │
└───────────────────────────┬─────────────────────────────┘
                            │
                ┌───────────▼───────────┐
                │    API Gateway        │
                │    (YARP Proxy)       │
                │    Port 80/443        │
                └───┬──────────────┬────┘
                    │              │
        ┌───────────▼──┐      ┌────▼──────────┐
        │ /admin/*     │      │ /api/*        │
        │              │      │               │
   ┌────▼─────┐       │      │  ┌─────────┐  │
   │ Sorcha   │       │      │  │ Tenant  │  │
   │ Admin    │       │      │  │ Service │  │
   │ (nginx)  │       │      │  └─────────┘  │
   └──────────┘       │      │               │
                      │      │  ┌─────────┐  │
                      │      │  │Blueprint│  │
                      │      │  │ Service │  │
                      │      │  └─────────┘  │
                      │      │               │
                      │      │  (+ 4 more)   │
                      └──────┴───────────────┘
```

---

## Quick Start

### 1. Build and Start All Services

```bash
# From repository root
docker-compose build sorcha-admin
docker-compose up -d
```

### 2. Verify All Services Healthy

```bash
# Check service status
docker-compose ps

# Should show 13 services (12 + sorcha-admin)
docker-compose ps | grep healthy
```

### 3. Access Admin UI

Open browser: **http://localhost/admin**

You should see the Sorcha Admin login page.

### 4. Login

Use bootstrap credentials:
- Email: `stuart.mackintosh@sorcha.dev`
- Password: `SorchaDev2025!`

**Profile:** Select "docker" from profile dropdown (or it should be selected if first launch)

---

## Testing Checklist

### ✅ Basic Access
- [ ] Admin UI loads at `http://localhost/admin`
- [ ] Static assets load (CSS, JS, images)
- [ ] No 404 errors in browser console
- [ ] Page renders correctly

### ✅ Authentication
- [ ] Login page appears when not authenticated
- [ ] Can select "docker" profile
- [ ] Login with bootstrap credentials succeeds
- [ ] JWT token stored in browser LocalStorage
- [ ] Redirected to home page after login

### ✅ API Integration
- [ ] Dashboard shows service statistics
- [ ] Blueprint list loads (may be empty)
- [ ] Can create new blueprint
- [ ] Can upload blueprint JSON
- [ ] System status shows healthy services

### ✅ Navigation
- [ ] All menu items accessible
- [ ] SPA routing works (no page refreshes)
- [ ] Browser back/forward buttons work
- [ ] Direct URL navigation works (e.g., `/admin/blueprints`)

### ✅ Service Communication
- [ ] Tenant Service: Authentication works
- [ ] Blueprint Service: Can fetch blueprints
- [ ] Wallet Service: Can view wallets
- [ ] Register Service: Can view transactions
- [ ] No CORS errors in console

---

## Access Points

| Service | URL | Purpose |
|---------|-----|---------|
| **Admin UI** | http://localhost/admin | Main admin interface |
| **API Gateway** | http://localhost/ | Gateway landing page |
| **API Documentation** | http://localhost/scalar/ | Interactive API docs |
| **Health Check** | http://localhost/api/health | Aggregated service health |
| **Aspire Dashboard** | http://localhost:18888 | Observability & telemetry |

---

## Troubleshooting

### Admin UI returns 404

**Check:**
```bash
# Verify sorcha-admin container is running
docker ps | grep sorcha-admin

# Check nginx logs
docker logs sorcha-admin

# Check API Gateway routing
curl http://localhost/admin
```

**Fix:** Rebuild container
```bash
docker-compose build sorcha-admin
docker-compose up -d sorcha-admin
```

### Static assets (CSS/JS) fail to load

**Symptom:** Page loads but appears unstyled

**Check:**
- Browser console for 404 errors
- Verify base href is `/admin/` in index.html
- Check nginx serving from correct directory

**Fix:**
```bash
# Rebuild with fresh publish
docker-compose build --no-cache sorcha-admin
docker-compose up -d sorcha-admin
```

### Authentication fails with 401

**Check:**
```bash
# Verify Tenant Service is healthy
curl http://localhost/api/tenant/health

# Test authentication directly
curl -X POST http://localhost/api/service-auth/token \
  -H "Content-Type: application/x-www-form-urlencoded" \
  -d "grant_type=password&username=stuart.mackintosh@sorcha.dev&password=SorchaDev2025!&client_id=sorcha-admin"
```

**Common causes:**
- Tenant Service not running
- Wrong profile selected (should be "docker")
- Invalid credentials
- Token expired (logout and login again)

### CORS errors in browser console

**Symptom:** API calls blocked by CORS policy

**Fix:** CORS should be disabled via API Gateway. If still seeing errors:

```bash
# Check API Gateway CORS configuration
docker logs sorcha-api-gateway | grep -i cors

# Restart API Gateway
docker-compose restart api-gateway
```

### Admin UI shows blank page

**Check browser console:**
```
F12 → Console tab
```

**Common causes:**
1. Blazor framework failed to load (check .wasm files)
2. JavaScript errors (encryption.js issue)
3. Base path mismatch

**Fix:**
```bash
# Clear browser cache and hard reload
# Ctrl+Shift+R or Cmd+Shift+R

# Check nginx MIME types
docker exec sorcha-admin cat /etc/nginx/nginx.conf
```

---

## Configuration Details

### Environment Variables (docker-compose.yml)

```yaml
Services__Admin__Url: http://sorcha-admin:80  # Added to api-gateway
```

### Profile Configuration (Used by Admin UI)

**docker profile** (ProfileDefaults.cs):
- All service URLs point to API Gateway: `http://localhost/api/*`
- Authentication URL: `http://localhost/api/service-auth/token`
- No SSL verification (VerifySsl: false)
- 30-second timeout

### Authentication Flow

1. User clicks Login
2. Admin UI → POST `/api/service-auth/token` (via API Gateway)
3. API Gateway → forwards to Tenant Service
4. Tenant Service validates credentials
5. Returns JWT token
6. Admin UI stores token in LocalStorage (encrypted via Web Crypto API)
7. All subsequent API calls include `Authorization: Bearer {token}` header
8. `AuthenticatedHttpMessageHandler` automatically adds header

---

## Related Documentation

- Docker Compose: [../../docker-compose.yml](../../docker-compose.yml)
- API Gateway: [../../src/Services/Sorcha.ApiGateway/](../../src/Services/Sorcha.ApiGateway/)

---

## Next Steps

1. **Blueprint Designer:** Use visual designer to create blueprints
2. **Execution Monitoring:** Monitor blueprint instance execution
3. **Wallet Management:** Create and manage cryptographic wallets
4. **Register Explorer:** Browse distributed ledger transactions
5. **User Management:** Create additional users and organizations

---

## Known Limitations

1. **Development Mode:** Currently configured for local development
2. **HTTP Only:** HTTPS requires certificate configuration
3. **No Persistence:** Browser LocalStorage used for tokens (cleared on logout)
4. **Single Organization:** Bootstrap creates one org, multi-tenant UI not fully implemented

---

**Integration Complete!** You can now access the Sorcha Admin UI at `http://localhost/admin` and manage the platform through a web interface.
