using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PathJump;

/// <summary>Clipboard writes made by PathJump itself, so they aren't captured as new paths.</summary>
static class Clip
{
    public static DateTime SuppressUntil;

    public static void SetQuiet(string text)
    {
        SuppressUntil = DateTime.Now.AddMilliseconds(800);
        try { Clipboard.SetText(text); } catch (ExternalException) { }
    }

    /// <summary>Raw path candidates on the clipboard: copied files, or each line of copied text.</summary>
    public static List<string> ReadCandidates()
    {
        var candidates = new List<string>();
        for (int attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                if (Clipboard.ContainsFileDropList())
                    foreach (var f in Clipboard.GetFileDropList()) candidates.Add(f);
                else if (Clipboard.ContainsText())
                {
                    var text = Clipboard.GetText();
                    if (text.Length <= 8000)
                        foreach (var line in text.Split('\n'))
                            candidates.Add(Environment.ExpandEnvironmentVariables(line.Trim().Trim('"', '\'')));
                }
                break;
            }
            catch (ExternalException) { candidates.Clear(); Thread.Sleep(40); } // clipboard busy in another app
        }
        return candidates;
    }

    /// <summary>The first existing path on the clipboard, or null.</summary>
    public static string ReadPath() =>
        ReadCandidates().Take(50).Select(Store.Normalize).FirstOrDefault(p => p != null && Exists(p));

    /// <summary>Existence check with a time limit, so an offline network share can't hang the app.</summary>
    public static bool Exists(string p)
    {
        if (!p.StartsWith(@"\\")) return Directory.Exists(p) || File.Exists(p);
        var t = Task.Run(() => Directory.Exists(p) || File.Exists(p));
        return t.Wait(1500) && t.Result;
    }
}

/// <summary>Hidden window that receives hotkey and clipboard-change messages.</summary>
class MessageWindow : NativeWindow
{
    public event Action<int, IntPtr> Message;
    public MessageWindow() => CreateHandle(new CreateParams());

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == Native.WM_HOTKEY || m.Msg == Native.WM_CLIPBOARDUPDATE)
            Message?.Invoke(m.Msg, m.WParam);
        base.WndProc(ref m);
    }
}

class App : ApplicationContext
{
    const int HOTKEY_PANEL = 1, HOTKEY_QUICKJUMP = 2;
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    readonly Store store = Store.Load();
    readonly PanelForm panel;
    readonly NotifyIcon tray;
    readonly MessageWindow messages = new();
    readonly ToolStripMenuItem pauseItem = new("Pause capturing");
    readonly ToolStripMenuItem startupItem = new("Start with Windows");
    IntPtr target;
    string lastCapture;
    DateTime lastCaptureTime;

    public App()
    {
        panel = new PanelForm(store);
        panel.Picked += OnPicked;

        var menu = new ContextMenuStrip();
        menu.Items.Add("Show paths", null, (_, _) => ShowPanel(IntPtr.Zero));
        menu.Items.Add("Change hotkeys…", null, (_, _) => OpenHotkeyWizard());
        pauseItem.CheckOnClick = true;
        menu.Items.Add(pauseItem);
        RepairStartupPath();
        startupItem.Checked = IsStartupEnabled();
        startupItem.Click += (_, _) => { SetStartup(!startupItem.Checked); startupItem.Checked = IsStartupEnabled(); };
        menu.Items.Add(startupItem);
        menu.Items.Add("Open settings folder", null, (_, _) => Jumper.OpenInExplorer(Store.DataDir));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => Quit());

