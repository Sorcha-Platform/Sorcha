# Sorcha CLI - Development Workflow Guide

**Status:** Active
**Last Updated:** 2026-01-05
**Version:** 1.0.5+

---

## 🚀 Quick Start for Development

### **One-Command Rebuild and Reinstall**

```powershell
# From repository root
pwsh scripts/rebuild-cli.ps1
```

This script automatically:
1. ✅ Uninstalls the existing global tool
2. ✅ Cleans build artifacts
3. ✅ Builds the CLI in Release mode
4. ✅ Creates a NuGet package with auto-incremented version
5. ✅ Installs as a global tool
6. ✅ Verifies the installation

**Expected Output:**
```
���������������������������������������������������ͻ
�  ✅ Sorcha CLI Rebuilt and Installed Successfully  �
���������������������������������������������������ͼ

📋 Version Information:
Sorcha CLI v1.0.5-build.2146+08a162d9ce
Assembly Version: 1.0.0.0
.NET Runtime: 10.0.1
OS: Microsoft Windows NT 10.0.26200.0
Platform: win-x64
```

---

## 📦 Auto-Versioning System

### **Version Format**

The CLI uses **automatic timestamp-based versioning** for development builds:

```
Format: 1.0.{DayOfYear}-build.{HHmm}+{GitCommitHash}

Examples:
- 1.0.5-build.2146+08a162d9ce  (Day 5 of year, built at 21:46 UTC)
- 1.0.42-build.1530+a3f2e8b1da (Day 42 of year, built at 15:30 UTC)
- 1.0.365-build.2359+f1a2b3c4d5 (Day 365 of year, built at 23:59 UTC)
```

### **How It Works**

**In [Sorcha.Cli.csproj](Sorcha.Cli.csproj):**
```xml
<!-- Auto-incrementing version for development -->
<VersionPrefix>1.0.$([System.DateTime]::UtcNow.DayOfYear)</VersionPrefix>
<VersionSuffix>build.$([System.DateTime]::UtcNow.ToString(HHmm))</VersionSuffix>
<AssemblyVersion>1.0.0.0</AssemblyVersion>
<Deterministic>false</Deterministic>
```

**Benefits:**
- ✅ **Unique versions** for every build (down to the minute)
- ✅ **No manual version bumping** required
- ✅ **Global tool reinstall** works automatically (newer version detected)
- ✅ **Sortable** - later builds have higher versions on the same day
- ✅ **Git hash included** for traceability

---

## 🛠️ Development Commands

### **Standard Development Cycle**

```powershell
# 1. Make code changes in your editor
# Edit files in src/Apps/Sorcha.Cli/

# 2. Rebuild and reinstall
pwsh scripts/rebuild-cli.ps1

# 3. Test your changes
sorcha version
sorcha --help
sorcha config init --profile dev
```

### **First-Time Installation**

```powershell
# Skip uninstall on first run
pwsh scripts/rebuild-cli.ps1 -SkipUninstall
```

### **Debug Build**

```powershell
# Build in Debug mode (includes -dev suffix)
pwsh scripts/rebuild-cli.ps1 -Configuration Debug

# Result: 1.0.5-build.2146-dev+08a162d9ce
```

### **Manual Build Process**

If you prefer manual control:

```powershell
# 1. Uninstall existing tool
dotnet tool uninstall --global Sorcha.Cli

# 2. Navigate to CLI project
cd src/Apps/Sorcha.Cli

# 3. Build and pack
dotnet clean
dotnet build --configuration Release
dotnet pack --configuration Release --output ./nupkg

# 4. Install from local package
dotnet tool install --global --add-source ./nupkg Sorcha.Cli --prerelease

# 5. Verify
sorcha version
```

---

## 📋 Script Options

### **rebuild-cli.ps1 Parameters**

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `-Configuration` | String | `Release` | Build configuration (`Debug` or `Release`) |
| `-SkipUninstall` | Switch | `false` | Skip uninstalling existing tool (first-time install) |

**Examples:**
```powershell
# Standard rebuild (Release mode)
pwsh scripts/rebuild-cli.ps1

# Debug build with -dev suffix
pwsh scripts/rebuild-cli.ps1 -Configuration Debug

# First-time installation
pwsh scripts/rebuild-cli.ps1 -SkipUninstall

# Debug build, first-time install
pwsh scripts/rebuild-cli.ps1 -Configuration Debug -SkipUninstall
```

---

## 🔍 Troubleshooting

### **Issue: "Tool 'sorcha.cli' is not currently installed"**

**Cause:** First-time installation or tool was manually uninstalled.

