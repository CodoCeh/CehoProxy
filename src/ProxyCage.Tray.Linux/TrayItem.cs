using System.Diagnostics;
using System.Text;
using ProxyCage.Core;
using Tmds.DBus.Protocol;

namespace ProxyCage.Tray.Linux;

internal sealed class TrayItem : IDisposable
{
    public const string SingleName = "ru.codoceh.CehoProxyTray";

    private const string ItemPath = "/StatusNotifierItem";
    private const string MenuPath = "/MenuBar";
    private const string ItemInterface = "org.kde.StatusNotifierItem";
    private const string MenuInterface = "com.canonical.dbusmenu";
    private const string PropertiesInterface = "org.freedesktop.DBus.Properties";
    private const string WatcherName = "org.kde.StatusNotifierWatcher";
    private const string LayoutSignature = "(ia{sv}av)";

    private const int IdState = 1;
    private const int IdHint = 2;
    private const int IdOn = 4;
    private const int IdOff = 5;
    private const int IdSignIn = 6;
    private const int IdPanel = 8;
    private const int IdQuit = 10;

    private static readonly int[] Sizes = { 22, 32, 48 };

    private readonly DBusConnection _connection;
    private readonly PanelLink _link;
    private readonly CancellationTokenSource _stop = new();
    private readonly object _gate = new();
    private readonly Dictionary<TrayLook, (int Size, byte[] Argb)[]> _icons = new();
    private readonly string _itemName = $"org.kde.StatusNotifierItem-{Environment.ProcessId}-1";

    private string _lang = "ru";
    private bool _langKnown;
    private bool _passwordProved;
    private TrayLook _look = TrayLook.Stopped;
    private TrayState.Snapshot? _snapshot;
    private readonly TrayNotifier _notifier = new();
    private DateTime _resumeAtUtc = DateTime.MinValue;
    private uint _revision = 1;
    private int _working;
    private int _asking;
    private string? _watcherOwner;
    private readonly string? _exe = Environment.ProcessPath;
    private readonly DateTime _built = Environment.ProcessPath is { } exe ? File.GetLastWriteTimeUtc(exe) : default;

    public TrayItem(DBusConnection connection, string root)
    {
        _connection = connection;
        _link = new PanelLink(root);

        using var stream = typeof(TrayItem).Assembly.GetManifestResourceStream("cehoproxy.png")
            ?? throw new InvalidOperationException("cehoproxy.png");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var brand = TrayPixmap.Decode(buffer.ToArray());
        foreach (var look in Enum.GetValues<TrayLook>())
            _icons[look] = Sizes
                .Select(size => (size, TrayPixmap.NetworkOrder(TrayPixmap.Render(brand, look, size))))
                .ToArray();

        _connection.AddMethodHandler(new Handler(ItemPath, HandleItem));
        _connection.AddMethodHandler(new Handler(MenuPath, HandleMenu));
    }

    public async Task RunAsync()
    {
        await _connection.RequestNameAsync(_itemName, RequestNameOptions.None);
        var disconnected = _connection.DisconnectedAsync();

        while (!_stop.IsCancellationRequested && !disconnected.IsCompleted)
        {
            if (Replaced())
            {
                try { Process.Start(_exe!, "--restart"); } catch { }
                return;
            }

            await RegisterWithWatcherAsync();

            if (WaitSeconds > 0) Changed();
            else await ReadAsync();

            var pause = WaitSeconds > 0 ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(TrayState.PollSeconds);
            try { await Task.WhenAny(Task.Delay(pause, _stop.Token), disconnected); }
            catch (TaskCanceledException) { return; }
        }
    }

    private bool Replaced()
    {
        try { return _exe is not null && File.Exists(_exe) && File.GetLastWriteTimeUtc(_exe) != _built; }
        catch { return false; }
    }

