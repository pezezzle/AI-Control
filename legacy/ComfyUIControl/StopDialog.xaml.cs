using System.Windows;

namespace ComfyUIControl;

public enum StopChoice { Cancel, Wait, Force }
public partial class StopDialog : Window
{
    public StopChoice Choice { get; private set; } = StopChoice.Cancel;
    public StopDialog(ServerStatus status)
    {
        InitializeComponent();
        Explanation.Text = status.QueueKnown
            ? $"Es laufen {status.Running} Aufträge; {status.Pending} warten.\n\nSofortiges Beenden bricht sie ab. Alternativ wartet die App, bis alle Aufträge fertig sind."
            : "Die Warteschlange kann gerade nicht geprüft werden. Sofortiges Beenden könnte einen laufenden Render abbrechen. Alternativ wartet die App auf einen erkennbaren Leerlauf.";
    }
    private void Wait_Click(object sender, RoutedEventArgs e) { Choice = StopChoice.Wait; DialogResult = true; }
    private void Force_Click(object sender, RoutedEventArgs e) { Choice = StopChoice.Force; DialogResult = true; }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; }
}
