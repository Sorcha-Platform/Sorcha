# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors

@{
    RootModule        = 'SorchaWalkthrough.psm1'
    ModuleVersion     = '1.0.0'
    GUID              = 'a3f7e8d1-4b2c-4e9a-b6d5-1c8f3a2e7b94'
    Author            = 'Sorcha Contributors'
    Description       = 'Shared helper module for Sorcha walkthrough scripts. Provides console output, HTTP, JWT, auth, idempotent resource management, register creation, blueprint publishing, and action execution functions.'
    PowerShellVersion = '7.0'

    FunctionsToExport = @(
        # Console output (T002)
        'Write-WtStep'
        'Write-WtSuccess'
        'Write-WtFail'
        'Write-WtInfo'
        'Write-WtWarn'
        'Write-WtBanner'

        # HTTP & JWT (T003-T005)
        'Invoke-SorchaApi'
        'Decode-SorchaJwt'
        'Get-SorchaErrorBody'

        # Environment & Auth (T006-T008)
        'Initialize-SorchaEnvironment'
        'Get-SorchaSecrets'
        'Connect-SorchaAdmin'

        # Idempotent Resource Management (T009-T013)
        'Get-OrCreateOrganization'
        'Get-OrCreateUser'
        'New-SorchaWallet'
        'Register-SorchaParticipant'
        'Publish-SorchaParticipant'

        # Register & Blueprint (T014-T016)
        'New-SorchaRegister'
        'Publish-SorchaBlueprint'
        'Invoke-SorchaAction'
        'Get-SorchaCredentialPresentation'

        # Utilities
        'ConvertFrom-HexToBase64'

        # Feature 124 — citizen pending-application notice helpers
        'Set-SorchaCitizenPendingApplication'
        'Clear-SorchaCitizenPendingApplication'
    )

    CmdletsToExport   = @()
    VariablesToExport  = @()
    AliasesToExport    = @()
}
