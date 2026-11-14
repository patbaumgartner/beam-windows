using System.IO;
using System.Windows;
using System.Windows.Threading;
using Clipboard = System.Windows.Clipboard;
// Alias: with UseWPF + UseWindowsForms, SaveFileDialog is ambiguous
// (System.Windows.Forms.SaveFileDialog vs Microsoft.Win32.SaveFileDialog).
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Beam.Tray;

/// <summary>
/// C4: the diagnostics window (UC-14). Shows the live 200-event timeline ring buffer and
/// offers copy-summary / export-bundle / re-evaluate / reset-safe-mode. All logic lives in
/// <see cref="DiagnosticsViewModel"/>; this code-behind only marshals to the UI thread.
/// </summary>
public partial class DiagnosticsWindow : Window
{
    private readonly DiagnosticsViewModel _vm;
    private readonly DispatcherTimer _refresh;

    public DiagnosticsWindow(DiagnosticsViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        InitializeComponent();
        DataContext = vm;
        Refresh();

        // Live view: repaint the timeline once a second while the window is open.
        _refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _refresh.Tick += (_, _) => Refresh();
        _refresh.Start();
        Closed += (_, _) => _refresh.Stop();
    }

    /// <summary>Header line: session state, safe mode, event count.</summary>
    public string HeaderText =>
        $"{_vm.SessionState}{(_vm.IsSafeMode ? " (SAFE MODE)" : "")} — {_vm.EventCount}/{_vm.EventCapacity} recent events";

    private void Refresh()
    {
        EventList.ItemsSource = _vm.EventLines();
        EventList.ScrollIntoView(EventList.Items.Count > 0 ? EventList.Items[^1] : null);
        HeaderLine.Text = HeaderText;
    }

    private void OnCopySummary(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(_vm.BuildSummary());
            StatusLine.Text = TrayStrings.SummaryCopiedToast;
        }
        catch (Exception ex)
        {
            StatusLine.Text = ex.Message;
        }
    }

    private void OnExportBundle(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            FileName = "beam-diagnostics.zip",
            Filter = "Zip archives (*.zip)|*.zip",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        // The exporter builds its own timestamped name inside the chosen folder.
        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                string dir = Path.GetDirectoryName(dialog.FileName) ?? Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                _vm.ExportBundle(dir);
                StatusLine.Text = TrayStrings.BundleExportedToast;
            }
            catch
            {
                StatusLine.Text = TrayStrings.BundleFailedToast;
            }
        }
    }

    private void OnReevaluate(object sender, RoutedEventArgs e) => _ = _vm.ReevaluateNow();

    private void OnResetSafeMode(object sender, RoutedEventArgs e) => _vm.ResetSafeMode();

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
