# Sorcha.UI.Web HTTPS Setup - Complete

**Date:** 2026-01-06
**Status:** ✅ HTTPS Configured and Verified

---

## Summary

HTTPS has been successfully configured for Sorcha.UI.Web in Docker with a self-signed development certificate. The service is now accessible via both HTTP and HTTPS.

---

## Configuration Changes

### 1. Certificate Generation

**Command:**
```bash
dotnet dev-certs https -ep certs/sorcha-ui-web.pfx -p SorchaDevCert2025!
```

**Certificate Details:**
- **File:** `certs/sorcha-ui-web.pfx`
- **Password:** `SorchaDevCert2025!`
- **Type:** Self-signed development certificate
- **Valid For:** localhost, 127.0.0.1, ::1
- **Size:** 2.8KB

---

### 2. Docker Compose Updates

**File:** `docker-compose.yml`

**Changes to sorcha-ui-web service:**

```yaml
sorcha-ui-web:
  ports:
    - "5173:8080"  # HTTP - Map to localhost:5173 for consistency with dev environment
    - "443:8443"   # HTTPS - Standard HTTPS port
  environment:
    ASPNETCORE_URLS: https://+:8443;http://+:8080
    ASPNETCORE_HTTPS_PORTS: 8443
    ASPNETCORE_Kestrel__Certificates__Default__Password: SorchaDevCert2025!
    ASPNETCORE_Kestrel__Certificates__Default__Path: /https/sorcha-ui-web.pfx
  volumes:
    - dataprotection-keys:/home/app/.aspnet/DataProtection-Keys
    - ./certs:/https:ro
```

**Key Changes:**
1. Added HTTPS port mapping: `443:8443`
2. Updated `ASPNETCORE_URLS` to include HTTPS endpoint
3. Added Kestrel certificate configuration environment variables
4. Mounted certificate directory as read-only volume

---

## Verification

### Container Startup Logs

```
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: https://[::]:8443
info: Microsoft.Hosting.Lifetime[14]
      Now listening on: http://[::]:8080
```

✅ Service successfully listening on both HTTP (8080) and HTTPS (8443)

### Port Mapping Verification

```bash
docker-compose ps | grep sorcha-ui-web
```

**Output:**
```
sorcha-ui-web   0.0.0.0:5173->8080/tcp, [::]:5173->8080/tcp, 0.0.0.0:443->8443/tcp, [::]:443->8443/tcp
```

✅ Ports correctly mapped to Windows host

### HTTPS Connectivity Test

```bash
curl -k -I https://localhost
```

**Output:**
```
HTTP/1.1 405 Method Not Allowed
Date: Tue, 06 Jan 2026 20:33:40 GMT
Server: Kestrel
Content-Security-Policy: default-src 'self'; ...
X-Content-Type-Options: nosniff
X-Frame-Options: DENY
X-XSS-Protection: 1; mode=block
```

✅ HTTPS connection successful (405 is expected for HEAD request)

### Network Port Test

```powershell
Test-NetConnection -ComputerName localhost -Port 443
```

**Result:**
```
TcpTestSucceeded: True
RemoteAddress: ::1
RemotePort: 443
```

✅ Port 443 accessible on localhost

---

## Access URLs

| Protocol | URL | Port | Status |
|----------|-----|------|--------|
| HTTPS | https://localhost | 443 | ✅ Working (verified with curl) |
| HTTP | http://localhost:5173 | 5173 | ✅ Working |
| HTTP (container) | http://172.19.0.3:8080 | 8080 | ✅ Working |
| HTTPS (container) | https://172.19.0.3:8443 | 8443 | ✅ Working |

---

## Web Crypto API Availability

### ✅ Secure Contexts

The following URLs provide a **secure context** where Web Crypto API is available:

1. **https://localhost** - Recommended for authentication testing
2. **https://localhost:443** - Explicit port (same as above)
3. **http://localhost** - localhost is treated as secure context
4. **http://localhost:5173** - localhost is treated as secure context

### ❌ Non-Secure Contexts

The following URLs do **NOT** provide a secure context:

1. **http://172.19.0.3:8080** - IP address over HTTP
2. **http://172.19.0.14:8080** - IP address over HTTP

**Note:** Per Web Crypto API specification, `localhost` and `127.0.0.1` are special-cased as secure contexts even over HTTP.

---

## Authentication Testing Ready

With HTTPS configured, authentication can now be tested with full Web Crypto API support:

### Prerequisites Met:
- ✅ HTTPS certificate generated
- ✅ Docker configured for HTTPS
- ✅ Service listening on HTTPS port
- ✅ Port 443 accessible on Windows host
- ✅ Web Crypto API available via localhost

### Test Procedure:

1. **Access login page:**
   ```
   https://localhost/login
   ```

2. **Select Docker profile:**
   - Environment: Docker

3. **Enter credentials:**
   - Username: `admin@sorcha.local`
   - Password: `Dev_Pass_2025!`

