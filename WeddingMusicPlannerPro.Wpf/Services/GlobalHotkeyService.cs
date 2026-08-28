using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace WeddingMusicPlannerPro.Wpf.Services;

/// <summary>
/// Registers a global low-level keyboard hook (WH_KEYBOARD_LL) mapping the
/// Spacebar strictly to the audio engine's Emergency Fade &amp; Advance. Works even
/// when the app is not focused, so the operator can trigger the safety override
/// from anywhere. The key is swallowed so it never types a space elsewhere.
/// </summary>
public sealed class GlobalHotkeyService : IDisposable
{
    private const int WH_KEYBOARD_LL = 13;
    private const int WM_KEYDOWN = 0x0100;
    private const int VK_SPACE = 0x20;

    private readonly IPlaybackService _playback;
    private readonly LowLevelKeyboardProc _proc;
    private IntPtr _hookId = IntPtr.Zero;
    private volatile bool _handling;

    public GlobalHotkeyService(IPlaybackService playback)
    {
        _playback = playback;
        _proc = HookCallback; // keep a strong reference so the delegate isn't GC'd
    }

    public void Start()
    {
        if (_hookId != IntPtr.Zero) return;
        using var process = Process.GetCurrentProcess();
        using var module = process.MainModule!;
        _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc, GetModuleHandle(module.ModuleName), 0);
    }

    private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode >= 0 && wParam == WM_KEYDOWN)
        {
            int vkCode = Marshal.ReadInt32(lParam);
            if (vkCode == VK_SPACE && !IsTextInputFocused())
            {
                TriggerEmergencyFade();
                return (IntPtr)1; // swallow the keystroke
            }
        }
        return CallNextHookEx(_hookId, nCode, wParam, lParam);
    }

    /// <summary>
    /// True when a text-editable control in this app currently holds keyboard
    /// focus, so the Spacebar should type normally instead of triggering the
    /// emergency fade. Prevents the global hook from eating spaces in the search
    /// box and other text fields.
    /// </summary>
    private static bool IsTextInputFocused()
    {
        var app = Application.Current;
        if (app is null) return false;

        return app.Dispatcher.Invoke(() =>
        {
            if (Keyboard.FocusedElement is System.Windows.Controls.Primitives.TextBoxBase or
                System.Windows.Controls.PasswordBox)
            {
                return true;
            }
            return false;
        });
    }

    private void TriggerEmergencyFade()
    {
        if (_handling) return; // debounce key-repeat while a fade is in flight
        _handling = true;

        Application.Current?.Dispatcher.BeginInvoke(async () =>
        {
            try
            {
                await _playback.EmergencyFadeAsync().ConfigureAwait(true);
            }
            finally
            {
                _handling = false;
            }
        });
    }

    public void Dispose()
    {
        if (_hookId != IntPtr.Zero)
        {
            UnhookWindowsHookEx(_hookId);
            _hookId = IntPtr.Zero;
        }
    }

    private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);
}
