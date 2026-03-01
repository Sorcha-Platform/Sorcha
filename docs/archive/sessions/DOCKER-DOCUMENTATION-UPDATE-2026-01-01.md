# Docker Documentation Update - 2026-01-01

**Summary of documentation improvements for Docker deployment**

---

## Overview

Updated Sorcha documentation to clearly describe Docker deployment requirements, SSL certificate setup, and service access URLs. This addresses the common issue of missing HTTPS certificates preventing the API Gateway from starting.

---

## Changes Made

### 1. Main README.md

**Updated Section:** "Option 3: Using Docker Compose (Production-Like)"

**Changes:**
- ✅ Added **Prerequisites** section with SSL certificate generation instructions
- ✅ Added step-by-step certificate generation commands
- ✅ Reorganized **Access Points** into clear categories:
  - Primary access points (API Gateway, landing page, docs)
  - Infrastructure services (PostgreSQL, MongoDB, Redis)
  - P2P gRPC endpoints (Hub, Peer)
- ✅ Added detailed networking information
- ✅ Clarified that backend services are not directly exposed
- ✅ Added link to comprehensive Docker quick-start guide

**Key Addition - Certificate Generation:**
```bash
mkdir -p docker/certs
dotnet dev-certs https -ep docker/certs/aspnetapp.pfx -p SorchaDev2025 --trust
```

### 2. DEPLOYMENT.md

**Updated Sections:**
- Docker Deployment prerequisites
- HTTPS Certificate Setup (new section)
- Access Services (expanded)
- Troubleshooting (enhanced)

**New Content:**
- ✅ **HTTPS Certificate Setup (Required)** - Dedicated section explaining:
  - How to generate development certificates
  - Certificate path and password details
  - Production certificate configuration
  - Verification steps

- ✅ **Enhanced Access Services** - Reorganized into tables:
  - Primary access points with descriptions
  - Infrastructure services with credentials
  - P2P gRPC services with purposes
  - Backend services (internal only) with routing information

- ✅ **Enhanced Troubleshooting** - New section covering:
  - Missing HTTPS certificate (most common issue)
  - Certificate verification commands
  - Service startup issues
  - Port conflicts
  - Permission errors
  - Status checking commands

### 3. New Document: DOCKER-QUICK-START.md

**Created:** `docs/DOCKER-QUICK-START.md`

**Purpose:** Comprehensive quick-start guide for Docker deployment

**Contents:**
1. **Prerequisites** - Required software and tools
2. **Setup Steps** - 4-step deployment process:
   - Clone repository
   - Generate HTTPS certificate (with clear warning)
   - Start services
   - Verify deployment

3. **Access Points** - Complete reference tables:
   - Main entry points
   - Infrastructure services
   - P2P network endpoints

4. **Common Operations**:
   - View logs
   - Check service status
   - Restart services
   - Stop services

5. **Troubleshooting**:
   - Certificate missing error
   - Port already in use
   - Redis connection errors
   - Service won't start
   - Verify internal networking

6. **Testing the Deployment**:
   - API Gateway endpoints
   - Backend service routing
   - gRPC services

7. **Data Persistence**:
   - Volume management
   - Backup procedures
   - Restore procedures

8. **Network Architecture** - Brief overview with link to detailed doc

9. **Next Steps**:
   - Development workflow
   - Production deployment considerations

---

## Key Improvements

### Problem Solved

**Issue:** API Gateway fails to start with error about missing `aspnetapp.pfx` certificate.

**Root Cause:** HTTPS certificate not generated before starting Docker Compose.

**Solution Documented:**
1. Clear instructions to generate certificate BEFORE starting services
2. Exact commands with correct password
3. Verification steps
4. Troubleshooting section if certificate is missing

### Documentation Structure

**Before:**
- SSL certificate requirement was implicit
- No clear access URL reference
- Limited troubleshooting information

**After:**
- ✅ SSL certificate is first prerequisite
- ✅ Complete access URL reference with descriptions
- ✅ Comprehensive troubleshooting guide
- ✅ Dedicated quick-start document
- ✅ Clear production vs. development certificate guidance

### User Experience

**Improvements:**
1. **Preventive**: Users generate certificate BEFORE starting (prevents errors)
2. **Clear Access**: Organized tables show all access points and credentials
3. **Troubleshooting**: Dedicated section for common issues with solutions
4. **Reference**: Quick-start guide provides complete deployment walkthrough
5. **Production Ready**: Guidance for moving from development to production certificates

---

## Files Modified

| File | Changes | Status |
|------|---------|--------|
| `README.md` | Added SSL prerequisites, reorganized access points | ✅ Updated |
| `DEPLOYMENT.md` | Added certificate setup, enhanced troubleshooting | ✅ Updated |
| `docs/DOCKER-QUICK-START.md` | Created comprehensive quick-start guide | ✅ New |
| `docs/DOCKER-DOCUMENTATION-UPDATE-2026-01-01.md` | This summary document | ✅ New |

---

## Access URLs - Quick Reference

### Development (Docker Compose)

