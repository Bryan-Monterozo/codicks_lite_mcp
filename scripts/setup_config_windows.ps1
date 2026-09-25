param(
    [Parameter(Position = 0)]
    [string]$Action = "configure",

    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$RemainingArgs
)

$ErrorActionPreference = "Stop"

$main = Join-Path $PSScriptRoot "codicks-lite-windows.ps1"
if (-not (Test-Path -LiteralPath $main)) {
    throw "Codicks Lite Windows command wrapper was not found: $main"
}

$command = switch ($Action.ToLowerInvariant()) {
    "init" { "config-init" }
    "configure" { "config" }
    "show" { "config-show" }
    "apply" { "apply" }
    "doctor" { "doctor" }
    "run" { "run" }
    "all" { "setup" }
    "edit" { "config-edit" }
    default { throw "Unknown setup action: $Action" }
}

& $main $command @RemainingArgs
exit $LASTEXITCODE
