# Azure Custom Domain Setup for n0.sorcha.dev

**Guide for configuring the Peer Service (Hub Node) with custom domain**

---

## Overview

The Azure Peer Service is deployed as n0.sorcha.dev (the primary hub node). This document explains how to configure the custom domain and DNS records.

---

## Peer Service Configuration

The Peer Service knows it's n0.sorcha.dev through these environment variables:

```bicep
{
  name: 'PeerService__NodeId'
  value: 'n0.sorcha.dev'
}
{
  name: 'PeerService__PublicAddress'
  value: peerService.properties.configuration.ingress.fqdn
}
{
  name: 'PeerService__CentralNode__IsCentralNode'
  value: 'true'
}
{
  name: 'PeerService__CentralNode__ValidateHostname'
  value: 'false'  // Set to 'true' only after DNS is configured
}
{
  name: 'PeerService__CentralNode__Priority'
  value: '0'  // Primary hub node
}
```

---

## Step 1: Get Azure Container App Default URL

First, get the default Azure Container Apps URL:

```bash
# Get the peer service FQDN
az containerapp show \
  --name peer-service \
  --resource-group sorcha \
  --query properties.configuration.ingress.fqdn \
  --output tsv
```

**Example output:**
```
peer-service.nicegrass-12345678.uksouth.azurecontainerapps.io
```

---

## Step 2: Add Custom Domain to Container App

### Prerequisites
- You own the domain `sorcha.dev`
- You have access to DNS management for `sorcha.dev`

### Add Custom Domain

```bash
# Add custom domain (requires TXT record for verification)
az containerapp hostname add \
  --hostname n0.sorcha.dev \
  --resource-group sorcha \
  --name peer-service
```

**This will prompt you to add a TXT record for domain verification.**

---

## Step 3: DNS Configuration

### Option A: Direct CNAME (Recommended for Azure)

Add a CNAME record in your DNS provider:

```
Type:  CNAME
Name:  n0
Value: peer-service.nicegrass-12345678.uksouth.azurecontainerapps.io
TTL:   300 (or 3600)
```

### Option B: A Record (If CNAME not supported at apex)

1. Get the Container App Environment static IP:

```bash
# Get environment static IP
az containerapp env show \
  --name sorcha-env-prod \
  --resource-group sorcha \
  --query properties.staticIp \
  --output tsv
```

2. Add A record:

```
Type:  A
Name:  n0
Value: <static-ip-address>
TTL:   300
```

### TXT Record for Verification

Azure requires domain verification. Add this TXT record:

```bash
# Get verification code
az containerapp hostname list \
  --name peer-service \
  --resource-group sorcha \
  --query "[?name=='n0.sorcha.dev'].bindingType" \
  --output tsv
```

Add TXT record:
```
Type:  TXT
Name:  asuid.n0
Value: <verification-code-from-azure>
TTL:   300
```

---

## Step 4: Bind Certificate

Azure Container Apps supports managed certificates (free):

```bash
# Add managed certificate (Let's Encrypt)
az containerapp hostname bind \
  --hostname n0.sorcha.dev \
  --resource-group sorcha \
  --name peer-service \
  --environment sorcha-env-prod \
  --validation-method CNAME
```

**This automatically provisions and renews SSL/TLS certificates.**

---

## Step 5: Verify Configuration

### Test DNS Resolution

```bash
# Check DNS propagation
nslookup n0.sorcha.dev

# Check with dig (Linux/Mac)
dig n0.sorcha.dev

# Check with PowerShell
Resolve-DnsName n0.sorcha.dev
```

**Expected result:** Should resolve to Azure Container Apps IP or CNAME

### Test HTTPS Access

```bash
# Test HTTPS endpoint
curl https://n0.sorcha.dev/health

# Test gRPC endpoint (if applicable)
grpcurl n0.sorcha.dev:443 list
```

### Check Container App Logs

```bash
az containerapp logs show \
  --name peer-service \
  --resource-group sorcha \
  --tail 50 | grep -i "NodeId\|n0.sorcha.dev\|central"
```