        tray = new NotifyIcon { Icon = LoadIcon(SystemInformation.SmallIconSize), Text = "PathJump", Visible = true, ContextMenuStrip = menu };
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) ShowPanel(IntPtr.Zero); };
        tray.BalloonTipClicked += (_, _) => { if (failedHotkeys.Count > 0) OpenHotkeyWizard(); };

        messages.Message += OnMessage;
        Native.AddClipboardFormatListener(messages.Handle);

        // A hotkey held by another program at startup is retried quietly, in case it gets released.
        retryTimer.Tick += (_, _) => { if (!hotkeysSuspended && failedHotkeys.Count > 0) RegisterHotkeys(); };

        RegisterHotkeys();
        if (failedHotkeys.Count > 0) NotifyHotkeyProblem();
        else Notify($"Running. Copy any file/folder path, then press {store.Settings.PanelHotkey}.");

        // "PathJump.exe --hotkeys" opens the hotkey wizard right away.
        if (Environment.GetCommandLineArgs().Skip(1).Any(a => a.Equals("--hotkeys", StringComparison.OrdinalIgnoreCase)))
        {
            var once = new System.Windows.Forms.Timer { Interval = 300 };
            once.Tick += (_, _) => { once.Dispose(); OpenHotkeyWizard(); };
            once.Start();
        }
    }

    readonly List<string> failedHotkeys = new();
    readonly System.Windows.Forms.Timer retryTimer = new() { Interval = 20000 };
    bool hotkeysSuspended;
    HotkeyForm hotkeyForm;

    void RegisterHotkeys()
    {
        UnregisterHotkeys();
        failedHotkeys.Clear();
        if (!Register(HOTKEY_PANEL, store.Settings.PanelHotkey)) failedHotkeys.Add(store.Settings.PanelHotkey);
        if (!Register(HOTKEY_QUICKJUMP, store.Settings.QuickJumpHotkey)) failedHotkeys.Add(store.Settings.QuickJumpHotkey);
        retryTimer.Enabled = failedHotkeys.Count > 0;
        tray.Text = failedHotkeys.Count > 0
            ? "PathJump – hotkey not active (right-click → Change hotkeys)"
            : $"PathJump – {store.Settings.PanelHotkey}";
    }

    void UnregisterHotkeys()
    {
        Native.UnregisterHotKey(messages.Handle, HOTKEY_PANEL);
        Native.UnregisterHotKey(messages.Handle, HOTKEY_QUICKJUMP);
    }

    bool Register(int id, string hotkey)
    {
        if (!Hotkey.TryParse(hotkey, out var mods, out var vk)) return false;
        return Native.RegisterHotKey(messages.Handle, id, mods | Native.MOD_NOREPEAT, vk);
    }

    void NotifyHotkeyProblem() =>
        Notify($"{string.Join(" and ", failedHotkeys)} is being used by another program, so it won't work. Click here to pick a different key.");

    /// <summary>True if no other program holds this hotkey (checked by briefly registering it).</summary>
    bool IsHotkeyAvailable(string hotkey)
    {
        const int PROBE = 90;
        if (!Hotkey.TryParse(hotkey, out var mods, out var vk)) return false;
        if (!Native.RegisterHotKey(messages.Handle, PROBE, mods | Native.MOD_NOREPEAT, vk)) return false;
        Native.UnregisterHotKey(messages.Handle, PROBE);
        return true;
    }

    void OpenHotkeyWizard()
    {
        if (hotkeyForm != null) { hotkeyForm.Activate(); return; }
        // Release our own hotkeys so they can be pressed inside the wizard and show as available.
        hotkeysSuspended = true;
        UnregisterHotkeys();
        using (hotkeyForm = new HotkeyForm(store.Settings.PanelHotkey, store.Settings.QuickJumpHotkey, IsHotkeyAvailable))
        {
            if (hotkeyForm.ShowDialog() == DialogResult.OK)
            {
                store.Settings.PanelHotkey = hotkeyForm.PanelHotkey;
                store.Settings.QuickJumpHotkey = hotkeyForm.QuickJumpHotkey;
                store.Save();
            }
        }
        hotkeyForm = null;
        hotkeysSuspended = false;
        RegisterHotkeys();
        if (failedHotkeys.Count > 0) NotifyHotkeyProblem();
        else Notify($"Hotkeys set: {store.Settings.PanelHotkey} opens the panel, {store.Settings.QuickJumpHotkey} quick-jumps.");
    }

    void OnMessage(int msg, IntPtr wParam)
    {
        if (msg == Native.WM_CLIPBOARDUPDATE) { CaptureClipboard(); return; }
        if (wParam == HOTKEY_PANEL) ShowPanel(Native.GetForegroundWindow());
        else if (wParam == HOTKEY_QUICKJUMP) _ = QuickJump(Native.GetForegroundWindow());
    }

    // ---- capture ----

    void CaptureClipboard()
    {
        if (pauseItem.Checked || DateTime.Now < Clip.SuppressUntil) return;

        var candidates = Clip.ReadCandidates();

        // Many apps fire several clipboard updates for a single copy; only count it once.
        var signature = string.Join("\n", candidates);
        if (signature == lastCapture && DateTime.Now - lastCaptureTime < TimeSpan.FromSeconds(1)) return;
        lastCapture = signature;
        lastCaptureTime = DateTime.Now;

        bool added = false;
        foreach (var c in candidates.Take(50))
        {
            var p = Store.Normalize(c);
            if (p != null && Clip.Exists(p)) { store.Touch(p); added = true; }
        }
        if (added && panel.Visible) panel.Reload();
    }

    // ---- panel and actions ----

    void ShowPanel(IntPtr foreground)
    {
        if (panel.Visible && foreground == panel.Handle) { panel.Hide(); return; }
        target = foreground;
        var title = target == IntPtr.Zero ? "" : Native.TitleOf(target);
        var description = Jumper.Classify(target) switch
        {
            TargetKind.FileDialog => $"→ Jump dialog: {title}",
            TargetKind.Explorer => $"→ Jump Explorer: {title}",
            _ when target != IntPtr.Zero => $"→ Paste into: {title}",
            _ => "→ Open in Explorer",
        };
        panel.ShowFor(description);
    }

    async void OnPicked(string path, PickMode mode)
    {
        try
        {
            store.Touch(path);
            var folder = Jumper.FolderOf(path);

            if (mode == PickMode.Open) { Jumper.OpenInExplorer(path); return; }

            if (target == IntPtr.Zero || !Native.IsWindow(target))
            {
                if (mode == PickMode.Paste) { Clip.SetQuiet(path); Notify("Path copied to clipboard."); }
                else Jumper.OpenInExplorer(path);
                return;
            }

            Native.SetForegroundWindow(target);
            await Task.Delay(80);
            var kind = Jumper.Classify(target);

            if (mode == PickMode.Jump && kind == TargetKind.FileDialog && await Jumper.JumpFileDialog(target, folder)) return;
            if (mode == PickMode.Jump && kind == TargetKind.Explorer) { Jumper.JumpExplorer(target, folder); return; }

            Clip.SetQuiet(path);
            await Task.Delay(40);
            SendKeys.SendWait("^v");
        }
        catch (Exception ex)
        {
            Notify("Couldn't do that: " + ex.Message);
        }
    }

    /// <summary>In a file dialog, jumps straight to the most recently used Explorer folder.</summary>
    async Task QuickJump(IntPtr foreground)
    {
        if (Jumper.Classify(foreground) != TargetKind.FileDialog)
        {
            Notify($"{store.Settings.QuickJumpHotkey} works inside an Open/Save dialog. Use {store.Settings.PanelHotkey} for the full list.");
            return;
        }
        var folders = Jumper.OpenExplorerFolders();
        if (folders.Count == 0) { Notify("No Explorer windows are open."); return; }
        store.Touch(folders[0]);
        await Jumper.JumpFileDialog(foreground, folders[0]);
    }

    // ---- tray helpers ----

    void Notify(string text) => tray.ShowBalloonTip(3000, "PathJump", text, ToolTipIcon.Info);

    static bool IsStartupEnabled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(RunKey);
        return key?.GetValue("PathJump") != null;
    }

    /// <summary>If Start with Windows points at an exe that no longer exists (folder was moved), point it here.</summary>
    static void RepairStartupPath()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, true);
            if (key?.GetValue("PathJump") is not string registered) return;
            var exe = registered.Trim().Trim('"');
            if (!File.Exists(exe)) key.SetValue("PathJump", $"\"{Application.ExecutablePath}\"");
        }
        catch { }
    }

    static void SetStartup(bool enable)
    {
        using var key = Registry.CurrentUser.CreateSubKey(RunKey);
        if (enable) key.SetValue("PathJump", $"\"{Application.ExecutablePath}\"");
        else key.DeleteValue("PathJump", false);
    }

    /// <summary>The embedded app icon at the requested size (Windows picks the closest frame).</summary>
    public static Icon LoadIcon(Size size)
    {
        using var stream = typeof(App).Assembly.GetManifestResourceStream("PathJump.pathjump.ico");
        return new Icon(stream, size);
    }

    void Quit()
    {
        Native.UnregisterHotKey(messages.Handle, HOTKEY_PANEL);
        Native.UnregisterHotKey(messages.Handle, HOTKEY_QUICKJUMP);
        Native.RemoveClipboardFormatListener(messages.Handle);
        store.Save();
        tray.Visible = false;
        messages.DestroyHandle();
        ExitThread();
    }
}
