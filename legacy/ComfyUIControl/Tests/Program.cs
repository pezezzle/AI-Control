using ComfyUIControl;
using System.Net;
using System.Net.Sockets;
using System.Net.Http;

var passed = 0;
void Check(bool value, string label)
{
    if (!value) throw new Exception("FAIL: " + label);
    Console.WriteLine("PASS: " + label); passed++;
}
var handler = new Backend();
var scripts = new Scripts();
using var control = new ServerControl(@"C:\TestComfy", new HttpClient(handler), scripts);
handler.Offline = true;
Check((await control.ReadStatusAsync()).State == ServerState.Offline, "Verbindungsablehnung = gestoppt");
Check(await control.StopAsync() == StopResult.Stopped && scripts.Calls.Count == 0, "Gestoppt: kein Beenden aufrufen");
handler.Offline = false;
Check((await control.ReadStatusAsync()).State == ServerState.Running, "Leerlauf erkannt");
handler.Running = 1; handler.Pending = 2;
var status = await control.ReadStatusAsync();
Check(status.State == ServerState.Rendering && status.Running == 1 && status.Pending == 2, "Render und vorgemerkte Aufträge zählen");
Check(await control.StopAsync() == StopResult.Waiting && scripts.Calls.Count == 0, "Laufende Aufträge verhindern normales Beenden");
handler.Running = 0;
Check(await control.StopAsync() == StopResult.Waiting && scripts.Calls.Count == 0, "Auch nur vorgemerkte Aufträge schützen");
handler.Pending = 0;
Check(await control.StopAsync() == StopResult.Stopped && scripts.Calls.Single().Path.EndsWith("Stop-ComfyUI.ps1"), "Leerlauf: vorhandenes Stopskript verwenden");
scripts.Calls.Clear(); handler.Running = 1;
Check(await control.StopAsync(true) == StopResult.Stopped && scripts.Calls.Count == 1, "Expliziter Abbruch verwendet geprüftes Stopskript");
scripts.Calls.Clear(); handler.QueueBroken = true;
Check((await control.ReadStatusAsync()).State == ServerState.Unreachable, "Unbekannte Warteschlange erkannt");
Check(await control.StopAsync() == StopResult.Waiting && scripts.Calls.Count == 0, "Unbekannte Warteschlange nicht blind stoppen");
handler.Foreign = true;
Check((await control.ReadStatusAsync()).State == ServerState.Foreign, "Fremder Dienst erkannt");
bool refused = false;
try { await control.StopAsync(true); } catch (InvalidOperationException) { refused = true; }
Check(refused && scripts.Calls.Count == 0, "Fremden Dienst auch bei Sofort-Beenden schützen");
handler.Foreign = false; handler.QueueBroken = false; handler.Running = 0;
Check((await control.ReadStatusAsync()).State == ServerState.Running, "Vorheriger Leerlauf");
handler.Pending = 1;
Check(await control.StopAsync() == StopResult.Waiting && scripts.Calls.Count == 0, "Warteschlange direkt vor dem Stop erneut prüfen");
await control.StartAsync();
Check(scripts.Calls.Single().Path.EndsWith("Start-ComfyUI.ps1") && scripts.Calls.Single().NoBrowser, "Startskript ohne automatischen Browser verwenden");
Console.WriteLine($"{passed} Prüfungen erfolgreich.");

sealed class Scripts : IScriptRunner
{
    public List<(string Path, bool NoBrowser)> Calls = [];
    public Task RunAsync(string path, bool noBrowser = false) { Calls.Add((path, noBrowser)); return Task.CompletedTask; }
}
sealed class Backend : HttpMessageHandler
{
    public bool Offline, Foreign, QueueBroken;
    public int Running, Pending;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Offline) throw new HttpRequestException("Connection refused", new SocketException((int)SocketError.ConnectionRefused));
        string body;
        if (request.RequestUri!.AbsolutePath == "/system_stats")
            body = Foreign ? "{}" : "{\"system\":{\"comfyui_version\":\"0.39.0\"},\"devices\":[]}";
        else if (QueueBroken) return Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
        else body = "{\"queue_running\":[" + string.Join(',', Enumerable.Repeat("[]", Running)) + "],\"queue_pending\":[" + string.Join(',', Enumerable.Repeat("[]", Pending)) + "]}";
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) });
    }
}
