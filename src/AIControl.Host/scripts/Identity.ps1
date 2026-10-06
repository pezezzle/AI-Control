function Test-ServiceProcessIdentity {
    param($Process, [string]$ExpectedExecutable, [string]$Kind)
    if (-not $Process -or -not $Process.ExecutablePath) { return $false }
    if (-not [string]::Equals([IO.Path]::GetFullPath($Process.ExecutablePath), [IO.Path]::GetFullPath($ExpectedExecutable), [StringComparison]::OrdinalIgnoreCase)) { return $false }
    if ($Kind -eq 'ComfyUI') { return $Process.CommandLine -match 'ComfyUI[\\/]main\.py(?:\s|$|\")' }
    return $Kind -eq 'Llama'
}

function Stop-VerifiedServiceProcess {
    param($Process, [string]$ExpectedExecutable)
    $target = Get-Process -Id $Process.ProcessId -ErrorAction SilentlyContinue
    if (-not $target) { return }
    try {
        if ($target.HasExited) { return }
        if (-not [string]::Equals($target.Path, $ExpectedExecutable, [StringComparison]::OrdinalIgnoreCase)) { throw 'Die Prozessidentitaet hat sich geaendert.' }
        if ([Math]::Abs(($target.StartTime.ToUniversalTime() - $Process.CreationDate.ToUniversalTime()).TotalSeconds) -gt 1) { throw 'Die Prozess-ID wurde inzwischen wiederverwendet.' }
        Stop-Process -InputObject $target -ErrorAction Stop
        # Keep the original kernel handle: CIM snapshots may retain an exited
        # process briefly, and a fresh PID lookup can refer to another process.
        if (-not $target.WaitForExit(30000)) { throw 'Der Server beendet sich noch. Bitte kurz warten.' }
    } finally { $target.Dispose() }
}
