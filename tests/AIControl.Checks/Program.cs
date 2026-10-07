using AIControl;
using System.Net;
using System.Net.Sockets;

if(args.Length==1&&args[0]=="--settings-summary")
{
    var localSettings=SettingsStore.Load();
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new{path=SettingsStore.SettingsPath,model=localSettings.Coding.Model,context=localSettings.Coding.ContextSize,project=localSettings.ProjectDirectory},SettingsStore.Json));return;
}

if (args.Length == 2 && args[0] == "--sessions-smoke") { await SessionLiveChecks.Run(args[1]); return; }
if (args.Length == 2 && args[0] == "--output-budget-smoke") { await SessionLiveChecks.OutputBudget(args[1]); return; }
if (args.Length == 2 && args[0] == "--assistant-smoke") { await AssistantLiveChecks.Run(args[1]); return; }
if (args.Length == 2 && args[0] == "--speech-smoke") { await AssistantLiveChecks.Speech(args[1]); return; }
if (args.Length == 2 && args[0] == "--assistant-recovery-smoke") { await AssistantLiveChecks.Recovery(args[1]); return; }

if (args.Length == 2 && args[0] == "--smoke-cpu-port")
{
    var definitionForTest = new ControlSettings().Coding with { Port = int.Parse(args[1]), Model = "qwen2.5-coder-7b-instruct.gguf", ContextSize = 2048, GpuLayers = "0" };
    using var server = new ServerControl(definitionForTest);
    await server.StartAsync();
    try
    {
        var observed = await server.ReadStatusAsync();
        if (observed.State != ServerState.Running) throw new Exception("Test server is not reliably idle.");
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var content = new StringContent("{\"model\":\"local-coder\",\"messages\":[{\"role\":\"user\",\"content\":\"Reply with only OK.\"}],\"max_tokens\":8,\"temperature\":0}", System.Text.Encoding.UTF8, "application/json");
        using var response = await http.PostAsync(definitionForTest.Url + "/v1/chat/completions", content);
        response.EnsureSuccessStatusCode();
        using var answer = System.Text.Json.JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Console.WriteLine(answer.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString());
        if (await server.StopAsync() != StopResult.Stopped) throw new Exception("Idle test server was not stopped.");
        if ((await server.ReadStatusAsync()).State != ServerState.Offline) throw new Exception("Test server still running.");
        Console.WriteLine("Live ScriptRunner start, inference, workload and stop passed (CPU only).");
    }
    finally { if ((await server.ReadStatusAsync()).State != ServerState.Offline) await server.StopAsync(true); }
    return;
}
if (args.Length == 2 && args[0] == "--live-port")
{
    var live = new ControlSettings().Coding with { Port = int.Parse(args[1]) };
    using var server = new ServerControl(live);
    var observed = await server.ReadStatusAsync();
    Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(observed, SettingsStore.Json));
    if (!observed.QueueKnown) throw new Exception("Real server workload could not be determined.");
    return;
}

