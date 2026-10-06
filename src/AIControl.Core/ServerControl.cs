using System.Diagnostics;
using System.Globalization;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;

namespace AIControl;

public enum ServerState { Offline, Running, Busy, UnknownQueue, Loading, Unreachable, Foreign }
public record ServerStatus(ServerState State, int Running = 0, int Pending = 0, string? Model = null)
{
    public bool QueueKnown => State is ServerState.Running or ServerState.Busy;
    public bool Healthy => QueueKnown || State == ServerState.UnknownQueue;
    public bool HasJobs => Running > 0 || Pending > 0;
}
public enum StopResult { Stopped, Waiting }
public interface IScriptRunner { Task RunAsync(ServiceDefinition service, bool start); }
public sealed class ScriptRunner : IScriptRunner
{
    public async Task RunAsync(ServiceDefinition service, bool start)
    {
        service.Validate();
        var script = Path.Combine(AppContext.BaseDirectory, "scripts", "Control-Service.ps1");
        if (!File.Exists(script)) throw new FileNotFoundException("Das Steuerungsskript fehlt.", script);
        if (!Directory.Exists(service.Root)) throw new DirectoryNotFoundException("Der Installationsordner wurde nicht gefunden.");
        var info = new ProcessStartInfo
        {
            FileName = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"),
            WorkingDirectory = service.Root, UseShellExecute = false, CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden, RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var value in new[] { "-NoProfile", "-ExecutionPolicy", "Bypass", "-File", script, "-Action", start ? "Start" : "Stop",
            "-Kind", service.Kind.ToString(), "-InstallDirectory", service.Root, "-Port", service.Port.ToString(CultureInfo.InvariantCulture),
            "-Model", service.Model, "-ContextSize", service.ContextSize.ToString(CultureInfo.InvariantCulture), "-GpuLayers", service.GpuLayers }) info.ArgumentList.Add(value);
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Die Serversteuerung konnte nicht starten.");
        using var reading = new CancellationTokenSource();
        var stdout = process.StandardOutput.ReadToEndAsync(reading.Token); var stderr = process.StandardError.ReadToEndAsync(reading.Token);
        await process.WaitForExitAsync();
        // Child servers may inherit pipe handles; successful launchers need not wait for EOF.
        if (process.ExitCode == 0) { reading.Cancel(); return; }
        reading.CancelAfter(TimeSpan.FromSeconds(2));
        try
        {
            var output = await stdout; var error = await stderr;
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? output.Trim() : error.Trim());
        }
        catch (OperationCanceledException) { throw new InvalidOperationException($"Server-Aktion fehlgeschlagen (Fehlercode {process.ExitCode}). Bitte das Protokoll prüfen."); }
    }
}
public sealed class ServerControl : IDisposable
{
    private readonly HttpClient client;
    private readonly IScriptRunner runner;
    public ServiceDefinition Definition { get; }
    public ServerControl(ServiceDefinition definition, HttpClient? http = null, IScriptRunner? scripts = null)
    {
        definition.Validate(); Definition = definition;
        client = http ?? new HttpClient { Timeout = TimeSpan.FromSeconds(3) }; runner = scripts ?? new ScriptRunner();
    }
    public async Task<ServerStatus> ReadStatusAsync()
    {
        bool identified = false; string? model = null;
        try
        {
            if (Definition.Kind == ServiceKind.ComfyUI)
            {
                using var response = await client.GetAsync(Definition.Url + "/system_stats");
                if (!response.IsSuccessStatusCode) return new(ServerState.Foreign);
                using var stats = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                if (!stats.RootElement.TryGetProperty("system", out var system) || !system.TryGetProperty("comfyui_version", out _) || !stats.RootElement.TryGetProperty("devices", out _)) return new(ServerState.Foreign);
                identified = true;
                using var queueResponse = await client.GetAsync(Definition.Url + "/queue");
                if (!queueResponse.IsSuccessStatusCode) return new(ServerState.UnknownQueue);
                using var queue = JsonDocument.Parse(await queueResponse.Content.ReadAsStringAsync());
                var running = queue.RootElement.GetProperty("queue_running").GetArrayLength(); var pending = queue.RootElement.GetProperty("queue_pending").GetArrayLength();
                return new(running + pending > 0 ? ServerState.Busy : ServerState.Running, running, pending);
            }
            using var healthResponse = await client.GetAsync(Definition.Url + "/health");
            using var health = JsonDocument.Parse(await healthResponse.Content.ReadAsStringAsync());
            if (health.RootElement.TryGetProperty("error", out var error) && error.TryGetProperty("message", out var message) && message.GetString()?.Contains("loading", StringComparison.OrdinalIgnoreCase) == true) return new(ServerState.Loading);
            if (!healthResponse.IsSuccessStatusCode || !health.RootElement.TryGetProperty("status", out var state) || state.GetString() != "ok") return new(ServerState.Foreign);
            using var modelsResponse = await client.GetAsync(Definition.Url + "/v1/models");
            if (!modelsResponse.IsSuccessStatusCode) return new(ServerState.Foreign);
            using var models = JsonDocument.Parse(await modelsResponse.Content.ReadAsStringAsync()); var available = models.RootElement.GetProperty("data");
            if (available.GetArrayLength() == 0) return new(ServerState.Foreign);
            model = available[0].GetProperty("id").GetString(); identified = true;
            using var slotsResponse = await client.GetAsync(Definition.Url + "/slots");
            if (!slotsResponse.IsSuccessStatusCode) return new(ServerState.UnknownQueue, Model: model);
            using var slots = JsonDocument.Parse(await slotsResponse.Content.ReadAsStringAsync());
            if (slots.RootElement.ValueKind != JsonValueKind.Array || slots.RootElement.GetArrayLength() == 0) return new(ServerState.UnknownQueue, Model: model);
            var activeSlots = slots.RootElement.EnumerateArray().Count(s => s.GetProperty("is_processing").GetBoolean());
            using var metricsResponse = await client.GetAsync(Definition.Url + "/metrics");
            if (!metricsResponse.IsSuccessStatusCode) return new(ServerState.UnknownQueue, activeSlots, Model: model);
            var metrics = await metricsResponse.Content.ReadAsStringAsync();
            var deferred = ReadMetric(metrics, "llamacpp:requests_deferred"); var processing = ReadMetric(metrics, "llamacpp:requests_processing");
            if (deferred is null || processing is null) return new(ServerState.UnknownQueue, activeSlots, Model: model);
            var active = Math.Max(activeSlots, processing.Value);
            return new(active + deferred.Value > 0 ? ServerState.Busy : ServerState.Running, active, deferred.Value, model);
        }
        catch (HttpRequestException ex) when (ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionRefused }) { return new(identified ? ServerState.Unreachable : ServerState.Offline); }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException or KeyNotFoundException) { return new(identified ? ServerState.UnknownQueue : ServerState.Unreachable, Model: model); }
    }
    public static int? ReadMetric(string text, string name)
    {
        foreach (var line in text.Split('\n'))
        {
            var parts = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 && parts[0] == name && double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                && double.IsFinite(number) && number >= 0 && number <= int.MaxValue) return (int)Math.Ceiling(number);
        }
        return null;
    }
    public Task StartAsync() => runner.RunAsync(Definition, true);
    public async Task<StopResult> StopAsync(bool force = false)
    {
        var status = await ReadStatusAsync();
        if (status.State == ServerState.Offline) return StopResult.Stopped;
        if (status.State == ServerState.Foreign) throw new InvalidOperationException("Auf dem Server-Port antwortet ein anderes Programm. Es wird nicht beendet.");
        if (!force && (!status.QueueKnown || status.HasJobs)) return StopResult.Waiting;
        await runner.RunAsync(Definition, false); return StopResult.Stopped;
    }
    public void Dispose() => client.Dispose();
}
