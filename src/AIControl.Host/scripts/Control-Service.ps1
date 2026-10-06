param(
    [Parameter(Mandatory)][ValidateSet('Start','Stop')][string]$Action,
    [Parameter(Mandatory)][ValidateSet('ComfyUI','Llama')][string]$Kind,
    [Parameter(Mandatory)][string]$InstallDirectory,
    [Parameter(Mandatory)][ValidateRange(1024,65535)][int]$Port,
    [string]$Model = 'qwen2.5-coder-14b-instruct.gguf',
    [ValidateRange(2048,131072)][int]$ContextSize = 32768,
    [ValidatePattern('^(auto|all|[0-9]{1,3})$')][string]$GpuLayers = 'auto'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Identity.ps1')
$installRoot = [IO.Path]::GetFullPath($InstallDirectory)
$serverExe = if ($Kind -eq 'ComfyUI') { Join-Path $installRoot 'python_embeded\python.exe' } else { Join-Path $installRoot 'llama.cpp\build\bin\llama-server.exe' }
$id = if ($Kind -eq 'ComfyUI') { 'comfy' } else { 'coding' }
$logFolder = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) ('AIControl\' + $id)
$pidFile = Join-Path $logFolder 'server.json'
$url = 'http://127.0.0.1:' + $Port
New-Item -ItemType Directory -Path $logFolder -Force | Out-Null
$listener = Get-NetTCPConnection -State Listen -LocalPort $Port -ErrorAction SilentlyContinue | Select-Object -First 1
$serverProcess = if ($listener) { Get-CimInstance Win32_Process -Filter ('ProcessId=' + $listener.OwningProcess) } else { $null }
if ($serverProcess -and -not (Test-ServiceProcessIdentity $serverProcess $serverExe $Kind)) { throw 'Another application is using the port. It will not be changed.' }
if (-not $serverProcess -and (Test-Path -LiteralPath $pidFile)) {
    $stored = Get-Content -LiteralPath $pidFile -Raw | ConvertFrom-Json
    $candidate = Get-CimInstance Win32_Process -Filter ('ProcessId=' + [int]$stored.ProcessId)
    if ($candidate -and $stored.Port -eq $Port -and (Test-ServiceProcessIdentity $candidate $serverExe $Kind) -and $candidate.CreationDate.ToUniversalTime().ToString('o') -eq $stored.CreatedUtc) { $serverProcess = $candidate }
}
if ($Action -eq 'Stop') {
    if (-not $serverProcess) { Write-Output 'The server is already stopped.'; exit 0 }
    Stop-VerifiedServiceProcess $serverProcess $serverExe
    Write-Output 'The server has stopped.'; exit 0
}
if (-not $serverProcess) {
    if (-not (Test-Path -LiteralPath $serverExe -PathType Leaf)) { throw ('Server executable missing: ' + $serverExe) }
    if ($Kind -eq 'ComfyUI') {
        $env:PYTHONNOUSERSITE = '1'; $env:HF_HUB_DISABLE_TELEMETRY = '1'
        $arguments = @('-s','-u','ComfyUI/main.py','--windows-standalone-build','--listen','127.0.0.1','--port',[string]$Port,'--disable-auto-launch','--disable-partner-nodes')
    } else {
        if ([IO.Path]::GetFileName($Model) -ne $Model -or -not $Model.EndsWith('.gguf',[StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid model filename.' }
        $modelPath = Join-Path (Join-Path $installRoot 'models') $Model
        if (-not (Test-Path -LiteralPath $modelPath -PathType Leaf)) { throw ('Model missing: ' + $Model) }
        $env:PATH = (Split-Path -Parent $serverExe) + ';' + $env:PATH
        $arguments = @('-m', ('"' + $modelPath + '"'), '--alias','local-coder','--host','127.0.0.1','--port',[string]$Port,'--ctx-size',[string]$ContextSize,'--n-gpu-layers',$GpuLayers,'--fit-target','1536','--parallel','1','--slots','--metrics','--jinja')
    }
    $started = Start-Process -FilePath $serverExe -ArgumentList $arguments -WorkingDirectory $installRoot -WindowStyle Hidden -RedirectStandardOutput (Join-Path $logFolder 'server.out.log') -RedirectStandardError (Join-Path $logFolder 'server.err.log') -PassThru
    $serverProcess = Get-CimInstance Win32_Process -Filter ('ProcessId=' + $started.Id)
    if (-not $serverProcess) { throw 'Server startup failed. Check the log.' }
    @{ ProcessId=$started.Id; CreatedUtc=$serverProcess.CreationDate.ToUniversalTime().ToString('o'); Executable=$serverExe; Port=$Port } | ConvertTo-Json | Set-Content -LiteralPath $pidFile -Encoding UTF8
}
$serverId = $serverProcess.ProcessId
for ($attempt=0; $attempt -lt 180; $attempt++) {
    if (-not (Get-Process -Id $serverId -ErrorAction SilentlyContinue)) { throw 'The server exited during startup. Check the log.' }
    try {
        $healthPath = if ($Kind -eq 'ComfyUI') { '/system_stats' } else { '/health' }
        $health = Invoke-RestMethod -Uri ($url + $healthPath) -TimeoutSec 2
        if (($Kind -eq 'ComfyUI' -and $health.system.comfyui_version) -or ($Kind -eq 'Llama' -and $health.status -eq 'ok')) { Write-Output ('Server ready: ' + $url); exit 0 }
    } catch { }
    Start-Sleep -Seconds 1
}
throw 'The server is still starting or is not responding. Check the log.'
