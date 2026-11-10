using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

internal sealed class FeedbackSettingsDialog : Form
{
    private readonly CheckBox _enabled = new();
    private readonly TextBox _share = new();
    private readonly NumericUpDown _interval = new();
    private readonly CampaignProject _project;

    internal FeedbackSettingsDialog(CampaignProject project)
    {
        _project = project;
        Text = "Client feedback — SMB";
        Size = new Size(930, 560);
        MinimumSize = new Size(705, 475);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            Padding = new Padding(20), BackColor = UiStyle.Canvas,
            AutoScroll = true
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 59));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        Controls.Add(layout);
        layout.Controls.Add(new Label
        {
            Text = "Client feedback — no application server or WinRM",
            Font = new Font("Segoe UI Semibold", 14f), Dock = DockStyle.Fill
        }, 0, 0);
        _enabled.Text = "Enable campaign health reports from clients";
        _enabled.Dock = DockStyle.Fill;
        _enabled.Checked = project.EnableFeedback;
        _enabled.CheckedChanged += (_, _) => SetEnabledFields();
        layout.Controls.Add(_enabled, 0, 1);
        var source = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
        source.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 78));
        source.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 22));
        _share.Name = "FeedbackShareScrollable";
        _share.RightToLeft = RightToLeft.No; // Preserve UNC path direction
        _share.Dock = DockStyle.Fill;
        _share.Multiline = true;
        _share.ScrollBars = ScrollBars.Horizontal;
        _share.WordWrap = false;
        _share.AcceptsReturn = false;
        _share.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) e.SuppressKeyPress = true;
        };
        _share.PlaceholderText = @"\\fileserver\LockScreenReports$";
        _share.Text = project.FeedbackShare;
        source.Controls.Add(Field("Dedicated SMB share (NOT SYSVOL/NETLOGON)", _share), 0, 0);
        _interval.RightToLeft = RightToLeft.No;
        _interval.Minimum = 5;
        _interval.Maximum = 60;
        _interval.Value = Math.Clamp(project.FeedbackIntervalMinutes, 5, 60);
        _interval.Dock = DockStyle.Fill;
        source.Controls.Add(Field("Interval (minutes)", _interval), 1, 0);
        layout.Controls.Add(source, 0, 2);

        layout.Controls.Add(new TextBox
        {
            Name = "FeedbackSmbInstructions",
            ReadOnly = true, Multiline = true, WordWrap = true,
            ScrollBars = ScrollBars.Vertical,
            BorderStyle = BorderStyle.None,
            BackColor = UiStyle.Canvas,
            Text = UiLocale.Display("How it works: GPO installs the agent; each client periodically " +
                   "writes a small JSON report to the <share>\\<CampaignID> folder. " +
                   "When the manager is open, the table refreshes every 15 seconds. " +
                   "Reports continue to arrive when the manager is closed.\n\n" +
                   "Create a dedicated SMB share with secure ACLs: grant computers write access " +
                   "only to their permitted area and administrators read access. Endpoints need no RSAT, " +
                   "WinRM or installed .NET. Reports are diagnostic hints, not " +
                   "cryptographic attestation of Windows state.\n\n" +
                   "Important: the SMB path is stored in campaign.json and visible to domain computers. " +
                   "Do not use SYSVOL or NETLOGON for reports."),
            Dock = DockStyle.Fill, ForeColor = UiStyle.Muted
        }, 0, 3);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false,
            AutoScroll = true, FlowDirection = FlowDirection.RightToLeft
        };
        actions.Controls.Add(Button("Save", () => Save(), 140, true));
        actions.Controls.Add(Button("Cancel", Close, 110));
        actions.Controls.Add(Button("Browse SMB...", BrowseShare, 165));
        actions.Controls.Add(Button("Copy share path", CopyShare, 185));
        actions.Controls.Add(Button("Create campaign folder", CreateFolder, 240));
        layout.Controls.Add(actions, 0, 4);
        SetEnabledFields();
        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "feedback-settings");
    }

    private static Control Field(string title, Control control)
    {
        var box = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        box.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var caption = new Label { Text = title, Dock = DockStyle.Fill,
            AutoEllipsis = false, TextAlign = ContentAlignment.MiddleLeft };
        box.Controls.Add(caption, 0, 0);
        caption.FontChanged += (_, _) =>
        {
            if (!box.IsDisposed)
                box.RowStyles[0].Height = Math.Max(36,
                    TextRenderer.MeasureText("Agpqy", caption.Font).Height + 10);
        };
        box.Controls.Add(control, 0, 1);
        return box;
    }
    private static Button Button(string text, Action callback, int width, bool primary = false)
    {
        var b = new Button
        {
            Text = UiLocale.Display(text), Width = width, Height = 40,
            MinimumSize = new Size(width, 40), AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowOnly,
            Margin = new Padding(0, 6, 8, 0),
            BackColor = primary ? UiStyle.Accent : UiStyle.Surface,
            ForeColor = primary ? Color.White : UiStyle.Foreground
        };
        b.Click += (_, _) => callback();
        return b;
    }
    private void SetEnabledFields()
    {
        _share.Enabled = _enabled.Checked;
        _interval.Enabled = _enabled.Checked;
    }

    private void CopyShare()
    {
        if (!string.IsNullOrWhiteSpace(_share.Text))
            try { Clipboard.SetText(_share.Text.Trim()); }
            catch (Exception ex) { UiErrorDialog.Show(this, "Copy SMB share", ex); }
    }

    private void BrowseShare()
    {
        using var picker = new FolderBrowserDialog
        {
            Description = UiLocale.Display("Select the root of a dedicated SMB share (not its campaign folder)."),
            UseDescriptionForTitle = true, ShowNewFolderButton = false
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try { _share.Text = FeedbackPath.ValidateRoot(picker.SelectedPath); }
        catch (ArgumentException)
        {
            MessageBox.Show(this,
                UiLocale.Display("Choose an SMB share root such as \\\\server\\share$; " +
                "do not select a local folder or campaign subfolder."),
                UiLocale.Display("SMB share root required"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
    private void Save()
    {
        try
        {
            var root = _enabled.Checked ? FeedbackPath.ValidateRoot(_share.Text) : _share.Text.Trim();
            _project.EnableFeedback = _enabled.Checked;
            _project.FeedbackShare = root;
            _project.FeedbackIntervalMinutes = (int)_interval.Value;
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Feedback settings", ex); }
    }
    private void CreateFolder()
    {
        try
        {
            string share = FeedbackPath.ValidateRoot(_share.Text);
            string folder = FeedbackPath.CampaignDirectory(share, CampaignSchedule.BuildId(_project));
            if (MessageBox.Show(this, UiLocale.Display("Create feedback folder?\n\n") + folder +
                "\n\n" + UiLocale.Display("The application does not change ACLs. An administrator must configure permissions on the SMB server."),
                UiLocale.Display("Prepare folder"), MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            Directory.CreateDirectory(folder);
            MessageBox.Show(this, UiLocale.Display("Ready: ") + folder +
                "\n" + UiLocale.Display("Verify Domain Computers write permissions."));
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Create feedback folder", ex); }
    }
}
