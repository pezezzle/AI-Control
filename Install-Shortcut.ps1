$ErrorActionPreference = 'Stop'
$target = Join-Path $PSScriptRoot 'artifacts\app\AIControl.exe'
if (-not (Test-Path -LiteralPath $target)) { throw 'Run Build.ps1 -Publish first.' }
$desktop = [Environment]::GetFolderPath('Desktop')
$shellObject = New-Object -ComObject WScript.Shell
foreach ($name in @('AI Control.lnk','ComfyUI Control.lnk')) {
    if ($name -eq 'ComfyUI Control.lnk' -and -not (Test-Path -LiteralPath (Join-Path $desktop $name))) { continue }
    $shortcut = $shellObject.CreateShortcut((Join-Path $desktop $name))
    $shortcut.TargetPath = $target
    $shortcut.WorkingDirectory = Split-Path -Parent $target
    $shortcut.IconLocation = (Join-Path $PSScriptRoot 'src\AIControl.Host\Assets\ComfyUI-Control.ico') + ',0'
    $shortcut.Description = 'Local AI services: ComfyUI and coding server'
    $shortcut.Save()
}
