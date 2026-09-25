param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]]$Arguments
)

$ErrorActionPreference = "Stop"

$main = Join-Path $PSScriptRoot "codicks-lite-windows.ps1"
if (-not (Test-Path -LiteralPath $main)) {
    throw "Codicks Lite Windows command wrapper was not found: $main"
}

if (-not $Arguments -or $Arguments.Count -eq 0) {
    & $main "status"
    exit $LASTEXITCODE
}

$rest = if ($Arguments.Count -gt 1) {
    $rest
} else {
    @()
}

switch ($Arguments[0]) {
    "status" {
        & $main "status"
    }

    "--locked" {
        & $main "lock"
    }

    "--read" {
        & $main "read" @($Arguments[1..($Arguments.Count - 1)])
    }

    "--full" {
        & $main "full" @($Arguments[1..($Arguments.Count - 1)])
    }

    default {
        throw "Use status, --locked, --read, or --full."
    }
}

exit $LASTEXITCODE
