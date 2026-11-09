using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Edits only a local copy of the campaign; no AD/GPO/Task Scheduler calls.
/// </summary>
internal sealed class CampaignScheduleDialog : Form
{
    private readonly CampaignProject _draft;
    private readonly ComboBox _mode = new();
    private readonly ComboBox _timeZone = new();
    private readonly Label _timeZoneDifference = new();
    private readonly FlowLayoutPanel _intervalInputs = new();
    private readonly NumericUpDown _intervalHours = new();
    private readonly NumericUpDown _intervalMinutes = new();
    private readonly CheckBox[] _days = new CheckBox[7];
    private readonly CheckBox _randomize = new();
    private readonly TextBox _previewComputer = new();
    private readonly DataGridView _grid = new();
    private readonly MonthCalendar _calendar = new();
    private readonly DateTimePicker _date = new();
    private readonly DateTimePicker _time = new();
    private readonly Label _summary = new();
    private readonly Button _assign = new();
    private readonly Button _repeat = new();
    private readonly Button _importFolder = new();
    private readonly Button _apply = new();
    private bool _loading;

    internal CampaignProject? ResultProject { get; private set; }

    internal CampaignScheduleDialog(CampaignProject source)
    {
        _draft = new CampaignProject {
            Name = source.Name, CompanyProfileId = source.CompanyProfileId,
            StartLocal = source.StartLocal, TimeZoneId = source.TimeZoneId,
            LastImageHours = source.LastImageHours,
            ImagePaths = new List<string>(source.ImagePaths),
            ScheduleMode = source.ScheduleMode, WeekdayMask = source.WeekdayMask,
            IntervalMinutes = source.IntervalMinutes,
            SpecificDatesLocal = new List<DateTime>(source.SpecificDatesLocal),
            RandomizePerDevice = source.RandomizePerDevice,
            EnableFeedback = source.EnableFeedback, FeedbackShare = source.FeedbackShare,
            FeedbackIntervalMinutes = source.FeedbackIntervalMinutes
        };
        Text = "Campaign Schedule - Images and Dates";
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(875, 630);
        Size = new Size(1050, 755);
        BackColor = UiStyle.Canvas;
        Build();
        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "campaign-schedule");
        _loading = true;
        _mode.SelectedIndex = (int)_draft.ScheduleMode;
        _intervalHours.Value = Math.Clamp(_draft.IntervalMinutes / 60, 0, 168);
        _intervalMinutes.Value = Math.Clamp(_draft.IntervalMinutes % 60, 0, 59);
        for (int i = 0; i < 7; i++) _days[i].Checked = (_draft.WeekdayMask & (1 << i)) != 0;
        _randomize.Checked = _draft.RandomizePerDevice;
        _loading = false;
        UpdateMode();
        Draw();
    }

    private static Button Button(string text, int width, Action click)
    {
        var result = new Button {
            Text = text, Width = width, Height = 36,
            AutoEllipsis = true, Margin = new Padding(3, 4, 7, 4)
        };
        result.Click += (_, _) => click();
        return result;
    }

    private void Build()
    {
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, RowCount = 6, ColumnCount = 1,
            Padding = new Padding(14), AutoScroll = true
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 83));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 151));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 114));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        Controls.Add(root);
        root.Controls.Add(new Label {
            Dock = DockStyle.Fill, Text = "When to change the image" + Environment.NewLine +
                "Assign exact dates per image or choose weekdays. No GPO is modified here.",
            Font = new Font("Segoe UI Semibold", 15f),
            ForeColor = UiStyle.Foreground
        }, 0, 0);

        var choices = new FlowLayoutPanel {
            Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true
        };
        choices.Controls.Add(new Label {
            Text = "Schedule:", AutoSize = true, Margin = new Padding(3, 12, 8, 0)
        });
        _mode.DropDownStyle = ComboBoxStyle.DropDownList;
        _mode.Width = 211;
        _mode.Items.AddRange(new object[] {
            "Every day", "Work days (Mon-Fri)", "Custom weekdays",
            "Specific dates", "Every N hours/minutes"
        });
        _mode.SelectedIndexChanged += (_, _) => ChangeMode();
        choices.Controls.Add(_mode);
        _importFolder.Text = "Import image folder...";
        _importFolder.Width = 180;
        _importFolder.Height = 35;
        _importFolder.AutoEllipsis = true;
        _importFolder.Click += (_, _) => ImportFolder();
        choices.Controls.Add(_importFolder);
        _intervalInputs.AutoSize = true;
        _intervalInputs.WrapContents = false;
        _intervalInputs.Margin = new Padding(4, 2, 12, 0);
        _intervalInputs.Controls.Add(new Label {
            Text = "Change every:", AutoSize = true,
            Margin = new Padding(0, 12, 5, 0)
        });
        _intervalHours.Minimum = 0;
        _intervalHours.Maximum = 168;
        _intervalHours.Width = 58;
        _intervalHours.Margin = new Padding(0, 6, 4, 0);
        _intervalInputs.Controls.Add(_intervalHours);
        _intervalInputs.Controls.Add(new Label {
            Text = "h", AutoSize = true, Margin = new Padding(0, 12, 6, 0)
        });
        _intervalMinutes.Minimum = 0;
        _intervalMinutes.Maximum = 59;
        _intervalMinutes.Width = 57;
        _intervalMinutes.Margin = new Padding(0, 6, 4, 0);
        _intervalInputs.Controls.Add(_intervalMinutes);
        _intervalInputs.Controls.Add(new Label {
            Text = "min", AutoSize = true, Margin = new Padding(0, 12, 0, 0)
        });
        void IntervalChanged()
        {
            if (_loading) return;
            _draft.IntervalMinutes = (int)_intervalHours.Value * 60 +
                (int)_intervalMinutes.Value;
            Draw();
        }
        _intervalHours.ValueChanged += (_, _) => IntervalChanged();
        _intervalMinutes.ValueChanged += (_, _) => IntervalChanged();
        choices.Controls.Add(_intervalInputs);
        string[] labels = { "Mon", "Tue", "Wed", "Thu", "Fri", "Sat", "Sun" };
        for (int i = 0; i < 7; i++)
        {
            _days[i] = new CheckBox {
                Text = labels[i], AutoSize = true, Margin = new Padding(5, 10, 3, 0)
            };
            _days[i].CheckedChanged += (_, _) => {
                if (_loading) return;
                _draft.WeekdayMask = Enumerable.Range(0, 7)
                    .Where(x => _days[x].Checked).Aggregate(0, (mask, bit) => mask | (1 << bit));
                Draw();
            };
            choices.Controls.Add(_days[i]);
        }
        _randomize.Text = "Randomize images per device (stable)";
        _randomize.AutoSize = true;
        _randomize.Margin = new Padding(3, 12, 0, 0);
        _randomize.CheckedChanged += (_, _) => {
            _draft.RandomizePerDevice = _randomize.Checked;
            Draw();
        };
        choices.Controls.Add(_randomize);
        choices.Controls.Add(new Label {
            Text = "Preview device:", AutoSize = true,
            Margin = new Padding(10, 12, 4, 0)
        });
        _previewComputer.Width = 120;
        _previewComputer.PlaceholderText = "e.g. PC001";
        _previewComputer.TextChanged += (_, _) => Draw();
        choices.Controls.Add(_previewComputer);
        root.Controls.Add(choices, 0, 1);

        // A visible, wide Windows time-zone selector is part of Schedule
        // itself, not hidden in the main editor. Keep the real zone ID
        // in the draft for DST validation and client deployment.
        var zoneRow = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
            Padding = new Padding(2, 7, 2, 7)
        };
        zoneRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        zoneRow.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        zoneRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 165));
        zoneRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        zoneRow.Controls.Add(new Label
        {
            Text = "Campaign time zone:",
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = UiStyle.Navy,
            Font = new Font("Segoe UI Semibold", 10f)
        }, 0, 0);
        _timeZone.Name = "ScheduleTimeZoneSelect";
        _timeZone.Dock = DockStyle.Fill;
        _timeZone.DropDownWidth = 540;
        _timeZone.AccessibleName = "Windows time zone for scheduled images";
        TimeZoneChoices.Populate(_timeZone, _draft.TimeZoneId);
        _timeZone.SelectedIndexChanged += (_, _) =>
        {
            if (_loading) return;
            string chosen = TimeZoneChoices.SelectedId(_timeZone);
            if (chosen.Length == 0) return;
            _draft.TimeZoneId = chosen;
            _timeZoneDifference.Text =
                TimeZoneChoices.OffsetComparison(chosen, DateTimeOffset.UtcNow);
            Draw();
        };
        zoneRow.Controls.Add(_timeZone, 1, 0);
        _timeZoneDifference.Name = "ScheduleTimeZoneDifference";
        _timeZoneDifference.Dock = DockStyle.Fill;
        _timeZoneDifference.TextAlign = ContentAlignment.MiddleLeft;
        _timeZoneDifference.Padding = new Padding(6, 5, 6, 7);
        _timeZoneDifference.ForeColor = UiStyle.Green;
        _timeZoneDifference.AutoEllipsis = false;
        _timeZoneDifference.Text = TimeZoneChoices.OffsetComparison(
            _draft.TimeZoneId, DateTimeOffset.UtcNow);
        zoneRow.Controls.Add(_timeZoneDifference, 0, 1);
        zoneRow.SetColumnSpan(_timeZoneDifference, 2);
        root.Controls.Add(zoneRow, 0, 2);

        var middle = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 66));
        middle.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        root.Controls.Add(middle, 0, 3);
        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "#", FillWeight = 25 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "Change date / time", FillWeight = 110
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn {
            HeaderText = "Image", FillWeight = 150
        });
        _grid.SelectionChanged += (_, _) => ShowSelected();
        middle.Controls.Add(_grid, 0, 0);

        var right = new TableLayoutPanel {
            Dock = DockStyle.Fill, RowCount = 5, Padding = new Padding(9),
            AutoScroll = true
        };
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 210));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        middle.Controls.Add(right, 1, 0);
        _calendar.MaxSelectionCount = 1;
        _calendar.DateSelected += (_, _) => {
            if (_draft.ScheduleMode == CampaignScheduleMode.SpecificDates)
                _date.Value = _calendar.SelectionStart;
        };
        right.Controls.Add(_calendar, 0, 0);
        var pickers = new FlowLayoutPanel {
            Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true
        };
        _date.CustomFormat = "dd.MM.yyyy";
        _date.Format = DateTimePickerFormat.Custom;
        _date.Width = 125;
        _time.CustomFormat = "HH:mm";
        _time.Format = DateTimePickerFormat.Custom;
        _time.ShowUpDown = true;
        _time.Width = 93;
        pickers.Controls.Add(_date);
        pickers.Controls.Add(_time);
        right.Controls.Add(pickers, 0, 1);
        _assign.Text = "Set selected date/time";
        _assign.Dock = DockStyle.Fill;
        _assign.Click += (_, _) => Assign();
        right.Controls.Add(_assign, 0, 2);
        _repeat.Text = "Repeat selected image";
        _repeat.Dock = DockStyle.Fill;
        _repeat.Click += (_, _) => RepeatImage();
        right.Controls.Add(_repeat, 0, 3);
        right.Controls.Add(new Label {
            Dock = DockStyle.Fill, ForeColor = UiStyle.Muted,
            Text = "Specific dates may have different times." +
                Environment.NewLine + "Repeat adds another slot with the same image." +
                Environment.NewLine + "Stable randomization affects image order only, not scheduled dates."
        }, 0, 4);

        _summary.Dock = DockStyle.Fill;
        _summary.ForeColor = UiStyle.Foreground;
        root.Controls.Add(_summary, 0, 4);
        var actions = new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true, AutoScroll = true
        };
        actions.Controls.Add(Button("Cancel", 114, () => {
            DialogResult = DialogResult.Cancel; Close();
        }));
        _apply.Text = "Apply schedule";
        _apply.Width = 175;
        _apply.Height = 38;
        _apply.BackColor = UiStyle.Accent;
        _apply.ForeColor = Color.White;
        _apply.Click += (_, _) => Apply();
        actions.Controls.Add(_apply);
        root.Controls.Add(actions, 0, 5);
        AcceptButton = _apply;
    }

    private void ChangeMode()
    {
        if (_loading || _mode.SelectedIndex < 0) return;
        var mode = (CampaignScheduleMode)_mode.SelectedIndex;
        if (mode == CampaignScheduleMode.SpecificDates &&
            _draft.ScheduleMode != CampaignScheduleMode.SpecificDates)
        {
            List<DateTime> list;
            try
            {
                list = Enumerable.Range(0, _draft.ImagePaths.Count)
                    .Select(i => CampaignSchedule.At(_draft, i)).ToList();
            }
            catch (InvalidOperationException)
            {
                // Changing modes must remain possible when CustomDays is
                // temporarily invalid (e.g. all weekday boxes unchecked).
                list = Enumerable.Range(0, _draft.ImagePaths.Count)
                    .Select(i => _draft.StartLocal.AddDays(i)).ToList();
            }
            _draft.SpecificDatesLocal = list;
            if (list.Count > 0) _draft.StartLocal = list[0];
        }
        _draft.ScheduleMode = mode;
        UpdateMode();
        Draw();
    }

    private void UpdateMode()
    {
        bool specific = _draft.ScheduleMode == CampaignScheduleMode.SpecificDates;
        int displayedDays = _draft.ScheduleMode switch
        {
            CampaignScheduleMode.Daily => 127,
            CampaignScheduleMode.Workdays => 31,
            CampaignScheduleMode.CustomDays => _draft.WeekdayMask,
            CampaignScheduleMode.EveryInterval => 0,
            _ => 0
        };
        _loading = true;
        try
        {
            for (int i = 0; i < 7; i++)
                _days[i].Checked = (displayedDays & (1 << i)) != 0;
        }
        finally { _loading = false; }
        foreach (var day in _days) day.Enabled =
            _draft.ScheduleMode == CampaignScheduleMode.CustomDays;
        _intervalInputs.Visible = _draft.ScheduleMode == CampaignScheduleMode.EveryInterval;
        _date.Enabled = _time.Enabled = _assign.Enabled =
            _calendar.Enabled = specific && _draft.ImagePaths.Count > 0;
        _repeat.Enabled = _draft.ImagePaths.Count is > 0 and < CampaignSchedule.MaxImages;
    }

    private void Draw()
    {
        int selected = _grid.SelectedRows.Count == 1 ? _grid.SelectedRows[0].Index : 0;
        _grid.SuspendLayout();
        try
        {
            _grid.Rows.Clear();
            if (_draft.ScheduleMode == CampaignScheduleMode.EveryInterval &&
                _draft.IntervalMinutes is < 1 or > CampaignSchedule.MaxIntervalMinutes)
            {
                _calendar.BoldedDates = Array.Empty<DateTime>();
                _summary.Text = "Enter an interval from 00:01 to 168:00 (hours:minutes).";
                _apply.Enabled = false;
                return;
            }
            if (_draft.ScheduleMode == CampaignScheduleMode.CustomDays &&
                _draft.WeekdayMask == 0)
            {
                _calendar.BoldedDates = Array.Empty<DateTime>();
                _summary.Text = "Select at least one weekday.";
                _apply.Enabled = false;
                return;
            }
            var dates = new List<DateTime>();
            IReadOnlyList<int>? order = null;
            bool devicePreview = _draft.RandomizePerDevice &&
                !string.IsNullOrWhiteSpace(_previewComputer.Text);
            if (devicePreview)
            {
                order = CampaignDeviceImageOrder.Resolve(
                    CampaignSchedule.BuildId(_draft), _previewComputer.Text.Trim(),
                    _draft.ImagePaths.Count, randomize: true);
            }
            _grid.Columns[2].HeaderText = devicePreview
                ? "Image (sample device)" : "Image (editing order)";
            for (int i = 0; i < _draft.ImagePaths.Count; i++)
            {
                var day = CampaignSchedule.At(_draft, i);
                dates.Add(day);
                _grid.Rows.Add((i + 1).ToString("D2"), day.ToString("dd.MM.yyyy HH:mm"),
                    Path.GetFileName(_draft.ImagePaths[order is null ? i : order[i]]));
            }
            _calendar.BoldedDates = dates.Select(x => x.Date).ToArray();
            _calendar.UpdateBoldedDates();
            _summary.Text = $"Scheduled images: {dates.Count}  |  " +
                $"First: {(dates.Count == 0 ? _draft.StartLocal : dates[0]):dd.MM.yyyy HH:mm}  |  " +
                $"Restore: {CampaignSchedule.End(_draft):dd.MM.yyyy HH:mm}  |  " +
                $"Time zone: {_draft.TimeZoneId}" +
                (_draft.RandomizePerDevice
                    ? (devicePreview
                        ? "  |  Stable preview for: " + _previewComputer.Text.Trim()
                        : "  |  Enter a computer name to preview its stable shuffle")
                    : "");
            _apply.Enabled = true;
            _grid.ClearSelection();
            if (_grid.Rows.Count > 0)
            {
                int index = Math.Clamp(selected, 0, _grid.Rows.Count - 1);
                _grid.Rows[index].Selected = true;
                _grid.CurrentCell = _grid.Rows[index].Cells[0];
            }
        }
        catch (Exception ex)
        {
            _apply.Enabled = false;
            _summary.Text = "Schedule invalid: " +
                UniversalDiagnosticsEmbedded.Clean(ex.Message, 180);
        }
        finally { _grid.ResumeLayout(); }
        ShowSelected();
    }

    private void ShowSelected()
    {
        if (_grid.SelectedRows.Count != 1) return;
        try
        {
            var at = CampaignSchedule.At(_draft, _grid.SelectedRows[0].Index);
            if (at < _date.MinDate || at > _date.MaxDate) return;
            _date.Value = at.Date;
            _time.Value = DateTime.Today.Add(at.TimeOfDay);
            if (at >= _calendar.MinDate && at <= _calendar.MaxDate)
                _calendar.SetDate(at.Date);
        }
        catch (ArgumentException) { }
        catch (InvalidOperationException) { }
    }

    private void Assign()
    {
        if (_draft.ScheduleMode != CampaignScheduleMode.SpecificDates ||
            _grid.SelectedRows.Count != 1) return;
        int index = _grid.SelectedRows[0].Index;
        var at = DateTime.SpecifyKind(_date.Value.Date.Add(_time.Value.TimeOfDay),
            DateTimeKind.Unspecified);
        var dates = _draft.SpecificDatesLocal.ToList();
        dates[index] = at;
        if (dates.Distinct().Count() != dates.Count)
        {
            MessageBox.Show(this, "Every scheduled time must be unique.", "Schedule",
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        // Sort date/image pairs together, never detach an image from its date.
        var pairs = dates.Select((d, i) => (date: d, path: _draft.ImagePaths[i]))
            .OrderBy(x => x.date).ToArray();
        _draft.SpecificDatesLocal = pairs.Select(x => x.date).ToList();
        _draft.ImagePaths = pairs.Select(x => x.path).ToList();
        _draft.StartLocal = pairs[0].date;
        Draw();
    }

    private void ImportFolder()
    {
        try
        {
            if (_draft.ImagePaths.Count > 0 &&
                MessageBox.Show(this,
                    "Importing a folder will replace the current image list " +
                    "and its date assignments in this editor. Continue?",
                    "Replace campaign images", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            // Keep the selected schedule mode, chosen weekdays, exact interval,
            // company and time zone. The folder-mapping preview is the only
            // step allowed to commit new image assignments to this draft.
            using var importer = new ImageFolderImportDialog(_draft);
            if (importer.ShowDialog(this) != DialogResult.OK ||
                importer.ResultProject is not { } result) return;
            _draft.ImagePaths = result.ImagePaths;
            _draft.StartLocal = result.StartLocal;
            _draft.SpecificDatesLocal = result.SpecificDatesLocal;
            UpdateMode();
            Draw();
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Import image folder", ex); }
    }

    private void RepeatImage()
    {
        if (_grid.SelectedRows.Count != 1 ||
            _draft.ImagePaths.Count >= CampaignSchedule.MaxImages) return;
        string image = _draft.ImagePaths[_grid.SelectedRows[0].Index];
        if (_draft.ScheduleMode == CampaignScheduleMode.SpecificDates)
        {
            _draft.SpecificDatesLocal.Add(_draft.SpecificDatesLocal[^1].AddDays(1));
        }
        _draft.ImagePaths.Add(image);
        UpdateMode();
        Draw();
    }

    private void Apply()
    {
        try
        {
            CampaignSchedule.Validate(_draft, requireImages: true);
            ResultProject = _draft;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, UniversalDiagnosticsEmbedded.Clean(ex.Message, 220),
                "Invalid schedule", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
