param([switch]$Verify, [switch]$Publish)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (-not (Test-Path -LiteralPath 'node_modules\typescript\lib\tsc.js')) {
        npm ci
        if ($LASTEXITCODE -ne 0) { throw 'npm ci failed' }
    }
    npm run build
    if ($LASTEXITCODE -ne 0) { throw 'TypeScript build failed' }
    dotnet build src\AIControl.Host\AIControl.Host.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw '.NET build failed' }
    if ($Verify) {
        dotnet run --project tests\AIControl.Checks\AIControl.Checks.csproj -c Release
        if ($LASTEXITCODE -ne 0) { throw 'Core checks failed' }
        & .\tests\Check-Identity.ps1
        npm test
        if ($LASTEXITCODE -ne 0) { throw 'Interface checks failed' }
    }
    if ($Publish) {
        dotnet publish src\AIControl.Host\AIControl.Host.csproj -c Release -r win-x64 --self-contained false -o artifacts\app
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed' }
        Write-Output "Ready: $PSScriptRoot\artifacts\app\AIControl.exe"
    }
} finally { Pop-Location }
