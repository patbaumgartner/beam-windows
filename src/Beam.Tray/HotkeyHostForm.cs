using System.Runtime.InteropServices;
using System.Windows.Forms;
using Beam.Core.Input;

namespace Beam.Tray;

/// <summary>
/// B7: a hidden form that owns the global-hotkey registration. <c>RegisterHotKey</c>
/// needs an HWND to deliver <c>WM_HOTKEY</c> to, so the tray runs this zero-size,
/// invisible form alongside the NotifyIcon's host form. Registration failure is
/// reported to the caller (and logged as a warning by <see cref="TrayService"/>) —
/// it never blocks startup.
/// </summary>
internal sealed class HotkeyHostForm : Form
{
    private const int WmHotkey = 0x0312;
    private const int HotkeyId = 1;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    /// <summary>Raised on the UI thread when the registered hotkey is pressed.</summary>
    public event Action? Pressed;

    private bool _registered;

    public HotkeyHostForm()
    {
        // Invisible, non-activating, off the taskbar and alt-tab.
        ShowInTaskbar = false;
        FormBorderStyle = FormBorderStyle.None;
        WindowState = FormWindowState.Minimized;
        Opacity = 0;
        Width = 0;
        Height = 0;
        Text = "Beam hotkey host";
    }

    /// <summary>The combo currently registered, or null.</summary>
    public HotkeyCombo? RegisteredCombo { get; private set; }

    /// <summary>
    /// Registers the combo. Returns false (and leaves nothing registered) when the OS
    /// refuses — e.g. another app already owns the combo.
    /// </summary>
    public bool TryRegister(HotkeyCombo combo)
    {
        Unregister();
        _ = Handle; // force handle creation so RegisterHotKey has an HWND
        if (!RegisterHotKey(Handle, HotkeyId, (uint)combo.Modifiers, combo.VirtualKey))
        {
            return false;
        }

        _registered = true;
        RegisteredCombo = combo;
        return true;
    }

    /// <summary>Unregisters any registered combo. Idempotent.</summary>
    public void Unregister()
    {
        if (_registered && IsHandleCreated)
        {
            UnregisterHotKey(Handle, HotkeyId);
        }

        _registered = false;
        RegisteredCombo = null;
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WmHotkey && m.WParam.ToInt32() == HotkeyId)
        {
            Pressed?.Invoke();
        }

        base.WndProc(ref m);
    }

    protected override void Dispose(bool disposing)
    {
        Unregister();
        base.Dispose(disposing);
    }
}
