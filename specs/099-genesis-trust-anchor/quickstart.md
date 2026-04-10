# Quickstart: System Register Genesis Trust Anchor

## Creating a New Network

### 1. Run the Genesis Ceremony

```bash
# From repo root — writes genesis file to embedded resource location
dotnet run --project src/Apps/Sorcha.Cli -- system-register create \
  --network-id sorcha-dev

# Output:
#   src/Common/Sorcha.Register.Models/Resources/system-register-genesis.json
#   ./genesis-validator-key.json
```

### 2. Build and Deploy

```bash
# Genesis file is now embedded in the assembly
dotnet build
docker-compose build
```

### 3. Start Services and Import Validator Key

```bash
# Start all services
docker-compose up -d

# Import the genesis validator key into the first validator
dotnet run --project src/Apps/Sorcha.Cli -- system-register import-validator-key \
  --key genesis-validator-key.json

# The bootstrapper will detect the imported key, seal the genesis docket,
# and seed default blueprints. Check logs:
docker-compose logs -f register-service
```

### 4. Secure the Validator Key

```bash
# The key file is no longer needed for normal operation
# Move to secure storage or destroy
rm genesis-validator-key.json
```

## Joining an Existing Network

### 1. Get the Genesis File

Obtain `system-register-genesis.json` from the network operator.

### 2. Verify It

```bash
dotnet run --project src/Apps/Sorcha.Cli -- system-register verify \
  path/to/system-register-genesis.json
```

### 3. Deploy With It

```bash
# Option A: Replace the embedded resource and rebuild
cp system-register-genesis.json \
  src/Common/Sorcha.Register.Models/Resources/system-register-genesis.json
dotnet build && docker-compose build

# Option B: Mount as config file (no rebuild needed)
# In docker-compose.yml or appsettings.json:
#   SystemRegister__GenesisFile=/etc/sorcha/system-register-genesis.json
```

### 4. Start

```bash
docker-compose up -d
# Instance will sync system register from peers automatically
```

## Verifying Network Identity

```bash
# Check which network an instance is running on
docker-compose logs register-service | grep "Network ID"
# Expected: "System register bootstrap: Network ID = sorcha-dev"
```
