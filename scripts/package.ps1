<#
.SYNOPSIS
    Builds the release package: artifacts\dist\agent-secrets-<runtime>.zip

.DESCRIPTION
    The zip contains everything install.ps1 needs:
        agent-secrets.exe            self-contained, single file (no .NET runtime needed)
        skill\agent-secrets\SKILL.md the agent skill for Claude Code and Codex
        uninstall.ps1
        LICENSE
    Used by the release workflow and by 'install.ps1 -FromSource'. Requires the .NET 10 SDK.

.EXAMPLE
    .\scripts\package.ps1 -Runtime win-x64 -Version 0.2.0
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Runtime = 'win-x64',
    [string]$Version = '',
    [string]$OutputDir = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputDir) { $OutputDir = Join-Path $repoRoot 'artifacts\dist' }

$publishDir = Join-Path $repoRoot "artifacts\publish\$Runtime"
$stagingDir = Join-Path $repoRoot "artifacts\staging\$Runtime"
$zipPath = Join-Path $OutputDir "agent-secrets-$Runtime.zip"

$publishArgs = @(
    'publish', (Join-Path $repoRoot 'src\AgentSecrets.Cli\AgentSecrets.Cli.csproj'),
    '--configuration', 'Release', '--runtime', $Runtime, '--self-contained', 'true',
    '-p:PublishSingleFile=true', '-p:PublishTrimmed=true', '-p:DebugType=none',
    '--output', $publishDir, '--nologo', '--verbosity', 'quiet'
)
if ($Version) { $publishArgs += "-p:Version=$($Version.TrimStart('v'))" }

Write-Host "Publishing agent-secrets ($Runtime)..."
& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed (exit code $LASTEXITCODE)." }

foreach ($dir in @($stagingDir)) {
    if (Test-Path $dir) { Remove-Item $dir -Recurse -Force }
}
New-Item -ItemType Directory -Force -Path (Join-Path $stagingDir 'skill') | Out-Null
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

Copy-Item (Join-Path $publishDir 'agent-secrets.exe') $stagingDir
Copy-Item (Join-Path $repoRoot 'skill\agent-secrets') (Join-Path $stagingDir 'skill') -Recurse
Copy-Item (Join-Path $repoRoot 'scripts\uninstall.ps1') $stagingDir
Copy-Item (Join-Path $repoRoot 'LICENSE') $stagingDir

if (Test-Path $zipPath) { Remove-Item $zipPath -Force }
Compress-Archive -Path (Join-Path $stagingDir '*') -DestinationPath $zipPath
Write-Host "Package: $zipPath"
