# Quickstart: Peer Router App

**Feature**: 053-peer-router-completion

## Running the Peer Router

### Docker (recommended)
```bash
# Start router only
docker-compose --profile tools up peer-router -d

# View debug page
open http://localhost:5500

# Tail event stream (AI/CLI friendly)
curl -N http://localhost:5500/events?follow=true
```

### Local development
```bash
# Run with defaults
dotnet run --project src/Apps/Sorcha.PeerRouter

# Run with relay enabled
dotnet run --project src/Apps/Sorcha.PeerRouter -- --enable-relay

# Custom ports
dotnet run --project src/Apps/Sorcha.PeerRouter -- --port 6000 --http-port 9090
```

### With Aspire
```bash
# Starts alongside all services
dotnet run --project src/Apps/Sorcha.AppHost
# Router available at Aspire-assigned port
```

## Connecting Peers to the Router

Configure the Peer Service to use the router as a seed node:

```json
// appsettings.json
{
  "PeerService": {
    "Discovery": {
      "BootstrapNodes": ["http://peer-router:5000"]
    }
  }
}
```

Or via environment variable:
```bash
PeerService__Discovery__BootstrapNodes__0=http://peer-router:5000
```

## Debugging a P2P Network

1. Start the router: `docker-compose --profile tools up peer-router -d`
2. Open debug page: `http://localhost:5500`
3. Start peer services (they auto-connect to seed nodes)
4. Watch events stream in browser or via curl
5. Check routing table: `curl http://localhost:5500/peers | jq`

## AI-Assisted Debugging

Connect Claude Code to the event stream for real-time network diagnostics:
```
# In Claude Code, use WebFetch to tail events
GET http://localhost:5500/events?follow=true

# Or snapshot current state
GET http://localhost:5500/peers
GET http://localhost:5500/events
```