**Solution:**
```powershell
pwsh scripts/rebuild-cli.ps1 -SkipUninstall
```

---

### **Issue: "Tool not in PATH after installation"**

**Cause:** Terminal session hasn't picked up PATH changes.

**Solution:**
1. Close your terminal completely
2. Open a new terminal session
3. Run `sorcha version` to verify

**Alternative (PowerShell):**
```powershell
$env:PATH = [System.Environment]::GetEnvironmentVariable("PATH", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("PATH", "User")
sorcha version
```

---

### **Issue: Build fails with version error**

**Cause:** Deterministic builds conflict with timestamp-based versioning.

**Solution:**
Verify in `Sorcha.Cli.csproj`:
```xml
<Deterministic>false</Deterministic>
```

---

### **Issue: "Package already exists" error**

**Cause:** Local NuGet cache has a package with the same version.

**Solution:**
```powershell
# Clear local package cache
Remove-Item src/Apps/Sorcha.Cli/nupkg/*.nupkg -Force

# Rebuild
pwsh scripts/rebuild-cli.ps1
```

---

## 📚 Development Best Practices

### **Workflow Tips**

1. **Run `rebuild-cli.ps1` after every code change** you want to test
2. **Use `sorcha --help`** to verify commands registered correctly
3. **Test with `--verbose`** flag for debugging: `sorcha config init --verbose`
4. **Check exit codes** in scripts: `$LASTEXITCODE` (PowerShell) or `$?` (Bash)

### **Version Command Usage**

The `version` command shows detailed build information:

```bash
sorcha version
```

**Output:**
```
Sorcha CLI v1.0.5-build.2146+08a162d9ce03aef617aa44cd3a8382a467f6d183
Assembly Version: 1.0.0.0
File Version: 1.0.0.0
.NET Runtime: 10.0.1
OS: Microsoft Windows NT 10.0.26200.0
Platform: win-x64
```

**Use this to:**
- ✅ Verify latest version installed
- ✅ Check .NET runtime version
- ✅ Confirm platform architecture
- ✅ Get git commit hash for traceability

---

## 🚢 Production Versioning (Future)

For production releases, manual semantic versioning is recommended:

```xml
<!-- In Sorcha.Cli.csproj for production releases -->
<Version>1.0.0</Version>
<AssemblyVersion>1.0.0.0</AssemblyVersion>
<FileVersion>1.0.0.0</FileVersion>
```

**Production Release Process:**
1. Update version manually in `.csproj`
2. Build with `dotnet pack --configuration Release`
3. Publish to NuGet.org
4. Tag git commit: `git tag v1.0.0`

---

## 🎯 What to Implement Next

Based on the [CLI specification](../../../.specify/specs/sorcha-cli-admin-tool.md), the recommended implementation order is:

### **Phase 1: Foundation (COMPLETE) ✅**
- ✅ Auto-incrementing version
- ✅ Development rebuild script
- ✅ Dynamic version command

### **Phase 2: Core Infrastructure (NEXT)**
1. **Configuration Management** - Profile storage and switching
2. **Token Cache** - OS-specific encryption (DPAPI/Keychain/SecretService)
3. **HTTP Client Factory** - Refit clients with Polly resilience
4. **Output Formatters** - Table/JSON/CSV (already stubbed)

### **Phase 3: Authentication (Sprint 2)**
1. **Login flow** - Interactive password input
2. **Token refresh** - Automatic refresh on expiry
3. **Logout** - Clear cached tokens

### **Phase 4: Service Commands (Sprint 2-3)**
1. **Organization commands** - CRUD operations
2. **User commands** - User management
3. **Register commands** - Register and transaction operations
4. **Wallet commands** - Wallet operations

### **Phase 5: Bootstrap Automation (Sprint 5)**
1. **Bootstrap command** - One-command environment setup
2. **Tenant service bootstrap endpoint** - Atomic org creation

---

## 📖 Related Documentation

- **[CLI Specification](../../../.specify/specs/sorcha-cli-admin-tool.md)** - Complete feature requirements
- **[MASTER-TASKS.md](../../../.specify/MASTER-TASKS.md)** - Task tracking
- **[README.md](README.md)** - CLI overview and installation

---

## 🆘 Getting Help

**For CLI development questions:**
- Check this document first
- Review the [CLI specification](../../../.specify/specs/sorcha-cli-admin-tool.md)
- Check [MASTER-TASKS.md](../../../.specify/MASTER-TASKS.md) for implementation status

**For bugs or feature requests:**
- Create a GitHub issue with label `cli-tool`
- Include output of `sorcha version`
- Provide steps to reproduce

---

**Happy CLI Development!** 🚀
