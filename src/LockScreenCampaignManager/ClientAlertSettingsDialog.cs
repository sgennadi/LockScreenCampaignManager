using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

internal sealed class ClientAlertSettingsDialog : Form
{
    private readonly CheckBox _enabled = new();
    private readonly CheckBox _stale = new();
    private readonly NumericUpDown _threshold = new();
    private readonly NumericUpDown _cooldown = new();
    internal ClientAlertPreferences Result { get; private set; }

    public ClientAlertSettingsDialog(ClientAlertPreferences initial)
    {
        Result = initial;
        Text = "Client Health — Alert settings";
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(540, 390);
        Size = new Size(660, 450);
        var grid = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2,
            RowCount = 6, Padding = new Padding(18), AutoScroll = true };
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));
        grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        Controls.Add(grid);
        var title = new Label { Text = "Alert when problems increase", Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 14f) };
        grid.Controls.Add(title, 0, 0); grid.SetColumnSpan(title, 2);
        _enabled.Text = "Show Windows notifications";
        _enabled.Checked = initial.Enabled;
        _enabled.AutoSize = true;
        grid.Controls.Add(_enabled, 0, 1); grid.SetColumnSpan(_enabled, 2);
        AddRow(grid, 2, "Problem count increase by", _threshold,
            initial.NewErrorsThreshold, 1, 50);
        AddRow(grid, 3, "Minimum interval (minutes)", _cooldown,
            initial.CooldownMinutes, 5, 240);
        _stale.Text = "Include stale reports";
        _stale.Checked = initial.IncludeStale;
        _stale.AutoSize = true;
        grid.Controls.Add(_stale, 0, 4);
        var info = new Label {
            Text = "The first refresh establishes the baseline. Notifications appear " +
                   "only when problems increase while the manager is open. " +
                   "No endpoint changes are performed automatically.",
            Dock = DockStyle.Fill, ForeColor = UiStyle.Muted,
            AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft
        };
        grid.Controls.Add(info, 1, 4);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill,
            AutoScroll = true, FlowDirection = FlowDirection.RightToLeft };
        var save = new Button { Text = "Save", Width = 135, Height = 39,
            BackColor = UiStyle.Accent, ForeColor = Color.White };
        save.Click += (_, _) =>
        {
            var prefs = new ClientAlertPreferences {
                Enabled = _enabled.Checked,
                NewErrorsThreshold = (int)_threshold.Value,
                CooldownMinutes = (int)_cooldown.Value,
                IncludeStale = _stale.Checked
            };
            try { prefs.Save(); Result = prefs; DialogResult = DialogResult.OK; }
            catch (Exception ex) { UiErrorDialog.Show(this, "Save alert preferences", ex); }
        };
        var cancel = new Button { Text = "Cancel", Width = 105, Height = 39 };
        cancel.Click += (_, _) => DialogResult = DialogResult.Cancel;
        actions.Controls.Add(save); actions.Controls.Add(cancel);
        grid.Controls.Add(actions, 0, 5); grid.SetColumnSpan(actions, 2);
        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "alert-settings");
    }

    private static void AddRow(TableLayoutPanel grid, int row, string description,
        NumericUpDown input, int value, int min, int max)
    {
        grid.Controls.Add(new Label { Text = description, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft }, 0, row);
        input.Minimum = min; input.Maximum = max;
        input.Value = Math.Clamp(value, min, max);
        input.Dock = DockStyle.Left; input.Width = 112;
        grid.Controls.Add(input, 1, row);
    }
}