**Expected log messages:**
```
[INF] Node type detected: Hub Node
[INF] NodeId: n0.sorcha.dev
[INF] Hostname: n0.sorcha.dev
[INF] Node validated as hub node
```

---

## Step 6: Enable Hostname Validation (Production)

Once DNS is working, enable hostname validation for added security:

```bash
# Update Container App environment variable
az containerapp update \
  --name peer-service \
  --resource-group sorcha \
  --set-env-vars "PeerService__CentralNode__ValidateHostname=true"

# Restart to apply
az containerapp restart \
  --name peer-service \
  --resource-group sorcha
```

**What this does:**
- Validates that the container's hostname matches the expected pattern (`*.sorcha.dev`)
- Prevents accidental misconfiguration
- Ensures the node is running in the correct environment

---

## Complete DNS Configuration for Sorcha

For a complete production setup with all hub nodes:

| Hostname | Type | Value | Purpose |
|----------|------|-------|---------|
| `n0.sorcha.dev` | CNAME | `peer-service.*.uksouth.azurecontainerapps.io` | Primary hub node (Azure UK South) |
| `n1.sorcha.dev` | CNAME | `peer-service.*.eastus.azurecontainerapps.io` | Secondary hub node (Azure East US) |
| `n2.sorcha.dev` | CNAME | `peer-service.*.westeurope.azurecontainerapps.io` | Tertiary hub node (Azure West Europe) |
| `asuid.n0` | TXT | `<verification-code>` | Domain verification (Azure) |
| `asuid.n1` | TXT | `<verification-code>` | Domain verification (Azure) |
| `asuid.n2` | TXT | `<verification-code>` | Domain verification (Azure) |

**Note:** Currently only n0 is deployed. n1 and n2 are reserved for future multi-region deployment.

---

## Troubleshooting

### Problem: DNS Not Resolving

**Check:**
1. DNS records are correctly added
2. TTL has expired (wait 5-10 minutes)
3. Use a different DNS server: `nslookup n0.sorcha.dev 8.8.8.8`

**Solution:**
```bash
# Force DNS cache flush (Windows)
ipconfig /flushdns

# Force DNS cache flush (Linux/Mac)
sudo systemd-resolve --flush-caches
```

### Problem: Certificate Not Provisioning

**Check:**
```bash
az containerapp hostname list \
  --name peer-service \
  --resource-group sorcha \
  --output table
```

**Common issues:**
- TXT record not added or not propagated
- CNAME/A record not pointing to correct target
- Domain verification pending

**Solution:**
```bash
# Remove and re-add hostname
az containerapp hostname delete \
  --hostname n0.sorcha.dev \
  --resource-group sorcha \
  --name peer-service

# Wait 5 minutes for DNS propagation

# Re-add with managed cert
az containerapp hostname bind \
  --hostname n0.sorcha.dev \
  --resource-group sorcha \
  --name peer-service \
  --environment sorcha-env-prod \
  --validation-method CNAME
```

### Problem: "Hostname validation failed"

**Symptoms:** Peer Service logs show:
```
InvalidOperationException: IsCentralNode is true but hostname 'xyz' does not match expected pattern '*.sorcha.dev'
```

**Solution:**
```bash
# Temporarily disable validation
az containerapp update \
  --name peer-service \
  --resource-group sorcha \
  --set-env-vars "PeerService__CentralNode__ValidateHostname=false"

# Verify DNS is working
curl https://n0.sorcha.dev/health

# Re-enable after DNS confirmed
az containerapp update \
  --name peer-service \
  --resource-group sorcha \
  --set-env-vars "PeerService__CentralNode__ValidateHostname=true"
```

### Problem: Mixed HTTP/HTTPS Traffic

**Check Container App ingress settings:**
```bash
az containerapp ingress show \
  --name peer-service \
  --resource-group sorcha
```

**Ensure HTTPS only:**
```bash
az containerapp ingress update \
  --name peer-service \
  --resource-group sorcha \
  --allow-insecure false \
  --type external
```

