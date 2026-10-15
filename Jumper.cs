using System.Diagnostics;

namespace PathJump;

public enum TargetKind { FileDialog, Explorer, Other }

/// <summary>Detects the target window and sends it to a folder.</summary>
static class Jumper
{
    const int ID_FILENAME_COMBO = 0x47C; // cmb13: "File name" box in Open/Save dialogs
    const int ID_FILENAME_EDIT = 0x480;  // edt1: same box in old-style dialogs
    const int IDOK = 1;                  // Open / Save button

    public static TargetKind Classify(IntPtr h)
    {
        if (h == IntPtr.Zero || !Native.IsWindow(h)) return TargetKind.Other;
        var cls = Native.ClassOf(h);
        if (cls == "CabinetWClass") return TargetKind.Explorer;
        if (cls == "#32770" && FindFileNameEdit(h) != IntPtr.Zero) return TargetKind.FileDialog;
        return TargetKind.Other;
    }

    public static string FolderOf(string path) =>
        Directory.Exists(path) ? path : (Path.GetDirectoryName(path) ?? path);

    static IntPtr FindFileNameEdit(IntPtr dlg)
    {
        var all = Native.Descendants(dlg);
        foreach (var id in new[] { ID_FILENAME_COMBO, ID_FILENAME_EDIT })
        {
            var ctl = all.FirstOrDefault(k => Native.GetDlgCtrlID(k) == id);
            if (ctl == IntPtr.Zero) continue;
            if (Native.ClassOf(ctl) == "Edit") return ctl;
            var edit = Native.Descendants(ctl).FirstOrDefault(k => Native.ClassOf(k) == "Edit");
            if (edit != IntPtr.Zero) return edit;
        }
        // Save dialogs host the File name box as FloatNotifySink > ComboBox > Edit, without the usual ID.
        return all.FirstOrDefault(k =>
            Native.ClassOf(k) == "Edit" && Native.IsWindowVisible(k) &&
            Native.ClassOf(Native.GetParent(k)) == "ComboBox" &&
            Native.ClassOf(Native.GetParent(Native.GetParent(k))) == "FloatNotifySink");
    }

    /// <summary>
    /// Puts the folder into the dialog's File name box and presses Open/Save, which makes
    /// standard dialogs navigate there. Whatever filename was typed before is put back.
    /// </summary>
    public static async Task<bool> JumpFileDialog(IntPtr dlg, string folder)
    {
        var edit = FindFileNameEdit(dlg);
        var ok = Native.GetDlgItem(dlg, IDOK);
        if (edit == IntPtr.Zero || ok == IntPtr.Zero) return false;

        var original = Native.ControlText(edit);
        Native.SendMessage(edit, Native.WM_SETTEXT, IntPtr.Zero, folder);
        Native.PostMessage(ok, Native.BM_CLICK, IntPtr.Zero, IntPtr.Zero);

        await Task.Delay(350);
        if (!string.IsNullOrEmpty(original) && !Store.LooksLikePath(original) && Native.IsWindow(edit))
            Native.SendMessage(edit, Native.WM_SETTEXT, IntPtr.Zero, original);
        return true;
    }

    /// <summary>
    /// Navigates the active tab of an Explorer window instantly via the Shell COM API.
    /// Falls back to setting the address bar text in one go and pressing Enter once.
    /// </summary>
    public static void JumpExplorer(IntPtr hwnd, string folder)
    {
        if (NavigateActiveTab(hwnd, folder)) return;

        Native.SetForegroundWindow(hwnd);
        SendKeys.SendWait("^l");
        Thread.Sleep(80);
        var edit = Native.FocusedControl(hwnd);
        if (edit != IntPtr.Zero && Native.ClassOf(edit) == "Edit")
        {
            Native.SendMessage(edit, Native.WM_SETTEXT, IntPtr.Zero, folder);
            SendKeys.SendWait("{ENTER}");
        }
        else
            SendKeys.SendWait(EscapeSendKeys(folder) + "{ENTER}");
    }

    static readonly Guid SID_STopLevelBrowser = new("4C96BE40-915C-11CF-99D3-00AA004AE837");
    static readonly Guid IID_IShellBrowser = new("000214E2-0000-0000-C000-000000000046");

