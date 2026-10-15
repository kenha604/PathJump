namespace PathJump;

public enum PickMode { Jump, Paste, Open }

/// <summary>The popup list: open Explorer folders, pinned paths and recent paths.</summary>
public class PanelForm : Form
{
    readonly Store store;
    readonly Label header = new();
    readonly TextBox search = new();
    readonly ListView list = new();
    readonly Label hint = new();
    readonly ContextMenuStrip menu = new();
    readonly ToolStripMenuItem pinItem = new("Add to Favorites");
    ListViewGroup favoritesGroup;
    List<string> explorerFolders = new();
    string targetText, clipboardPath;
    bool inDialog;

    public event Action<string, PickMode> Picked;

    public PanelForm(Store store)
    {
        this.store = store;

        Text = "PathJump";
        Icon = App.LoadIcon(SystemInformation.SmallIconSize);
        FormBorderStyle = FormBorderStyle.SizableToolWindow;
        ShowInTaskbar = false;
        TopMost = true;
        KeyPreview = true;
        StartPosition = FormStartPosition.Manual;
        Font = new Font("Segoe UI", 10F);
        Size = new Size(680, 460);
        MinimumSize = new Size(380, 240);

        header.Dock = DockStyle.Top;
        header.AutoSize = false;
        header.Height = 28;
        header.Padding = new Padding(6, 6, 6, 0);
        header.ForeColor = SystemColors.HotTrack;
        header.AutoEllipsis = true;
        // Clicking the top line jumps to the path currently on the clipboard.
        header.MouseUp += (_, e) =>
        {
            if (clipboardPath == null || (e.Button != MouseButtons.Left && e.Button != MouseButtons.Right)) return;
            Hide();
            Picked?.Invoke(clipboardPath, e.Button == MouseButtons.Right ? PickMode.Open : ModeFromModifiers());
        };
        header.MouseEnter += (_, _) => { if (clipboardPath != null) header.BackColor = SystemColors.GradientInactiveCaption; };
        header.MouseLeave += (_, _) => header.BackColor = SystemColors.Control;

        search.Dock = DockStyle.Top;
        search.PlaceholderText = "Type to filter…";
        search.TextChanged += (_, _) => Refill();

        list.Dock = DockStyle.Fill;
        list.View = View.Details;
        list.FullRowSelect = true;
        list.MultiSelect = false;
        list.HideSelection = false;
        list.HeaderStyle = ColumnHeaderStyle.None;
        list.ShowGroups = true;
        list.Columns.Add("Name", 200);
        list.Columns.Add("Path", 440);
        list.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Pick(ModeFromModifiers()); };
        // Right-click shows the folder in Explorer (or, in Favorites, unfavorites it);
        // Shift+right-click opens the menu (rename, remove, …).
        list.MouseUp += (_, e) =>
        {
            if (e.Button != MouseButtons.Right) return;
            var item = list.HitTest(e.Location).Item;
            if (item == null) return;
            item.Selected = true;
            if ((ModifierKeys & Keys.Shift) != 0) menu.Show(list, e.Location);
            else if (item.Group == favoritesGroup) ToggleFavorite();
            else Pick(PickMode.Open);
        };
        // Hovering highlights a row and gives the list focus, so F favorites the row under the mouse.
        list.MouseMove += (_, e) =>
        {
            var item = list.HitTest(e.Location).Item;
            if (item == null) return;
            if (!item.Selected) item.Selected = true;
            if (!list.Focused) list.Focus();
        };
        // Typing while the list has focus still goes to the filter box (except F, handled in OnKeyDown).
        list.KeyPress += (_, e) =>
        {
            if (char.IsControl(e.KeyChar) && e.KeyChar != '') return;
            search.Focus();
            if (e.KeyChar == '') { if (search.TextLength > 0) search.Text = search.Text[..^1]; }
            else search.AppendText(e.KeyChar.ToString());
            search.SelectionStart = search.TextLength;
            e.Handled = true;
        };
        list.Resize += (_, _) => FitColumns();

        hint.Dock = DockStyle.Bottom;
        hint.AutoSize = false;
        hint.Height = 24;
        hint.Padding = new Padding(6, 3, 6, 0);
        hint.ForeColor = SystemColors.GrayText;
        hint.Font = new Font("Segoe UI", 8.5F);
        hint.Text = "Click: jump · Right-click: show in Explorer · F: favorite · Shift+click: paste · Shift+right-click: menu · F2 rename · Del remove";

