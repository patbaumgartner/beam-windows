using System.Windows;
// Aliases: with UseWPF + UseWindowsForms both System.Windows and System.Windows.Forms
// are implicitly imported, so these names would otherwise be ambiguous.
using MessageBox = System.Windows.MessageBox;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Beam.Tray;

/// <summary>
/// C1: the settings window (WPF inside the tray exe). All logic lives in
/// <see cref="SettingsViewModel"/>; this code-behind only wires buttons to it.
/// </summary>
public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsWindow(SettingsViewModel vm)
    {
        _vm = vm ?? throw new ArgumentNullException(nameof(vm));
        InitializeComponent();
        DataContext = vm;
    }

    private void OnForgetDevice(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button b && b.Tag is string identity)
        {
            _vm.ForgetDevice(identity);
        }
    }

    private void OnReevaluate(object sender, RoutedEventArgs e) => _ = _vm.ReevaluateNow();

    private void OnResetSafeMode(object sender, RoutedEventArgs e) => _vm.ResetSafeMode();

    private void OnOpenLogFolder(object sender, RoutedEventArgs e) => _vm.OpenLogFolder?.Invoke();

    private void OnExportSettings(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            FileName = "beam-settings.json",
            Filter = "JSON files (*.json)|*.json",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
        };
        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                _vm.ExportSettings(dialog.FileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Could not export settings: {ex.Message}", TrayStrings.SettingsWindowTitle,
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
    }

    private void OnResetDefaults(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Reset all Beam settings to their defaults?", TrayStrings.SettingsWindowTitle,
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
        {
            _vm.ResetToDefaults();
        }
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