int checks = 0;
void Check(bool result, string label) { if (!result) throw new Exception("FAIL: " + label); Console.WriteLine("PASS: " + label); checks++; }
void Reject(Action action, string label) { bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; } Check(rejected, label); }
var definition = new ControlSettings().ComfyUI;
var backend = new Backend(); var scripts = new Scripts();
using var control = new ServerControl(definition, new HttpClient(backend), scripts);
backend.Offline = true;
Check((await control.ReadStatusAsync()).State == ServerState.Offline, "Connection refusal means offline");
Check(await control.StopAsync() == StopResult.Stopped && scripts.Calls.Count == 0, "Offline stop is a no-op");
backend.Offline = false;
Check((await control.ReadStatusAsync()).State == ServerState.Running, "ComfyUI idle");
backend.Running = 1; backend.Pending = 2;
var status = await control.ReadStatusAsync();
Check(status.State == ServerState.Busy && status.Running == 1 && status.Pending == 2, "ComfyUI queue counts");
Check(await control.StopAsync() == StopResult.Waiting && scripts.Calls.Count == 0, "Active work protects normal stop");
backend.Running = 0;
Check(await control.StopAsync() == StopResult.Waiting && scripts.Calls.Count == 0, "Pending-only work is protected");
backend.Pending = 0;
Check(await control.StopAsync() == StopResult.Stopped && scripts.Calls.Single().Start == false, "Idle stop invokes configured service");
scripts.Calls.Clear(); backend.Running = 1;
Check(await control.StopAsync(true) == StopResult.Stopped && scripts.Calls.Count == 1, "Explicit force is honored");
scripts.Calls.Clear(); backend.QueueBroken = true;
Check((await control.ReadStatusAsync()).State == ServerState.UnknownQueue, "Known healthy server with unknown queue");
Check(await control.StopAsync() == StopResult.Waiting && scripts.Calls.Count == 0, "Unknown queue cannot stop automatically");
backend.QueueBroken = false; backend.MalformedQueue = true;
Check((await control.ReadStatusAsync()).State == ServerState.UnknownQueue, "Malformed queue stays unknown");
backend.Foreign = true;
Check((await control.ReadStatusAsync()).State == ServerState.Foreign, "Foreign service detected");
bool refused = false; try { await control.StopAsync(true); } catch (InvalidOperationException) { refused = true; }
Check(refused && scripts.Calls.Count == 0, "Foreign service protected even with force");
backend.Foreign = false; backend.MalformedQueue = false; backend.Running = 0;
Check((await control.ReadStatusAsync()).QueueKnown, "Earlier idle snapshot");
backend.Pending = 1;
Check(await control.StopAsync() == StopResult.Waiting && scripts.Calls.Count == 0, "Stop rechecks queue instead of trusting old snapshot");
await control.StartAsync(); Check(scripts.Calls.Single().Start && scripts.Calls.Single().Service.Kind == ServiceKind.ComfyUI, "Start uses configured service");

var llama = new ControlSettings().Coding;
backend = new Backend(); scripts = new Scripts();
using var coding = new ServerControl(llama, new HttpClient(backend), scripts);
Check((await coding.ReadStatusAsync()).State == ServerState.Running, "Llama idle slots and metrics");
backend.Running = 1; status = await coding.ReadStatusAsync();
Check(status.State == ServerState.Busy && status.Running == 1 && status.Model == "local-coder", "Llama processing counts and model ID");
Check(await coding.StopAsync() == StopResult.Waiting && scripts.Calls.Count == 0, "Llama active work protected");
backend.Running = 0; backend.Pending = 3;
Check(await coding.StopAsync() == StopResult.Waiting && scripts.Calls.Count == 0, "Llama deferred requests protected");
backend.Pending = 0; backend.MetricsBroken = true;
Check((await coding.ReadStatusAsync()).State == ServerState.UnknownQueue, "Missing llama metrics is not idle");
Check(await coding.StopAsync() == StopResult.Waiting && scripts.Calls.Count == 0, "Unknown deferred queue prevents auto-stop");
backend.MetricsBroken = false; backend.MalformedMetrics = true;
Check((await coding.ReadStatusAsync()).State == ServerState.UnknownQueue, "Malformed llama metrics is not idle");
backend.MalformedMetrics = false; backend.Loading = true;
Check((await coding.ReadStatusAsync()).State == ServerState.Loading, "Llama loading state recognized");
backend.Loading = false;
Check(await coding.StopAsync() == StopResult.Stopped && scripts.Calls.Count == 1, "Llama idle stop works");

