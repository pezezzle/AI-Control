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
            ? $"{status.Running} jobs are active; {status.Pending} are queued.\n\nStopping now cancels them. Alternatively, wait until all jobs finish."
            : "The queue cannot currently be checked. Stopping now could interrupt a render. Alternatively, wait until the server is reliably idle.";
    }
    private void Wait_Click(object sender, RoutedEventArgs e) { Choice = StopChoice.Wait; DialogResult = true; }
    private void Force_Click(object sender, RoutedEventArgs e) { Choice = StopChoice.Force; DialogResult = true; }
    private void Cancel_Click(object sender, RoutedEventArgs e) { DialogResult = false; }
}
