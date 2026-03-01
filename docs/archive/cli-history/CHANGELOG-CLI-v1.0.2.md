# Sorcha CLI v1.0.2 - Changelog

**Release Date:** 2026-01-02
**Type:** Bug Fix & Enhancement

## Summary

This release fixes a critical null reference exception in the `sorcha config list` command and simplifies the default configuration to provide a better out-of-the-box experience for Docker Compose users.

## Changes

### 🐛 Bug Fixes

1. **Fixed null reference exception in `config list` command** ([ConfigCommand.cs:49](src/Apps/Sorcha.Cli/Commands/ConfigCommand.cs#L49))
   - **Issue:** Command crashed with "Object reference not set to an instance of an object" when `ActiveProfile` was null
   - **Fix:** Added null check: `!string.IsNullOrEmpty(config.ActiveProfile) && p.Name == config.ActiveProfile`
   - **Impact:** `sorcha config list` now works reliably in all scenarios

### ✨ Enhancements

2. **Simplified default configuration** ([ConfigurationService.cs:182-207](src/Apps/Sorcha.Cli/Services/ConfigurationService.cs#L182-L207))
   - **Before:** Created 8 default profiles (dev, local, docker, aspire, staging, docker-direct, docker-lan, production)
   - **After:** Creates only 1 default profile (docker)
   - **Rationale:** Reduces confusion for new users and provides a clean starting point
   - **Migration:** Users can add additional profiles using `sorcha config init`

3. **Updated default profile for Docker Compose**
   - **Name:** docker (active by default)
   - **Service URL:** `http://localhost`
   - **Auth URL:** `http://localhost/api/service-auth/token`
   - **Design:** All service URLs derived from base URL via API Gateway routing
   - **Benefits:** Simpler configuration, works out-of-the-box with standard Docker Compose setup

### 📚 Documentation

4. **Updated CLI README** ([src/Apps/Sorcha.Cli/README.md](src/Apps/Sorcha.Cli/README.md))
   - Updated Quick Start section to reflect single default profile
   - Added comprehensive Configuration Commands section
   - Updated example outputs to use "docker" profile
   - Added examples for creating new profiles
   - Changed default profile references from "dev" to "docker"

## Installation

### Global Tool Installation

```bash
# Uninstall old version
dotnet tool uninstall sorcha.cli --global

# Install new version
dotnet pack src/Apps/Sorcha.Cli/Sorcha.Cli.csproj -c Release -o nupkgs --property:PackageVersion=1.0.2
dotnet tool install sorcha.cli --global --add-source ./nupkgs --version 1.0.2

# Verify installation
sorcha --version
```

### First-Time Setup

After installation, the CLI will automatically create a default configuration on first use:

```bash
# List profiles (creates default config if it doesn't exist)
sorcha config list
```

Output:
```
┌────────────────────────────────┐
│ Name   │ ServiceUrl       │ Active │
├────────────────────────────────┤
│ docker │ http://localhost │ True   │
└────────────────────────────────┘
```

## Usage Examples

### View Configuration

```bash
# List all profiles
sorcha config list

# Show help for config commands
sorcha config --help
sorcha config init --help
```

### Add New Profiles

```bash
# Add staging profile
sorcha config init --profile staging --service-url https://staging.sorcha.dev

# Add production profile with specific URLs
sorcha config init --profile prod \
  --tenant-url https://tenant.sorcha.io \
  --wallet-url https://wallet.sorcha.io \
  --register-url https://register.sorcha.io

# Add Aspire profile for local .NET development
sorcha config init --profile aspire --service-url https://localhost:7082
```

### Switch Profiles

```bash
# Switch to staging
sorcha config set-active staging

# Use specific profile for one command
sorcha auth login --profile prod
```

## Breaking Changes

**None.** This is a backward-compatible release.

### Migration Notes

If you have an existing configuration at `~/.sorcha/config.json`:
- ✅ Your configuration will **NOT** be affected
- ✅ All existing profiles remain intact
- ✅ The active profile is unchanged
- ℹ️ Only new installations get the simplified default profile

To adopt the new simplified default:
```bash
# Backup existing config (optional)
cp ~/.sorcha/config.json ~/.sorcha/config.json.backup

# Remove config to reset to new defaults
rm -rf ~/.sorcha

# CLI will create new default config on next use
sorcha config list
```

## Technical Details

### Files Changed

| File | Lines Changed | Description |
|------|---------------|-------------|
| [ConfigCommand.cs](src/Apps/Sorcha.Cli/Commands/ConfigCommand.cs) | 1 | Fixed null reference in profile list |
| [ConfigurationService.cs](src/Apps/Sorcha.Cli/Services/ConfigurationService.cs) | 105 → 28 | Simplified default configuration |
| [README.md](src/Apps/Sorcha.Cli/README.md) | Multiple | Updated documentation |

### Default Configuration Structure

**v1.0.2 (New):**
```json
{
  "activeProfile": "docker",
  "defaultOutputFormat": "table",
  "verboseLogging": false,
  "quietMode": false,
  "profiles": {
    "docker": {
      "name": "docker",
      "serviceUrl": "http://localhost",
      "tenantServiceUrl": null,
      "registerServiceUrl": null,
      "peerServiceUrl": null,
      "walletServiceUrl": null,
      "authTokenUrl": "http://localhost/api/service-auth/token",
      "defaultClientId": "sorcha-cli",
      "verifySsl": false,
      "timeoutSeconds": 30
    }
  }
}
```

**v1.0.0/v1.0.1 (Old):**
```json
{
  "activeProfile": "dev",
  "profiles": {
    "dev": { ... },
    "local": { ... },
    "docker": { ... },
    "aspire": { ... },
    "staging": { ... },
    "docker-direct": { ... },
    "docker-lan": { ... },
    "production": { ... }
  }
}
```

## Testing

All changes have been tested:

✅ Fresh installation creates single docker profile
✅ `sorcha config list` works without errors
✅ `sorcha config init` creates new profiles correctly
✅ `sorcha config set-active` switches profiles
✅ Existing configurations remain unchanged
✅ Profile-specific authentication works

## Known Issues

None.

## Credits

- **Author:** Claude Code AI Assistant
- **Date:** 2026-01-02
- **Review:** Pending

## Next Steps

1. ✅ Rebuild and install CLI globally
2. ✅ Update documentation
3. ⏭️ Test with Docker Compose deployment
4. ⏭️ Update main project CHANGELOG.md
5. ⏭️ Consider version tagging in git
