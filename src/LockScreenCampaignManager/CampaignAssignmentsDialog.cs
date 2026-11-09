using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Edits a multi-target campaign without touching AD. Computer and user
/// scopes are deliberately independent. The selected OU/group is confirmed
/// again by the deployment engine, never trusted from these ComboBoxes.
/// </summary>
internal sealed class CampaignAssignmentsDialog : Form
{
    internal sealed record AdChoice(string Name, string Dn)
    {
        public override string ToString() => Name + "  [" + Dn + "]";
    }

    private readonly CampaignProject _project;
    private readonly List<CampaignGroupTarget> _draft;
    private readonly List<AdChoice> _ous;
    private readonly List<AdChoice> _groups;
    private readonly ListBox _rows = new();
    private readonly TextBox _name = new();
    private readonly ComboBox _mode = new();
    private readonly ComboBox _computerOu = new();
    private readonly ComboBox _computerGroup = new();
    private readonly ComboBox _userOu = new();
    private readonly ComboBox _userGroup = new();
    private readonly ComboBox _saver = new();
    private readonly CheckBox _photo = new();
    private readonly NumericUpDown _slide = new();
    private readonly NumericUpDown _idle = new();
    private readonly CheckBox _password = new();
    private readonly TextBox _preview = new();
    private readonly Label _help = new();
    private int _editing = -1;
    private bool _loading;