        // Dock order: the last control added docks first (header ends up on top).
        Controls.Add(list);
        Controls.Add(hint);
        Controls.Add(search);
        Controls.Add(header);

        menu.Items.Add("Jump here", null, (_, _) => Pick(PickMode.Jump));
        menu.Items.Add("Paste as text", null, (_, _) => Pick(PickMode.Paste));
        menu.Items.Add("Show in Explorer", null, (_, _) => Pick(PickMode.Open));
        menu.Items.Add(new ToolStripSeparator());
        pinItem.Click += (_, _) => ToggleFavorite();
        menu.Items.Add(pinItem);
        menu.Items.Add("Rename…", null, (_, _) => RenameSelected());
        menu.Items.Add("Copy path", null, (_, _) => { if (Selected != null) Clip.SetQuiet(Selected); });
        menu.Items.Add("Remove", null, (_, _) => RemoveSelected());
        menu.Opening += (_, e) =>
        {
            if (Selected == null) { e.Cancel = true; return; }
            pinItem.Text = store.Find(Selected)?.Pinned == true ? "Remove from Favorites  (F)" : "Add to Favorites  (F)";
        };

        Deactivate += (_, _) => { if (!inDialog) Hide(); };
    }

    public void ShowFor(string targetDescription)
    {
        targetText = targetDescription;
        search.Text = "";
        Reload();

        var pos = Cursor.Position;
        var area = Screen.FromPoint(pos).WorkingArea;
        Left = Math.Max(area.Left, Math.Min(pos.X - 60, area.Right - Width));
        Top = Math.Max(area.Top, Math.Min(pos.Y - 20, area.Bottom - Height));

        Show();
        Activate();
        search.Focus();
    }

    /// <summary>Re-reads open Explorer folders and the clipboard, then rebuilds the list.</summary>
    public void Reload()
    {
        explorerFolders = Jumper.OpenExplorerFolders();
        clipboardPath = Clip.ReadPath();
        if (clipboardPath != null)
        {
            header.Text = $"{targetText}\n📋 Click here to jump to clipboard: {clipboardPath}";
            header.Height = 50;
            header.Cursor = Cursors.Hand;
        }
        else
        {
            header.Text = targetText;
            header.Height = 28;
            header.Cursor = Cursors.Default;
        }
        Refill();
    }

    string Selected => list.SelectedItems.Count > 0 ? (string)list.SelectedItems[0].Tag : null;

    static PickMode ModeFromModifiers() =>
        (ModifierKeys & Keys.Shift) != 0 ? PickMode.Paste :
        (ModifierKeys & Keys.Control) != 0 ? PickMode.Open : PickMode.Jump;

    void Pick(PickMode mode)
    {
        var path = Selected;
        if (path == null) return;
        Hide();
        Picked?.Invoke(path, mode);
    }

    void Refill()
    {
        var terms = search.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        bool Match(string label, string path) => terms.All(t =>
            label.Contains(t, StringComparison.OrdinalIgnoreCase) || path.Contains(t, StringComparison.OrdinalIgnoreCase));

        list.BeginUpdate();
        list.Items.Clear();
        list.Groups.Clear();
        var gOpen = new ListViewGroup("Open in Explorer");
        var gPinned = favoritesGroup = new ListViewGroup("★ Favorites");
        var gRecent = new ListViewGroup("Recent");
        list.Groups.AddRange(new[] { gOpen, gPinned, gRecent });

        var shown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var p in explorerFolders)
        {
            var e = store.Find(p);
            var label = e != null ? Store.LabelOf(e) : Store.DefaultLabel(p);
            if (!Match(label, p)) continue;
            AddItem(label, p, gOpen);
            shown.Add(p);
        }

        foreach (var e in store.Entries.Where(e => e.Pinned).OrderBy(Store.LabelOf, StringComparer.OrdinalIgnoreCase))
        {
            var label = Store.LabelOf(e);
            if (!Match(label, e.Path)) continue;
            AddItem(label, e.Path, gPinned);
            shown.Add(e.Path);
        }

        foreach (var e in store.Entries.Where(e => !e.Pinned).OrderByDescending(e => e.LastUsed))
        {
            var label = Store.LabelOf(e);
            if (shown.Contains(e.Path) || !Match(label, e.Path)) continue;
            AddItem(label, e.Path, gRecent);
        }

        if (list.Items.Count > 0) list.Items[0].Selected = true;
        list.EndUpdate();
        FitColumns();
    }

    void AddItem(string label, string path, ListViewGroup group)
    {
        var item = new ListViewItem(new[] { label, path }) { Group = group, Tag = path };
        // Skip the existence check for network paths so an offline share can't freeze the panel.
        if (!path.StartsWith(@"\\") && !Directory.Exists(path) && !File.Exists(path))
            item.ForeColor = SystemColors.GrayText;
        list.Items.Add(item);
    }

    void FitColumns()
    {
        if (list.Columns.Count < 2) return;
        list.Columns[1].Width = Math.Max(100, list.ClientSize.Width - list.Columns[0].Width - SystemInformation.VerticalScrollBarWidth);
    }

    void MoveSelection(int delta)
    {
        if (list.Items.Count == 0) return;
        int i = list.SelectedIndices.Count > 0 ? list.SelectedIndices[0] : -1;
        // Items are in display order because groups are filled in order.
        i = Math.Clamp(i + delta, 0, list.Items.Count - 1);
        list.Items[i].Selected = true;
        list.Items[i].EnsureVisible();
    }

    void ToggleFavorite()
    {
        var p = Selected;
        if (p == null) return;
        store.SetPinned(p, store.Find(p)?.Pinned != true);
        Refill();
        Reselect(p);
    }

    void RemoveSelected()
    {
        var p = Selected;
        if (p == null) return;
        int index = list.SelectedIndices[0];
        store.Remove(p);
        Refill();
        if (list.Items.Count > 0) list.Items[Math.Min(index, list.Items.Count - 1)].Selected = true;
    }

    void RenameSelected()
    {
        var p = Selected;
        if (p == null) return;
        var e = store.Find(p);
        inDialog = true;
        var name = Prompt("Rename", "Label for " + p, e != null ? Store.LabelOf(e) : Store.DefaultLabel(p));
        inDialog = false;
        Activate();
        if (name == null) return;
        store.Rename(p, name == Store.DefaultLabel(p) ? null : name);
        Refill();
        Reselect(p);
    }

    void Reselect(string path)
    {
        foreach (ListViewItem item in list.Items)
            if (string.Equals((string)item.Tag, path, StringComparison.OrdinalIgnoreCase)) { item.Selected = true; item.EnsureVisible(); return; }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        bool handled = true;
        switch (e.KeyCode)
        {
            case Keys.Escape: Hide(); break;
            case Keys.Enter: Pick(e.Shift ? PickMode.Paste : e.Control ? PickMode.Open : PickMode.Jump); break;
            case Keys.Down: MoveSelection(1); list.Focus(); break;
            case Keys.Up: MoveSelection(-1); list.Focus(); break;
            case Keys.PageDown: MoveSelection(10); list.Focus(); break;
            case Keys.PageUp: MoveSelection(-10); list.Focus(); break;
            case Keys.F when list.Focused && e.Modifiers == Keys.None: ToggleFavorite(); break;
            case Keys.F2: RenameSelected(); break;
            case Keys.P when e.Control: ToggleFavorite(); break;
            case Keys.Delete when search.TextLength == 0 || list.Focused: RemoveSelected(); break;
            default: handled = false; break;
        }
        if (handled) { e.Handled = true; e.SuppressKeyPress = true; }
        base.OnKeyDown(e);
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        // The close button just hides the panel; the app keeps running in the tray.
        if (e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; Hide(); }
        base.OnFormClosing(e);
    }

    static string Prompt(string title, string text, string value)
    {
        using var f = new Form
        {
            Text = title, FormBorderStyle = FormBorderStyle.FixedDialog, StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false, MaximizeBox = false, TopMost = true, ShowInTaskbar = false,
            Font = new Font("Segoe UI", 10F), ClientSize = new Size(460, 120),
        };
        var label = new Label { Text = text, Left = 12, Top = 10, Width = 436, AutoEllipsis = true };
        var box = new TextBox { Text = value, Left = 12, Top = 38, Width = 436 };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 282, Top = 78, Width = 80 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 368, Top = 78, Width = 80 };
        f.Controls.AddRange(new Control[] { label, box, ok, cancel });
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        return f.ShowDialog() == DialogResult.OK ? box.Text : null;
    }
}
