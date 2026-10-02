using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ProxyCage.Core;

namespace ProxyCage.Tray.Windows;

[SupportedOSPlatform("windows")]
internal sealed class TrayWindow : IDisposable
{
    private const string ClassName = "CehoProxyTrayWindow";
    private const int IdOn = 1;
    private const int IdOff = 2;
    private const int IdPanel = 3;
    private const int IdQuit = 4;
    private const int IdSignIn = 5;

    private readonly PanelLink _link;
    private readonly string _root;
    private readonly Win32.WindowProc _proc;
    private readonly IntPtr _window;
    private readonly CancellationTokenSource _stop = new();
    private Win32.NOTIFYICONDATA _data;

    private string _lang = "ru";
    private bool _langKnown;
    private bool _passwordProved;
    private TrayLook _look = TrayLook.Stopped;
    private TrayState.Snapshot? _snapshot;
    private DateTime _resumeAtUtc = DateTime.MinValue;
    private string? _balloon;
    private readonly TrayNotifier _notifier = new();
    private int _working;
    private int _goneChecks;
    private readonly string? _exe = Environment.ProcessPath;
    private readonly DateTime _built = Environment.ProcessPath is { } exe ? File.GetLastWriteTimeUtc(exe) : default;

    public TrayWindow(string root)
    {
        _link = new PanelLink(root);
        _root = root;
        _proc = Handle;

        var instance = Win32.GetModuleHandle(null);
        var cls = new Win32.WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<Win32.WNDCLASSEX>(),
            lpfnWndProc = _proc,
            hInstance = instance,
            lpszClassName = ClassName,
        };
        if (Win32.RegisterClassEx(ref cls) == 0)
            throw new InvalidOperationException("RegisterClassEx");

        _window = Win32.CreateWindowEx(
            0, ClassName, "CehoProxy", 0, 0, 0, 0, 0, IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (_window == IntPtr.Zero) throw new InvalidOperationException("CreateWindowEx");

        _data = new Win32.NOTIFYICONDATA
        {
            cbSize = Marshal.SizeOf<Win32.NOTIFYICONDATA>(),
            hWnd = _window,
            uID = 1,
            uFlags = Win32.NIF_MESSAGE | Win32.NIF_ICON | Win32.NIF_TIP,
            uCallbackMessage = Win32.WM_TRAY,
            hIcon = TrayIcons.Handle(_look),
            szTip = TrayState.Tooltip(_lang, _look, null),
            szInfo = "",
            szInfoTitle = "",
        };
        Win32.Shell_NotifyIcon(Win32.NIM_ADD, ref _data);

        _ = Task.Run(PollLoopAsync);
    }

    public void Run()
    {
        while (Win32.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            Win32.TranslateMessage(ref message);
            Win32.DispatchMessage(ref message);
        }
    }

    private int WaitSeconds =>
        _resumeAtUtc > DateTime.UtcNow ? (int)Math.Ceiling((_resumeAtUtc - DateTime.UtcNow).TotalSeconds) : 0;

    private async Task PollLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            if (WaitSeconds > 0)
            {
                Win32.PostMessage(_window, Win32.WM_REFRESH, IntPtr.Zero, IntPtr.Zero);
                try { await Task.Delay(TimeSpan.FromSeconds(1), _stop.Token); }
                catch (TaskCanceledException) { return; }
                continue;
            }

