#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Delete the zip02 SAM stack and all associated resources.

.PARAMETER Env
    Target environment. Must be one of: dev, test, prod.
    Defaults to dev.

.PARAMETER Region
    AWS region. Defaults to eu-west-1.

.PARAMETER Profile
    AWS CLI named profile. Omit to use the default credential chain.

.PARAMETER Force
    Skip the interactive confirmation prompt.
    Use with care — prod tables have DeletionPolicy: Retain so the table
    data is preserved, but all other resources are deleted immediately.

.EXAMPLE
    # Tear down the dev stack
    .\teardown.ps1 -Env dev

.EXAMPLE
    # Force-delete prod (table data is retained)
    .\teardown.ps1 -Env prod -Force
#>
[CmdletBinding()]
param (
    [ValidateSet('dev', 'test', 'prod')]
    [string]$Env = 'dev',

    [string]$Region = 'eu-west-1',

    [string]$Profile = '',

    [switch]$Force
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$stackName = "zip02-$Env"

# ── Safety guard for prod ─────────────────────────────────────────────────────
# The DynamoDB table has DeletionPolicy: Retain for prod, so data survives.
# Everything else (Lambda, API Gateway, IAM role, DLQ) will be deleted.
if ($Env -eq 'prod' -and -not $Force) {
    Write-Warning "You are about to delete the PROD stack ($stackName)."
    Write-Warning "The DynamoDB table will be RETAINED, but all other resources will be removed."
    $confirm = Read-Host 'Type the stack name to confirm'
    if ($confirm -ne $stackName) {
        Write-Host 'Teardown cancelled.' -ForegroundColor Yellow
        exit 0
    }
}

Write-Host "`n=== zip02 teardown — env: $Env  region: $Region  stack: $stackName ===`n" -ForegroundColor Cyan

$deleteArgs = @(
    'delete'
    '--stack-name', $stackName
    '--region', $Region
    '--no-prompts'
)
if ($Profile) { $deleteArgs += '--profile', $Profile }

& sam @deleteArgs
if ($LASTEXITCODE -ne 0) { throw "sam delete failed (exit $LASTEXITCODE)" }

Write-Host "`n=== Teardown complete ===`n" -ForegroundColor Green
