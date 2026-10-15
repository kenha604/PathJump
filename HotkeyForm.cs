namespace PathJump;

/// <summary>Text box that records the key combination pressed in it instead of typing.</summary>
class HotkeyBox : TextBox
{
    public event Action<string> Captured;

    public HotkeyBox()
    {
        ReadOnly = true;
        BackColor = SystemColors.Window;
        Cursor = Cursors.Hand;
        TextAlign = HorizontalAlignment.Center;
        ShortcutsEnabled = false;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        var text = Hotkey.Format(keyData);
        if (text != null) { Text = text; Captured?.Invoke(text); }
        return true; // swallow everything, including Alt combos and Tab
    }

    protected override void OnKeyDown(KeyEventArgs e) { e.SuppressKeyPress = true; }
}

/// <summary>"Change hotkeys" wizard: press a combination, see whether it's free, save.</summary>
class HotkeyForm : Form
{
    readonly HotkeyBox panelBox = new(), quickBox = new();
    readonly Label panelStatus = new(), quickStatus = new();
    readonly Button save = new() { Text = "Save", DialogResult = DialogResult.OK };
    readonly Func<string, bool> isAvailable;

    public string PanelHotkey => panelBox.Text;
    public string QuickJumpHotkey => quickBox.Text;

    public HotkeyForm(string panelHotkey, string quickHotkey, Func<string, bool> isAvailable)
    {
        this.isAvailable = isAvailable;
        Text = "PathJump – Change hotkeys";
        Icon = App.LoadIcon(SystemInformation.SmallIconSize);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = MinimizeBox = false;
        TopMost = true;
        Font = new Font("Segoe UI", 10F);
        ClientSize = new Size(520, 290);

        var intro = new Label
        {
            Text = "Click a box, then press the key combination you want (e.g. Alt+~ or Ctrl+Shift+Space).",
            Left = 16, Top = 14, Width = 490, Height = 40,
        };
        Controls.Add(intro);
        AddRow("Open the panel", panelBox, panelStatus, 62, panelHotkey);
        AddRow("Quick jump (in Open/Save dialogs)", quickBox, quickStatus, 142, quickHotkey);

        var defaults = new Button { Text = "Reset to defaults", Left = 16, Top = 242, Width = 150 };
        defaults.Click += (_, _) =>
        {
            var d = new Settings();
            panelBox.Text = d.PanelHotkey; quickBox.Text = d.QuickJumpHotkey;
            Validate();
        };
        save.SetBounds(312, 242, 90, 32);
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 412, Top = 242, Width = 90, Height = 32 };
        Controls.AddRange(new Control[] { defaults, save, cancel });
        AcceptButton = null; // Enter must be capturable as part of a hotkey
        CancelButton = null;
        Validate();
    }

    void AddRow(string title, HotkeyBox box, Label status, int top, string value)
    {
        Controls.Add(new Label { Text = title, Left = 16, Top = top, Width = 490, Font = new Font(Font, FontStyle.Bold) });
        box.SetBounds(16, top + 26, 220, 30);
        box.Text = value;
        box.Captured += _ => Validate();
        status.SetBounds(248, top + 30, 260, 40);
        Controls.Add(box);
        Controls.Add(status);
    }

    new void Validate()
    {
        bool ok = Check(panelBox.Text, panelStatus) & Check(quickBox.Text, quickStatus);
        if (ok && string.Equals(panelBox.Text, quickBox.Text, StringComparison.OrdinalIgnoreCase))
        {
            quickStatus.Text = "✖ Same as the panel hotkey";
            quickStatus.ForeColor = Color.Firebrick;
            ok = false;
        }
        save.Enabled = ok;
    }

    bool Check(string hotkey, Label status)
    {
        string message; bool ok;
        if (!Hotkey.TryParse(hotkey, out _, out _)) { message = "✖ Press a key combination"; ok = false; }
        else if (Hotkey.NeedsModifier(hotkey)) { message = "✖ Add Ctrl, Alt or Win (or use an F-key)"; ok = false; }
        else if (!isAvailable(hotkey)) { message = "✖ Used by another program – try another"; ok = false; }
        else { message = "✔ Available"; ok = true; }
        status.Text = message;
        status.ForeColor = ok ? Color.SeaGreen : Color.Firebrick;
        return ok;
    }
}