            if (Replaced())
            {
                try { System.Diagnostics.Process.Start(_exe!, "--restart"); } catch { }
                Win32.PostMessage(_window, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                return;
            }

            await ReadAsync();
            try { await Task.Delay(TimeSpan.FromSeconds(TrayState.PollSeconds), _stop.Token); }
            catch (TaskCanceledException) { return; }
        }
    }

    private bool Replaced()
    {
        try { return _exe is not null && File.Exists(_exe) && File.GetLastWriteTimeUtc(_exe) != _built; }
        catch { return false; }
    }

    private async Task ReadAsync()
    {
        var reading = await _link.StatusAsync(quick: true);

        if (reading.Outcome == PanelLink.Outcome.Ok && reading.Snapshot is { } fresh)
        {
            var merged = TrayState.Remember(fresh, _snapshot);
            if (TrayState.NeedsExitProbe(merged))
            {
                var full = await _link.StatusAsync(quick: false);
                if (full is { Outcome: PanelLink.Outcome.Ok, Snapshot: not null }) merged = full.Snapshot;
            }
            reading = reading with { Snapshot = merged };
        }

        Accept(reading);
        Win32.PostMessage(_window, Win32.WM_REFRESH, IntPtr.Zero, IntPtr.Zero);

        if (reading.Outcome == PanelLink.Outcome.Ok && !_langKnown
            && await _link.LanguageAsync() is { } lang)
        {
            _lang = lang;
            _langKnown = true;
            Win32.PostMessage(_window, Win32.WM_REFRESH, IntPtr.Zero, IntPtr.Zero);
        }
    }

    private void Accept(PanelLink.Reading reading)
    {
        switch (reading.Outcome)
        {
            case PanelLink.Outcome.Waiting:
                _resumeAtUtc = DateTime.UtcNow.AddSeconds(reading.WaitSeconds);
                if (!_passwordProved) _link.Password = null;
                _snapshot = null;
                _look = TrayLook.Locked;
                break;

            case PanelLink.Outcome.NeedPassword:
                _link.Password = null;
                _passwordProved = false;
                _snapshot = null;
                _look = TrayLook.Locked;
                break;

            case PanelLink.Outcome.Ok:
                if (_link.Password is { Length: > 0 }) _passwordProved = true;
                _snapshot = reading.Snapshot;
                _look = TrayState.Look(reading.Snapshot, false);
                break;

            default:
                _langKnown = false;
                _snapshot = null;
                _look = TrayLook.Stopped;
                if (Installer.IsGone(_root) && ++_goneChecks >= Installer.GoneChecksToQuit)
                    Win32.PostMessage(_window, Win32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                break;
        }
        if (reading.Outcome != PanelLink.Outcome.Unreachable) _goneChecks = 0;
        if (_notifier.Next(_lang, _look, _snapshot) is { } notice) _balloon = notice;
    }

    private IntPtr Handle(IntPtr window, int message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case Win32.WM_REFRESH:
                Paint();
                return IntPtr.Zero;

            case Win32.WM_TRAY:
                var click = (int)lParam;
                if (click is Win32.WM_RBUTTONUP or Win32.WM_CONTEXTMENU) ShowMenu();
                else if (click == Win32.WM_LBUTTONDBLCLK) OpenPanel();
                return IntPtr.Zero;

            case Win32.WM_COMMAND:
                Dispatch((int)wParam & 0xFFFF);
                return IntPtr.Zero;

            case Win32.WM_DESTROY:
                Win32.PostQuitMessage(0);
                return IntPtr.Zero;
        }
        return Win32.DefWindowProc(window, message, wParam, lParam);
    }

    private void Paint()
    {
        _data.uFlags = Win32.NIF_ICON | Win32.NIF_TIP;
        _data.hIcon = TrayIcons.Handle(_look);
        _data.szTip = TrayState.Tooltip(_lang, _look, _snapshot, WaitSeconds);

        if (_balloon is { Length: > 0 } text)
        {
            _data.uFlags |= Win32.NIF_INFO;
            _data.szInfoTitle = "CehoProxy";
            _data.szInfo = text;
            _data.dwInfoFlags = Win32.NIIF_WARNING;
            _balloon = null;
        }
        else _data.szInfo = "";

        Win32.Shell_NotifyIcon(Win32.NIM_MODIFY, ref _data);
    }

    private void ShowMenu()
    {
        var wait = WaitSeconds;
        var menu = Win32.CreatePopupMenu();
        try
        {
            Win32.AppendMenu(menu, Win32.MF_STRING | Win32.MF_GRAYED, 0,
                TrayState.StateText(_lang, _look, wait, _snapshot?.Recovering == true));
            if (TrayState.Hint(_lang, _look, wait) is { } hint)
                Win32.AppendMenu(menu, Win32.MF_STRING | Win32.MF_GRAYED, 0, hint);
            Win32.AppendMenu(menu, Win32.MF_SEPARATOR, 0, null);

            if (_look == TrayLook.Locked)
                Win32.AppendMenu(menu, Win32.MF_STRING | (wait > 0 ? Win32.MF_GRAYED : 0),
                    IdSignIn, Strings.T(_lang, "tray_sign_in"));
            else if (TrayState.ShowsControls(_snapshot))
            {
                var busy = Volatile.Read(ref _working) == 1;
                Win32.AppendMenu(menu, Win32.MF_STRING | (!busy && TrayState.CanTurnOn(_look) ? 0 : Win32.MF_GRAYED),
                    IdOn, Strings.T(_lang, "btn_on"));
                Win32.AppendMenu(menu, Win32.MF_STRING | (!busy && TrayState.CanTurnOff(_look) ? 0 : Win32.MF_GRAYED),
                    IdOff, Strings.T(_lang, "btn_off"));
            }

            if (_look == TrayLook.Locked || TrayState.ShowsControls(_snapshot))
                Win32.AppendMenu(menu, Win32.MF_SEPARATOR, 0, null);
            Win32.AppendMenu(menu, Win32.MF_STRING | (_link.Url is null ? Win32.MF_GRAYED : 0),
                IdPanel, Strings.T(_lang, "tray_panel"));
            Win32.AppendMenu(menu, Win32.MF_SEPARATOR, 0, null);
            Win32.AppendMenu(menu, Win32.MF_STRING, IdQuit, Strings.T(_lang, "tray_quit"));

            Win32.GetCursorPos(out var where);
            Win32.SetForegroundWindow(_window);
            var picked = Win32.TrackPopupMenuEx(
                menu, Win32.TPM_RIGHTBUTTON | Win32.TPM_RETURNCMD,
                where.X, where.Y, _window, IntPtr.Zero);
            if (picked != 0) Dispatch(picked);
        }
        finally { Win32.DestroyMenu(menu); }
    }

    private void Dispatch(int command)
    {
        switch (command)
        {
            case IdOn: Command(_link.TurnOnAsync); break;
            case IdOff: Command(_link.TurnOffAsync); break;
            case IdSignIn: SignIn(); break;
            case IdPanel: OpenPanel(); break;
            case IdQuit: Win32.DestroyWindow(_window); break;
        }
    }

    private void SignIn()
    {
        var entered = AskPassword.Show(
            "CehoProxy", Strings.T(_lang, "tray_password_prompt"),
            Strings.T(_lang, "auth_enter"), Strings.T(_lang, "btn_cancel"));
        if (entered is null) return;

        _link.Password = entered;
        _ = Task.Run(async () =>
        {
            var reading = await _link.StatusAsync(quick: true);
            if (reading.Outcome == PanelLink.Outcome.NeedPassword)
                _balloon = Strings.T(_lang, "auth_wrong");
            Accept(reading);
            Win32.PostMessage(_window, Win32.WM_REFRESH, IntPtr.Zero, IntPtr.Zero);
        });
    }

    private void Command(Func<Task<bool>> action)
    {
        if (Interlocked.Exchange(ref _working, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                if (!await action()) _balloon = Strings.T(_lang, "tray_failed");
                await ReadAsync();
            }
            finally { Interlocked.Exchange(ref _working, 0); }
        });
    }

    private void OpenPanel()
    {
        if (_link.Url is { } url) Os.OpenInBrowser(url);
    }

    public void Dispose()
    {
        _stop.Cancel();
        Win32.Shell_NotifyIcon(Win32.NIM_DELETE, ref _data);
        TrayIcons.Release();
        _link.Dispose();
        _stop.Dispose();
    }
}
