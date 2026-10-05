using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Input;

namespace Qtranslator.Desktop;

public sealed class GlobalHotKeyService : IDisposable
{
    private const int WmHotKey = 0x0312;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModNoRepeat = 0x4000;
    private readonly int _hotKeyId;

    public GlobalHotKeyService(int hotKeyId = 0x5154) => _hotKeyId = hotKeyId;

    private HwndSource? _source;
    private bool _registered;

    public event EventHandler? Pressed;

    public string DisplayName { get; private set; } = ShortcutSettings.Default.DisplayName;

    public bool TryRegister(Window window, ShortcutSettings settings, out string? error)
    {
        Dispose();
        error = null;
        var handle = new WindowInteropHelper(window).Handle;
        _source = HwndSource.FromHwnd(handle);
        if (_source is null)
        {
            error = "The application window is not ready for a global shortcut.";
            return false;
        }

        _source.AddHook(ProcessWindowMessage);
        var modifiers = settings.Modifiers switch
        {
            ShortcutModifiers.ControlAlt => ModControl | ModAlt,
            ShortcutModifiers.AltShift => ModAlt | ModShift,
            _ => ModControl | ModShift
        };
        var key = Enum.TryParse<Key>(settings.Key, true, out var parsedKey)
            ? parsedKey
            : Key.X;
        DisplayName = settings.DisplayName;
        _registered = RegisterHotKey(
            handle,
            _hotKeyId,
            modifiers | ModNoRepeat,
            (uint)KeyInterop.VirtualKeyFromKey(key));
        if (_registered)
        {
            return true;
        }

        _source.RemoveHook(ProcessWindowMessage);
        _source = null;
        error =
            $"{DisplayName} is already used by another application. " +
            "Use the Select screen region button instead.";
        return false;
    }

    public void Dispose()
    {
        if (_source is null)
        {
            return;
        }

        if (_registered)
        {
            UnregisterHotKey(_source.Handle, _hotKeyId);
        }
        _source.RemoveHook(ProcessWindowMessage);
        _source = null;
        _registered = false;
    }

    private IntPtr ProcessWindowMessage(
        IntPtr window,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == WmHotKey && wParam.ToInt32() == _hotKeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }
        return IntPtr.Zero;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool RegisterHotKey(
        IntPtr window,
        int id,
        uint modifiers,
        uint virtualKey);

    [DllImport("user32.dll")]
    private static extern bool UnregisterHotKey(IntPtr window, int id);
}