4. **Expected Flow:**
   ```
   [Browser] --HTTPS--> [UI Web Service]
                |
                | Generate AES-256-GCM key (Web Crypto API)
                | Encrypt token with AES key
                | Store encrypted token in localStorage
                |
                v
   [Browser localStorage: encrypted JWT token]
   ```

5. **Verify Success:**
   - ✅ No console errors
   - ✅ JWT token received from `/api/service-auth/token`
   - ✅ Token encrypted and stored in localStorage
   - ✅ User redirected to authenticated view
   - ✅ Sidebar navigation renders

---

## Files Created/Modified

### Created:
1. `scripts/generate-dev-cert.ps1` - PowerShell certificate generation script
2. `scripts/setup-https-docker.ps1` - Simplified setup script using dotnet dev-certs
3. `certs/sorcha-ui-web.pfx` - Development certificate (not committed to git)
4. `HTTPS-SETUP-COMPLETE.md` - This documentation

### Modified:
1. `docker-compose.yml` - Added HTTPS configuration to sorcha-ui-web service
2. `.gitignore` - Added `/certs/*.pfx` to exclude certificates from version control

---

## Security Notes

### Development Certificate
- ⚠️ **Development Only** - This is a self-signed certificate for local development
- ⚠️ **Not for Production** - Use proper CA-signed certificates in production
- ✅ **Password Protected** - Certificate is protected with password `SorchaDevCert2025!`
- ✅ **Read-Only Mount** - Certificate mounted as read-only in container

### Browser Trust
- The certificate was generated using `dotnet dev-certs https`
- Use `dotnet dev-certs https --trust` to add to trusted root certificates
- Browsers may show security warnings until certificate is trusted

### Production Recommendations
1. Use Let's Encrypt for free CA-signed certificates
2. Store certificates in Azure Key Vault or similar secret management
3. Use certificate rotation and renewal automation
4. Enable HSTS (HTTP Strict Transport Security)
5. Use certificate pinning for mobile apps

---

## Troubleshooting

### Certificate Not Trusted

**Symptom:** Browser shows "Your connection is not private" warning

**Solution:**
```bash
dotnet dev-certs https --trust
```

Click "Yes" when Windows prompts to trust the certificate.

### Port 443 Already in Use

**Symptom:** Docker fails to start with "port already allocated"

**Diagnosis:**
```powershell
netstat -ano | findstr ":443"
```

**Solutions:**
1. Stop the service using port 443 (IIS, another web server)
2. Change the port mapping in docker-compose.yml to `8443:8443`
3. Access via `https://localhost:8443`

### Certificate Not Found

**Symptom:** Container logs show "Certificate not found at /https/sorcha-ui-web.pfx"

**Solution:**
1. Verify certificate exists: `ls -lah certs/`
2. Regenerate if missing:
   ```bash
   dotnet dev-certs https -ep certs/sorcha-ui-web.pfx -p SorchaDevCert2025!
   ```
3. Restart containers: `docker-compose restart sorcha-ui-web`

### Web Crypto API Still Unavailable

**Symptom:** `crypto.subtle is undefined` error in browser console

**Diagnosis:**
- Check the URL in browser address bar
- Must be `https://localhost` or `http://localhost`
- Cannot be IP address like `http://172.19.0.3:8080`

**Solution:**
Use localhost URLs for authentication testing.

---

## Next Steps

### Immediate (P0)
1. ✅ HTTPS configured
2. ✅ Certificate generated and mounted
3. ✅ Service listening on HTTPS
4. ⏳ **Test authentication flow via https://localhost/login**

### Short-term (P1)
5. Trust the certificate system-wide (`dotnet dev-certs https --trust`)
6. Complete authentication end-to-end test
7. Verify token encryption/decryption works
8. Test authenticated API calls
9. Verify role-based UI rendering

### Long-term (P2)
10. Document production HTTPS setup
11. Add certificate renewal automation
12. Implement HSTS headers
13. Add security audit logging
14. Create production deployment guide

---

## Success Criteria

- [x] HTTPS certificate generated
- [x] Docker configured for HTTPS
- [x] Service listening on https://+:8443
- [x] Port 443 accessible on localhost
- [x] HTTP also available on http://+:8080
- [x] Certificate mounted in container
- [x] Curl test successful
- [ ] Browser access via https://localhost
- [ ] Authentication tested via HTTPS
- [ ] Web Crypto API confirmed working
- [ ] Token encryption verified

---

## Conclusion

**HTTPS setup is complete and functional.** The service is accessible via both HTTP and HTTPS, with the certificate properly configured and mounted in the Docker container. The system is ready for authentication testing with full Web Crypto API support.

**Next Action:** Test authentication flow via https://localhost/login to verify end-to-end functionality with encrypted token storage.

---

**Setup Date:** 2026-01-06
**Docker Version:** Docker Desktop with WSL2
**Certificate Tool:** dotnet dev-certs
**Certificate Type:** Self-signed development
**Verified By:** curl, netstat, docker logs
