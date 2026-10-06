using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;

namespace AIControl;
public partial class MainWindow : Window
{
    private ControlSettings settings;
    private readonly Dictionary<string, LiveService> services = [];
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private CoreWebView2Environment? environment;
    private bool polling, ready, closed;
    private string? workspaceId;
    private string notice = "";
    public MainWindow(ControlSettings initial)
    {
        InitializeComponent(); settings = initial; ReplaceServices();
        Loaded += async (_, _) => await InitializeAsync();
        timer.Tick += async (_, _) => await RefreshAsync();
        Closing += OnClosing;
        Closed += (_, _) => { closed = true; timer.Stop(); Shell.Dispose(); ServiceView.Dispose(); foreach (var service in services.Values) service.Control.Dispose(); };
    }
    private void ReplaceServices()
    {
        foreach (var service in services.Values) service.Control.Dispose(); services.Clear();
        foreach (var definition in new[] { settings.ComfyUI, settings.Coding }) services.Add(definition.Id, new(definition));
    }
    private async Task InitializeAsync()
    {
        try
        {
            environment = await CoreWebView2Environment.CreateAsync(null, Path.Combine(SettingsStore.DataDirectory, "WebView2"));
            if (closed) return;
            await Shell.EnsureCoreWebView2Async(environment);
            var core = Shell.CoreWebView2;
            core.Settings.IsStatusBarEnabled = false; core.Settings.AreDefaultContextMenusEnabled = false;
            core.SetVirtualHostNameToFolderMapping("ai-control.local", Path.Combine(AppContext.BaseDirectory, "Web"), CoreWebView2HostResourceAccessKind.DenyCors);
            core.NavigationStarting += (_, e) => { if (!BridgePolicy.IsTrustedSource(e.Uri)) e.Cancel = true; };
            core.NewWindowRequested += (_, e) => e.Handled = true;
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
            core.WebMessageReceived += OnMessage;
            core.Navigate(BridgePolicy.Origin + "/index.html");
            await RefreshAsync(); if (!closed) timer.Start();
        }
        catch (Exception ex) { if (!closed) MessageBox.Show(this, "Die App-Oberfläche konnte nicht starten.\n" + ex.Message, "AI Control", MessageBoxButton.OK, MessageBoxImage.Error); }
    }
    private async Task RefreshAsync()
    {
        if (closed || polling) return; polling = true;
        try
        {
            await Task.WhenAll(services.Values.Select(async service =>
            {
                service.Status = await service.Control.ReadStatusAsync();
                if (!closed && service.StopScheduled && !service.Operating && (service.Status.State == ServerState.Offline || service.Status.QueueKnown && !service.Status.HasJobs)) await StopAsync(service, false);
            }));
            if (!closed && workspaceId is not null && !services[workspaceId].Status.Healthy && !services[workspaceId].Operating) HideWorkspace();
        }
        catch (Exception ex) { if (!closed) notice = ex.Message; }
        finally { polling = false; SendSnapshot(); }
    }
    private void SendSnapshot()
    {
        if (!ready || closed || Shell.CoreWebView2 is null) return;
        var snapshot = new
        {
            type = "snapshot", settings, notice,
            services = services.Values.Select(s => new
            {
                id = s.Definition.Id, name = s.Definition.Name, url = s.Definition.Url,
                state = s.Status.State, running = s.Status.Running, pending = s.Status.Pending, loadedModel = s.Status.Model,
                healthy = s.Status.Healthy, queueKnown = s.Status.QueueKnown, operating = s.Operating, starting = s.Starting,
                stopScheduled = s.StopScheduled, error = s.Error,
                models = s.Definition.Kind == ServiceKind.Llama ? s.Definition.ListModels().ToArray() : [],
            })
        };
        Shell.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(snapshot, SettingsStore.Json));
    }
    private async void OnMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!BridgePolicy.IsTrustedSource(e.Source) || closed) return;
        try
        {
            if (e.WebMessageAsJson.Length > 16_384) throw new InvalidDataException("Die Anfrage ist zu groß.");
            using var document = JsonDocument.Parse(e.WebMessageAsJson); var root = document.RootElement;
            var type = root.GetProperty("type").GetString(); notice = "";
            if (type == "ready") { ready = true; SendSnapshot(); return; }
            if (type == "saveSettings")
            {
                if (services.Values.Any(s => s.Operating || s.Status.State != ServerState.Offline)) throw new InvalidOperationException("Bitte beide Server beenden, bevor du die Server-Einstellungen änderst.");
                var updated = root.GetProperty("settings").Deserialize<ControlSettings>(SettingsStore.Json) ?? throw new InvalidDataException("Einstellungen fehlen.");
                updated.Validate(); SettingsStore.Save(updated); settings = updated; ReplaceServices();
                Send(new { type = "settingsSaved" }); await RefreshAsync(); return;
            }
            if (type == "browseFolder")
            {
                var field = root.GetProperty("field").GetString();
                if (field is not ("comfy" or "coding" or "project")) throw new InvalidDataException("Ungültiges Ordnerfeld.");
                var dialog = new OpenFolderDialog { Title = "Ordner auswählen" };
                if (dialog.ShowDialog(this) == true) Send(new { type = "folderChosen", field, path = dialog.FolderName }); return;
            }
            if (type == "chooseProject")
            {
                var dialog = new OpenFolderDialog { Title = "Coding-Projekt auswählen", InitialDirectory = settings.ProjectDirectory };
                if (dialog.ShowDialog(this) == true) { settings = settings with { ProjectDirectory = dialog.FolderName }; SettingsStore.Save(settings); SendSnapshot(); } return;
            }
            if (type == "openEditor") { OpenEditor(); return; }
            if (type == "copyApi") { Clipboard.SetText(settings.Coding.Url + "/v1"); Send(new { type = "apiCopied" }); return; }
            if (type == "selectModel")
            {
                var coding = services["coding"];
                if (coding.Operating || coding.Status.State != ServerState.Offline) throw new InvalidOperationException("Bitte den Coding-Server vor dem Modellwechsel beenden.");
                var model = root.GetProperty("model").GetString() ?? "";
                if (!settings.Coding.ListModels().Contains(model, StringComparer.OrdinalIgnoreCase)) throw new InvalidDataException("Das Modell wurde nicht im Modellordner gefunden.");
                settings = settings with { Coding = settings.Coding with { Model = model } }; SettingsStore.Save(settings);
                coding.Control.Dispose(); services["coding"] = new(settings.Coding); await RefreshAsync(); return;
            }
            if (type == "openRepository") { OpenPath(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", ".."))); return; }
            if (type == "back") { HideWorkspace(); return; }
            var id = root.GetProperty("serviceId").GetString() ?? "";
            if (!services.TryGetValue(id, out var service)) throw new InvalidDataException("Unbekannter Dienst.");
            if (type == "openLog") { OpenLog(service); return; }
            if (type == "openFolder") { OpenPath(service.Definition.Root); return; }
            if (type == "openBrowser") { OpenPath(service.Definition.Url); return; }
            if (type == "openWorkspace") { if (service.Status.Healthy) await ShowWorkspaceAsync(service); return; }
            if (type == "cancelStop") { service.StopScheduled = false; SendSnapshot(); return; }
            if (service.Operating) return;
            if (type == "start") { await StartAsync(service); return; }
            if (type == "stop")
            {
                var mode = root.GetProperty("mode").GetString();
                if (mode is not ("wait" or "force")) throw new InvalidDataException("Ungültige Beenden-Auswahl.");
                await StopAsync(service, mode == "force"); await RefreshAsync();
            }
        }
        catch (Exception ex) { if (!closed) { notice = FriendlyError(ex); Log("host", ex); SendSnapshot(); } }
    }
    private async Task StartAsync(LiveService service)
    {
        service.Operating = service.Starting = true; service.Error = ""; SendSnapshot();
        try
        {
            foreach (var other in services.Values.Where(s => s != service))
            {
                var status = await other.Control.ReadStatusAsync();
                if (other.Operating || status.State != ServerState.Offline)
                    throw new InvalidOperationException($"Bitte zuerst {other.Definition.Name} beenden. Beide Dienste nutzen dieselbe GPU; laufende Aufträge werden nicht abgebrochen.");
            }
            await service.Control.StartAsync();
        }
        catch (Exception ex) { service.Error = FriendlyError(ex); Log(service.Definition.Id, ex); }
        finally { service.Operating = service.Starting = false; await RefreshAsync(); SendSnapshot(); }
    }
    private async Task StopAsync(LiveService service, bool force)
    {
        service.Operating = true; service.Starting = false; service.Error = ""; SendSnapshot();
        try { service.StopScheduled = await service.Control.StopAsync(force) == StopResult.Waiting; }
        catch (Exception ex) { service.StopScheduled = false; service.Error = FriendlyError(ex); Log(service.Definition.Id, ex); }
        finally { service.Operating = false; SendSnapshot(); }
    }
    private async Task ShowWorkspaceAsync(LiveService service)
    {
        if (environment is null) return;
        await ServiceView.EnsureCoreWebView2Async(environment);
        var core = ServiceView.CoreWebView2;
        core.Settings.IsWebMessageEnabled = false; // Server pages never receive the privileged shell bridge.
        if (workspaceId is null)
        {
            core.NavigationStarting += (_, e) => { if (workspaceId is null || !Uri.TryCreate(e.Uri, UriKind.Absolute, out var uri) || uri.Scheme != "http" || uri.Host != "127.0.0.1" || uri.Port != services[workspaceId].Definition.Port) e.Cancel = true; };
            core.NewWindowRequested += (_, e) => { e.Handled = true; };
            core.PermissionRequested += (_, e) => e.State = CoreWebView2PermissionState.Deny;
        }
        workspaceId = service.Definition.Id; WorkspaceTitle.Text = service.Definition.Name + " · " + service.Definition.Url;
        Shell.Visibility = Visibility.Collapsed; Workspace.Visibility = Visibility.Visible; core.Navigate(service.Definition.Url);
    }
    private void HideWorkspace() { Workspace.Visibility = Visibility.Collapsed; Shell.Visibility = Visibility.Visible; }
    private void Back_Click(object sender, RoutedEventArgs e) => HideWorkspace();
    private void Send(object value) { if (!closed && ready) Shell.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(value, SettingsStore.Json)); }
    private static void OpenPath(string value) => Process.Start(new ProcessStartInfo(value) { UseShellExecute = true });
    private void OpenEditor()
    {
        if (!Directory.Exists(settings.ProjectDirectory)) throw new DirectoryNotFoundException("Der ausgewählte Projektordner fehlt.");
        var candidates = new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Microsoft VS Code", "Code.exe"), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Microsoft VS Code", "Code.exe") };
        var executable = candidates.FirstOrDefault(File.Exists) ?? throw new FileNotFoundException("VS Code wurde nicht gefunden.");
        Process.Start(new ProcessStartInfo(executable) { UseShellExecute = false, ArgumentList = { settings.ProjectDirectory } });
    }
    private static void OpenLog(LiveService service)
    {
        var folder = service.Definition.LogDirectory; Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, string.IsNullOrEmpty(service.Error) ? "server.err.log" : "control-app.log");
        if (!File.Exists(path) && service.Definition.Kind == ServiceKind.ComfyUI) path = Path.Combine(service.Definition.Root, "logs", "server.err.log");
        if (!File.Exists(path)) { Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "Noch kein Protokoll vorhanden.\r\n"); }
        Process.Start(new ProcessStartInfo("notepad.exe") { UseShellExecute = true, ArgumentList = { path } });
    }
    private static string FriendlyError(Exception ex)
    {
        var message = ex.Message.Split('\n')[0].Trim(); return message.Length > 280 ? message[..280] + " … Details im Protokoll." : message;
    }
    private static void Log(string id, Exception ex)
    { try { var folder = Path.Combine(SettingsStore.DataDirectory, id); Directory.CreateDirectory(folder); File.AppendAllText(Path.Combine(folder, "control-app.log"), $"{DateTimeOffset.Now:O} {ex}\n"); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!services.Values.Any(s => s.StopScheduled)) return;
        var answer = MessageBox.Show(this, "Ein Server soll nach seinen Aufträgen beendet werden.\n\nWenn du die App schließt, wird diese Vormerkung aufgehoben und die Server laufen weiter.\n\nTrotzdem schließen?", "Beenden vorgemerkt", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        e.Cancel = answer != MessageBoxResult.Yes;
    }
    private sealed class LiveService(ServiceDefinition definition)
    {
        public ServiceDefinition Definition { get; } = definition;
        public ServerControl Control { get; } = new(definition);
        public ServerStatus Status { get; set; } = new(ServerState.Unreachable);
        public bool Operating, Starting, StopScheduled;
        public string Error = "";
    }
}