    internal CampaignAssignmentsDialog(CampaignProject project,
        IEnumerable<AdChoice> ous, IEnumerable<AdChoice> groups,
        string currentComputerOu, string currentComputerGroup)
    {
        _project = project;
        _ous = ous.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        _groups = groups.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        _draft = (project.TargetAssignments ?? []).Select(x => x.Clone()).ToList();
        if (_draft.Count == 0)
            _draft.Add(new CampaignGroupTarget {
                Name = project.EffectiveCampaignMode == CampaignTargetMode.ScreenSaver
                    ? "User Screen Saver"
                    : project.EffectiveCampaignMode == CampaignTargetMode.Both
                        ? "Both policies" : "Computer Lock Screen",
                Mode = project.EffectiveCampaignMode,
                ComputerOuDn = currentComputerOu, ComputerGroupDn = currentComputerGroup
            });

        Text = "LockScreen Campaign Manager " + AppVersion.Current +
            " - Group assignments: Lock Screen / Screen Saver / Both";
        Font = new Font("Segoe UI", 9.5f);
        BackColor = UiStyle.Canvas;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(950, 680);
        Size = new Size(1330, 880);
        Build();
        Populate();
        ReloadRows(0);
        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "campaign-assignments");
    }

    private static Button Action(string name, Action callback, int width)
    {
        var button = new Button {
            Text = name, Width = width, Height = 43,
            Margin = new Padding(4, 5, 7, 5), Tag = "NoIcon"
        };
        button.Click += (_, _) => callback();
        return button;
    }

    private static Control Field(string label, Control input, int height = 72)
    {
        var table = new TableLayoutPanel {
            Dock = DockStyle.Top, Height = height,
            ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 0, 9)
        };
        table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        table.Controls.Add(new Label {
            Text = label, Dock = DockStyle.Fill, AutoEllipsis = false,
            ForeColor = UiStyle.Navy, TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        input.Dock = DockStyle.Fill;
        table.Controls.Add(input, 0, 1);
        return table;
    }

    private void Build()
    {
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(17)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 79));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        Controls.Add(root);

        var title = new Label {
            Text = "Group assignments — choose Lock Screen, Screen Saver or Both" +
                Environment.NewLine +
                "Lock Screen = computer OU/group (HKLM). Screen Saver = user OU/group (HKCU). " +
                "Both uses two independent, filtered GPOs.",
            Dock = DockStyle.Fill, ForeColor = UiStyle.Navy,
            Font = new Font("Segoe UI Semibold", 11f)
        };
        root.Controls.Add(title, 0, 0);

        var split = new SplitContainer {
            Dock = DockStyle.Fill, Orientation = Orientation.Vertical,
            SplitterWidth = 8
        };
        Shown += (_, _) =>
        {
            if (split.Width >= 850)
            {
                split.Panel1MinSize = 280;
                split.Panel2MinSize = 400;
                split.SplitterDistance = Math.Clamp(375, split.Panel1MinSize,
                    split.Width - split.Panel2MinSize - split.SplitterWidth);
            }
        };
        root.Controls.Add(split, 0, 1);
        var left = new TableLayoutPanel { Dock = DockStyle.Fill,
            ColumnCount = 1, RowCount = 3, Padding = new Padding(4) };
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        left.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        left.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        left.Controls.Add(new Label {
            Text = "Assignment list (up to 20)", Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 11f),
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        _rows.Name = "AssignmentMatrixList";
        _rows.Dock = DockStyle.Fill;
        _rows.IntegralHeight = false;
        _rows.HorizontalScrollbar = true;
        _rows.SelectedIndexChanged += (_, _) => SelectRow();
        left.Controls.Add(_rows, 0, 1);
        var listActions = new FlowLayoutPanel {
            Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true
        };
        listActions.Controls.Add(Action("Add group...", AddRow, 135));
        listActions.Controls.Add(Action("Remove", RemoveRow, 110));
        left.Controls.Add(listActions, 0, 2);
        split.Panel1.Controls.Add(left);

        var details = new Panel { Dock = DockStyle.Fill, AutoScroll = true,
            BackColor = UiStyle.Surface, Padding = new Padding(14, 10, 14, 10) };
        var fields = new TableLayoutPanel {
            Name = "AssignmentScrollableFields",
            Dock = DockStyle.Top, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            RowCount = 13, ColumnCount = 1, Padding = new Padding(5)
        };
        foreach (int h in new[] { 75, 75, 74, 74, 75, 74, 75, 55, 65, 60, 62, 84, 50 })
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
        details.Controls.Add(fields);
        split.Panel2.Controls.Add(details);

        _name.Name = "AssignmentName";
        _name.TextChanged += (_, _) => CommitRow();
        fields.Controls.Add(Field("Assignment name", _name), 0, 0);
        _mode.Name = "AssignmentTargetMode";
        _mode.DropDownStyle = ComboBoxStyle.DropDownList;
        foreach (CampaignTargetMode mode in Enum.GetValues<CampaignTargetMode>())
            _mode.Items.Add(mode);
        _mode.SelectedIndexChanged += (_, _) =>
        {
            CommitRow();
            SetModeVisibility();
        };
        fields.Controls.Add(Field("Target mode: LockScreen / ScreenSaver / Both", _mode), 0, 1);

        FillAdChoices(_computerOu, _ous, "AssignmentComputerOu");
        FillAdChoices(_computerGroup, _groups, "AssignmentComputerGroup");
        FillAdChoices(_userOu, _ous, "AssignmentUserOu");
        FillAdChoices(_userGroup, _groups, "AssignmentUserGroup");
        _computerOu.SelectedIndexChanged += (_, _) => CommitRow();
        _computerGroup.SelectedIndexChanged += (_, _) => CommitRow();
        _userOu.SelectedIndexChanged += (_, _) => CommitRow();
        _userGroup.SelectedIndexChanged += (_, _) => CommitRow();
        fields.Controls.Add(Field("Computer OU (Lock Screen target)", _computerOu), 0, 2);
        fields.Controls.Add(Field("Computer security group (Lock Screen)", _computerGroup), 0, 3);
        fields.Controls.Add(Field("User OU (Screen Saver target)", _userOu), 0, 4);
        fields.Controls.Add(Field("User security group (Screen Saver)", _userGroup), 0, 5);

        _saver.Name = "AssignmentScreenSaverExecutable";
        _saver.DropDownStyle = ComboBoxStyle.DropDownList;
        _saver.Items.AddRange(CampaignTargetAssignments.BuiltInScreenSavers
            .Cast<object>().ToArray());
        _saver.SelectedItem = _project.ScreenSaver.Executable;
        _saver.SelectedIndexChanged += (_, _) => Preview();
        fields.Controls.Add(Field("Windows built-in .scr program", _saver), 0, 6);

        _photo.Name = "AssignmentScreenSaverJpegMode";
        _photo.Dock = DockStyle.Fill;
        _photo.Text = "Use JPG slideshow from selected campaign images (pilot only)";
        _photo.Checked = _project.ScreenSaver.UseJpegSlideshow;
        _photo.CheckedChanged += (_, _) => { SetModeVisibility(); Preview(); };
        fields.Controls.Add(_photo, 0, 7);
        _slide.Name = "AssignmentScreenSaverSlideSeconds";
        _slide.Minimum = 5;
        _slide.Maximum = 3600;
        _slide.Increment = 5;
        _slide.Value = Math.Clamp(_project.ScreenSaver.SlideSeconds, 5, 3600);
        _slide.ValueChanged += (_, _) => Preview();
        fields.Controls.Add(Field("JPG slide interval (seconds)", _slide), 0, 8);

        _idle.Name = "AssignmentScreenSaverIdleSeconds";
        _idle.Minimum = 60;
        _idle.Maximum = 86400;
        _idle.Increment = 60;
        _idle.Value = Math.Clamp(_project.ScreenSaver.IdleSeconds, 60, 86400);
        _idle.ValueChanged += (_, _) => Preview();
        fields.Controls.Add(Field("Screen Saver idle timeout (seconds)", _idle), 0, 9);
        _password.Name = "AssignmentScreenSaverPassword";
        _password.Checked = _project.ScreenSaver.PasswordOnResume;
        _password.Text = "Require Windows sign-in when resuming Screen Saver";
        _password.Dock = DockStyle.Fill;
        _password.AutoSize = false;
        _password.CheckedChanged += (_, _) => Preview();
        fields.Controls.Add(_password, 0, 10);

        _preview.Name = "AssignmentReadOnlyPreview";
        _preview.Multiline = true; _preview.ReadOnly = true;
        _preview.WordWrap = false;
        _preview.ScrollBars = ScrollBars.Both;
        _preview.Font = new Font("Consolas", 9.5f);
        _preview.Dock = DockStyle.Fill;
        fields.Controls.Add(_preview, 0, 11);
        _help.Name = "AssignmentModeHelp";
        _help.Dock = DockStyle.Fill;
        _help.Text = "Screen Saver is a User Configuration policy. " +
            "No loopback/Replace setting is made automatically.";
        _help.ForeColor = UiStyle.Muted;
        fields.Controls.Add(_help, 0, 12);

        var footer = new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            AutoScroll = true, WrapContents = false, Padding = new Padding(5)
        };
        footer.Controls.Add(Action("Cancel", Close, 116));
        footer.Controls.Add(Action("Save assignments", Save, 195));
        footer.Controls.Add(new Label {
            Text = "Read-only editing: no GPO/AD change until a separately previewed Deploy.",
            AutoSize = true, Margin = new Padding(5, 16, 25, 5),
            ForeColor = UiStyle.Muted
        });
        root.Controls.Add(footer, 0, 2);
    }

    private static void FillAdChoices(ComboBox box, List<AdChoice> choices, string name)
    {
        box.Name = name;
        box.DropDownStyle = ComboBoxStyle.DropDown;
        box.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
        box.AutoCompleteSource = AutoCompleteSource.ListItems;
        box.DropDownWidth = 760;
        foreach (var item in choices) box.Items.Add(item);
    }

    private void Populate()
    {
        _loading = true;
        try { _saver.SelectedItem = _project.ScreenSaver.Executable; }
        finally { _loading = false; }
    }

    private void CommitRow()
    {
        if (_loading || _editing < 0 || _editing >= _draft.Count) return;
        var target = _draft[_editing];
        target.Name = _name.Text.Trim();
        if (_mode.SelectedItem is CampaignTargetMode mode) target.Mode = mode;
        target.ComputerOuDn = SelectedDn(_computerOu);
        target.ComputerGroupDn = SelectedDn(_computerGroup);
        target.UserOuDn = SelectedDn(_userOu);
        target.UserGroupDn = SelectedDn(_userGroup);
        int selection = _rows.SelectedIndex;
        _loading = true;
        try
        {
            _rows.Items[_editing] = target.ToString();
            if (selection >= 0 && selection < _rows.Items.Count)
                _rows.SelectedIndex = selection;
        }
        finally { _loading = false; }
        Preview();
    }

    private static string SelectedDn(ComboBox box) => box.SelectedItem is AdChoice item
        ? item.Dn : "";

    private static void SelectDn(ComboBox box, string dn)
    {
        box.SelectedIndex = -1;
        if (string.IsNullOrWhiteSpace(dn)) return;
        var match = box.Items.Cast<AdChoice>().FirstOrDefault(x =>
            x.Dn.Equals(dn, StringComparison.OrdinalIgnoreCase));
        if (match is not null) box.SelectedItem = match;
    }

    private void SelectRow()
    {
        if (_loading) return;
        int index = _rows.SelectedIndex;
        if (index < 0 || index >= _draft.Count) return;
        _editing = index;
        var current = _draft[index];
        _loading = true;
        try
        {
            _name.Text = current.Name;
            _mode.SelectedItem = current.Mode;
            SelectDn(_computerOu, current.ComputerOuDn);
            SelectDn(_computerGroup, current.ComputerGroupDn);
            SelectDn(_userOu, current.UserOuDn);
            SelectDn(_userGroup, current.UserGroupDn);
        }
        finally { _loading = false; }
        SetModeVisibility();
        Preview();
    }

    private void ReloadRows(int index)
    {
        _loading = true;
        _rows.BeginUpdate();
        try
        {
            _rows.Items.Clear();
            foreach (var row in _draft) _rows.Items.Add(row.ToString());
            _editing = -1;
        }
        finally { _rows.EndUpdate(); _loading = false; }
        if (_rows.Items.Count > 0)
            _rows.SelectedIndex = Math.Clamp(index, 0, _rows.Items.Count - 1);
        else Preview();
    }

    private void SetModeVisibility()
    {
        bool hasComputer = _mode.SelectedItem is CampaignTargetMode.LockScreen
            or CampaignTargetMode.Both;
        bool hasUser = _mode.SelectedItem is CampaignTargetMode.ScreenSaver
            or CampaignTargetMode.Both;
        _computerOu.Enabled = _computerGroup.Enabled = hasComputer;
        _userOu.Enabled = _userGroup.Enabled = hasUser;
        _saver.Enabled = hasUser && !_photo.Checked;
        _photo.Enabled = hasUser;
        _slide.Enabled = hasUser && _photo.Checked;
        _idle.Enabled = _password.Enabled = hasUser;
    }

    private void AddRow()
    {
        if (_draft.Count >= 20)
        {
            MessageBox.Show(this, "Maximum 20 group assignments.",
                "Target groups", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        _draft.Add(new CampaignGroupTarget {
            Name = "Assignment " + (_draft.Count + 1),
            Mode = CampaignTargetMode.ScreenSaver
        });
        ReloadRows(_draft.Count - 1);
    }

    private void RemoveRow()
    {
        int index = _rows.SelectedIndex;
        if (index < 0 || index >= _draft.Count) return;
        _draft.RemoveAt(index);
        ReloadRows(Math.Max(0, index - 1));
    }

    private void Preview()
    {
        var lines = new List<string> {
            "Assignment matrix (local preview only; no AD changes):",
        };
        foreach (var target in _draft)
        {
            lines.Add(target.Name + " = " + target.Mode);
            if (target.UsesLockScreen)
                lines.Add("  Computer OU: " + target.ComputerOuDn +
                    " | Computer group: " + target.ComputerGroupDn);
            if (target.UsesScreenSaver)
                lines.Add("  User OU: " + target.UserOuDn +
                    " | User group: " + target.UserGroupDn);
        }
        lines.Add("Screen Saver: " + (_photo.Checked
            ? "JPG slideshow (local .scr pilot; GPO blocked)"
            : _saver.Text) +
            " | JPG every " + _slide.Value + " s" +
            " | idle " + _idle.Value + " sec | password " + _password.Checked);
        lines.Add("User GPO applies to the user group, not automatically to a computer group.");
        _preview.Text = string.Join(Environment.NewLine, lines);
    }

    private void Save()
    {
        try
        {
            CommitRow();
            if (_draft.Count < 1)
                throw new CampaignValidationException("Add at least one target assignment.");
            var settings = new CampaignScreenSaverSettings {
                Executable = _saver.SelectedItem as string ?? "scrnsave.scr",
                UseJpegSlideshow = _photo.Checked,
                SlideSeconds = (int)_slide.Value,
                IdleSeconds = (int)_idle.Value, PasswordOnResume = _password.Checked
            };
            var checker = new CampaignProject {
                TargetAssignments = _draft.Select(row => row.Clone()).ToList(),
                ScreenSaver = settings,
                ImagePaths = _project.ImagePaths.ToList()
            };
            CampaignTargetAssignments.Validate(checker);
            _project.TargetAssignments = checker.TargetAssignments;
            _project.ScreenSaver = settings;
            DialogResult = DialogResult.OK;
        }
        catch (CampaignValidationException ex)
        {
            MessageBox.Show(this, ex.Message, "Check target assignments",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
