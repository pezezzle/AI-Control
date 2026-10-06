$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot '..\src\AIControl.Host\scripts\Identity.ps1')
$expected = 'C:\Test\ComfyUI\python_embeded\python.exe'
$good = [pscustomobject]@{ ExecutablePath=$expected; CommandLine='"C:\Test\ComfyUI\python_embeded\python.exe" -s ComfyUI/main.py --port 8188' }
if (-not (Test-ServiceProcessIdentity $good $expected 'ComfyUI')) { throw 'Own ComfyUI rejected' }
$foreign = [pscustomobject]@{ ExecutablePath='C:\Other\python.exe'; CommandLine='ComfyUI/main.py' }
if (Test-ServiceProcessIdentity $foreign $expected 'ComfyUI') { throw 'Other executable accepted' }
$wrongScript = [pscustomobject]@{ ExecutablePath=$expected; CommandLine='other.py' }
if (Test-ServiceProcessIdentity $wrongScript $expected 'ComfyUI') { throw 'Other script accepted' }
$missing = [pscustomobject]@{ ExecutablePath=$null; CommandLine='ComfyUI/main.py' }
if (Test-ServiceProcessIdentity $missing $expected 'ComfyUI') { throw 'Unknown identity accepted' }
$llama = [pscustomobject]@{ ExecutablePath='C:\Test\llama-server.exe'; CommandLine='llama-server.exe --port 8080' }
if (-not (Test-ServiceProcessIdentity $llama $llama.ExecutablePath 'Llama')) { throw 'Own llama rejected' }
if (Test-ServiceProcessIdentity $llama 'C:\Other\llama-server.exe' 'Llama') { throw 'Foreign llama accepted' }
Write-Output '6 process identity checks passed.'
$fixtureExe = Join-Path ([Environment]::GetFolderPath('System')) 'WindowsPowerShell\v1.0\powershell.exe'
$fixture = Start-Process -FilePath $fixtureExe -ArgumentList @('-NoProfile','-Command','Start-Sleep -Seconds 15') -WindowStyle Hidden -PassThru
try {
    $metadata = [pscustomobject]@{ ProcessId=$fixture.Id; CreationDate=$fixture.StartTime }
    Stop-VerifiedServiceProcess $metadata $fixtureExe
    if (-not $fixture.WaitForExit(1000)) { throw 'Verified helper did not stop its owned test process' }
    Write-Output '1 live process lifecycle check passed.'
} finally { if (-not $fixture.HasExited) { $fixture.Kill() }; $fixture.Dispose() }