    private int WaitSeconds =>
        _resumeAtUtc > DateTime.UtcNow ? (int)Math.Ceiling((_resumeAtUtc - DateTime.UtcNow).TotalSeconds) : 0;

    private async Task RegisterWithWatcherAsync()
    {
        string? owner;
        try
        {
            owner = await _connection.CallMethodAsync(Call("org.freedesktop.DBus", "/org/freedesktop/DBus",
                    "org.freedesktop.DBus", "GetNameOwner", WatcherName),
                (Message message, object? _) => message.GetBodyReader().ReadString(), null);
        }
        catch { owner = null; }

        if (owner is null || owner == _watcherOwner) return;
        try
        {
            await _connection.CallMethodAsync(Call(WatcherName, "/StatusNotifierWatcher",
                WatcherName, "RegisterStatusNotifierItem", _itemName));
            _watcherOwner = owner;
        }
        catch { }
    }

    private MessageBuffer Call(string destination, string path, string iface, string member, string argument)
    {
        using var writer = _connection.GetMessageWriter();
        writer.WriteMethodCallHeader(destination, path, iface, member, "s", MessageFlags.None);
        writer.WriteString(argument);
        return writer.CreateMessage();
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

        if (reading.Outcome == PanelLink.Outcome.Ok && !_langKnown && await _link.LanguageAsync() is { } lang)
        {
            lock (_gate)
            {
                _lang = lang;
                _langKnown = true;
            }
        }
        Changed();
    }

