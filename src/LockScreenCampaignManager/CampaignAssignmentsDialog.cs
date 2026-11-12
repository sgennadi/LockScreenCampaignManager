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
    // Optional signed, read-only LDAP lookup provided by the connection form.
    // Offline layout tests use All groups without any directory connection.
    private readonly Func<string, bool, Task<OuGroupDirectCoverage>>? _membershipLookup;
    private OuGroupDirectCoverage? _computerCoverage;
    private OuGroupDirectCoverage? _userCoverage;
    private readonly Label _computerCoverageLabel = new() { Name = "AssignmentComputerCoverage" };
    private readonly Label _userCoverageLabel = new() { Name = "AssignmentUserCoverage" };
    private readonly CheckBox _computerDirectMembers = new();
    private readonly CheckBox _userDirectMembers = new();
    private int _computerMembershipGeneration;
    private int _userMembershipGeneration;
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
        string currentComputerOu, string currentComputerGroup,
        Func<string, bool, Task<OuGroupDirectCoverage>>? membershipLookup = null)
    {
        _project = project;
        _membershipLookup = membershipLookup;
        _ous = ous.DistinctBy(x => x.Dn, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
        _groups = groups.DistinctBy(x => x.Dn, StringComparer.OrdinalIgnoreCase)
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToList();
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
            Text = UiLocale.Display(name), Width = width, Height = 43,
            MinimumSize = new Size(width, 43), AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowOnly,
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
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        Controls.Add(root);

        // Use two independently translated labels; concatenating both into
        // a single Label used to bypass the reversible RU/HE caption map.
        var heading = new TableLayoutPanel {
            Name = "AssignmentTargetHeading", Dock = DockStyle.Fill,
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, RowCount = 2
        };
        heading.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        heading.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        heading.Controls.Add(new Label {
            Name = "AssignmentTargetHeadingTitle",
            Text = "Group assignments — choose Lock Screen, Screen Saver or Both",
            Dock = DockStyle.Top, AutoSize = true,
            ForeColor = UiStyle.Navy, Font = new Font("Segoe UI Semibold", 11f)
        }, 0, 0);
        heading.Controls.Add(new Label {
            Name = "AssignmentTargetHeadingHelp",
            Text = "Lock Screen = computer OU/group (HKLM). Screen Saver = user OU/group (HKCU). Both uses two independent, filtered GPOs.",
            Dock = DockStyle.Top, AutoSize = true,
            ForeColor = UiStyle.Muted
        }, 0, 1);
        // Header captions vary considerably across RU/HE at 200% DPI.
        heading.SizeChanged += (_, _) =>
        {
            int width = Math.Max(300, heading.ClientSize.Width - 18);
            foreach (Label label in heading.Controls.OfType<Label>())
                if (label.MaximumSize.Width != width)
                    label.MaximumSize = new Size(width, 0);
        };
        root.Controls.Add(heading, 0, 0);

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
            RowCount = 16, ColumnCount = 1, Padding = new Padding(5)
        };
        foreach (int h in new[] { 75, 75, 74, 74, 50, 75, 74, 50, 75, 55, 65, 60, 62, 84, 58 })
            fields.RowStyles.Add(h == 58
                ? new RowStyle(SizeType.AutoSize) // Long translated warning must not be clipped.
                : new RowStyle(SizeType.Absolute, h));
        fields.RowStyles.Add(new RowStyle(SizeType.AutoSize)); // Coverage summary for both scopes
        details.Controls.Add(fields);
        split.Panel2.Controls.Add(details);

        _name.Name = "AssignmentName";
        _name.RightToLeft = RightToLeft.No; // user-provided exact campaign assignment
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
        _computerOu.SelectedIndexChanged += async (_, _) =>
        {
            _computerCoverage = null;
            UpdateCoverageLabel(false);
            CommitRow();
            if (!_loading) await RefreshScopeGroupChoicesAsync(false);
        };
        _computerGroup.SelectedIndexChanged += (_, _) => {
            CommitRow();
            UpdateCoverageLabel(false);
        };
        _userOu.SelectedIndexChanged += async (_, _) =>
        {
            _userCoverage = null;
            UpdateCoverageLabel(true);
            CommitRow();
            if (!_loading) await RefreshScopeGroupChoicesAsync(true);
        };
        _userGroup.SelectedIndexChanged += (_, _) => {
            CommitRow();
            UpdateCoverageLabel(true);
        };
        fields.Controls.Add(Field("Computer OU (Lock Screen target)", _computerOu), 0, 2);
        fields.Controls.Add(Field("Computer security group (Lock Screen)", _computerGroup), 0, 3);
        _computerDirectMembers.Name = "AssignmentFilterComputerGroupsByOu";
        _computerDirectMembers.Dock = DockStyle.Fill;
        _computerDirectMembers.Text = "Filter computer groups by selected OU";
        _computerDirectMembers.Checked = _membershipLookup is not null;
        _computerDirectMembers.CheckedChanged += async (_, _) =>
        {
            if (!_loading) await RefreshScopeGroupChoicesAsync(false);
        };
        fields.Controls.Add(_computerDirectMembers, 0, 4);
        fields.Controls.Add(Field("User OU (Screen Saver target)", _userOu), 0, 5);
        fields.Controls.Add(Field("User security group (Screen Saver)", _userGroup), 0, 6);
        _userDirectMembers.Name = "AssignmentFilterUserGroupsByOu";
        _userDirectMembers.Dock = DockStyle.Fill;
        _userDirectMembers.Text = "Filter user groups by selected OU";
        _userDirectMembers.Checked = _membershipLookup is not null;
        _userDirectMembers.CheckedChanged += async (_, _) =>
        {
            if (!_loading) await RefreshScopeGroupChoicesAsync(true);
        };
        fields.Controls.Add(_userDirectMembers, 0, 7);
        var scopeTooltip = new ToolTip();
        scopeTooltip.SetToolTip(_computerDirectMembers, UiLocale.Display(
            "Only groups with direct COMPUTER members in the selected OU (uncheck for all groups)"));
        scopeTooltip.SetToolTip(_userDirectMembers, UiLocale.Display(
            "Only groups with direct USER members in the selected OU (uncheck for all groups)"));

        _saver.Name = "AssignmentScreenSaverExecutable";
        _saver.DropDownStyle = ComboBoxStyle.DropDownList;
        _saver.Items.AddRange(CampaignTargetAssignments.BuiltInScreenSavers
            .Cast<object>().ToArray());
        _saver.SelectedItem = _project.ScreenSaver.Executable;
        _saver.SelectedIndexChanged += (_, _) => Preview();
        fields.Controls.Add(Field("Windows built-in .scr program", _saver), 0, 8);

        _photo.Name = "AssignmentScreenSaverJpegMode";
        _photo.Dock = DockStyle.Fill;
        _photo.Text = "Use JPG slideshow from selected campaign images (pilot only)";
        _photo.Checked = _project.ScreenSaver.UseJpegSlideshow;
        _photo.CheckedChanged += (_, _) => { SetModeVisibility(); Preview(); };
        fields.Controls.Add(_photo, 0, 9);
        _slide.Name = "AssignmentScreenSaverSlideSeconds";
        _slide.Minimum = 5;
        _slide.Maximum = 3600;
        _slide.Increment = 5;
        _slide.Value = Math.Clamp(_project.ScreenSaver.SlideSeconds, 5, 3600);
        _slide.ValueChanged += (_, _) => Preview();
        fields.Controls.Add(Field("JPG slide interval (seconds)", _slide), 0, 10);

        _idle.Name = "AssignmentScreenSaverIdleSeconds";
        _idle.Minimum = 60;
        _idle.Maximum = 86400;
        _idle.Increment = 60;
        _idle.Value = Math.Clamp(_project.ScreenSaver.IdleSeconds, 60, 86400);
        _idle.ValueChanged += (_, _) => Preview();
        fields.Controls.Add(Field("Screen Saver idle timeout (seconds)", _idle), 0, 11);
        _password.Name = "AssignmentScreenSaverPassword";
        _password.Checked = _project.ScreenSaver.PasswordOnResume;
        _password.Text = "Require Windows sign-in when resuming Screen Saver";
        _password.Dock = DockStyle.Fill;
        _password.AutoSize = false;
        _password.CheckedChanged += (_, _) => Preview();
        fields.Controls.Add(_password, 0, 12);

        _preview.Name = "AssignmentReadOnlyPreview";
        _preview.Multiline = true; _preview.ReadOnly = true;
        _preview.WordWrap = false;
        _preview.ScrollBars = ScrollBars.Both;
        _preview.Font = new Font("Consolas", 9.5f);
        _preview.Dock = DockStyle.Fill;
        fields.Controls.Add(_preview, 0, 13);
        _help.Name = "AssignmentModeHelp";
        _help.Dock = DockStyle.Top;
        _help.AutoSize = true;
        fields.SizeChanged += (_, _) =>
        {
            int width = Math.Max(210, fields.ClientSize.Width - fields.Padding.Horizontal - 22);
            if (_help.MaximumSize.Width != width)
                _help.MaximumSize = new Size(width, 0);
        };
        _help.Text = UiLocale.Display(
            "Screen Saver is a USER policy; Lock Screen is a COMPUTER policy. " +
            "A group may be stored anywhere in AD: the OU is the GPO LINK target. " +
            "Direct-member filters exclude nested/primary memberships; uncheck for All groups. " +
            "No loopback/Replace is configured automatically.");
        _help.ForeColor = UiStyle.Muted;
        fields.Controls.Add(_help, 0, 14);

        var coverageRows = new TableLayoutPanel {
            Name = "AssignmentScopeCoverageRows",
            Dock = DockStyle.Top, AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            RowCount = 2, ColumnCount = 1
        };
        coverageRows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        coverageRows.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        foreach (Label label in new[] { _computerCoverageLabel, _userCoverageLabel })
        {
            label.AutoSize = true;
            label.Dock = DockStyle.Top;
            label.ForeColor = UiStyle.Navy;
            label.MaximumSize = new Size(600, 0);
        }
        fields.SizeChanged += (_, _) =>
        {
            int width = Math.Max(210, fields.ClientSize.Width - fields.Padding.Horizontal - 22);
            foreach (Label label in new[] { _computerCoverageLabel, _userCoverageLabel })
                if (label.MaximumSize.Width != width)
                    label.MaximumSize = new Size(width, 0);
        };
        coverageRows.Controls.Add(_computerCoverageLabel, 0, 0);
        coverageRows.Controls.Add(_userCoverageLabel, 0, 1);
        fields.Controls.Add(coverageRows, 0, 15);
        UpdateCoverageLabel(false);
        UpdateCoverageLabel(true);

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
        // AD distinguished names remain canonical LTR, including in Hebrew RTL.
        box.RightToLeft = RightToLeft.No;
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
            // Restore complete choices before selecting each saved group;
            // the previous assignment row may have shown a different OU's
            // direct-member subset, which omits this row's saved group.
            ReplaceGroupChoices(_computerGroup, null, current.ComputerGroupDn);
            ReplaceGroupChoices(_userGroup, null, current.UserGroupDn);
            SelectDn(_computerOu, current.ComputerOuDn);
            SelectDn(_computerGroup, current.ComputerGroupDn);
            SelectDn(_userOu, current.UserOuDn);
            SelectDn(_userGroup, current.UserGroupDn);
        }
        finally { _loading = false; }
        _computerCoverage = null;
        _userCoverage = null;
        UpdateCoverageLabel(false);
        UpdateCoverageLabel(true);
        SetModeVisibility();
        _ = RefreshScopeGroupChoicesAsync(false);
        _ = RefreshScopeGroupChoicesAsync(true);
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


    // UI filter rules never silently erase an explicitly selected group.
    // A group outside a direct-member filter is still valid to BROWSE,
    // but its effective GPO applicability requires independent Preview.
    internal static bool MustShowAllToKeepGroup(string? chosenDn,
        IReadOnlyCollection<string> directGroupDns) =>
        !string.IsNullOrWhiteSpace(chosenDn) &&
        !directGroupDns.Contains(chosenDn, StringComparer.OrdinalIgnoreCase);

    private string RememberedGroupDn(bool userScope, ComboBox box)
    {
        string selected = SelectedDn(box);
        if (selected.Length > 0) return selected;
        if (_editing < 0 || _editing >= _draft.Count) return string.Empty;
        return userScope ? _draft[_editing].UserGroupDn :
            _draft[_editing].ComputerGroupDn;
    }

    private void ReplaceGroupChoices(ComboBox box, HashSet<string>? allowed,
        string? preferredDn = null)
    {
        string oldDn = preferredDn ?? SelectedDn(box);
        bool priorLoading = _loading;
        _loading = true;
        try
        {
            box.BeginUpdate();
            try
            {
                box.Items.Clear();
                foreach (AdChoice choice in _groups)
                    if (allowed is null || allowed.Contains(choice.Dn))
                        box.Items.Add(choice);
                SelectDn(box, oldDn);
            }
            finally { box.EndUpdate(); }
        }
        finally { _loading = priorLoading; }
    }

    private async Task RefreshScopeGroupChoicesAsync(bool userScope)
    {
        int generation = userScope
            ? ++_userMembershipGeneration : ++_computerMembershipGeneration;
        ComboBox ou = userScope ? _userOu : _computerOu;
        ComboBox groups = userScope ? _userGroup : _computerGroup;
        CheckBox filter = userScope ? _userDirectMembers : _computerDirectMembers;
        string selectedOuDn = SelectedDn(ou);
        string chosenDn = RememberedGroupDn(userScope, groups);

        // Retain a chosen group while querying. Even in All groups mode we
        // read the OU counts when possible, without filtering the choices.
        if (selectedOuDn.Length == 0 || _membershipLookup is null)
        {
            if (userScope) _userCoverage = null;
            else _computerCoverage = null;
            ReplaceGroupChoices(groups, null, chosenDn);
            UpdateCoverageLabel(userScope);
            return;
        }
        if (!filter.Checked) ReplaceGroupChoices(groups, null, chosenDn);
        // Retain the current choice until LDAP has returned an exact answer.
        // The prior implementation cleared the list immediately and then
        // CommitRow() silently replaced a saved DN with an empty string.
        _help.Text = string.Format(UiLocale.Display(
            "Reading direct {0} group membership in selected OU. Existing target is retained."),
            UiLocale.Display(userScope ? "User" : "Computer"));
        await Task.Delay(250);
        if (generation != (userScope ? _userMembershipGeneration : _computerMembershipGeneration) ||
            IsDisposed || Disposing || SelectedDn(ou) != selectedOuDn)
            return;
        try
        {
            OuGroupDirectCoverage coverage =
                await _membershipLookup(selectedOuDn, userScope);
            if (generation != (userScope ? _userMembershipGeneration : _computerMembershipGeneration) ||
                IsDisposed || Disposing || SelectedDn(ou) != selectedOuDn)
                return;
            // The operator can change the selected group during lookup;
            // do not overwrite that newer selection with a stale snapshot.
            chosenDn = RememberedGroupDn(userScope, groups);
            if (userScope) _userCoverage = coverage;
            else _computerCoverage = coverage;
            var allowed = coverage.DirectGroupDns.ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!filter.Checked)
            {
                ReplaceGroupChoices(groups, null, chosenDn);
                _help.Text = UiLocale.Display(
                    "All groups shown. OU direct-member coverage is read-only and not effective GPO application.");
            }
            else if (MustShowAllToKeepGroup(chosenDn, allowed))
            {
                bool priorLoading = _loading;
                _loading = true;
                try { filter.Checked = false; }
                finally { _loading = priorLoading; }
                ReplaceGroupChoices(groups, null, chosenDn);
                _help.Text = string.Format(UiLocale.Display(
                    "Selected {0} group kept. It is not in the selected OU's DIRECT membership list. " +
                    "Showing All groups; verify nesting, GPO link and applicability in Preview."),
                    UiLocale.Display(userScope ? "User" : "Computer"));
            }
            else
            {
                ReplaceGroupChoices(groups, allowed, chosenDn);
                _help.Text = string.Format(UiLocale.Display(
                    "{0} OU direct member groups: {1}. Nested groups / primary groups are not expanded. " +
                    "Uncheck OU filter to choose any domain security group."),
                    UiLocale.Display(userScope ? "User" : "Computer"), groups.Items.Count);
            }
            UpdateCoverageLabel(userScope);
        }
        catch
        {
            if (generation != (userScope ? _userMembershipGeneration : _computerMembershipGeneration) ||
                IsDisposed || Disposing || SelectedDn(ou) != selectedOuDn)
                return;
            if (userScope) _userCoverage = null;
            else _computerCoverage = null;
            bool priorLoading = _loading;
            _loading = true;
            try { filter.Checked = false; }
            finally { _loading = priorLoading; }
            ReplaceGroupChoices(groups, null, RememberedGroupDn(userScope, groups));
            UpdateCoverageLabel(userScope);
            _help.Text = UiLocale.Display(
                "OU membership lookup unavailable/incomplete. All security groups shown, " +
                "existing selection retained. Verify USER/COMPUTER targeting and OU link in Preview.");
        }
    }

    private void UpdateCoverageLabel(bool userScope)
    {
        Label label = userScope ? _userCoverageLabel : _computerCoverageLabel;
        OuGroupDirectCoverage? coverage = userScope ? _userCoverage : _computerCoverage;
        string expectedOu = SelectedDn(userScope ? _userOu : _computerOu);
        if (coverage is null ||
            !string.Equals(coverage.OuDn, expectedOu,
                StringComparison.OrdinalIgnoreCase))
        {
            label.Text = UiLocale.Display(userScope
                ? "User OU/group coverage: not verified."
                : "Computer OU/group coverage: not verified.");
            return;
        }
        string chosenGroup = SelectedDn(userScope ? _userGroup : _computerGroup);
        label.Text = string.IsNullOrWhiteSpace(chosenGroup)
            ? string.Format(UiLocale.Display(
                "{0} objects in User/Computer OU subtree. Select a security group."),
                coverage.PrincipalCount)
            : string.Format(UiLocale.Display(
                "OU subtree: {0} {1} objects; selected group: {2} direct matches. " +
                "Inactive accounts included. Not effective GPO application."),
                coverage.PrincipalCount, UiLocale.Display(userScope ? "User" : "Computer"),
                coverage.DirectMatchesFor(chosenGroup));
    }

    private void SetModeVisibility()
    {
        bool hasComputer = _mode.SelectedItem is CampaignTargetMode.LockScreen
            or CampaignTargetMode.Both;
        bool hasUser = _mode.SelectedItem is CampaignTargetMode.ScreenSaver
            or CampaignTargetMode.Both;
        _computerOu.Enabled = _computerGroup.Enabled = hasComputer;
        _userOu.Enabled = _userGroup.Enabled = hasUser;
        _computerDirectMembers.Enabled = hasComputer && _membershipLookup is not null;
        _userDirectMembers.Enabled = hasUser && _membershipLookup is not null;
        _computerCoverageLabel.Visible = hasComputer;
        _userCoverageLabel.Visible = hasUser;
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
