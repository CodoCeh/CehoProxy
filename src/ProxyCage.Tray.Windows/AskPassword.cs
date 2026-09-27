using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace ProxyCage.Tray.Windows;

[SupportedOSPlatform("windows")]
internal static class AskPassword
{
    private const string ClassName = "CehoProxyTrayAsk";
    private const int IdOk = 1;
    private const int IdCancel = 2;

    private static Win32.WindowProc? _proc;
    private static bool _registered;
    private static IntPtr _font;
    private static IntPtr _edit;
    private static string? _answer;
    private static bool _closed;

    public static string? Show(string title, string prompt, string okText, string cancelText)
    {
        Register();
        _answer = null;
        _closed = false;

        const int width = 460;
        const int height = 210;
        var x = (Win32.GetSystemMetrics(Win32.SM_CXSCREEN) - width) / 2;
        var y = (Win32.GetSystemMetrics(Win32.SM_CYSCREEN) - height) / 3;
        var instance = Win32.GetModuleHandle(null);

        var dialog = Win32.CreateWindowEx(
            Win32.WS_EX_DLGMODALFRAME | Win32.WS_EX_TOPMOST, ClassName, title,
            Win32.WS_CAPTION | Win32.WS_SYSMENU, x, y, width, height,
            IntPtr.Zero, IntPtr.Zero, instance, IntPtr.Zero);
        if (dialog == IntPtr.Zero) return null;

        var label = Child(dialog, "STATIC", prompt, Win32.SS_LEFT, 18, 18, width - 52, 60, 0);
        _edit = Child(dialog, "EDIT", "",
            Win32.WS_BORDER | Win32.WS_TABSTOP | Win32.ES_PASSWORD | Win32.ES_AUTOHSCROLL,
            18, 88, width - 52, 28, 0);
        var ok = Child(dialog, "BUTTON", okText,
            Win32.WS_TABSTOP | Win32.BS_DEFPUSHBUTTON, width - 260, 132, 110, 30, IdOk);
        var cancel = Child(dialog, "BUTTON", cancelText,
            Win32.WS_TABSTOP, width - 142, 132, 110, 30, IdCancel);

        foreach (var child in new[] { label, _edit, ok, cancel })
            Win32.SendMessage(child, Win32.WM_SETFONT, _font, new IntPtr(1));

        Win32.ShowWindow(dialog, Win32.SW_SHOW);
        Win32.SetForegroundWindow(dialog);
        Win32.SetFocus(_edit);

        while (!_closed && Win32.GetMessage(out var message, IntPtr.Zero, 0, 0) > 0)
        {
            if (Win32.IsDialogMessage(dialog, ref message)) continue;
            Win32.TranslateMessage(ref message);
            Win32.DispatchMessage(ref message);
        }

        Win32.DestroyWindow(dialog);
        _edit = IntPtr.Zero;
        return _answer;
    }

    private static IntPtr Child(
        IntPtr parent, string cls, string text, int style, int x, int y, int w, int h, int id) =>
        Win32.CreateWindowEx(
            0, cls, text, Win32.WS_CHILD | Win32.WS_VISIBLE | style, x, y, w, h,
            parent, new IntPtr(id), Win32.GetModuleHandle(null), IntPtr.Zero);

    private static void Register()
    {
        if (_registered) return;
        _proc = Handle;
        _font = Win32.CreateFont(-15, 0, 0, 0, 400, 0, 0, 0, 1, 0, 0, 5, 0, "Segoe UI");
        var cls = new Win32.WNDCLASSEX
        {
            cbSize = Marshal.SizeOf<Win32.WNDCLASSEX>(),
            lpfnWndProc = _proc,
            hInstance = Win32.GetModuleHandle(null),
            lpszClassName = ClassName,
            hbrBackground = new IntPtr(16),
        };
        Win32.RegisterClassEx(ref cls);
        _registered = true;
    }

    private static IntPtr Handle(IntPtr window, int message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case Win32.WM_COMMAND:
                var id = (int)wParam & 0xFFFF;
                if (id == IdOk) _answer = Read();
                if (id is IdOk or IdCancel) _closed = true;
                return IntPtr.Zero;

            case Win32.WM_CLOSE:
                _closed = true;
                return IntPtr.Zero;
        }
        return Win32.DefWindowProc(window, message, wParam, lParam);
    }

    private static string? Read()
    {
        if (_edit == IntPtr.Zero) return null;
        var length = Win32.GetWindowTextLength(_edit);
        if (length <= 0) return null;
        var buffer = new StringBuilder(length + 1);
        Win32.GetWindowText(_edit, buffer, buffer.Capacity);
        var text = buffer.ToString();
        return text.Length == 0 ? null : text;
    }
}
