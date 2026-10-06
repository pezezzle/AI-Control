using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;

namespace ComfyUIControl;

public partial class MainWindow : Window
{
    private readonly ServerControl control;
    private readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(2) };
    private ServerStatus status = new(ServerState.Unreachable);
    private bool reading;
    private bool operating;
    private bool starting;
    private bool stopScheduled;
    private bool deciding;
    private bool closed;

    public MainWindow(ServerControl service)
    {
        InitializeComponent();
        control = service;
        Loaded += async (_, _) => { await RefreshAsync(); if (!closed) timer.Start(); };
        timer.Tick += async (_, _) => await RefreshAsync();
        Closing += OnClosing;
        Closed += (_, _) => { closed = true; timer.Stop(); control.Dispose(); };
    }

    private async Task RefreshAsync()
    {
        if (reading || closed) return;
        reading = true;
        try
        {
            status = await control.ReadStatusAsync();
            if (closed) return;
            if (stopScheduled && !operating && !deciding && (status.State == ServerState.Offline || (status.QueueKnown && !status.HasJobs)))
                await StopNowAsync(false);
            DrawStatus();
        }
        catch (Exception ex) { if (!closed) ShowError(ex); }
        finally { reading = false; }
    }

    private void DrawStatus()
    {
        if (closed) return;
        var (label, detail, color) = status.State switch
        {
            ServerState.Offline => ("Gestoppt", "Bereit zum Starten.", "#8F9FB7"),
            ServerState.Running => ("Läuft", "ComfyUI ist bereit. Du kannst den Browser öffnen.", "#53D6AD"),
            ServerState.Rendering => ("Render läuft", $"{status.Running} laufend · {status.Pending} vorgemerkt", "#53D6AD"),
            ServerState.Foreign => ("Port belegt", "Ein anderes Programm antwortet auf dem ComfyUI-Port.", "#F2B7BA"),
            _ => ("Nicht erreichbar", "ComfyUI antwortet noch nicht. Du kannst den Start versuchen oder das Protokoll öffnen.", "#F5BF69"),
        };
        if (starting) (label, detail, color) = ("Startet …", "Das kann einen Moment dauern.", "#75B9FF");
        else if (operating) (label, detail, color) = ("Wird beendet …", "ComfyUI wird gestoppt.", "#F5BF69");
        else if (stopScheduled) (label, detail, color) = ("Beenden vorgemerkt", status.QueueKnown ? $"Wartet auf alle Aufträge · {status.Running} laufend · {status.Pending} vorgemerkt" : "Wartet auf einen zuverlässig erkennbaren Leerlauf.", "#F5BF69");
        StatusText.Text = label;
        DetailText.Text = detail;
        StatusDot.Fill = (Brush)new BrushConverter().ConvertFromString(color)!;
        StartButton.IsEnabled = !operating && !deciding && !stopScheduled && status.State is ServerState.Offline or ServerState.Unreachable;
        BrowserButton.IsEnabled = status.QueueKnown;
        StopButton.IsEnabled = !operating && !deciding && (stopScheduled || status.State is ServerState.Running or ServerState.Rendering or ServerState.Unreachable);
        StopButton.Content = stopScheduled ? "Aufheben" : "Beenden";
        LogButton.Visibility = NoticeText.Text.Length > 0 || status.State is ServerState.Foreign or ServerState.Unreachable ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        operating = starting = true;
        NoticeText.Text = "";
        DrawStatus();
        try { await control.StartAsync(); }
        catch (Exception ex) { ShowError(ex); }
        finally { operating = starting = false; await RefreshAsync(); DrawStatus(); }
    }

    private async void Stop_Click(object sender, RoutedEventArgs e)
    {
        if (operating || deciding) return;
        if (stopScheduled) { stopScheduled = false; DrawStatus(); return; }
        deciding = true;
        DrawStatus();
        try
        {
        status = await control.ReadStatusAsync();
        if (closed) return;
        if (status.State == ServerState.Foreign) { DrawStatus(); return; }
        if (status.HasJobs || !status.QueueKnown && status.State != ServerState.Offline)
        {
            var dialog = new StopDialog(status) { Owner = this };
            dialog.ShowDialog();
            if (dialog.Choice == StopChoice.Cancel) return;
            if (dialog.Choice == StopChoice.Wait) { stopScheduled = true; DrawStatus(); return; }
            await StopNowAsync(true);
        }
        else await StopNowAsync(false);
        await RefreshAsync();
        }
        catch (Exception ex) { if (!closed) ShowError(ex); }
        finally { deciding = false; DrawStatus(); }
    }

    private async Task StopNowAsync(bool force)
    {
        operating = true;
        DrawStatus();
        try
        {
            var result = await control.StopAsync(force);
            stopScheduled = result == StopResult.Waiting;
            if (!stopScheduled) NoticeText.Text = "";
        }
        catch (Exception ex) { stopScheduled = false; ShowError(ex); }
        finally { operating = false; }
    }

    private void ShowError(Exception ex)
    {
        if (closed) return;
        var message = ex.Message.Split('\n')[0].Trim();
        NoticeText.Text = (message.Length > 160 ? message[..160] + " …" : message) + " Details im Protokoll.";
        try
        {
            var folder = System.IO.Path.Combine(control.InstallRoot, "logs");
            System.IO.Directory.CreateDirectory(folder);
            System.IO.File.AppendAllText(System.IO.Path.Combine(folder, "control-app.log"), $"{DateTime.Now:O} {ex}\n");
        }
        catch (System.IO.IOException) { }
        catch (UnauthorizedAccessException) { }
        LogButton.Visibility = Visibility.Visible;
    }

    private void Browser_Click(object sender, RoutedEventArgs e)
    { try { control.OpenBrowser(); } catch (Exception ex) { ShowError(ex); } }

    private void Log_Click(object sender, RoutedEventArgs e)
    { try { control.OpenLog(NoticeText.Text.Length > 0); } catch (Exception ex) { ShowError(ex); } }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (!stopScheduled) return;
        var answer = MessageBox.Show(this, "ComfyUI soll nach den Aufträgen beendet werden.\n\nWenn du dieses Fenster schließt, wird die Vormerkung aufgehoben und ComfyUI läuft weiter.\n\nTrotzdem schließen?", "Beenden vorgemerkt", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        e.Cancel = answer != MessageBoxResult.Yes;
        if (!e.Cancel) stopScheduled = false;
    }
}
