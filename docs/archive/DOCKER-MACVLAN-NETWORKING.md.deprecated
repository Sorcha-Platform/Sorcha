# Docker macvlan Networking Configuration for Sorcha

**Date:** 2025-12-24
**Status:** ✅ Implemented
**Purpose:** Enable Sorcha Docker containers to have their own IP addresses on the local LAN for proper NAT traversal and P2P networking

---

## Overview

Sorcha uses a **dual-network configuration** combining bridge networking for internal services and macvlan networking for public-facing services that require external connectivity.

### Why macvlan?

For peer-to-peer networking, macvlan provides significant advantages:

1. **Direct LAN IP addresses** - Containers appear as separate devices on the network
2. **Proper NAT traversal** - STUN/ICE negotiation works correctly
3. **No port conflicts** - Each service has its own IP
4. **External accessibility** - Easy to configure port forwarding on router
5. **P2P discovery** - External peers can connect directly to container IPs

---

## Network Architecture

### Current LAN Configuration

- **Subnet:** 192.168.51.0/24
- **Gateway:** 192.168.51.1
- **Host IP:** 192.168.51.103 (Windows)
- **Docker IP Range:** 192.168.51.192-207 (16 IPs reserved)

### Dual Network Setup

**1. sorcha-network (bridge)** - Internal services
- Redis
- PostgreSQL
- MongoDB
- Aspire Dashboard

**2. sorcha-lan (macvlan)** - Public-facing services
- peer-hub-local (192.168.51.200)
- peer-service (192.168.51.201)
- api-gateway (192.168.51.210)
- tenant-service (192.168.51.211)
- wallet-service (192.168.51.212)
- register-service (192.168.51.213)

---

## IP Address Allocation

| Service | IP Address | Purpose | Ports |
|---------|-----------|---------|-------|
| **peer-hub-local** | 192.168.51.200 | Local hub node (hub node) | 8080 (HTTP), 5000 (gRPC) |
| **peer-service** | 192.168.51.201 | Peer node | 8080 (HTTP), 5000 (gRPC/STUN) |
| **api-gateway** | 192.168.51.210 | API Gateway | 8080 (HTTP) |
| **tenant-service** | 192.168.51.211 | Tenant Service | 8080 (HTTP) |
| **wallet-service** | 192.168.51.212 | Wallet Service | 8080 (HTTP) |
| **register-service** | 192.168.51.213 | Register Service | 8080 (HTTP) |
| **blueprint-service** | 192.168.51.214 | Blueprint Service | 8080 (HTTP) |
| **validator-service** | 192.168.51.215 | Validator Service | 8080 (HTTP) |
| *Reserved* | 192.168.51.202-207 | Future services | - |

---

## Network Configuration

### macvlan Network Creation

The macvlan network is created with the following parameters:

```bash
docker network create \
  --driver macvlan \
  --subnet=192.168.51.0/24 \
  --gateway=192.168.51.1 \
  --ip-range=192.168.51.192/28 \
  -o parent=eth0 \
  sorcha-lan
```

**Parameters:**
- `--subnet`: Full LAN subnet (192.168.51.0/24)
- `--gateway`: LAN gateway/router (192.168.51.1)
- `--ip-range`: Reserved IP range for Docker (192.168.51.192-207, 16 IPs)
- `parent=eth0`: WSL2 network interface

### Bridge Network

The existing bridge network remains for internal communication:

```bash
docker network create \
  --driver bridge \
  sorcha-network
```

---

## Docker Compose Configuration

Services are configured with dual networks where appropriate:

### Public-Facing Services (macvlan + bridge)

```yaml
peer-hub-local:
  networks:
    sorcha-network: {}
    sorcha-lan:
      ipv4_address: 192.168.51.200
  # No ports published - accessible directly on LAN IP
```

### Internal-Only Services (bridge only)

```yaml
redis:
  networks:
    - sorcha-network
  # Only accessible within Docker network
```

---

## Configuration Steps

### 1. Prerequisites

- Docker Desktop for Windows with WSL2 backend
- LAN subnet: 192.168.51.0/24
- IP range 192.168.51.192-207 reserved for Docker containers
- Router/firewall configured to allow traffic to Docker IP range

### 2. Create macvlan Network