**Main Access:**
- Landing Page: `http://localhost/`
- API Docs: `http://localhost/scalar/`
- Health Check: `http://localhost/api/health`
- Dashboard Stats: `http://localhost/api/dashboard`
- Aspire Dashboard: `http://localhost:18888`

**Infrastructure:**
- PostgreSQL: `localhost:5432` (user: `sorcha`, password: `sorcha_dev_password`)
- MongoDB: `localhost:27017` (user: `sorcha`, password: `sorcha_dev_password`)
- Redis: `localhost:6379` (no auth)

**P2P Network:**
- Hub Node (gRPC): `localhost:50051`
- Peer Service (gRPC): `localhost:50052`

**Backend Services (via API Gateway):**
- Blueprint: `http://localhost/api/blueprints/...`
- Wallet: `http://localhost/api/wallets/...`
- Register: `http://localhost/api/register/...`
- Tenant: `http://localhost/api/tenants/...`
- Validator: `http://localhost/api/validator/...`

---

## Certificate Generation - Quick Reference

### Development Certificate

```bash
# Create directory
mkdir -p docker/certs

# Generate certificate
dotnet dev-certs https -ep docker/certs/aspnetapp.pfx -p SorchaDev2025 --trust

# Verify
ls -la docker/certs/aspnetapp.pfx
```

**Certificate Details:**
- **Path**: `docker/certs/aspnetapp.pfx`
- **Password**: `SorchaDev2025`
- **Type**: Self-signed development certificate
- **Valid For**: Local development and testing

### Production Certificate

For production, use CA-signed certificates:

```yaml
# docker-compose.yml (production)
environment:
  ASPNETCORE_Kestrel__Certificates__Default__Path: /https/production.pfx
  ASPNETCORE_Kestrel__Certificates__Default__Password: ${CERT_PASSWORD}
volumes:
  - /path/to/production/certs:/https:ro
```

---

## Networking Architecture

**Pattern**: Bridge networking with centralized API Gateway

**Characteristics:**
- Single bridge network (`sorcha-network`)
- Internal service communication via Docker DNS
- External access via published ports only
- API Gateway provides HTTP/HTTPS ingress
- Direct gRPC for P2P communication
- Backend services are isolated (no direct external access)

**See:** [DOCKER-BRIDGE-NETWORKING.md](DOCKER-BRIDGE-NETWORKING.md) for complete details

---

## Testing Deployment

### Verify Certificate

```bash
# Check file exists
ls -la docker/certs/aspnetapp.pfx

# Inspect certificate (optional)
openssl pkcs12 -info -in docker/certs/aspnetapp.pfx -nodes -passin pass:SorchaDev2025
```

### Verify Services

```bash
# Start services
docker-compose up -d

# Check status
docker-compose ps

# Test API Gateway
curl http://localhost/api/health

# View dashboard
open http://localhost/
```

### Verify Internal Networking

```bash
# Test API Gateway to backend services
docker exec sorcha-api-gateway curl http://wallet-service:8080/health
docker exec sorcha-api-gateway curl http://blueprint-service:8080/health

# Test peer-to-hub connection
docker logs sorcha-peer-service | grep "Successfully connected"
```

---

## Next Steps

### For Users

1. **First Time Setup**:
   - Follow [DOCKER-QUICK-START.md](DOCKER-QUICK-START.md)
   - Generate certificate before starting services
   - Access landing page at `http://localhost/`

2. **Ongoing Development**:
   - Use `docker-compose logs -f` to monitor services
   - Access Aspire Dashboard for observability
   - Use Scalar API docs for testing endpoints

3. **Production Deployment**:
   - Review [DEPLOYMENT.md](../DEPLOYMENT.md)
   - Replace development certificates
   - Configure production secrets
   - Review [DOCKER-BRIDGE-NETWORKING.md](DOCKER-BRIDGE-NETWORKING.md)

### For Documentation Maintainers

1. **Keep Updated**:
   - Update access URLs if ports change
   - Update certificate instructions if process changes
   - Add new troubleshooting entries as issues are discovered

2. **Cross-Reference**:
   - README.md → DOCKER-QUICK-START.md → DEPLOYMENT.md → DOCKER-BRIDGE-NETWORKING.md
   - Ensure consistency across all documentation

3. **User Feedback**:
   - Monitor GitHub issues for documentation gaps
   - Add common questions to troubleshooting sections

---

## Summary

**Problem:** Users couldn't start Docker instance due to missing SSL certificate.

**Solution:**
- Documented certificate requirement prominently
- Provided exact generation commands
- Added comprehensive access URL reference
- Created dedicated quick-start guide

**Result:**
- Clear, step-by-step deployment process
- All access points documented with credentials
- Comprehensive troubleshooting guide
- Production-ready documentation

**Status:** ✅ Complete

---

**Created:** 2026-01-01
**Author:** Claude Sonnet 4.5
**Related Documents:**
- [README.md](../README.md#option-3-using-docker-compose-production-like)
- [DEPLOYMENT.md](../DEPLOYMENT.md#docker-deployment)
- [DOCKER-QUICK-START.md](DOCKER-QUICK-START.md)
- [DOCKER-BRIDGE-NETWORKING.md](DOCKER-BRIDGE-NETWORKING.md)