    private void Accept(PanelLink.Reading reading)
    {
        lock (_gate)
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
                    break;
            }
            if (_notifier.Next(_lang, _look, _snapshot) is { } notice) Notify(notice);
        }
    }

    private string _shown = "";

    private void Changed()
    {
        string key;
        lock (_gate) key = $"{_look}|{_lang}|{Tooltip()}|{Volatile.Read(ref _working)}|{TrayState.ShowsControls(_snapshot)}";
        if (key == _shown) return;
        _shown = key;

        uint revision;
        lock (_gate) revision = ++_revision;
        Signal(ItemPath, ItemInterface, "NewIcon");
        Signal(ItemPath, ItemInterface, "NewToolTip");
        try
        {
            var writer = _connection.GetMessageWriter();
            try
            {
                writer.WriteSignalHeader(null, MenuPath, MenuInterface, "LayoutUpdated", "ui");
                writer.WriteUInt32(revision);
                writer.WriteInt32(0);
                _connection.TrySendMessage(writer.CreateMessage());
            }
            finally { writer.Dispose(); }
        }
        catch { }
    }

    private void Signal(string path, string iface, string member)
    {
        try
        {
            var writer = _connection.GetMessageWriter();
            try
            {
                writer.WriteSignalHeader(null, path, iface, member, null);
                _connection.TrySendMessage(writer.CreateMessage());
            }
            finally { writer.Dispose(); }
        }
        catch { }
    }

    private string Tooltip() => TrayState.Tooltip(_lang, _look, _snapshot, WaitSeconds, limit: 0);

    private ValueTask HandleItem(MethodContext context)
    {
        var request = context.Request;
        if (context.IsDBusIntrospectRequest)
        {
            context.ReplyIntrospectXml(new[] { ItemXml }, (IList<string>)Array.Empty<string>());
            return ValueTask.CompletedTask;
        }

        switch (request.InterfaceAsString, request.MemberAsString)
        {
            case (PropertiesInterface, "Get"):
            {
                var reader = request.GetBodyReader();
                reader.ReadString();
                var name = reader.ReadString();
                var writer = context.CreateReplyWriter("v");
                if (!WriteItemProperty(ref writer, name))
                {
                    writer.Dispose();
                    context.ReplyError("org.freedesktop.DBus.Error.UnknownProperty", name);
                    return ValueTask.CompletedTask;
                }
                context.Reply(writer.CreateMessage());
                return ValueTask.CompletedTask;
            }

            case (PropertiesInterface, "GetAll"):
            {
                var writer = context.CreateReplyWriter("a{sv}");
                var dict = writer.WriteDictionaryStart();
                foreach (var name in ItemProperties)
                {
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString(name);
                    WriteItemProperty(ref writer, name);
                }
                writer.WriteDictionaryEnd(dict);
                context.Reply(writer.CreateMessage());
                return ValueTask.CompletedTask;
            }

            case (ItemInterface, "Activate"):
            case (ItemInterface, "SecondaryActivate"):
                if (_link.Url is { } url) Os.OpenInBrowser(url);
                ReplyEmpty(context);
                return ValueTask.CompletedTask;

            case (ItemInterface, "ContextMenu"):
            case (ItemInterface, "Scroll"):
                ReplyEmpty(context);
                return ValueTask.CompletedTask;
        }

        context.ReplyUnknownMethodError();
        return ValueTask.CompletedTask;
    }

    private static readonly string[] ItemProperties =
    {
        "Category", "Id", "Title", "Status", "WindowId", "IconName", "IconThemePath", "IconPixmap",
        "OverlayIconName", "OverlayIconPixmap", "AttentionIconName", "AttentionIconPixmap",
        "AttentionMovieName", "ToolTip", "ItemIsMenu", "Menu",
    };

    private bool WriteItemProperty(ref MessageWriter writer, string name)
    {
        TrayLook look;
        string tip;
        lock (_gate)
        {
            look = _look;
            tip = Tooltip();
        }

        switch (name)
        {
            case "Category": writer.WriteVariantString("ApplicationStatus"); return true;
            case "Id": writer.WriteVariantString("cehoproxy"); return true;
            case "Title": writer.WriteVariantString("CehoProxy"); return true;
            case "Status": writer.WriteVariantString("Active"); return true;
            case "WindowId": writer.WriteVariantInt32(0); return true;
            case "IconName":
            case "IconThemePath":
            case "OverlayIconName":
            case "AttentionIconName":
            case "AttentionMovieName":
                writer.WriteVariantString("");
                return true;
            case "IconPixmap":
                writer.WriteSignature("a(iiay)");
                WritePixmaps(ref writer, _icons[look]);
                return true;
            case "OverlayIconPixmap":
            case "AttentionIconPixmap":
                writer.WriteSignature("a(iiay)");
                WritePixmaps(ref writer, Array.Empty<(int, byte[])>());
                return true;
            case "ToolTip":
                writer.WriteSignature("(sa(iiay)ss)");
                writer.WriteStructureStart();
                writer.WriteString("");
                WritePixmaps(ref writer, Array.Empty<(int, byte[])>());
                writer.WriteString("CehoProxy");
                writer.WriteString(tip);
                return true;
            case "ItemIsMenu": writer.WriteVariantBool(true); return true;
            case "Menu":
                writer.WriteSignature("o");
                writer.WriteObjectPath(MenuPath);
                return true;
        }
        return false;
    }

    private static void WritePixmaps(ref MessageWriter writer, (int Size, byte[] Argb)[] pixmaps)
    {
        var array = writer.WriteArrayStart(DBusType.Struct);
        foreach (var (size, argb) in pixmaps)
        {
            writer.WriteStructureStart();
            writer.WriteInt32(size);
            writer.WriteInt32(size);
            writer.WriteArray(argb);
        }
        writer.WriteArrayEnd(array);
    }

    private sealed record Entry(int Id, string? Label, bool Enabled, bool Separator = false);

    private List<Entry> Entries()
    {
        lock (_gate)
        {
            var wait = WaitSeconds;
            var busy = Volatile.Read(ref _working) == 1;
            var list = new List<Entry> { new(IdState, TrayState.StateText(_lang, _look, wait, _snapshot?.Recovering == true), false) };
            if (TrayState.Hint(_lang, _look, wait) is { } hint) list.Add(new(IdHint, hint, false));
            list.Add(new(3, null, true, true));
            if (_look == TrayLook.Locked)
                list.Add(new(IdSignIn, Strings.T(_lang, "tray_sign_in"), wait == 0));
            else if (TrayState.ShowsControls(_snapshot))
            {
                list.Add(new(IdOn, Strings.T(_lang, "btn_on"), !busy && TrayState.CanTurnOn(_look)));
                list.Add(new(IdOff, Strings.T(_lang, "btn_off"), !busy && TrayState.CanTurnOff(_look)));
            }
            if (_look == TrayLook.Locked || TrayState.ShowsControls(_snapshot)) list.Add(new(7, null, true, true));
            list.Add(new(IdPanel, Strings.T(_lang, "tray_panel"), _link.Url is not null));
            list.Add(new(9, null, true, true));
            list.Add(new(IdQuit, Strings.T(_lang, "tray_quit"), true));
            return list;
        }
    }

    private static void WriteEntryProperties(ref MessageWriter writer, Entry entry)
    {
        var dict = writer.WriteDictionaryStart();
        if (entry.Separator)
        {
            writer.WriteDictionaryEntryStart();
            writer.WriteString("type");
            writer.WriteVariantString("separator");
        }
        else
        {
            writer.WriteDictionaryEntryStart();
            writer.WriteString("label");
            writer.WriteVariantString(entry.Label ?? "");
            writer.WriteDictionaryEntryStart();
            writer.WriteString("enabled");
            writer.WriteVariantBool(entry.Enabled);
        }
        writer.WriteDictionaryEnd(dict);
    }

    private static void WriteRootProperties(ref MessageWriter writer)
    {
        var dict = writer.WriteDictionaryStart();
        writer.WriteDictionaryEntryStart();
        writer.WriteString("children-display");
        writer.WriteVariantString("submenu");
        writer.WriteDictionaryEnd(dict);
    }

    private ValueTask HandleMenu(MethodContext context)
    {
        var request = context.Request;
        if (context.IsDBusIntrospectRequest)
        {
            context.ReplyIntrospectXml(new[] { MenuXml }, (IList<string>)Array.Empty<string>());
            return ValueTask.CompletedTask;
        }

        switch (request.InterfaceAsString, request.MemberAsString)
        {
            case (PropertiesInterface, "Get"):
            {
                var reader = request.GetBodyReader();
                reader.ReadString();
                var name = reader.ReadString();
                var writer = context.CreateReplyWriter("v");
                if (!WriteMenuProperty(ref writer, name))
                {
                    writer.Dispose();
                    context.ReplyError("org.freedesktop.DBus.Error.UnknownProperty", name);
                    return ValueTask.CompletedTask;
                }
                context.Reply(writer.CreateMessage());
                return ValueTask.CompletedTask;
            }

            case (PropertiesInterface, "GetAll"):
            {
                var writer = context.CreateReplyWriter("a{sv}");
                var dict = writer.WriteDictionaryStart();
                foreach (var name in new[] { "Version", "TextDirection", "Status", "IconThemePath" })
                {
                    writer.WriteDictionaryEntryStart();
                    writer.WriteString(name);
                    WriteMenuProperty(ref writer, name);
                }
                writer.WriteDictionaryEnd(dict);
                context.Reply(writer.CreateMessage());
                return ValueTask.CompletedTask;
            }

            case (MenuInterface, "GetLayout"):
            {
                var parent = request.GetBodyReader().ReadInt32();
                var entries = Entries();
                uint revision;
                lock (_gate) revision = _revision;
                var writer = context.CreateReplyWriter("u" + LayoutSignature);
                writer.WriteUInt32(revision);
                writer.WriteStructureStart();
                var item = entries.FirstOrDefault(e => e.Id == parent);
                writer.WriteInt32(item?.Id ?? 0);
                if (item is null) WriteRootProperties(ref writer);
                else WriteEntryProperties(ref writer, item);
                var children = writer.WriteArrayStart(DBusType.Variant);
                if (item is null)
                    foreach (var entry in entries)
                    {
                        writer.WriteSignature(LayoutSignature);
                        writer.WriteStructureStart();
                        writer.WriteInt32(entry.Id);
                        WriteEntryProperties(ref writer, entry);
                        var none = writer.WriteArrayStart(DBusType.Variant);
                        writer.WriteArrayEnd(none);
                    }
                writer.WriteArrayEnd(children);
                context.Reply(writer.CreateMessage());
                return ValueTask.CompletedTask;
            }

            case (MenuInterface, "GetGroupProperties"):
            {
                var ids = request.GetBodyReader().ReadArrayOfInt32();
                var entries = Entries();
                var writer = context.CreateReplyWriter("a(ia{sv})");
                var array = writer.WriteArrayStart(DBusType.Struct);
                foreach (var entry in entries.Where(e => ids.Length == 0 || ids.Contains(e.Id)))
                {
                    writer.WriteStructureStart();
                    writer.WriteInt32(entry.Id);
                    WriteEntryProperties(ref writer, entry);
                }
                writer.WriteArrayEnd(array);
                context.Reply(writer.CreateMessage());
                return ValueTask.CompletedTask;
            }

            case (MenuInterface, "GetProperty"):
            {
                var reader = request.GetBodyReader();
                var id = reader.ReadInt32();
                var name = reader.ReadString();
                var entry = Entries().FirstOrDefault(e => e.Id == id);
                var writer = context.CreateReplyWriter("v");
                if (name == "enabled") writer.WriteVariantBool(entry?.Enabled ?? false);
                else writer.WriteVariantString(name == "label" ? entry?.Label ?? "" : "");
                context.Reply(writer.CreateMessage());
                return ValueTask.CompletedTask;
            }

            case (MenuInterface, "Event"):
            {
                var reader = request.GetBodyReader();
                var id = reader.ReadInt32();
                var kind = reader.ReadString();
                ReplyEmpty(context);
                if (kind == "clicked") Dispatch(id);
                return ValueTask.CompletedTask;
            }

            case (MenuInterface, "EventGroup"):
            {
                var reader = request.GetBodyReader();
                var clicked = new List<int>();
                var end = reader.ReadArrayStart(DBusType.Struct);
                while (reader.HasNext(end))
                {
                    reader.AlignStruct();
                    var id = reader.ReadInt32();
                    var kind = reader.ReadString();
                    reader.ReadVariantValue();
                    reader.ReadUInt32();
                    if (kind == "clicked") clicked.Add(id);
                }
                using var writer = context.CreateReplyWriter("ai");
                writer.WriteArray(Array.Empty<int>());
                context.Reply(writer.CreateMessage());
                foreach (var id in clicked) Dispatch(id);
                return ValueTask.CompletedTask;
            }

            case (MenuInterface, "AboutToShow"):
            {
                using var writer = context.CreateReplyWriter("b");
                writer.WriteBool(false);
                context.Reply(writer.CreateMessage());
                return ValueTask.CompletedTask;
            }

            case (MenuInterface, "AboutToShowGroup"):
            {
                using var writer = context.CreateReplyWriter("aiai");
                writer.WriteArray(Array.Empty<int>());
                writer.WriteArray(Array.Empty<int>());
                context.Reply(writer.CreateMessage());
                return ValueTask.CompletedTask;
            }
        }

        context.ReplyUnknownMethodError();
        return ValueTask.CompletedTask;
    }

    private static bool WriteMenuProperty(ref MessageWriter writer, string name)
    {
        switch (name)
        {
            case "Version": writer.WriteVariantUInt32(3); return true;
            case "TextDirection": writer.WriteVariantString("ltr"); return true;
            case "Status": writer.WriteVariantString("normal"); return true;
            case "IconThemePath":
                writer.WriteSignature("as");
                writer.WriteArray(Array.Empty<string>());
                return true;
        }
        return false;
    }

    private static void ReplyEmpty(MethodContext context)
    {
        if (context.NoReplyExpected) return;
        using var writer = context.CreateReplyWriter(null);
        context.Reply(writer.CreateMessage());
    }

    private void Dispatch(int id)
    {
        switch (id)
        {
            case IdOn when TrayState.ShowsControls(_snapshot): Command(_link.TurnOnAsync); break;
            case IdOff when TrayState.ShowsControls(_snapshot): Command(_link.TurnOffAsync); break;
            case IdSignIn: _ = Task.Run(SignInAsync); break;
            case IdPanel:
                if (_link.Url is { } url) Os.OpenInBrowser(url);
                break;
            case IdQuit: _stop.Cancel(); break;
        }
    }

    private void Command(Func<Task<bool>> action)
    {
        if (Interlocked.Exchange(ref _working, 1) == 1) return;
        Changed();
        _ = Task.Run(async () =>
        {
            try
            {
                if (!await action()) Notify(Strings.T(_lang, "tray_failed"));
                await ReadAsync();
            }
            finally
            {
                Interlocked.Exchange(ref _working, 0);
                Changed();
            }
        });
    }

    private async Task SignInAsync()
    {
        if (Interlocked.Exchange(ref _asking, 1) == 1) return;
        try
        {
            var entered = await AskPasswordAsync();
            if (entered is null) return;

            _link.Password = entered;
            var reading = await _link.StatusAsync(quick: true);
            if (reading.Outcome == PanelLink.Outcome.NeedPassword) Notify(Strings.T(_lang, "auth_wrong"));
            Accept(reading);
            Changed();
        }
        finally { Interlocked.Exchange(ref _asking, 0); }
    }

    private async Task<string?> AskPasswordAsync()
    {
        var prompt = Strings.T(_lang, "tray_password_prompt");
        (string File, string[] Args)? dialog =
            Os.FindOnPath("zenity") is not null
                ? ("zenity", new[] { "--entry", "--hide-text", "--title=CehoProxy", "--text=" + prompt })
            : Os.FindOnPath("kdialog") is not null
                ? ("kdialog", new[] { "--title", "CehoProxy", "--password", prompt })
            : null;
        if (dialog is not { } found)
        {
            Notify(Strings.T(_lang, "tray_no_dialog"));
            return null;
        }

        var start = new ProcessStartInfo(found.File)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
        };
        foreach (var arg in found.Args) start.ArgumentList.Add(arg);
        try
        {
            using var process = Process.Start(start);
            if (process is null) return null;
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            if (process.ExitCode != 0) return null;
            var text = output.TrimEnd('\r', '\n');
            return text.Length == 0 ? null : text;
        }
        catch { return null; }
    }

    private static void Notify(string text)
    {
        if (Os.FindOnPath("notify-send") is null) return;
        try
        {
            var start = new ProcessStartInfo("notify-send") { UseShellExecute = false };
            foreach (var arg in new[] { "-a", "CehoProxy", "CehoProxy", text }) start.ArgumentList.Add(arg);
            using var _ = Process.Start(start);
        }
        catch { }
    }

    public void Dispose()
    {
        _stop.Cancel();
        _link.Dispose();
        _stop.Dispose();
    }

    private sealed class Handler(string path, Func<MethodContext, ValueTask> handle) : IPathMethodHandler
    {
        public string Path => path;

        public bool HandlesChildPaths => false;

        public ValueTask HandleMethodAsync(MethodContext context) => handle(context);
    }

    private static readonly ReadOnlyMemory<byte> ItemXml = Encoding.UTF8.GetBytes("""
        <interface name="org.kde.StatusNotifierItem">
          <property name="Category" type="s" access="read"/>
          <property name="Id" type="s" access="read"/>
          <property name="Title" type="s" access="read"/>
          <property name="Status" type="s" access="read"/>
          <property name="WindowId" type="i" access="read"/>
          <property name="IconName" type="s" access="read"/>
          <property name="IconThemePath" type="s" access="read"/>
          <property name="IconPixmap" type="a(iiay)" access="read"/>
          <property name="OverlayIconName" type="s" access="read"/>
          <property name="OverlayIconPixmap" type="a(iiay)" access="read"/>
          <property name="AttentionIconName" type="s" access="read"/>
          <property name="AttentionIconPixmap" type="a(iiay)" access="read"/>
          <property name="AttentionMovieName" type="s" access="read"/>
          <property name="ToolTip" type="(sa(iiay)ss)" access="read"/>
          <property name="ItemIsMenu" type="b" access="read"/>
          <property name="Menu" type="o" access="read"/>
          <method name="ContextMenu"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
          <method name="Activate"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
          <method name="SecondaryActivate"><arg name="x" type="i" direction="in"/><arg name="y" type="i" direction="in"/></method>
          <method name="Scroll"><arg name="delta" type="i" direction="in"/><arg name="orientation" type="s" direction="in"/></method>
          <signal name="NewTitle"/>
          <signal name="NewIcon"/>
          <signal name="NewAttentionIcon"/>
          <signal name="NewOverlayIcon"/>
          <signal name="NewToolTip"/>
          <signal name="NewStatus"><arg name="status" type="s"/></signal>
        </interface>
        """);

    private static readonly ReadOnlyMemory<byte> MenuXml = Encoding.UTF8.GetBytes("""
        <interface name="com.canonical.dbusmenu">
          <property name="Version" type="u" access="read"/>
          <property name="TextDirection" type="s" access="read"/>
          <property name="Status" type="s" access="read"/>
          <property name="IconThemePath" type="as" access="read"/>
          <method name="GetLayout">
            <arg type="i" name="parentId" direction="in"/>
            <arg type="i" name="recursionDepth" direction="in"/>
            <arg type="as" name="propertyNames" direction="in"/>
            <arg type="u" name="revision" direction="out"/>
            <arg type="(ia{sv}av)" name="layout" direction="out"/>
          </method>
          <method name="GetGroupProperties">
            <arg type="ai" name="ids" direction="in"/>
            <arg type="as" name="propertyNames" direction="in"/>
            <arg type="a(ia{sv})" name="properties" direction="out"/>
          </method>
          <method name="GetProperty">
            <arg type="i" name="id" direction="in"/>
            <arg type="s" name="name" direction="in"/>
            <arg type="v" name="value" direction="out"/>
          </method>
          <method name="Event">
            <arg type="i" name="id" direction="in"/>
            <arg type="s" name="eventId" direction="in"/>
            <arg type="v" name="data" direction="in"/>
            <arg type="u" name="timestamp" direction="in"/>
          </method>
          <method name="EventGroup">
            <arg type="a(isvu)" name="events" direction="in"/>
            <arg type="ai" name="idErrors" direction="out"/>
          </method>
          <method name="AboutToShow">
            <arg type="i" name="id" direction="in"/>
            <arg type="b" name="needUpdate" direction="out"/>
          </method>
          <method name="AboutToShowGroup">
            <arg type="ai" name="ids" direction="in"/>
            <arg type="ai" name="updatesNeeded" direction="out"/>
            <arg type="ai" name="idErrors" direction="out"/>
          </method>
          <signal name="ItemsPropertiesUpdated">
            <arg type="a(ia{sv})" name="updatedProps"/>
            <arg type="a(ias)" name="removedProps"/>
          </signal>
          <signal name="LayoutUpdated">
            <arg type="u" name="revision"/>
            <arg type="i" name="parent"/>
          </signal>
        </interface>
        """);
}
