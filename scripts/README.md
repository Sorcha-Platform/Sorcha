# Sorcha Scripts

Utility scripts for installing, bootstrapping and operating Sorcha.

## I want to… → run this

The names in here used to collide badly enough to send new contributors down the wrong path — three
overlapping "setup" entry points, one of which was retired but still documented (issue #1234). Start
from this table.

| I want to… | Run | Notes |
|---|---|---|
| **Install Sorcha from scratch** (don't have the repo) | `install.sh` / `install.ps1` | The one-liners in the root README. Clone the repo, then hand off to `sorcha-setup.sh`. `install.ps1` runs it via bash — there is no PowerShell wizard. |
| **Set up / re-run the stack** (already have the repo) | **`sorcha-setup.sh`** | **The canonical stack setup.** Prerequisite checks, config questions, `.env` + JWT key, image pull, start, bootstrap. Bats-tested; what both installers and the README use. |
| **Point the Sorcha CLI at a running instance** | `bootstrap-sorcha.sh` / `bootstrap-sorcha.ps1` | **A different concern from the above** — this configures a CLI *auth profile*, it does not install or start anything. See `README-BOOTSTRAP.md`. The `bootstrap-` prefix is the trap: it reads like "bootstrap the platform" and isn't. |
| **Wipe local Docker state and start clean** | `reset-docker-state.sh` / `.ps1` | Documented below. |
| **Check a clean install works** | `check-clean-install.sh` | CI/verification helper. |

**Retired (issue #1234):** `setup.sh`, `setup.ps1` and `setup-config.yaml` — an older first-run
wizard pair superseded by `sorcha-setup.sh`. Removed rather than left alongside it, because the whole
problem was three things called "setup" with no way to tell which was live. If you find a reference
to them anywhere, it is stale.

---

## 🔄 Reset Docker State

Scripts to reset the Docker environment by clearing all Sorcha containers and database volumes. Use this to get a clean slate before running bootstrap or when troubleshooting.

### Windows (PowerShell)

**Script:** `reset-docker-state.ps1`

#### Basic Usage

```powershell
# Reset with confirmation prompt
.\scripts\reset-docker-state.ps1

# Reset without confirmation (CI/CD)
.\scripts\reset-docker-state.ps1 -Yes

# Remove containers but keep database volumes
.\scripts\reset-docker-state.ps1 -KeepVolumes
```

#### Parameters

| Parameter | Alias | Default | Description |
|-----------|-------|---------|-------------|
| `-Yes` | `-y` | `false` | Skip confirmation prompt |
| `-KeepVolumes` | - | `false` | Keep database volumes (only remove containers) |

### Linux/macOS (Bash)

**Script:** `reset-docker-state.sh`

#### Basic Usage

```bash
# Make script executable (first time only)
chmod +x scripts/reset-docker-state.sh

# Reset with confirmation prompt
./scripts/reset-docker-state.sh

# Reset without confirmation
./scripts/reset-docker-state.sh -y

# Remove containers but keep volumes
./scripts/reset-docker-state.sh --keep-volumes
```

### What Gets Reset

The script performs the following actions:

1. ✅ Checks Docker daemon is running and healthy
2. 🛑 Stops all Sorcha containers
3. 🗑️ Removes all Sorcha containers
4. 💾 Removes database volumes (unless `--keep-volumes` is specified):
   - PostgreSQL data (`sorcha_postgres-data`)
   - MongoDB data (`sorcha_mongodb-data`)
   - Redis data (`sorcha_redis-data`)
   - Data protection keys (`sorcha_dataprotection-keys`)

### When to Use

- 🔧 **Before bootstrap**: Get a fresh state for initial setup
- 🐛 **Troubleshooting**: Clear corrupted state or stuck containers
- 🧪 **Testing**: Reset to known clean state between test runs
- 🔄 **Schema changes**: Clear databases after migration changes

### Safety Features

- Requires explicit confirmation unless `-y` or `-Yes` flag is used
- Shows current state before resetting
- Verifies clean state after reset
- Provides clear next steps

---

## 🚀 Bootstrap Sorcha

Scripts to bootstrap a fresh Sorcha installation with initial tenant, wallet, and configuration.

**Scripts:**
- Windows: `bootstrap-sorcha.ps1`
- Linux/macOS: `bootstrap-sorcha.sh`

See `README-BOOTSTRAP.md` for detailed documentation.

---

## 📦 Push to DockerHub

Scripts to build and push all Sorcha service images to DockerHub.

### Windows (PowerShell)

**Script:** `push-to-dockerhub.ps1`

#### Basic Usage

```powershell
# Push all services with 'latest' tag
.\scripts\push-to-dockerhub.ps1 -DockerHubUser "yourusername"

# Push with specific version tag
.\scripts\push-to-dockerhub.ps1 -DockerHubUser "yourusername" -Tag "v1.0.0"

# Push only specific services
.\scripts\push-to-dockerhub.ps1 -DockerHubUser "yourusername" -Services blueprint,wallet

# Dry run (see what would happen without pushing)
.\scripts\push-to-dockerhub.ps1 -DockerHubUser "yourusername" -DryRun

# Skip building and use existing local images
.\scripts\push-to-dockerhub.ps1 -DockerHubUser "yourusername" -SkipBuild
```

#### Parameters

| Parameter | Required | Default | Description |
|-----------|----------|---------|-------------|
| `-DockerHubUser` | ✅ Yes | - | Your DockerHub username or organization |
| `-Tag` | ❌ No | `latest` | Version tag for images (e.g., `v1.0.0`, `1.2.3`) |
| `-Services` | ❌ No | All | Specific services to push: `blueprint`, `wallet`, `register`, `tenant`, `peer`, `validator`, `gateway` |
| `-SkipBuild` | ❌ No | `false` | Skip building, only tag and push existing images |
| `-DryRun` | ❌ No | `false` | Show what would be done without actually pushing |

---

### Linux/macOS (Bash)

**Script:** `push-to-dockerhub.sh`

#### Basic Usage

```bash
# Make script executable (first time only)
chmod +x scripts/push-to-dockerhub.sh

# Push all services
./scripts/push-to-dockerhub.sh -u yourusername

# Push with version tag
./scripts/push-to-dockerhub.sh -u yourusername -t v1.0.0

# Push specific services
./scripts/push-to-dockerhub.sh -u yourusername -s blueprint,wallet

# Dry run
./scripts/push-to-dockerhub.sh -u yourusername --dry-run
```

---

## 🔐 DockerHub Authentication

Before running the scripts:

```bash
docker login
```

The scripts will check authentication and prompt if needed.

---

## 🎯 Available Services

- `blueprint` - Blueprint workflow management
- `wallet` - Cryptographic wallet service
- `register` - Distributed ledger service
- `tenant` - Multi-tenant management
- `peer` - P2P networking
- `validator` - Transaction validation
- `gateway` - YARP API gateway

---

## 📝 Examples

```powershell
# Production release (Windows)
.\scripts\push-to-dockerhub.ps1 -DockerHubUser "sorchaorg" -Tag "v1.0.0"

# Production release (Linux/Mac)
./scripts/push-to-dockerhub.sh -u sorchaorg -t v1.0.0

# Update specific services only
.\scripts\push-to-dockerhub.ps1 -DockerHubUser "myuser" -Services validator,gateway
```

---

**Last Updated:** 2025-12-23
