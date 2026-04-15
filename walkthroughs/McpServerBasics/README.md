# MCP Server Basics

**Purpose:** Authenticate with Sorcha and use the MCP Server to check platform health using AI assistant tools
**Date Created:** 2026-01-29
**Status:** ✅ Complete
**Prerequisites:** Docker Desktop, PowerShell or Bash

---

## Overview

This walkthrough demonstrates how to:
1. Start the Sorcha platform in Docker
2. Authenticate and obtain a JWT token
3. Run the MCP Server with the token
4. Use MCP tools to interact with Sorcha (platform health check example)

The MCP (Model Context Protocol) Server enables AI assistants like Claude Desktop to interact with the Sorcha platform through a standardized set of tools organized by user role.

---

## Files in This Walkthrough

- **get-token-and-run-mcp.ps1** - PowerShell script to get JWT token and run MCP server (Windows)
- **get-token-and-run-mcp.sh** - Bash script for Linux/Mac users
- **test-mcp-server.ps1** - Quick test script to verify MCP server setup
- **RESULTS.md** - Walkthrough results and findings

---

## Quick Start

### Windows (PowerShell)

```powershell
# Navigate to repository root
cd c:\projects\Sorcha

# Run the walkthrough script
.\walkthroughs\McpServerBasics\get-token-and-run-mcp.ps1
```

### Linux/Mac (Bash)

```bash
# Navigate to repository root
cd /path/to/Sorcha

# Make script executable
chmod +x walkthroughs/McpServerBasics/get-token-and-run-mcp.sh

# Run the walkthrough script
./walkthroughs/McpServerBasics/get-token-and-run-mcp.sh
```

### Manual Steps

If you prefer to run commands manually:

#### 1. Start Sorcha Services
```bash
docker-compose up -d
```

#### 2. Wait for Services (30 seconds)
```bash
# Check service status
docker-compose ps
```

#### 3. Get JWT Token

**PowerShell:**
```powershell
$response = Invoke-RestMethod -Uri "http://localhost:5450/api/auth/login" `
  -Method POST `
  -ContentType "application/json" `
  -Body '{"email":"admin@sorcha.local","password":"Dev_Pass_2025\!"}'

$token = $response.accessToken
Write-Host "Token: $token"
```

**Bash/curl:**
```bash
TOKEN=$(curl -s -X POST http://localhost:5450/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@sorcha.local","password":"Dev_Pass_2025\!"}' \
  | jq -r '.accessToken')

