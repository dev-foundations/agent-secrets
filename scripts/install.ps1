<#
.SYNOPSIS
    Installs AgentSecrets for the current Windows user. No Administrator rights needed.

.DESCRIPTION
    One-line install (PowerShell 5.1 or 7+):

        irm https://github.com/dev-foundations/agent-secrets/releases/latest/download/install.ps1 | iex

    What it does, after showing the plan and asking once:
      1. Downloads the release package for your CPU (x64 or ARM64) and verifies its SHA-256.
      2. Installs agent-secrets.exe to %LOCALAPPDATA%\AgentSecrets\bin (self-contained, no .NET needed).
      3. Adds that directory to your *user* PATH (HKCU; the system PATH is not touched).
      4. Installs the agent skill for Claude Code (~\.claude\skills) and Codex (~\.agents\skills).

    Running it again upgrades in place, also while agent-secrets is running.
    Your secrets (Windows Credential Manager) are never touched.

    To pass options with the one-liner:
        & ([scriptblock]::Create((irm https://github.com/dev-foundations/agent-secrets/releases/latest/download/install.ps1))) -Version v0.1.0 -Yes

.PARAMETER Version
    Release tag to install, e.g. v0.1.0. Default: the latest release.

.PARAMETER FromSource
    Build from this local clone instead of downloading (requires the .NET 10 SDK).

.PARAMETER PackagePath
    Install from a local agent-secrets-<runtime>.zip (offline installs, testing).

.PARAMETER NoPath
    Do not modify PATH.

.PARAMETER NoSkills
    Do not install the Claude Code / Codex skill.

.PARAMETER CodexHomeSkills
    Install the Codex skill to $CODEX_HOME\skills (default ~\.codex\skills) instead of
    ~\.agents\skills. Codex scans both, so only one is installed: a skill present in both
    directories is listed twice.

.PARAMETER Yes
    Do not ask for confirmation (unattended installs).
#>
[CmdletBinding()]
param(
    [string]$Version = 'latest',
    [string]$Repository = 'dev-foundations/agent-secrets',
    [string]$InstallDir = '',
    [ValidateSet('', 'win-x64', 'win-arm64')]
    [string]$Runtime = '',
    [string]$PackagePath = '',
    [switch]$FromSource,
    [switch]$NoPath,
    [switch]$NoSkills,
    [switch]$CodexHomeSkills,
    [switch]$Yes
)

# Everything runs in a child scope so that, when this script is piped into Invoke-Expression,
# StrictMode and ErrorActionPreference do not leak into the caller's session. Errors are thrown,
# never 'exit', because 'exit' would close the caller's terminal.
& {
    $ErrorActionPreference = 'Stop'
    $ProgressPreference = 'SilentlyContinue'   # Invoke-WebRequest is very slow with the progress bar in PS 5.1
    Set-StrictMode -Version 2.0

    if ($env:OS -ne 'Windows_NT') { throw 'AgentSecrets supports Windows only.' }

    if (-not $InstallDir) { $InstallDir = Join-Path $env:LOCALAPPDATA 'AgentSecrets' }
    $binDir = Join-Path $InstallDir 'bin'
    $exePath = Join-Path $binDir 'agent-secrets.exe'
    # Codex scans both ~\.agents\skills (its documented user-level location) and
    # $CODEX_HOME\skills (default ~\.codex\skills). It does NOT de-duplicate: a skill present in
    # both is listed twice. So exactly one is installed - the documented one by default.
    $codexHome = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path $HOME '.codex' }
    $codexHomeSkillPath = Join-Path $codexHome 'skills\agent-secrets'
    $agentsSkillPath = Join-Path $HOME '.agents\skills\agent-secrets'
    $codexSkill = if ($CodexHomeSkills) { $codexHomeSkillPath } else { $agentsSkillPath }
    $staleSkill = if ($CodexHomeSkills) { $agentsSkillPath } else { $codexHomeSkillPath }
    $skillTargets = @(
        @{ Agent = 'Claude Code'; Path = (Join-Path $HOME '.claude\skills\agent-secrets') },
        @{ Agent = 'Codex';       Path = $codexSkill }
    )

    function Test-SamePath([string]$A, [string]$B) {
        $left = [Environment]::ExpandEnvironmentVariables($A).Trim().TrimEnd('\')
        $right = [Environment]::ExpandEnvironmentVariables($B).Trim().TrimEnd('\')
        return $left -ieq $right
    }

    function Get-UserPathEntries {
        # The raw registry value keeps %VARIABLES% in the user's PATH unexpanded.
        $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment')
        try {
            $raw = [string]$key.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        } finally {
            $key.Close()
        }
        return @($raw -split ';' | Where-Object { $_ -ne '' })
    }

    function Get-DefaultRuntime {
        $arch = ''
        try { $arch = [string][System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture } catch { }
        if (-not $arch) {
            $arch = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
        }
        if ($arch -match 'arm64') { return 'win-arm64' }
        return 'win-x64'
    }

    if (-not $Runtime) { $Runtime = Get-DefaultRuntime }
    $packageName = "agent-secrets-$Runtime.zip"

    if ($FromSource -and $PackagePath) { throw '-FromSource and -PackagePath cannot be combined.' }
    if ($FromSource -and -not $PSScriptRoot) { throw '-FromSource works only when running install.ps1 from a clone of the repository.' }

    if ($PackagePath) {
        $source = "local package $PackagePath"
    } elseif ($FromSource) {
        $source = "build from source ($(Split-Path -Parent $PSScriptRoot))"
    } else {
        $source = "GitHub release '$Version' of $Repository ($Runtime)"
    }

    # --- Plan -----------------------------------------------------------------------------------

    $pathPresent = [bool](Get-UserPathEntries | Where-Object { Test-SamePath $_ $binDir })
    Write-Host ''
    Write-Host 'AgentSecrets installer' -ForegroundColor Cyan
    Write-Host "  Source  : $source"
    Write-Host "  Program : $exePath"
    if ($NoPath) {
        Write-Host '  PATH    : not changed (-NoPath)'
    } elseif ($pathPresent) {
        Write-Host '  PATH    : already contains the install directory'
    } else {
        Write-Host "  PATH    : add $binDir to YOUR user PATH (HKCU\Environment; no admin rights, system PATH untouched)"
    }
    if ($NoSkills) {
        Write-Host '  Skills  : not installed (-NoSkills)'
    } else {
        foreach ($target in $skillTargets) { Write-Host ("  Skill   : {0}   ({1})" -f $target.Path, $target.Agent) }
    }
    Write-Host '  Secrets : not touched (they stay in Windows Credential Manager)'
    Write-Host ''

    if (-not $Yes) {
        if ([Console]::IsInputRedirected) {
            throw 'No interactive terminal to confirm in. Re-run with -Yes for an unattended install.'
        }
        $answer = Read-Host 'Continue? [Y/n]'
        if ($answer -and $answer -notmatch '^(y|yes)$') {
            Write-Host 'Nothing changed.'
            return
        }
    }

    # --- 1. Get the package -----------------------------------------------------------------------

    $work = Join-Path ([System.IO.Path]::GetTempPath()) ("agentsecrets-install-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force -Path $work | Out-Null
    try {
        if ($PackagePath) {
            $zip = (Resolve-Path $PackagePath).Path
        } elseif ($FromSource) {
            if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
                throw 'Building from source needs the .NET 10 SDK: https://dotnet.microsoft.com/download'
            }
            & (Join-Path $PSScriptRoot 'package.ps1') -Runtime $Runtime -OutputDir $work
            $zip = Join-Path $work $packageName
        } else {
            [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
            if ($Version -eq 'latest') {
                $baseUrl = "https://github.com/$Repository/releases/latest/download"
            } else {
                $tag = if ($Version.StartsWith('v')) { $Version } else { "v$Version" }
                $baseUrl = "https://github.com/$Repository/releases/download/$tag"
            }

            $zip = Join-Path $work $packageName
            $sums = Join-Path $work 'SHA256SUMS.txt'
            Write-Host "Downloading $baseUrl/$packageName"
            try {
                Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/$packageName" -OutFile $zip
                Invoke-WebRequest -UseBasicParsing -Uri "$baseUrl/SHA256SUMS.txt" -OutFile $sums
            } catch {
                throw "Download failed. Check the version ('$Version') and your connection. ($($_.Exception.Message))"
            }

            $expected = $null
            foreach ($line in Get-Content $sums) {
                $parts = $line.Trim() -split '\s+\*?', 2
                if ($parts.Count -eq 2 -and $parts[1] -eq $packageName) { $expected = $parts[0] }
            }
            if (-not $expected) { throw "SHA256SUMS.txt has no entry for $packageName." }
            $actual = (Get-FileHash -Algorithm SHA256 -Path $zip).Hash
            if ($actual -ne $expected.ToUpperInvariant()) {
                throw "Checksum mismatch for $packageName. The download is corrupt or was tampered with; nothing was installed."
            }
            Write-Host 'Checksum verified (SHA-256).'
        }

        $staging = Join-Path $work 'package'
        Expand-Archive -Path $zip -DestinationPath $staging -Force
        $newExe = Join-Path $staging 'agent-secrets.exe'
        if (-not (Test-Path $newExe)) { throw "The package does not contain agent-secrets.exe: $zip" }

        # --- 2. Program ----------------------------------------------------------------------------

        New-Item -ItemType Directory -Force -Path $binDir | Out-Null
        # Leftovers from upgrades done while agent-secrets was running (see below).
        Get-ChildItem $binDir -Filter '*.old-*' -ErrorAction SilentlyContinue |
            ForEach-Object { Remove-Item $_.FullName -Force -ErrorAction SilentlyContinue }

        try {
            Copy-Item $newExe $exePath -Force
        } catch {
            # A running .exe cannot be overwritten, but it can be renamed. Move it aside so the
            # running copy keeps working and new invocations get the new version.
            $aside = "$exePath.old-$([DateTime]::UtcNow.ToString('yyyyMMddHHmmss'))"
            Move-Item $exePath $aside -Force
            Copy-Item $newExe $exePath -Force
            Write-Host 'Note: agent-secrets was running; the running copy finishes on the old version.'
        }
        Copy-Item (Join-Path $staging 'uninstall.ps1') (Join-Path $InstallDir 'uninstall.ps1') -Force
        Write-Host "Installed: $exePath" -ForegroundColor Green

        # --- 3. PATH -------------------------------------------------------------------------------

        if (-not $NoPath) {
            if (-not $pathPresent) {
                $entries = Get-UserPathEntries
                $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $true)
                try {
                    $key.SetValue('Path', ((@($entries) + $binDir) -join ';'), [Microsoft.Win32.RegistryValueKind]::ExpandString)
                } finally {
                    $key.Close()
                }
                # Setting a user variable through .NET broadcasts WM_SETTINGCHANGE, so terminals
                # opened from now on see the new PATH without signing out.
                [Environment]::SetEnvironmentVariable('AGENTSECRETS_INSTALL_NOTIFY', '1', 'User')
                [Environment]::SetEnvironmentVariable('AGENTSECRETS_INSTALL_NOTIFY', $null, 'User')
                Write-Host "PATH: added $binDir" -ForegroundColor Green
            }
            # Also make it usable in this terminal right away.
            if (-not (($env:Path -split ';') | Where-Object { $_ -and (Test-SamePath $_ $binDir) })) {
                $env:Path = "$env:Path;$binDir"
            }
        }

        # --- 4. Agent skill ------------------------------------------------------------------------

        if (-not $NoSkills) {
            $skillSource = Join-Path $staging 'skill\agent-secrets'
            # Remove the copy in the other supported directory, so Codex never lists the skill twice.
            if (Test-Path $staleSkill) {
                Remove-Item $staleSkill -Recurse -Force
                Write-Host "Removed the duplicate skill copy in $staleSkill"
            }
            foreach ($target in $skillTargets) {
                if (Test-Path $target.Path) { Remove-Item $target.Path -Recurse -Force }
                New-Item -ItemType Directory -Force -Path $target.Path | Out-Null
                Copy-Item (Join-Path $skillSource '*') $target.Path -Recurse -Force
                Write-Host ("Skill installed for {0}: {1}" -f $target.Agent, $target.Path) -ForegroundColor Green
            }
        }

        $installed = & $exePath --version
        Write-Host ''
        Write-Host "$installed is ready." -ForegroundColor Cyan
        Write-Host 'Next steps:'
        Write-Host '    agent-secrets doctor'
        Write-Host '    agent-secrets set openai          (you type or paste the key; it is not shown)'
        Write-Host '    agent-secrets run --env OPENAI_API_KEY=openai -- python app.py'
        if (-not $NoPath -and -not $pathPresent) {
            Write-Host 'Other terminals that were already open need to be restarted to find agent-secrets.'
        }
        if (-not $NoSkills) {
            Write-Host 'Restart Claude Code / Codex so they load the skill.'
        }
    } finally {
        Remove-Item $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}
