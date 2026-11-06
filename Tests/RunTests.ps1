# Builds and runs the offline kernel, production rendering lifecycle and UI event-sequence checks. The UI checks run on the bundle that UI\ builds (UI\build).
# Usage: powershell -ExecutionPolicy Bypass -File Tests\RunTests.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root "Tests\CDLC.TS.Tests\CDLC.TS.Tests.csproj"

dotnet build $project -c Release --nologo -v quiet
if ($LASTEXITCODE -ne 0) { throw "Test build failed" }

Push-Location (Join-Path $root "UI")
try {
    if (-not (Test-Path "node_modules\webpack-cli\bin\cli.js")) {
        npm ci --no-audit --no-fund
        if ($LASTEXITCODE -ne 0) { throw "npm ci failed" }
    }

    npm run build
    if ($LASTEXITCODE -ne 0) { throw "UI build failed" }
}
finally {
    Pop-Location
}

& (Join-Path $root "Tests\CDLC.TS.Tests\bin\CDLC.TS.Tests.exe") $root
$model = $LASTEXITCODE

node (Join-Path $root "Tests\ui-smoke.mjs") $root
$ui = $LASTEXITCODE

node (Join-Path $root "Tests\ui-edit-sequences.mjs") $root
$editor = $LASTEXITCODE

dotnet run --project (Join-Path $root "Tests\RenderLifecycle\RenderLifecycle.csproj") -c Release
$render = $LASTEXITCODE

if ($model -ne 0 -or $ui -ne 0 -or $editor -ne 0 -or $render -ne 0) {
    Write-Host "TESTS FAILED"
    exit 1
}

Write-Host "ALL TESTS PASSED"