echo "Token: $TOKEN"
```

#### 4. Run MCP Server

**PowerShell:**
```powershell
docker-compose run --rm mcp-server --jwt-token $token
```

**Bash:**
```bash
docker-compose run --rm mcp-server --jwt-token $TOKEN
```

---

## What Happens

### 1. Service Startup
The script starts the following Sorcha services:
- **postgres** - PostgreSQL database
- **mongodb** - MongoDB document store
- **redis** - Redis cache
- **tenant-service** - Authentication & JWT issuer
- **blueprint-service** - Workflow management
- **register-service** - Distributed ledger
- **wallet-service** - Cryptographic operations
- **validator-service** - Transaction validation
- **peer-service** - P2P networking
- **api-gateway** - YARP reverse proxy

### 2. Authentication
- Logs in using default admin credentials
- Email: `admin@sorcha.local`
- Password: `Dev_Pass_2025\!`
- Receives JWT token with roles: `sorcha:admin`, `sorcha:designer`, `sorcha:participant`

### 3. MCP Server Launch
The MCP Server starts with:
- JWT token authentication
- Role-based tool access
- Connection to all backend services via Docker network
- stdio transport for AI assistant communication

### 4. Available Tools (Based on Admin Role)

The admin user has access to **all 36 MCP tools** across three categories:

**Administrator Tools (13 tools):**
- `get_platform_health` - Check overall platform status
- `get_service_health` - Check individual service health
- `get_platform_logs` - View system logs
- `get_platform_metrics` - View performance metrics
- `list_tenants` - List all tenants
- `create_tenant` - Create new tenant
- `list_users` - List users in organization
- `create_user` - Create new user
- And 5 more admin tools...

**Designer Tools (13 tools):**
- `create_blueprint` - Create workflow blueprints
- `validate_blueprint` - Validate blueprint schemas
- `list_blueprints` - List all blueprints
- `simulate_blueprint` - Test blueprint execution
- And 9 more designer tools...

**Participant Tools (10 tools):**
- `get_action_inbox` - View assigned actions
- `submit_action` - Complete workflow actions
- `query_register` - Search ledger data
- `create_wallet` - Create crypto wallet
- And 6 more participant tools...

---

## Example Usage Scenarios

### Scenario 1: Check Platform Health (Admin)

When the MCP server is running, an AI assistant can use:
```
Tool: get_platform_health
```

This returns:
- Overall platform status (healthy/degraded/unhealthy)
- Individual service health checks
- Total counts (blueprints, wallets, registers, etc.)
- Connected peer count

### Scenario 2: Create a Blueprint (Designer)

```
Tool: create_blueprint
Input: {
  "title": "Purchase Order Approval",
  "participants": ["buyer", "approver"],
  "actions": [...]
}
```

### Scenario 3: View Action Inbox (Participant)

```
Tool: get_action_inbox
```

Returns pending actions assigned to the authenticated user.

---

## Default Credentials

The Tenant Service creates these accounts on first startup:

### Admin User
- **Email:** `admin@sorcha.local`
- **Password:** `Dev_Pass_2025\!`
- **Organization:** `Sorcha Local`
- **Roles:** `sorcha:admin`, `sorcha:designer`, `sorcha:participant`

### Organization
- **ID:** Auto-generated GUID
- **Name:** `Sorcha Local`
- **Type:** Default

---

## Key Results

After running this walkthrough, you will have:

✅ Sorcha platform running in Docker
✅ JWT token for authenticated access
✅ MCP Server running and connected to backend services
✅ Understanding of role-based tool access
✅ Ability to use AI assistants with Sorcha via MCP protocol

---

## Access Points

After starting services:

| Service | URL | Notes |
|---------|-----|-------|
| API Gateway | http://localhost | Main entry point |
| Aspire Dashboard | http://localhost:18888 | Service monitoring |
| Tenant Service | http://localhost:5450 | Direct access for auth |
| Gateway Status | http://localhost/gateway | System overview |

---

## Token Information

The JWT token contains:
```json
{
  "sub": "user-guid",
  "email": "admin@sorcha.local",
  "name": "Administrator",
  "organizationId": "org-guid",
  "organizationName": "Sorcha Local",
  "roles": [
    "sorcha:admin",
    "sorcha:designer",
    "sorcha:participant"
  ],
  "exp": 1738195200,
  "iss": "https://tenant.sorcha.io",
  "aud": "https://api.sorcha.io"
}
```

**Token Lifetime:** 24 hours (configurable)

---

## Integration with Claude Desktop

To use the MCP server with Claude Desktop, configure your `claude_desktop_config.json`:

### Option 1: Manual Token Update
```json
{
  "mcpServers": {
    "sorcha": {
      "command": "docker-compose",
      "args": [
        "-f", "C:/projects/Sorcha/docker-compose.yml",
        "run", "--rm", "mcp-server",
        "--jwt-token", "paste-token-here"
      ],
      "cwd": "C:/projects/Sorcha"
    }
  }
}
```

### Option 2: Environment Variable
```json
{
  "mcpServers": {
    "sorcha": {
      "command": "pwsh",
      "args": [
        "-File", "C:/projects/Sorcha/walkthroughs/McpServerBasics/get-token-and-run-mcp.ps1",
        "-AutoRun"
      ],
      "cwd": "C:/projects/Sorcha"
    }
  }
}
```

---

## Troubleshooting

### "Connection refused" on tenant service
**Cause:** Services not fully started
**Solution:** Wait 30 seconds after `docker-compose up -d`, then retry

### "Invalid credentials"
**Cause:** Incorrect email or password
**Solution:** Ensure exact credentials:
- Email: `admin@sorcha.local` (note `.local`, not `.com`)
- Password: `Dev_Pass_2025\!` (case-sensitive, includes `!`)

### "JWT token is required"
**Cause:** Token not provided to MCP server
**Solution:** Verify token is passed via `--jwt-token` or `SORCHA_JWT_TOKEN` env var

### "Token expired"
**Cause:** JWT expired (24-hour lifetime)
**Solution:** Login again to get a fresh token

### "No tools available"
**Cause:** Invalid token or missing roles
**Solution:** Verify token contains proper role claims (`sorcha:admin`, etc.)

### Services not starting
**Cause:** Docker not running or resource limits
**Solution:**
1. Verify Docker Desktop is running
2. Check `docker-compose ps` for service status
3. View logs: `docker-compose logs -f <service-name>`

---

## Next Steps

After completing this walkthrough:

1. **Explore MCP Tools:** Try different tools based on your role
2. **Create Blueprints:** Use designer tools to create workflows
3. **Setup Participants:** Add users and assign them to workflows
4. **Test Wallets:** Create and manage cryptographic wallets
5. **Query Ledger:** Search and analyze register data

### Related Walkthroughs

- [RegisterCreationFlow](../RegisterCreationFlow/) - Work with the distributed ledger
- [ConstructionPermit](../ConstructionPermit/) - Multi-org workflow with encrypted fields

---

## Known Limitations

- Token must be manually refreshed every 24 hours
- MCP server requires all backend services to be running
- stdio transport means one client connection at a time
- Windows requires PowerShell 7+ for optimal script compatibility

---

## Security Notes

⚠️ **Development Mode Only**

The default credentials and configuration are for **development/testing only**:
- Default password is well-known
- JWT validation is relaxed in development
- HTTPS is disabled for local testing

For production:
- Change all default passwords
- Use Azure Key Vault or similar for secrets
- Enable HTTPS with valid certificates
- Configure proper JWT signing keys
- Use environment-specific configuration

---

**Version:** 1.0
**Last Updated:** 2026-01-29
**Tested With:** Docker Desktop 4.36, .NET 10.0, Sorcha v2.5
