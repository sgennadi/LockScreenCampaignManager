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
        Size = new Size(805, 485);
        MinimumSize = new Size(630, 420);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            Padding = new Padding(20), BackColor = UiStyle.Canvas,
            AutoScroll = true
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
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
        _share.Dock = DockStyle.Fill;
        _share.PlaceholderText = @"\\fileserver\LockScreenReports$";
        _share.Text = project.FeedbackShare;
        source.Controls.Add(Field("Dedicated SMB share (NOT SYSVOL/NETLOGON)", _share), 0, 0);
        _interval.Minimum = 5;
        _interval.Maximum = 60;
        _interval.Value = Math.Clamp(project.FeedbackIntervalMinutes, 5, 60);
        _interval.Dock = DockStyle.Fill;
        source.Controls.Add(Field("Interval (minutes)", _interval), 1, 0);
        layout.Controls.Add(source, 0, 2);

        layout.Controls.Add(new Label
        {
            Text = "How it works: GPO installs the agent; each client periodically " +
                   "writes a small JSON report to the <share>\\<CampaignID> folder. " +
                   "When the manager is open, the table refreshes every 15 seconds. " +
                   "Reports continue to arrive when the manager is closed.\n\n" +
                   "Create a dedicated SMB share with secure ACLs: grant computers write access " +
                   "only to their permitted area and administrators read access. Endpoints need no RSAT, " +
                   "WinRM or installed .NET. Reports are diagnostic hints, not " +
                   "cryptographic attestation of Windows state.\n\n" +
                   "Important: the SMB path is stored in campaign.json and visible to domain computers. " +
                   "Do not use SYSVOL or NETLOGON for reports.",
            Dock = DockStyle.Fill, ForeColor = UiStyle.Muted
        }, 0, 3);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false,
            AutoScroll = true, FlowDirection = FlowDirection.RightToLeft
        };
        actions.Controls.Add(Button("Save", () => Save(), 140, true));
        actions.Controls.Add(Button("Cancel", Close, 110));
        actions.Controls.Add(Button("Create campaign folder", CreateFolder, 240));
        layout.Controls.Add(actions, 0, 4);
        SetEnabledFields();
        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "feedback-settings");
    }

    private static Control Field(string title, Control control)
    {
        var box = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        box.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        box.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill }, 0, 0);
        box.Controls.Add(control, 0, 1);
        return box;
    }
    private static Button Button(string text, Action callback, int width, bool primary = false)
    {
        var b = new Button
        {
            Text = text, Width = width, Height = 40,
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
            if (MessageBox.Show(this, "Create feedback folder?\n\n" + folder +
                "\n\nThe application does not change ACLs. An administrator must configure permissions on the SMB server.",
                "Prepare folder", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            Directory.CreateDirectory(folder);
            MessageBox.Show(this, "Ready: " + folder + "\nVerify Domain Computers write permissions.");
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Create feedback folder", ex); }
    }
}
