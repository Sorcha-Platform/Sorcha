# Diagnostic script for Sorcha CLI authentication issues
# This helps identify why tokens aren't being cached/retrieved properly

Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "Sorcha CLI Authentication Diagnostic" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

# 1. Check CLI installation
Write-Host "1. Checking CLI installation..." -ForegroundColor Yellow
$cliVersion = sorcha --version 2>&1
if ($LASTEXITCODE -eq 0) {
    Write-Host "   ✓ CLI installed: $cliVersion" -ForegroundColor Green
} else {
    Write-Host "   ✗ CLI not found or not working" -ForegroundColor Red
    exit 1
}
Write-Host ""

# 2. Check config directory
Write-Host "2. Checking configuration directory..." -ForegroundColor Yellow
$configDir = "$env:USERPROFILE\.sorcha"
$tokensDir = "$configDir\tokens"

if (Test-Path $configDir) {
    Write-Host "   ✓ Config directory exists: $configDir" -ForegroundColor Green

    # List contents
    $configFiles = Get-ChildItem $configDir -File
    if ($configFiles) {
        Write-Host "   Files in config directory:" -ForegroundColor Gray
        foreach ($file in $configFiles) {
            Write-Host "     - $($file.Name) ($($file.Length) bytes)" -ForegroundColor Gray
        }
    }
} else {
    Write-Host "   ⚠ Config directory doesn't exist: $configDir" -ForegroundColor Yellow
}

if (Test-Path $tokensDir) {
    Write-Host "   ✓ Tokens directory exists: $tokensDir" -ForegroundColor Green

    # List token files
    $tokenFiles = Get-ChildItem $tokensDir -Filter "*.token"
    if ($tokenFiles) {
        Write-Host "   Token files found:" -ForegroundColor Gray
        foreach ($file in $tokenFiles) {
            $profile = $file.BaseName
            Write-Host ("     - " + $file.Name + " [" + $file.Length + " B] - Profile: " + $profile) -ForegroundColor Gray
            Write-Host ("       Created: " + $file.CreationTime) -ForegroundColor DarkGray
            Write-Host ("       Modified: " + $file.LastWriteTime) -ForegroundColor DarkGray
        }
    } else {
        Write-Host "   ⚠ No token files found in $tokensDir" -ForegroundColor Yellow
    }
} else {
    Write-Host "   ⚠ Tokens directory doesn't exist: $tokensDir" -ForegroundColor Yellow
}
Write-Host ""

# 3. Check config.json
Write-Host "3. Checking CLI configuration..." -ForegroundColor Yellow
$configFile = "$configDir\config.json"
if (Test-Path $configFile) {
    Write-Host "   ✓ Config file exists: $configFile" -ForegroundColor Green
    try {
        $config = Get-Content $configFile -Raw | ConvertFrom-Json

        if ($config.activeProfile) {
            Write-Host "   Active Profile: $($config.activeProfile)" -ForegroundColor Cyan
        } else {
            Write-Host "   ⚠ No active profile set (will default to 'dev')" -ForegroundColor Yellow
        }

        if ($config.profiles) {
            Write-Host "   Available Profiles:" -ForegroundColor Gray
            foreach ($profile in $config.profiles.PSObject.Properties) {
                $p = $profile.Value
                Write-Host "     - $($profile.Name):" -ForegroundColor Gray
                Write-Host "       Tenant URL: $($p.tenantServiceUrl)" -ForegroundColor DarkGray
                Write-Host "       Auth URL: $($p.authTokenUrl)" -ForegroundColor DarkGray
            }
        }
    } catch {
        Write-Host "   ✗ Failed to parse config.json: $($_.Exception.Message)" -ForegroundColor Red
    }
} else {
    Write-Host "   ⚠ Config file doesn't exist: $configFile" -ForegroundColor Yellow
}
Write-Host ""

# 4. Check authentication status for common profiles
Write-Host "4. Checking authentication status..." -ForegroundColor Yellow
$profiles = @("dev", "local", "docker", "aspire")

foreach ($profile in $profiles) {
    Write-Host "   Profile: $profile" -ForegroundColor Cyan

    $tokenFile = "$tokensDir\$profile.token"
    if (Test-Path $tokenFile) {
        Write-Host "     ✓ Token file exists ($((Get-Item $tokenFile).Length) bytes)" -ForegroundColor Green

        # Try auth status command
        $status = sorcha auth status --profile $profile 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host "     Status output:" -ForegroundColor Gray
            $status | ForEach-Object { Write-Host "       $_" -ForegroundColor DarkGray }
        } else {
            Write-Host "     ⚠ Status check failed" -ForegroundColor Yellow
        }
    } else {
        Write-Host "     ✗ No token file" -ForegroundColor Red
    }
    Write-Host ""
}

# 5. Test API connectivity
Write-Host "5. Testing API connectivity..." -ForegroundColor Yellow
$endpoints = @{
    "Tenant Service (direct)" = "http://localhost:5110/health"
    "API Gateway" = "http://localhost/api/health"
}

foreach ($endpoint in $endpoints.GetEnumerator()) {
    Write-Host "   Testing: $($endpoint.Key)" -ForegroundColor Cyan
    try {
        $response = Invoke-WebRequest -Uri $endpoint.Value -Method Get -TimeoutSec 5 2>$null
        Write-Host "     ✓ Responded with status: $($response.StatusCode)" -ForegroundColor Green
    } catch {
        Write-Host "     ✗ Failed: $($_.Exception.Message)" -ForegroundColor Red
    }
}
Write-Host ""

# 6. Recommendations
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host "Recommendations:" -ForegroundColor Cyan
Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
Write-Host ""

if (!(Test-Path $tokensDir) -or !(Get-ChildItem $tokensDir -Filter "*.token")) {
    Write-Host "⚠ No tokens found. Try logging in:" -ForegroundColor Yellow
    Write-Host "   sorcha auth login --profile docker" -ForegroundColor White
    Write-Host ""
}

if (Test-Path $configFile) {
    try {
        $config = Get-Content $configFile -Raw | ConvertFrom-Json
        if (!$config.activeProfile) {
            Write-Host "⚠ No active profile set. Set one with:" -ForegroundColor Yellow
            Write-Host "   sorcha config use docker" -ForegroundColor White
            Write-Host ""
        }
    } catch {}
}

Write-Host "📝 Common troubleshooting steps:" -ForegroundColor Cyan
Write-Host "   1. Login with explicit profile:" -ForegroundColor White
Write-Host "      sorcha auth login --profile docker" -ForegroundColor Gray
Write-Host ""
Write-Host "   2. Check authentication status:" -ForegroundColor White
Write-Host "      sorcha auth status --profile docker" -ForegroundColor Gray
Write-Host ""
Write-Host "   3. Use same profile for commands:" -ForegroundColor White
Write-Host "      sorcha org list --profile docker" -ForegroundColor Gray
Write-Host ""
Write-Host "   4. If still failing, clear and retry:" -ForegroundColor White
Write-Host "      sorcha auth logout --all" -ForegroundColor Gray
Write-Host "      sorcha auth login --profile docker" -ForegroundColor Gray
Write-Host ""

Write-Host "═══════════════════════════════════════════════════" -ForegroundColor Cyan