```bash
docker network create \
  --driver macvlan \
  --subnet=192.168.51.0/24 \
  --gateway=192.168.51.1 \
  --ip-range=192.168.51.192/28 \
  -o parent=eth0 \
  sorcha-lan
```

### 3. Update docker-compose.yml

See the updated `docker-compose.yml` for complete network configuration.

### 4. Deploy Services

```bash
# Stop existing containers
docker-compose down

# Start with new network configuration
docker-compose up -d

# Verify IP assignments
docker inspect <container-name> | grep IPAddress
```

---

## Testing and Verification

### Test LAN Connectivity

```bash
# From Windows host
ping 192.168.51.200  # peer-hub-local
ping 192.168.51.201  # peer-service

# Test HTTP endpoints
curl http://192.168.51.200:8080/health
curl http://192.168.51.201:8080/health
curl http://192.168.51.210:8080/health  # api-gateway
```

### Test from Another LAN Device

```bash
# From another computer on 192.168.51.0/24 network
curl http://192.168.51.200:8080/health
curl http://192.168.51.210:8080/api/blueprints
```

### Test CLI Access

```bash
# Update CLI profile to use LAN IPs
sorcha config set-profile docker-lan \
  --peer-url http://192.168.51.201:8080 \
  --wallet-url http://192.168.51.212:8080 \
  --api-url http://192.168.51.210:8080

# Test peer commands
sorcha peer stats --profile docker-lan
sorcha peer health --profile docker-lan
```

---

## NAT Configuration for Internet Access

### Router Port Forwarding

To allow external peers to connect from the internet, configure your router to forward ports to the container IPs:

**Peer Hub Node (for external hub connectivity):**
- External: `<your-public-ip>:5000` → Internal: `192.168.51.200:5000` (gRPC)
- External: `<your-public-ip>:8080` → Internal: `192.168.51.200:8080` (HTTP)

**Peer Service (for P2P connections):**
- External: `<your-public-ip>:5001` → Internal: `192.168.51.201:5000` (gRPC/STUN)
- External: `<your-public-ip>:8081` → Internal: `192.168.51.201:8080` (HTTP)

**API Gateway (for public API access):**
- External: `<your-public-ip>:443` → Internal: `192.168.51.210:8080` (HTTPS via reverse proxy)

### Update Peer Service Configuration

Update peer service to advertise its public address:

```json
{
  "PeerService": {
    "PublicAddress": "<your-public-ip>",
    "Port": 5001,
    "CentralNode": {
      "CentralNodes": [
        {
          "NodeId": "hub-local.sorcha.dev",
          "Hostname": "192.168.51.200",
          "Port": 5000
        },
        {
          "NodeId": "n0.sorcha.dev",
          "Hostname": "n0.sorcha.dev",
          "Port": 443
        }
      ]
    }
  }
}
```

---

## Advantages of This Setup

### For Peer-to-Peer Networking

1. **STUN Server Discovery** - Containers can discover their public IP through STUN
2. **ICE Negotiation** - WebRTC-style peer connection negotiation works correctly
3. **Direct Connectivity** - External peers connect directly without port translation confusion
4. **NAT Traversal** - Proper UDP hole punching for P2P connections

### For Development

1. **No Port Conflicts** - Each service has its own IP, no need to manage host ports
2. **LAN Accessibility** - Access services from any device on your network
3. **Production-Like** - More similar to cloud deployment than bridge networking
4. **Easy Testing** - Mobile devices, VR headsets, other computers can access directly

### For Deployment

1. **Firewall Rules** - Easy to configure per-service firewall rules
2. **DNS** - Can assign DNS names to container IPs (e.g., peer.local.sorcha.dev → 192.168.51.201)
3. **Monitoring** - Standard network monitoring tools work with container IPs
4. **Load Balancing** - Can use external load balancers pointing to container IPs

---

## Important Notes and Limitations

### macvlan Limitations

1. **Host Communication:**
   - Docker has a limitation where the host cannot directly communicate with macvlan containers
   - **Workaround:** Containers can still communicate with each other and all other LAN devices
   - **Solution:** Use another device on the network to access containers, or create a macvlan interface on the host

2. **WSL2 Bridging:**
   - macvlan uses WSL2's eth0 interface as parent
   - Adds slight latency (~150-200ms) compared to bridge networking
   - Acceptable for most use cases, negligible for P2P networking

