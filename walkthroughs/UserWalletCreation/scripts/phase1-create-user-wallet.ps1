<#
.SYNOPSIS
    Create a user in an organization and set up their default wallet
.DESCRIPTION
    Complete workflow demonstrating:
    - Admin authentication
    - User creation in organization (Tenant Service)
    - User authentication (JWT token)
    - Wallet creation with HD wallet support (Wallet Service)
    - Mnemonic phrase display with security warnings
    - Wallet ownership verification
.PARAMETER ApiBaseUrl
    API Gateway base URL (default: http://localhost)
.PARAMETER TenantServiceUrl
    Direct Tenant Service URL (default: http://localhost:5110)
.PARAMETER WalletServiceUrl
    Direct Wallet Service URL (default: http://localhost:5000)
.PARAMETER OrgId
    Existing organization ID (Guid)
.PARAMETER OrgSubdomain
    Organization subdomain to lookup (alternative to OrgId)
.PARAMETER UserEmail
    New user email address (required)
.PARAMETER UserDisplayName
    New user display name (required)
.PARAMETER UserPassword
    New user password (required)
.PARAMETER UserRoles
    User roles array (default: Member)
.PARAMETER WalletName
    Wallet display name (default: "Default Wallet")
.PARAMETER WalletAlgorithm
    Crypto algorithm: ED25519, NISTP256, or RSA4096 (default: ED25519)
.PARAMETER MnemonicWordCount
    BIP39 word count: 12 or 24 (default: 12)
.PARAMETER SaveMnemonicPath
    File path to save mnemonic (TESTING ONLY! Never use in production)
.PARAMETER AdminEmail
    Admin email for user creation (default: stuart.mackintosh@sorcha.dev)
.PARAMETER AdminPassword
    Admin password (default: SorchaDev2025!)
.EXAMPLE
    .\phase1-create-user-wallet.ps1 `
        -UserEmail "alice@example.com" `
        -UserDisplayName "Alice Johnson" `
        -UserPassword "SecurePass123!" `
        -OrgSubdomain "demo"
.EXAMPLE
    .\phase1-create-user-wallet.ps1 `
        -UserEmail "bob@example.com" `
        -UserDisplayName "Bob Smith" `
        -UserPassword "SecurePass123!" `
        -UserRoles @("Consumer", "Designer") `
        -WalletAlgorithm "NISTP256" `
        -MnemonicWordCount 24 `
        -OrgSubdomain "demo" `
        -Verbose
#>

[CmdletBinding()]
param(
    [Parameter(Mandatory = $false)]
    [string]$ApiBaseUrl = "http://localhost",

    [Parameter(Mandatory = $false)]
    [string]$TenantServiceUrl = "http://localhost:5110",

    [Parameter(Mandatory = $false)]
    [string]$WalletServiceUrl = "http://localhost:5000",

    [Parameter(Mandatory = $false)]
    [Guid]$OrgId,

    [Parameter(Mandatory = $false)]
    [string]$OrgSubdomain,

    [Parameter(Mandatory = $true)]
    [string]$UserEmail,

    [Parameter(Mandatory = $true)]
    [string]$UserDisplayName,

    [Parameter(Mandatory = $true)]
    [string]$UserPassword,

    [Parameter(Mandatory = $false)]
    [string[]]$UserRoles = @("Consumer"),

    [Parameter(Mandatory = $false)]
    [string]$WalletName = "Default Wallet",

    [Parameter(Mandatory = $false)]
    [ValidateSet("ED25519", "NISTP256", "RSA4096")]
    [string]$WalletAlgorithm = "ED25519",

    [Parameter(Mandatory = $false)]
    [ValidateSet(12, 24)]
    [int]$MnemonicWordCount = 12,

    [Parameter(Mandatory = $false)]
    [string]$SaveMnemonicPath,

    [Parameter(Mandatory = $false)]
    [string]$AdminEmail = "admin@sorcha.local",

    [Parameter(Mandatory = $false)]
    [string]$AdminPassword = "Dev_Pass_2025!"
)

# Script configuration
$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

# Helper function to display section headers
function Write-Section {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

# Helper function to display success messages
function Write-Success {
    param([string]$Message)
    Write-Host "  ✓ $Message" -ForegroundColor Green
}

# Helper function to display info messages
function Write-Info {
    param([string]$Message, [string]$Value = "")
    if ($Value) {
        Write-Host "    $Message $Value" -ForegroundColor Gray
    } else {
        Write-Host "    $Message" -ForegroundColor Gray
    }
}

# Helper function to display warnings
function Write-Warning2 {
    param([string]$Message)
    Write-Host "  ⚠️  $Message" -ForegroundColor Yellow
}

# Helper function to make REST API calls with error handling
function Invoke-ApiRequest {
    param(
        [string]$Uri,
        [string]$Method = "GET",
        [hashtable]$Headers = @{},
        [object]$Body = $null,
        [string]$Description = ""
    )

    try {
        $params = @{
            Uri = $Uri
            Method = $Method
            Headers = $Headers
            ContentType = "application/json"
        }

        if ($Body) {
            $params.Body = ($Body | ConvertTo-Json -Depth 10)
        }

        if ($VerbosePreference -eq "Continue") {
            Write-Verbose "API Request: $Method $Uri"
            if ($Body) {
                Write-Verbose "Body: $($params.Body)"
            }
        }

        $response = Invoke-RestMethod @params
        return $response
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        $statusDescription = $_.Exception.Response.StatusDescription

        Write-Host ""
        Write-Host "❌ API Request Failed" -ForegroundColor Red
        if ($Description) {
            Write-Host "   Operation: $Description" -ForegroundColor Yellow
        }
        Write-Host "   Endpoint: $Method $Uri" -ForegroundColor Yellow
        Write-Host "   Status: $statusCode $statusDescription" -ForegroundColor Yellow

        # Try to extract error details
        if ($_.ErrorDetails.Message) {
            try {
                $errorObj = $_.ErrorDetails.Message | ConvertFrom-Json
                Write-Host "   Error: $($errorObj.title)" -ForegroundColor Yellow
                if ($errorObj.detail) {
                    Write-Host "   Detail: $($errorObj.detail)" -ForegroundColor Yellow
                }
            }
            catch {
                Write-Host "   Error: $($_.ErrorDetails.Message)" -ForegroundColor Yellow
            }
        }

        Write-Host ""
        throw
    }
}

# Main script
try {
    Write-Host ""
    Write-Host "╔════════════════════════════════════════════════════════════════════════╗" -ForegroundColor Cyan
    Write-Host "║  Sorcha User and Wallet Creation - Phase 1                             ║" -ForegroundColor Cyan
    Write-Host "╚════════════════════════════════════════════════════════════════════════╝" -ForegroundColor Cyan
    Write-Host ""

    # Validate parameters
    if (-not $OrgId -and -not $OrgSubdomain) {
        Write-Host "❌ Error: Must provide either -OrgId or -OrgSubdomain" -ForegroundColor Red
        exit 1
    }

    # Display configuration
    Write-Info "Configuration:"
    Write-Info "  Tenant Service:" $TenantServiceUrl
    Write-Info "  Wallet Service:" $WalletServiceUrl
    Write-Info "  User Email:" $UserEmail
    Write-Info "  User Name:" $UserDisplayName
    Write-Info "  User Roles:" ($UserRoles -join ", ")
    Write-Info "  Wallet Algorithm:" $WalletAlgorithm
    Write-Info "  Mnemonic Words:" $MnemonicWordCount

    #
    # STEP 1: Admin Authentication
    #
    Write-Section "Step 1: Admin Authentication"

    $loginRequest = @{
        email = $AdminEmail
        password = $AdminPassword
    }

    $tokenResponse = Invoke-ApiRequest `
        -Uri "$TenantServiceUrl/api/auth/login" `
        -Method POST `
        -Body $loginRequest `
        -Description "Admin login"

    $adminToken = $tokenResponse.access_token
    Write-Success "Admin authenticated"
    Write-Info "Token expires in: $($tokenResponse.expires_in) seconds"

    #
    # STEP 2: Resolve Organization
    #
    Write-Section "Step 2: Resolve Organization"

    $resolvedOrgId = $null
    $orgName = ""

    if ($OrgId) {
        $resolvedOrgId = $OrgId
        Write-Info "Using provided Organization ID: $resolvedOrgId"
    }
    elseif ($OrgSubdomain) {
        # Use the by-subdomain endpoint (AllowAnonymous - no auth required)
        $org = Invoke-ApiRequest `
            -Uri "$TenantServiceUrl/api/organizations/by-subdomain/$OrgSubdomain" `
            -Method GET `
            -Description "Get organization by subdomain"

        if (-not $org) {
            Write-Host "❌ Organization with subdomain '$OrgSubdomain' not found" -ForegroundColor Red
            exit 1
        }

        $resolvedOrgId = $org.id
        $orgName = $org.name

        Write-Success "Organization found: $orgName ($OrgSubdomain)"
        Write-Info "Organization ID: $resolvedOrgId"
    }

    #
    # STEP 3: Create User
    #
    Write-Section "Step 3: Create User in Organization"

    # Convert role names to numeric values
    # UserRole enum: Administrator=0, SystemAdmin=1, Designer=2, Developer=3, User=4, Consumer=5, Auditor=6, Member=7
    $roleMap = @{
        "Administrator" = 0
        "SystemAdmin" = 1
        "Designer" = 2
        "Developer" = 3
        "User" = 4
        "Consumer" = 5
        "Auditor" = 6
        "Consumer" = 7
    }

    $numericRoles = @()
    foreach ($role in $UserRoles) {
        if ($roleMap.ContainsKey($role)) {
            $numericRoles += $roleMap[$role]
        } else {
            Write-Warning "Unknown role: $role, skipping"
        }
    }

    # Default to Member role if no valid roles specified
    if ($numericRoles.Count -eq 0) {
        $numericRoles = @(7)  # Member
    }

    $createUserRequest = @{
        email = $UserEmail
        displayName = $UserDisplayName
        externalIdpUserId = $UserEmail  # Use email as external IDP user ID for local users
        roles = $numericRoles
    }

    $user = Invoke-ApiRequest `
        -Uri "$TenantServiceUrl/api/organizations/$resolvedOrgId/users" `
        -Method POST `
        -Headers @{ Authorization = "Bearer $adminToken" } `
        -Body $createUserRequest `
        -Description "Create user"

    Write-Success "User created successfully"
    Write-Info "User ID:" $user.id
    Write-Info "Email:" $user.email
    Write-Info "Display Name:" $user.displayName
    Write-Info "Roles:" ($user.roles -join ", ")
    Write-Info "Status:" $user.status
    Write-Info "Created At:" $user.createdAt

    Write-Host ""
    Write-Host "╔════════════════════════════════════════════════════════════════════════╗" -ForegroundColor Yellow
    Write-Host "║  ⚠️  IMPORTANT LIMITATION - OIDC Users vs Local Users                  ║" -ForegroundColor Yellow
    Write-Host "╚════════════════════════════════════════════════════════════════════════╝" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "The user has been created as an OIDC user (External IDP user)." -ForegroundColor White
    Write-Host "This means:" -ForegroundColor White
    Write-Host "  • User exists in the organization" -ForegroundColor Gray
    Write-Host "  • User can be assigned roles and permissions" -ForegroundColor Gray
    Write-Host "  • User CANNOT login with username/password" -ForegroundColor Yellow
    Write-Host "  • User is intended for OIDC/SSO authentication" -ForegroundColor Gray
    Write-Host ""
    Write-Host "To create a local user with password (for testing):" -ForegroundColor Cyan
    Write-Host "  Use the bootstrap endpoint: POST /api/tenants/bootstrap" -ForegroundColor White
    Write-Host "  Or use the Tenant Service DatabaseInitializer seed data" -ForegroundColor White
    Write-Host ""
    Write-Host "For production:" -ForegroundColor Cyan
    Write-Host "  Configure OIDC provider (Auth0, Azure AD, etc.)" -ForegroundColor White
    Write-Host "  Users authenticate via SSO" -ForegroundColor White
    Write-Host "  Tenant Service maps OIDC users to organizations" -ForegroundColor White
    Write-Host ""

    Write-Host "╔════════════════════════════════════════════════════════════════════════╗" -ForegroundColor Green
    Write-Host "║  ✅ PHASE 1 COMPLETE - USER CREATED IN ORGANIZATION ✅                  ║" -ForegroundColor Green
    Write-Host "╚════════════════════════════════════════════════════════════════════════╝" -ForegroundColor Green
    Write-Host ""

    Write-Host "Next Steps (Manual):" -ForegroundColor Cyan
    Write-Host "  1. Create user via bootstrap endpoint for password-based auth" -ForegroundColor White
    Write-Host "  2. Login with the user credentials" -ForegroundColor White
    Write-Host "  3. Create wallet using the user's token" -ForegroundColor White
    Write-Host "  4. Save the mnemonic phrase securely" -ForegroundColor White
    Write-Host ""

    # Skip wallet creation since user can't login
    Write-Host "Skipping wallet creation (requires user authentication)" -ForegroundColor Yellow
    Write-Host ""

    return

    #
    # NOTE: Steps 4-5 below are currently disabled because the created user
    # is an OIDC user without password authentication capability.
    # To enable these steps, create a local user via bootstrap endpoint first.
    #

    #
    # STEP 4: User Login (DISABLED - OIDC user cannot login with password)
    #
    Write-Section "Step 4: User Authentication"

    $userLoginRequest = @{
        email = $UserEmail
        password = $UserPassword
    }

    $userTokenResponse = Invoke-ApiRequest `
        -Uri "$TenantServiceUrl/api/auth/login" `
        -Method POST `
        -Body $userLoginRequest `
        -Description "User login"

    $userToken = $userTokenResponse.access_token
    Write-Success "User authenticated successfully"
    Write-Info "Token expires in: $($userTokenResponse.expires_in) seconds"
    Write-Info "Token type: $($userTokenResponse.token_type)"

    # Decode JWT to show claims (optional, for verification)
    if ($VerbosePreference -eq "Continue") {
        try {
            $tokenParts = $userToken.Split(".")
            if ($tokenParts.Length -ge 2) {
                $payloadBase64 = $tokenParts[1]
                # Pad base64 string
                $padding = 4 - ($payloadBase64.Length % 4)
                if ($padding -lt 4) {
                    $payloadBase64 += "=" * $padding
                }
                $payloadJson = [System.Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($payloadBase64))
                $payload = $payloadJson | ConvertFrom-Json

                Write-Verbose "Token Claims:"
                Write-Verbose "  sub (user_id): $($payload.sub)"
                Write-Verbose "  email: $($payload.email)"
                Write-Verbose "  name: $($payload.name)"
                Write-Verbose "  org_id: $($payload.org_id)"
                Write-Verbose "  roles: $($payload.roles)"
            }
        }
        catch {
            Write-Verbose "Could not decode token payload"
        }
    }

    #
    # STEP 5: Create Wallet
    #
    Write-Section "Step 5: Create Default Wallet"

    $createWalletRequest = @{
        name = $WalletName
        algorithm = $WalletAlgorithm
        wordCount = $MnemonicWordCount
    }

    $walletResponse = Invoke-ApiRequest `
        -Uri "$WalletServiceUrl/api/v1/wallets" `
        -Method POST `
        -Headers @{ Authorization = "Bearer $userToken" } `
        -Body $createWalletRequest `
        -Description "Create wallet"

    Write-Success "Wallet created successfully!"
    Write-Host ""
    Write-Host "    ┌─────────────────────────────────────────────────────────────┐" -ForegroundColor Yellow
    Write-Host "    │  WALLET DETAILS                                             │" -ForegroundColor Yellow
    Write-Host "    └─────────────────────────────────────────────────────────────┘" -ForegroundColor Yellow
    Write-Info "Address:" $walletResponse.wallet.address
    Write-Info "Name:" $walletResponse.wallet.name
    Write-Info "Algorithm:" $walletResponse.wallet.algorithm
    Write-Info "Status:" $walletResponse.wallet.status
    Write-Info "Public Key:" "$($walletResponse.wallet.publicKey.Substring(0, [Math]::Min(64, $walletResponse.wallet.publicKey.Length)))..."
    Write-Info "Created At:" $walletResponse.wallet.createdAt

    #
    # STEP 6: Display Mnemonic Warning
    #
    Write-Host ""
    Write-Host "╔════════════════════════════════════════════════════════════════════════╗" -ForegroundColor Red
    Write-Host "║  ⚠️  CRITICAL: SAVE YOUR MNEMONIC PHRASE ⚠️                            ║" -ForegroundColor Red
    Write-Host "╚════════════════════════════════════════════════════════════════════════╝" -ForegroundColor Red
    Write-Host ""
    Write-Host "Your $MnemonicWordCount-word mnemonic phrase:" -ForegroundColor Yellow
    Write-Host ""

    $mnemonicWords = $walletResponse.mnemonicWords
    for ($i = 0; $i -lt $mnemonicWords.Length; $i++) {
        $num = ($i + 1).ToString().PadLeft(2)
        Write-Host "  $num. $($mnemonicWords[$i])" -ForegroundColor White
    }

    Write-Host ""
    Write-Host "⚠️  WARNING: " -ForegroundColor Red -NoNewline
    Write-Host $walletResponse.warning -ForegroundColor Yellow
    Write-Host ""
    Write-Host "IMPORTANT SECURITY REMINDERS:" -ForegroundColor Red
    Write-Host "  • Write down these words on paper and store securely" -ForegroundColor Yellow
    Write-Host "  • NEVER share your mnemonic with anyone" -ForegroundColor Yellow
    Write-Host "  • NEVER store digitally (no screenshots, cloud storage, email)" -ForegroundColor Yellow
    Write-Host "  • This is the ONLY way to recover your wallet" -ForegroundColor Yellow
    Write-Host "  • Sorcha does NOT store your mnemonic - only you have access" -ForegroundColor Yellow
    Write-Host ""

    # Optionally save mnemonic to file (TESTING ONLY!)
    if ($SaveMnemonicPath) {
        $mnemonicData = @{
            walletAddress = $walletResponse.wallet.address
            userEmail = $UserEmail
            mnemonicWords = $mnemonicWords
            wordCount = $MnemonicWordCount
            algorithm = $WalletAlgorithm
            createdAt = (Get-Date -Format "o")
            warning = "⚠️  NEVER commit this file to source control! Delete after testing!"
        }

        $mnemonicData | ConvertTo-Json -Depth 5 | Set-Content -Path $SaveMnemonicPath -Encoding UTF8

        Write-Host "╔════════════════════════════════════════════════════════════════════════╗" -ForegroundColor Yellow
        Write-Host "║  ⚠️  TESTING MODE: Mnemonic saved to file                              ║" -ForegroundColor Yellow
        Write-Host "╚════════════════════════════════════════════════════════════════════════╝" -ForegroundColor Yellow
        Write-Info "File path:" $SaveMnemonicPath
        Write-Host "  ⚠️  DELETE THIS FILE AFTER TESTING!" -ForegroundColor Red
        Write-Host ""
    }

    #
    # STEP 7: Verify Wallet Ownership
    #
    Write-Section "Step 6: Verify Wallet Ownership"

    $wallets = Invoke-ApiRequest `
        -Uri "$WalletServiceUrl/api/v1/wallets" `
        -Method GET `
        -Headers @{ Authorization = "Bearer $userToken" } `
        -Description "List user wallets"

    Write-Success "User has $($wallets.Count) wallet(s)"

    foreach ($w in $wallets) {
        Write-Info "  - $($w.name): $($w.address) ($($w.algorithm))"
    }

    #
    # FINAL SUMMARY
    #
    Write-Host ""
    Write-Host "╔════════════════════════════════════════════════════════════════════════╗" -ForegroundColor Green
    Write-Host "║  ✅ PHASE 1 COMPLETE - USER AND WALLET CREATED SUCCESSFULLY ✅          ║" -ForegroundColor Green
    Write-Host "╚════════════════════════════════════════════════════════════════════════╝" -ForegroundColor Green
    Write-Host ""

    Write-Host "📋 USER DETAILS:" -ForegroundColor Cyan
    Write-Info "  Email:" $UserEmail
    Write-Info "  Display Name:" $UserDisplayName
    Write-Info "  User ID:" $user.id
    Write-Info "  Organization ID:" $resolvedOrgId
    if ($orgName) {
        Write-Info "  Organization Name:" $orgName
    }
    Write-Info "  Roles:" ($user.roles -join ", ")
    Write-Host ""

    Write-Host "🔐 WALLET DETAILS:" -ForegroundColor Cyan
    Write-Info "  Address:" $walletResponse.wallet.address
    Write-Info "  Name:" $WalletName
    Write-Info "  Algorithm:" $WalletAlgorithm
    Write-Info "  Mnemonic Words:" "$MnemonicWordCount words"
    Write-Host ""

    Write-Host "🎯 NEXT STEPS:" -ForegroundColor Yellow
    Write-Host "  1. Save the mnemonic phrase in a secure location (paper backup recommended)" -ForegroundColor White
    Write-Host "  2. Test user login:" -ForegroundColor White
    Write-Host "     .\scripts\test-user-login.ps1 -Email '$UserEmail' -Password '***'" -ForegroundColor Gray
    Write-Host "  3. Test wallet operations:" -ForegroundColor White
    Write-Host "     .\scripts\test-wallet-creation.ps1 -UserToken '***'" -ForegroundColor Gray
    Write-Host "  4. Create additional users for multi-user scenarios (Phase 2)" -ForegroundColor White
    Write-Host ""

    Write-Host "📚 DOCUMENTATION:" -ForegroundColor Cyan
    Write-Info "  Walkthrough README:" "walkthroughs/UserWalletCreation/README.md"
    Write-Info "  API Docs (Tenant):" "$TenantServiceUrl/scalar"
    Write-Info "  API Docs (Wallet):" "$WalletServiceUrl/scalar"
    Write-Host ""

}
catch {
    Write-Host ""
    Write-Host "╔════════════════════════════════════════════════════════════════════════╗" -ForegroundColor Red
    Write-Host "║  ❌ SCRIPT FAILED                                                      ║" -ForegroundColor Red
    Write-Host "╚════════════════════════════════════════════════════════════════════════╝" -ForegroundColor Red
    Write-Host ""
    Write-Host "Error: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Troubleshooting:" -ForegroundColor Yellow
    Write-Host "  • Verify all Sorcha services are running: docker-compose ps" -ForegroundColor White
    Write-Host "  • Check service health: curl $TenantServiceUrl/health" -ForegroundColor White
    Write-Host "  • Review logs: docker-compose logs tenant-service" -ForegroundColor White
    Write-Host "  • See walkthrough documentation for common issues" -ForegroundColor White
    Write-Host ""

    exit 1
}
