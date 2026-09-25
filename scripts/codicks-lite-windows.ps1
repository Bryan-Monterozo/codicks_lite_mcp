param(
    [Parameter(Position = 0)]
    [string]$Command = "help",

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArgs
)

$ErrorActionPreference = "Stop"

function Get-AppRoot {
    if ($env:CODICKS_LITE_APP_ROOT) {
        return $env:CODICKS_LITE_APP_ROOT
    }

    if (-not $env:LOCALAPPDATA) {
        throw "LOCALAPPDATA is not available."
    }

    return Join-Path $env:LOCALAPPDATA "CodicksLiteMcp"
}

function Get-HostPath {
    if ($env:CODICKS_LITE_HOST_PATH) {
        return $env:CODICKS_LITE_HOST_PATH
    }

    $appRoot = Get-AppRoot
    $currentPointer = Join-Path $appRoot "current.txt"

    if (Test-Path -LiteralPath $currentPointer) {
        $releaseName = (Get-Content -LiteralPath $currentPointer -Raw).Trim()

        if ($releaseName -and
            $releaseName -notmatch '[\\/:]' -and
            $releaseName -ne "." -and
            $releaseName -ne "..") {
            $candidate = Join-Path $appRoot (Join-Path "releases" (Join-Path $releaseName "LocalAgent.Host.exe"))

            if (Test-Path -LiteralPath $candidate) {
                return $candidate
            }
        }
    }

    $compatibilityCandidate = Join-Path $appRoot "current\LocalAgent.Host.exe"
    if (Test-Path -LiteralPath $compatibilityCandidate) {
        return $compatibilityCandidate
    }

    throw "Codicks Lite host executable was not found. Set CODICKS_LITE_HOST_PATH or install Codicks Lite."
}

function Invoke-Host {
    param(
        [Parameter(Mandatory = $true)]
        [string[]]$HostArgs
    )

    $hostPath = Get-HostPath
    & $hostPath @HostArgs
    exit $LASTEXITCODE
}

function Open-TextFile {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path
    )

    if (-not (Test-Path -LiteralPath $Path)) {
        throw "File does not exist: $Path"
    }

    if ($env:EDITOR) {
        & $env:EDITOR $Path
        exit $LASTEXITCODE
    }

    Start-Process -FilePath "notepad.exe" -ArgumentList @($Path)
    exit 0
}

function Show-Help {
@"
Codicks Lite command center

Tunnel/config:
  codicks-lite setup
  codicks-lite config-init
  codicks-lite config
  codicks-lite config-show
  codicks-lite config-edit
  codicks-lite apply
  codicks-lite run
  codicks-lite doctor
  codicks-lite doctor-local
  codicks-lite agent-config

Session security:
  codicks-lite status
  codicks-lite lock
  codicks-lite read --otp <OTP> [--for <minutes>]
  codicks-lite full --otp <OTP> [--for <minutes>]

Update backups:
  codicks-lite backup-list
  codicks-lite backup-show <backupId>
  codicks-lite backup-restore <backupId>

Release management:
  codicks-lite version
  codicks-lite releases
  codicks-lite rollback
  codicks-lite paths

Direct host:
  codicks-lite host
"@
}

switch ($Command.ToLowerInvariant()) {
    { $_ -in @("help", "-h", "--help") } {
        Show-Help
        exit 0
    }

    "setup" {
        Invoke-Host (@("--local-deployment", "all") + $RemainingArgs)
    }

    "config-init" {
        Invoke-Host (@("--local-deployment", "init") + $RemainingArgs)
    }

    { $_ -in @("config", "configure") } {
        Invoke-Host (@("--local-deployment", "configure") + $RemainingArgs)
    }

    { $_ -in @("config-show", "show") } {
        Invoke-Host (@("--local-deployment", "show") + $RemainingArgs)
    }

    "config-edit" {
        $path = Join-Path (Get-AppRoot) "config\deployment.json"
        Open-TextFile $path
    }

    "apply" {
        Invoke-Host (@("--local-deployment", "apply") + $RemainingArgs)
    }

    "run" {
        Invoke-Host (@("--local-deployment", "run") + $RemainingArgs)
    }

    "doctor" {
        Invoke-Host (@("--local-deployment", "doctor") + $RemainingArgs)
    }

    "doctor-local" {
        Invoke-Host @("--local-info", "doctor-local")
    }

    "agent-config" {
        $path = Join-Path (Get-AppRoot) "config\agent.json"
        Open-TextFile $path
    }

    { $_ -in @("status", "session") } {
        Invoke-Host @("--local-session", "status")
    }

    { $_ -in @("lock", "locked") } {
        Invoke-Host @("--local-session", "lock")
    }

    "read" {
        Invoke-Host (@("--local-session", "read") + $RemainingArgs)
    }

    "full" {
        Invoke-Host (@("--local-session", "full") + $RemainingArgs)
    }

    "backup-list" {
        Invoke-Host @("--local-backup-list")
    }

    "backup-show" {
        Invoke-Host (@("--local-backup-show") + $RemainingArgs)
    }

    "backup-restore" {
        Invoke-Host (@("--local-backup-restore") + $RemainingArgs)
    }

    "version" {
        Invoke-Host @("--local-info", "version")
    }

    "releases" {
        Invoke-Host @("--local-info", "releases")
    }

    "rollback" {
        Invoke-Host @("--local-info", "rollback")
    }

    "paths" {
        Invoke-Host @("--local-info", "paths")
    }

    "host" {
        Invoke-Host $RemainingArgs
    }

    default {
        Write-Error "Unknown Codicks Lite command: $Command"
        Show-Help
        exit 2
    }
}