    static bool NavigateActiveTab(IntPtr hwnd, string folder)
    {
        try
        {
            // With Windows 11 tabs, every tab is its own shell window sharing the frame's HWND;
            // the active tab is the topmost ShellTabWindowClass child.
            var activeTab = Native.FindWindowEx(hwnd, IntPtr.Zero, "ShellTabWindowClass", null);
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
            dynamic windows = shell.Windows();
            int count = windows.Count;
            for (int i = 0; i < count; i++)
            {
                dynamic w;
                try { w = windows.Item(i); if (w == null || Convert.ToInt64((object)w.HWND) != hwnd.ToInt64()) continue; }
                catch { continue; }
                if (activeTab == IntPtr.Zero || TabWindowOf((object)w) == activeTab) { w.Navigate2(folder); return true; }
            }
        }
        catch { }
        return false;
    }

    static IntPtr TabWindowOf(object browser)
    {
        if (browser is not IComServiceProvider sp) return IntPtr.Zero;
        var sid = SID_STopLevelBrowser; var iid = IID_IShellBrowser;
        if (sp.QueryService(ref sid, ref iid, out var ptr) != 0 || ptr == IntPtr.Zero) return IntPtr.Zero;
        try
        {
            var sb = (IOleWindow)System.Runtime.InteropServices.Marshal.GetObjectForIUnknown(ptr);
            return sb.GetWindow(out var h) == 0 ? h : IntPtr.Zero;
        }
        finally { System.Runtime.InteropServices.Marshal.Release(ptr); }
    }

