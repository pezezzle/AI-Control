using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;

namespace ComfyUIControl;

public enum ServerState { Offline, Running, Rendering, Unreachable, Foreign }
public record ServerStatus(ServerState State, int Running = 0, int Pending = 0)
{
    public bool QueueKnown => State is ServerState.Running or ServerState.Rendering;
    public bool HasJobs => Running > 0 || Pending > 0;
}
public enum StopResult { Stopped, Waiting }

public interface IScriptRunner
{
    Task RunAsync(string path, bool noBrowser = false);
}

public sealed class ScriptRunner : IScriptRunner
{
    public async Task RunAsync(string path, bool noBrowser = false)
    {
        if (!File.Exists(path)) throw new FileNotFoundException("Das ComfyUI-Skript fehlt.", path);
        var info = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            WorkingDirectory = Path.GetDirectoryName(path)!,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var value in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", path }) info.ArgumentList.Add(value);
        if (noBrowser) info.ArgumentList.Add("-NoBrowser");
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Das Skript konnte nicht gestartet werden.");
        using var reading = new CancellationTokenSource();
        var stdout = process.StandardOutput.ReadToEndAsync(reading.Token);
        var stderr = process.StandardError.ReadToEndAsync(reading.Token);
        await process.WaitForExitAsync();
        // A background server can inherit pipe handles. Its lifetime must not
        // hold the launcher open after PowerShell has exited successfully.
        if (process.ExitCode == 0) { reading.Cancel(); return; }
        reading.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var output = await stdout;
            var error = await stderr;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim());
        }
        catch (OperationCanceledException)
        { throw new InvalidOperationException($"Das ComfyUI-Skript wurde mit Fehlercode {process.ExitCode} beendet. Bitte das Serverprotokoll prüfen."); }
    }
}

public sealed class ServerControl : IDisposable
{
    public const string Url = "http://127.0.0.1:8188";
    private readonly HttpClient client;
    private readonly IScriptRunner runner;
    public string InstallRoot { get; }

    public ServerControl(string root, HttpClient? http = null, IScriptRunner? scripts = null)
    {
        InstallRoot = root;
        client = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        runner = scripts ?? new ScriptRunner();
    }

    public async Task<ServerStatus> ReadStatusAsync()
    {
        try
        {
            using var statsResponse = await client.GetAsync(Url + "/system_stats");
            if (!statsResponse.IsSuccessStatusCode) return new(ServerState.Foreign);
            using var stats = JsonDocument.Parse(await statsResponse.Content.ReadAsStringAsync());
            if (!stats.RootElement.TryGetProperty("system", out var system) ||
                !system.TryGetProperty("comfyui_version", out _) ||
                !stats.RootElement.TryGetProperty("devices", out _)) return new(ServerState.Foreign);
            using var queueResponse = await client.GetAsync(Url + "/queue");
            if (!queueResponse.IsSuccessStatusCode) return new(ServerState.Unreachable);
            using var queue = JsonDocument.Parse(await queueResponse.Content.ReadAsStringAsync());
            var running = queue.RootElement.GetProperty("queue_running").GetArrayLength();
            var pending = queue.RootElement.GetProperty("queue_pending").GetArrayLength();
            return new(running + pending > 0 ? ServerState.Rendering : ServerState.Running, running, pending);
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused })
        { return new(ServerState.Offline); }
        catch (HttpRequestException) { return new(ServerState.Unreachable); }
        catch (TaskCanceledException) { return new(ServerState.Unreachable); }
        catch (JsonException) { return new(ServerState.Foreign); }
        catch (InvalidOperationException) { return new(ServerState.Unreachable); }
        catch (KeyNotFoundException) { return new(ServerState.Unreachable); }
    }

    public Task StartAsync() => runner.RunAsync(Path.Combine(InstallRoot, "Start-ComfyUI.ps1"), true);

    public async Task<StopResult> StopAsync(bool force = false)
    {
        var status = await ReadStatusAsync();
        if (status.State == ServerState.Offline) return StopResult.Stopped;
        if (status.State == ServerState.Foreign) throw new InvalidOperationException("Auf Port 8188 antwortet ein anderes Programm. Es wird nicht beendet.");
        if (!force && (!status.QueueKnown || status.HasJobs)) return StopResult.Waiting;
        // The existing script rechecks executable, command line and port owner before stopping.
        await runner.RunAsync(Path.Combine(InstallRoot, "Stop-ComfyUI.ps1"));
        return StopResult.Stopped;
    }

    public void OpenBrowser() => Process.Start(new ProcessStartInfo(Url) { UseShellExecute = true });
    public void OpenLog(bool controllerError = false) => Process.Start(new ProcessStartInfo("notepad.exe")
    { ArgumentList = { Path.Combine(InstallRoot, "logs", controllerError ? "control-app.log" : "server.err.log") }, UseShellExecute = true });
    public void Dispose() => client.Dispose();
}
