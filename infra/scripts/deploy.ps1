#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Build and deploy the zip02 SAM stack to AWS.

.PARAMETER Env
    Target environment. Must be one of: dev, test, prod.
    Defaults to dev.

.PARAMETER Region
    AWS region to deploy to. Defaults to eu-west-1.

.PARAMETER Profile
    AWS CLI named profile. Omit to use the default credential chain
    (instance profile, environment variables, etc.).

.EXAMPLE
    # Deploy to dev using the default credential chain
    .\deploy.ps1 -Env dev

.EXAMPLE
    # Deploy to prod with an explicit profile
    .\deploy.ps1 -Env prod -Region eu-west-1 -Profile my-prod-profile
#>
[CmdletBinding()]
param (
    [ValidateSet('dev', 'test', 'prod')]
    [string]$Env = 'dev',

    [string]$Region = 'eu-west-1',

    [string]$Profile = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir   = $PSScriptRoot
$repoRoot    = Resolve-Path (Join-Path $scriptDir '../..')
$templatePath = Join-Path $scriptDir '../sam/template.yaml'
$paramFile    = Join-Path $scriptDir "../sam/parameters/$Env.json"
$stackName    = "zip02-$Env"

if (-not (Test-Path $paramFile)) {
    Write-Error "Parameter file not found: $paramFile"
    exit 1
}

Write-Host "`n=== zip02 deploy — env: $Env  region: $Region  stack: $stackName ===`n" -ForegroundColor Cyan

# ── 1. sam build ──────────────────────────────────────────────────────────────
# Compiles and packages the Lambda function inside a Docker container that
# matches the provided.al2023 runtime. The --use-container flag ensures the
# linux-x64 self-contained binary is built correctly on any host OS.
Write-Host '--- sam build ---' -ForegroundColor Yellow
$buildArgs = @(
    'build'
    '--template-file', $templatePath
    '--use-container'       # build inside a Lambda-compatible Docker image
    '--cached'              # skip unchanged functions on incremental builds
)
Push-Location $repoRoot
try {
    & sam @buildArgs
    if ($LASTEXITCODE -ne 0) { throw "sam build failed (exit $LASTEXITCODE)" }
} finally {
    Pop-Location
}

# ── 2. sam deploy ─────────────────────────────────────────────────────────────
Write-Host "`n--- sam deploy ---" -ForegroundColor Yellow
$deployArgs = @(
    'deploy'
    '--template-file',          (Join-Path $repoRoot '.aws-sam/build/template.yaml')
    '--stack-name',             $stackName
    '--region',                 $Region
    '--parameter-overrides',    "file://$paramFile"
    '--capabilities',           'CAPABILITY_IAM', 'CAPABILITY_NAMED_IAM'
    '--no-fail-on-empty-changeset'   # idempotent re-deploys do not error
    '--resolve-s3'              # SAM auto-creates a deployment S3 bucket
)

if ($Profile) {
    $deployArgs += '--profile', $Profile
}

& sam @deployArgs
if ($LASTEXITCODE -ne 0) { throw "sam deploy failed (exit $LASTEXITCODE)" }

# ── 3. Print the API URL from stack outputs ───────────────────────────────────
Write-Host "`n--- Stack outputs ---" -ForegroundColor Yellow
$awsArgs = @(
    'cloudformation', 'describe-stacks'
    '--stack-name', $stackName
    '--region', $Region
    '--query', 'Stacks[0].Outputs'
    '--output', 'table'
)
if ($Profile) { $awsArgs += '--profile', $Profile }
& aws @awsArgs

Write-Host "`n=== Deploy complete ===`n" -ForegroundColor Green