    [System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.Guid("6d5140c1-7436-11ce-8034-00aa006009fa"),
     System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
    interface IComServiceProvider
    {
        [System.Runtime.InteropServices.PreserveSig] int QueryService(ref Guid service, ref Guid riid, out IntPtr obj);
    }

    // IShellBrowser derives from IOleWindow; GetWindow is its first method.
    [System.Runtime.InteropServices.ComImport, System.Runtime.InteropServices.Guid("000214E2-0000-0000-C000-000000000046"),
     System.Runtime.InteropServices.InterfaceType(System.Runtime.InteropServices.ComInterfaceType.InterfaceIsIUnknown)]
    interface IOleWindow
    {
        [System.Runtime.InteropServices.PreserveSig] int GetWindow(out IntPtr hwnd);
    }

    /// <summary>
    /// Shows a folder in Explorer without disturbing existing tabs: focuses the tab that already has it open,
    /// otherwise opens it in a new tab of the most recent Explorer window, otherwise in a new window.
    /// </summary>
    public static void OpenInExplorer(string path)
    {
        var folder = FolderOf(path);
        if (FocusExistingExplorer(folder)) return;
        if (OpenInNewTab(folder)) return;
        if (File.Exists(path))
            Process.Start("explorer.exe", $"/select,\"{path}\"");
        else
            Process.Start("explorer.exe", $"\"{FolderOf(path)}\"");
    }

    /// <summary>Every Explorer tab as (frame window, path, is the frame's active tab).</summary>
    static List<(IntPtr hwnd, string path, bool active)> ExplorerTabs()
    {
        var tabs = new List<(IntPtr, string, bool)>();
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
            dynamic windows = shell.Windows();
            int count = windows.Count;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    dynamic w = windows.Item(i);
                    if (w == null) continue;
                    var hwnd = new IntPtr(Convert.ToInt64((object)w.HWND));
                    var path = Store.Normalize((string)w.Document.Folder.Self.Path);
                    if (path == null) continue;
                    var activeTab = Native.FindWindowEx(hwnd, IntPtr.Zero, "ShellTabWindowClass", null);
                    tabs.Add((hwnd, path, activeTab == IntPtr.Zero || TabWindowOf((object)w) == activeTab));
                }
                catch { }
            }
        }
        catch { }
        return tabs;
    }

    static bool FocusExistingExplorer(string folder)
    {
        folder = Store.Normalize(folder);
        if (folder == null) return false;
        var matches = ExplorerTabs().Where(t => string.Equals(t.path, folder, StringComparison.OrdinalIgnoreCase)).ToList();
        if (matches.Count == 0) return false;

        // Prefer a window where the folder is already the visible tab.
        var (hwnd, _, active) = matches.OrderByDescending(m => m.active).First();
        if (Native.IsIconic(hwnd)) Native.ShowWindow(hwnd, Native.SW_RESTORE);
        Native.SetForegroundWindow(hwnd);
        if (active) return true;

        // The folder is in a background tab: cycle tabs with Ctrl+Tab until it's showing.
        int tabCount = ExplorerTabs().Count(t => t.hwnd == hwnd);
        Thread.Sleep(100);
        for (int i = 0; i < tabCount; i++)
        {
            SendKeys.SendWait("^{TAB}");
            Thread.Sleep(120);
            if (ExplorerTabs().Any(t => t.hwnd == hwnd && t.active && string.Equals(t.path, folder, StringComparison.OrdinalIgnoreCase)))
                break;
        }
        return true;
    }

    static bool OpenInNewTab(string folder)
    {
        var frame = ExplorerFramesByZOrder().FirstOrDefault();
        if (frame == IntPtr.Zero) return false;

        if (Native.IsIconic(frame)) Native.ShowWindow(frame, Native.SW_RESTORE);
        Native.SetForegroundWindow(frame);
        var before = Native.FindWindowEx(frame, IntPtr.Zero, "ShellTabWindowClass", null);
        Thread.Sleep(100);
        SendKeys.SendWait("^t");

        // Wait for the new tab to become the active one, then navigate it (no tabs on older Windows: give up).
        for (int i = 0; i < 25; i++)
        {
            Thread.Sleep(80);
            var now = Native.FindWindowEx(frame, IntPtr.Zero, "ShellTabWindowClass", null);
            if (now == IntPtr.Zero || now == before) continue;
            for (int attempt = 0; attempt < 10; attempt++)
            {
                if (NavigateActiveTab(frame, folder)) return true;
                Thread.Sleep(100); // the new tab may not be registered with the shell yet
            }
            return false;
        }
        return false;
    }

    static List<IntPtr> ExplorerFramesByZOrder()
    {
        var frames = new List<IntPtr>();
        Native.EnumWindows((h, _) =>
        {
            if (Native.IsWindowVisible(h) && Native.ClassOf(h) == "CabinetWClass") frames.Add(h);
            return true;
        }, IntPtr.Zero);
        return frames;
    }

    /// <summary>Folders open in Explorer windows/tabs, most recently active window first.</summary>
    public static List<string> OpenExplorerFolders()
    {
        var zOrder = new List<IntPtr>();
        Native.EnumWindows((h, _) =>
        {
            if (Native.IsWindowVisible(h) && Native.ClassOf(h) == "CabinetWClass") zOrder.Add(h);
            return true;
        }, IntPtr.Zero);

        var byWindow = new Dictionary<long, List<string>>();
        try
        {
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application"));
            dynamic windows = shell.Windows();
            int count = windows.Count;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    dynamic w = windows.Item(i);
                    if (w == null) continue;
                    long hwnd = Convert.ToInt64((object)w.HWND);
                    string path = (string)w.Document.Folder.Self.Path;
                    if (string.IsNullOrEmpty(path) || !Store.LooksLikePath(path)) continue;
                    if (!byWindow.TryGetValue(hwnd, out var list)) byWindow[hwnd] = list = new();
                    list.Add(path);
                }
                catch { /* non-filesystem views (This PC, Home, etc.) */ }
            }
        }
        catch { }

        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var h in zOrder)
            if (byWindow.TryGetValue(h.ToInt64(), out var paths))
                foreach (var p in paths)
                {
                    var n = Store.Normalize(p);
                    if (n != null && seen.Add(n)) result.Add(n);
                }
        return result;
    }

    static string EscapeSendKeys(string s)
    {
        var sb = new System.Text.StringBuilder();
        foreach (var c in s)
            sb.Append("+^%~(){}[]".Contains(c) ? "{" + c + "}" : c.ToString());
        return sb.ToString();
    }
}