Check(ServerControl.ReadMetric("# HELP ignored\nllamacpp:requests_deferred 2\n", "llamacpp:requests_deferred") == 2, "Prometheus metric parsed");
Check(ServerControl.ReadMetric("llamacpp:requests_deferred NaN\n", "llamacpp:requests_deferred") is null, "NaN metric rejected");
Check(ServerControl.ReadMetric("llamacpp:requests_deferred -1\n", "llamacpp:requests_deferred") is null, "Negative metric rejected");
Check(BridgePolicy.IsTrustedSource("https://ai-control.local/index.html"), "Local trusted UI origin");
Check(!BridgePolicy.IsTrustedSource("https://ai-control.local.evil.example/"), "Lookalike bridge origin rejected");
Check(!BridgePolicy.IsTrustedSource("http://127.0.0.1:8188/"), "ComfyUI page has no control bridge");
Check(!BridgePolicy.IsTrustedSource("https://user@ai-control.local/"), "Credential-bearing bridge origin rejected");
Check(!BridgePolicy.IsTrustedSource("https://ai-control.local:9999/"), "Unexpected bridge port rejected");
Reject(() => (llama with { Model = "..\\outside.gguf" }).Validate(), "Model path traversal rejected");
Reject(() => (llama with { InstallDirectory = "relative" }).Validate(), "Relative install path rejected");
Reject(() => (llama with { GpuLayers = "all --other-flag" }).Validate(), "Invalid GPU argument rejected");
Reject(() => (llama with { ContextSize = -1 }).Validate(), "Invalid context rejected");
Reject(() => (new ControlSettings() with { Coding = llama with { Port = 8188 } }).Validate(), "Duplicate ports rejected");
var temporary = Path.Combine(Path.GetTempPath(), "AIControl-check-" + Guid.NewGuid().ToString("N") + ".json");
try
{
    var original = new ControlSettings() with { Coding = llama with { ContextSize = 24576, Model = "qwen2.5-coder-32b-instruct.gguf", VisionEnabled=false }, Assistant=new(){Access=WorkspaceAccess.ReadWrite,AllowScriptExecution=true,SyncContinueContext=false} };
    SettingsStore.Save(original, temporary); var restored = SettingsStore.Load(temporary);
    Check(restored == original, "Settings survive atomic save and reload");
    Check(!File.ReadAllText(temporary).Contains("logDirectory"), "Computed runtime paths are not persisted");
    File.WriteAllText(temporary, "null"); Reject(() => SettingsStore.Load(temporary), "Empty config rejected");
} finally { File.Delete(temporary); }
await AssistantChecks.Run(Check,Reject);
await SessionChecks.Run(Check,Reject);
Console.WriteLine($"{checks} checks passed.");

sealed class Scripts : IScriptRunner
{
    public List<(ServiceDefinition Service, bool Start)> Calls = [];
    public Task RunAsync(ServiceDefinition service, bool start) { Calls.Add((service, start)); return Task.CompletedTask; }
}
sealed class Backend : HttpMessageHandler
{
    public bool Offline, Foreign, QueueBroken, MalformedQueue, MetricsBroken, MalformedMetrics, Loading;
    public int Running, Pending;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
    {
        if (Offline) throw new HttpRequestException("Connection refused", new SocketException((int)SocketError.ConnectionRefused));
        string body = "{}"; var code = HttpStatusCode.OK;
        switch (request.RequestUri!.AbsolutePath)
        {
            case "/system_stats": body = Foreign ? "{}" : "{\"system\":{\"comfyui_version\":\"0.39.0\"},\"devices\":[]}"; break;
            case "/queue": if (QueueBroken) code = HttpStatusCode.ServiceUnavailable; body = MalformedQueue ? "[]" : "{\"queue_running\":[" + string.Join(',', Enumerable.Repeat("[]", Running)) + "],\"queue_pending\":[" + string.Join(',', Enumerable.Repeat("[]", Pending)) + "]}"; break;
            case "/health": body = Loading ? "{\"error\":{\"message\":\"Loading model\"}}" : "{\"status\":\"ok\"}"; if (Loading) code = HttpStatusCode.ServiceUnavailable; break;
            case "/v1/models": body = "{\"data\":[{\"id\":\"local-coder\"}]}"; break;
            case "/slots": body = "[{\"is_processing\":" + (Running > 0 ? "true" : "false") + "}]"; break;
            case "/metrics": if (MetricsBroken) code = HttpStatusCode.NotFound; body = MalformedMetrics ? "wrong 0" : $"llamacpp:requests_processing {Running}\nllamacpp:requests_deferred {Pending}\n"; break;
        }
        return Task.FromResult(new HttpResponseMessage(code) { Content = new StringContent(body) });
    }
}
