<#
.SYNOPSIS
    Removes AgentSecrets for the current Windows user.

.DESCRIPTION
    One-line uninstall:

        irm https://github.com/dev-foundations/agent-secrets/releases/latest/download/uninstall.ps1 | iex

    or run the copy the installer left in %LOCALAPPDATA%\AgentSecrets\uninstall.ps1.

    Removes the program, the PATH entry and the Claude Code / Codex skills.
    Your stored secrets are KEPT unless you pass -RemoveSecrets.

.PARAMETER RemoveSecrets
    Also delete every secret stored by AgentSecrets from Windows Credential Manager.

.PARAMETER KeepSkills
    Leave the Claude Code / Codex skill directories in place.

.PARAMETER Yes
    Do not ask for confirmation.
#>
[CmdletBinding()]
param(
    [string]$InstallDir = '',
    [switch]$RemoveSecrets,
    [switch]$KeepSkills,
    [switch]$Yes
)

# Child scope and no 'exit': safe to run through Invoke-Expression (see install.ps1).
& {
    $ErrorActionPreference = 'Stop'
    Set-StrictMode -Version 2.0

    if (-not $InstallDir) { $InstallDir = Join-Path $env:LOCALAPPDATA 'AgentSecrets' }
    $binDir = Join-Path $InstallDir 'bin'
    $exe = Join-Path $binDir 'agent-secrets.exe'
    $skillDirs = @((Join-Path $HOME '.claude\skills\agent-secrets'), (Join-Path $HOME '.agents\skills\agent-secrets'))

    function Test-SamePath([string]$A, [string]$B) {
        $left = [Environment]::ExpandEnvironmentVariables($A).Trim().TrimEnd('\')
        $right = [Environment]::ExpandEnvironmentVariables($B).Trim().TrimEnd('\')
        return $left -ieq $right
    }

    # Refuse up front (changing nothing) while agent-secrets is running: its files cannot be deleted
    # and a half-finished uninstall is worse than none.
    $installRoot = [System.IO.Path]::GetFullPath($InstallDir).TrimEnd('\') + '\'
    $running = @(Get-Process -Name 'agent-secrets*' -ErrorAction SilentlyContinue | Where-Object {
        $path = $null
        try { $path = $_.Path } catch { }
        $path -and $path.StartsWith($installRoot, [StringComparison]::OrdinalIgnoreCase)
    })
    if ($running.Count -gt 0) {
        $ids = ($running | ForEach-Object { $_.Id }) -join ', '
        throw "agent-secrets is running (process id $ids), probably an 'agent-secrets run' that has not finished. Let it finish or close it, then run the uninstaller again. Nothing was changed."
    }

    if (-not $Yes) {
        if ([Console]::IsInputRedirected) { throw 'No interactive terminal to confirm in. Re-run with -Yes.' }
        $what = "AgentSecrets ($InstallDir), its PATH entry"
        if (-not $KeepSkills) { $what += ' and the agent skills' }
        if ($RemoveSecrets) { $what += ', AND ALL SECRETS stored by AgentSecrets' }
        $answer = Read-Host "Remove $what? [y/N]"
        if ($answer -notmatch '^(y|yes)$') { Write-Host 'Nothing changed.'; return }
    }

    # --- Secrets (only on request) --------------------------------------------------------------

    if ($RemoveSecrets) {
        if (Test-Path $exe) {
            foreach ($name in @(& $exe list 2>$null)) {
                if ($name) {
                    & $exe remove $name --yes | Out-Null
                    Write-Host "Removed secret '$name'."
                }
            }
        } else {
            Write-Warning "agent-secrets.exe not found, so secrets were not removed. Delete the 'AgentSecrets/...' entries in Control Panel > Credential Manager > Windows Credentials."
        }
    } else {
        Write-Host 'Secrets were kept (Windows Credential Manager, "AgentSecrets/<name>"). A reinstall picks them up again.'
    }

    # --- PATH -----------------------------------------------------------------------------------

    $key = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey('Environment', $true)
    try {
        $raw = [string]$key.GetValue('Path', '', [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
        $entries = @($raw -split ';' | Where-Object { $_ -ne '' })
        $kept = @($entries | Where-Object { -not (Test-SamePath $_ $binDir) })
        if ($kept.Count -ne $entries.Count) {
            $key.SetValue('Path', ($kept -join ';'), [Microsoft.Win32.RegistryValueKind]::ExpandString)
            [Environment]::SetEnvironmentVariable('AGENTSECRETS_INSTALL_NOTIFY', '1', 'User')
            [Environment]::SetEnvironmentVariable('AGENTSECRETS_INSTALL_NOTIFY', $null, 'User')
            Write-Host 'PATH: entry removed.'
        }
    } finally {
        $key.Close()
    }
    $env:Path = (($env:Path -split ';') | Where-Object { $_ -and -not (Test-SamePath $_ $binDir) }) -join ';'

    # --- Files ----------------------------------------------------------------------------------

    if (Test-Path $InstallDir) {
        Remove-Item $InstallDir -Recurse -Force
        Write-Host "Removed $InstallDir"
    }

    if (-not $KeepSkills) {
        foreach ($skillDir in $skillDirs) {
            if (Test-Path $skillDir) {
                Remove-Item $skillDir -Recurse -Force
                Write-Host "Removed skill: $skillDir"
            }
        }
    }

    Write-Host 'AgentSecrets was uninstalled.'
}