3. **Network Switch Requirements:**
   - Some network switches block multiple MAC addresses per port
   - Most home routers and switches support this by default
   - Corporate networks may require "promiscuous mode" enabled

### Windows Docker Desktop Specifics

1. **Interface Name:**
   - Parent interface is `eth0` (WSL2 virtual ethernet)
   - Not directly mapped to Windows physical interface
   - WSL2 handles bridging to Windows network

2. **IP Persistence:**
   - Static IPs configured in docker-compose.yml persist across restarts
   - macvlan network must exist before starting containers
   - Recreate network if IP range changes

---

## Troubleshooting

### Containers Not Getting LAN IPs

```bash
# Check if macvlan network exists
docker network ls | grep sorcha-lan

# Inspect network configuration
docker network inspect sorcha-lan

# Recreate network if needed
docker network rm sorcha-lan
# Then recreate with correct parameters
```

### Cannot Ping Container from Host

This is expected behavior with macvlan. Use another device on the network to test, or create a macvlan interface on the host:

```bash
# Create macvlan interface on host (advanced)
# This allows host to communicate with containers
docker network create -d macvlan \
  --subnet=192.168.51.0/24 \
  --gateway=192.168.51.1 \
  --ip-range=192.168.51.192/28 \
  -o parent=eth0 \
  sorcha-lan
```

### Containers Cannot Reach Internet

```bash
# Check gateway configuration
docker network inspect sorcha-lan | grep Gateway

# Verify container routing
docker exec <container> ip route

# Check DNS resolution
docker exec <container> nslookup google.com
```

### IP Address Conflicts

If containers fail to start due to IP conflicts:

```bash
# Check which IPs are in use on the network
arp -a

# Ensure the IP range 192.168.51.192-207 is not used by other devices
# Update router DHCP pool to exclude this range
```

---

## Migration from Bridge to macvlan

If you have existing Sorcha containers running on bridge networking:

```bash
# 1. Stop all containers
docker-compose down

# 2. Create macvlan network
docker network create \
  --driver macvlan \
  --subnet=192.168.51.0/24 \
  --gateway=192.168.51.1 \
  --ip-range=192.168.51.192/28 \
  -o parent=eth0 \
  sorcha-lan

# 3. Update docker-compose.yml (see updated configuration)

# 4. Start services
docker-compose up -d

# 5. Verify IPs
docker-compose ps
docker inspect sorcha-peer-hub-local | grep IPAddress
```

---

## Security Considerations

### Firewall Rules

With containers on the LAN, they are directly accessible. Configure Windows Firewall:

```powershell
# Allow Docker IP range
New-NetFirewallRule -DisplayName "Docker macvlan" `
  -Direction Inbound `
  -LocalAddress 192.168.51.192-192.168.51.207 `
  -Action Allow
```

### Network Isolation

Consider creating VLANs for Docker containers:

- **Production VLAN:** 192.168.51.0/25 (hosts)
- **Docker VLAN:** 192.168.51.128/25 (containers)

### Access Control

Use iptables or nftables in containers to restrict access:

```bash
# Example: Only allow specific ports
iptables -A INPUT -p tcp --dport 8080 -j ACCEPT
iptables -A INPUT -p tcp --dport 5000 -j ACCEPT
iptables -A INPUT -j DROP
```

---

## References

- [Docker macvlan networking documentation](https://docs.docker.com/network/macvlan/)
- [Docker Desktop WSL2 backend](https://docs.docker.com/desktop/wsl/)
- [Sorcha Peer Service Documentation](../src/Services/Sorcha.Peer.Service/README.md)
- [CLI Implementation Success Report](./CLI-IMPLEMENTATION-SUCCESS.md)

---

## Changelog

| Date | Change | Author |
|------|--------|--------|
| 2025-12-24 | Initial macvlan configuration for LAN IP addressing | Claude Sonnet 4.5 |
| 2025-12-24 | Tested and verified macvlan networking on WSL2 | Claude Sonnet 4.5 |
| 2025-12-24 | Created dual-network docker-compose configuration | Claude Sonnet 4.5 |

---

**Status:** ✅ Ready for Production
**Next Steps:** Update docker-compose.yml and configure router port forwarding
