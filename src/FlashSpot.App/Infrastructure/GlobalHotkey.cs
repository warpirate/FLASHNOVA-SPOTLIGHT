using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace FlashSpot.App.Infrastructure;

public sealed class GlobalHotkey : IDisposable
{
    private const int WmHotKey = 0x0312;

    private readonly HwndSource _source;
    private readonly int _hotkeyId;
    private bool _disposed;

    public event EventHandler? Pressed;
    public ModifierKeys Modifiers { get; }
    public Key Key { get; }
    public string DisplayText => $"{FormatModifiers(Modifiers)}+{Key}";

    public GlobalHotkey(ModifierKeys modifier, Key key)
    {
        Modifiers = modifier;
        Key = key;
        _hotkeyId = GetHashCode();

        var parameters = new HwndSourceParameters("FlashSpotHotkeySink")
        {
            Width = 0,
            Height = 0,
            WindowStyle = 0
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);

        var virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
        if (!RegisterHotKey(_source.Handle, _hotkeyId, (uint)modifier, virtualKey))
        {
            throw new InvalidOperationException("Could not register global hotkey Alt+Space.");
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WmHotKey && wParam.ToInt32() == _hotkeyId)
        {
            Pressed?.Invoke(this, EventArgs.Empty);
            handled = true;
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        UnregisterHotKey(_source.Handle, _hotkeyId);
        _source.RemoveHook(WndProc);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private static string FormatModifiers(ModifierKeys modifiers)
    {
        var parts = new List<string>();

        if (modifiers.HasFlag(ModifierKeys.Control))
        {
            parts.Add("Ctrl");
        }

        if (modifiers.HasFlag(ModifierKeys.Alt))
        {
            parts.Add("Alt");
        }

        if (modifiers.HasFlag(ModifierKeys.Shift))
        {
            parts.Add("Shift");
        }

        if (modifiers.HasFlag(ModifierKeys.Windows))
        {
            parts.Add("Win");
        }

        return parts.Count == 0 ? "None" : string.Join("+", parts);
    }
}