---

## Configuration Summary

### Current Configuration (Development)

```json
{
  "PeerService": {
    "NodeId": "n0.sorcha.dev",
    "PublicAddress": "peer-service.nicegrass-12345678.uksouth.azurecontainerapps.io",
    "Port": 5000,
    "CentralNode": {
      "IsCentralNode": true,
      "ExpectedHostnamePattern": "*.sorcha.dev",
      "ValidateHostname": false,
      "Priority": 0
    }
  }
}
```

### Production Configuration (After DNS Setup)

```json
{
  "PeerService": {
    "NodeId": "n0.sorcha.dev",
    "PublicAddress": "n0.sorcha.dev",
    "Port": 5000,
    "CentralNode": {
      "IsCentralNode": true,
      "ExpectedHostnamePattern": "*.sorcha.dev",
      "ValidateHostname": true,  // ✅ Enabled for security
      "Priority": 0
    }
  }
}
```

---

## Automation Script

Save this as `scripts/configure-peer-domain.ps1`:

```powershell
param(
    [Parameter(Mandatory=$true)]
    [string]$Hostname = "n0.sorcha.dev",

    [Parameter(Mandatory=$false)]
    [string]$ResourceGroup = "sorcha",

    [Parameter(Mandatory=$false)]
    [string]$ServiceName = "peer-service"
)

Write-Host "Configuring custom domain for Peer Service..." -ForegroundColor Cyan

# Get current FQDN
$currentFqdn = az containerapp show `
    --name $ServiceName `
    --resource-group $ResourceGroup `
    --query properties.configuration.ingress.fqdn `
    --output tsv

Write-Host "Current FQDN: $currentFqdn" -ForegroundColor Yellow

# Add custom hostname
Write-Host "Adding custom hostname: $Hostname" -ForegroundColor Cyan
az containerapp hostname add `
    --hostname $Hostname `
    --resource-group $ResourceGroup `
    --name $ServiceName

Write-Host ""
Write-Host "Next Steps:" -ForegroundColor Green
Write-Host "1. Add DNS CNAME record:" -ForegroundColor White
Write-Host "   Type: CNAME" -ForegroundColor Gray
Write-Host "   Name: n0" -ForegroundColor Gray
Write-Host "   Value: $currentFqdn" -ForegroundColor Gray
Write-Host ""
Write-Host "2. Add TXT record for verification (check Azure Portal)" -ForegroundColor White
Write-Host ""
Write-Host "3. After DNS propagates, bind certificate:" -ForegroundColor White
Write-Host "   az containerapp hostname bind --hostname $Hostname --resource-group $ResourceGroup --name $ServiceName --environment sorcha-env-prod --validation-method CNAME" -ForegroundColor Gray
```

---

## Security Considerations

### HTTPS Only
- Always use HTTPS for custom domains
- Managed certificates auto-renew
- No manual certificate management needed

### Hostname Validation
- Enable after DNS is confirmed working
- Prevents accidental deployment to wrong environment
- Protects against DNS hijacking

### Firewall Rules
- Consider Azure Front Door or Application Gateway for additional security
- Implement rate limiting for gRPC endpoints
- Use Azure WAF for DDoS protection

---

## Cost Impact

- **Custom Domain:** Free (included with Container Apps)
- **Managed Certificate:** Free (Let's Encrypt via Azure)
- **DNS:** Depends on provider (~$1-5/month)
- **SSL/TLS Traffic:** No additional cost

---

## Next Steps

1. ✅ Configure DNS CNAME record for n0.sorcha.dev
2. ✅ Add TXT record for domain verification
3. ✅ Bind managed certificate
4. ✅ Test HTTPS access
5. ✅ Enable hostname validation
6. ✅ Update other peer nodes to connect to n0.sorcha.dev

---

**Questions?** See [AZURE-DATABASE-INITIALIZATION.md](AZURE-DATABASE-INITIALIZATION.md) for related Azure setup.
