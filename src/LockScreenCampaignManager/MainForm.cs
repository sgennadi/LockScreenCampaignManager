using System.Windows.Forms;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.Json;

namespace LockScreenCampaignManager;

public sealed class MainForm : Form
{
    private CampaignProject _project = new();
    private CompanyProfile? _company;
    private bool _projectDirty;
    private readonly Label _companyLabel = new();
    private readonly Label _adConnectionStatus = new();
    private readonly Button _adConnectButton = new();
    private AdDirectorySnapshot? _adSnapshot;
    private AdConnectionScope _adScope = AdConnectionScope.From(null);
    private bool _adFirstStepShown;
    private readonly TextBox _name = new();
    private readonly DateTimePicker _startDate = new();
    private readonly DateTimePicker _startTime = new();
    private readonly NumericUpDown _lastHours = new();
    private readonly ComboBox _timeZone = new();
    private readonly DataGridView _table = new();
    private readonly PictureBox _preview = new();
    private readonly Label _previewName = new();
    private readonly Label _summary = new();
    private readonly Label _status = new();
    private bool _loading;
    private static Color Blue => UiStyle.Accent;
    private static Color Bg => UiStyle.Canvas;
    private static Color Border => UiStyle.Border;

    public MainForm()
    {
        Text = "LockScreen Campaign Manager " + AppVersion.Current;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(1100, 720);
        Size = new Size(1390, 850);
        BackColor = Bg;
        ConfigureLayout();
        try
        {
            _company = CompanyProfilesStore.Active();
            SetProject(CompanyProfilesStore.NewCampaign(_company));
        }
        catch (Exception ex)
        {
            // Corrupt company profiles cannot silently select the wrong AD scope.
            _company = null;
            SetProject(new CampaignProject());
            _status.Text = "Company profiles unavailable: inspect Diagnostics.";
            AppLog.Write(ex, "Company startup");
        }
        RestoreWindow();
        UiStyle.Apply(this);
        // The first interactive action is DC connection/inventory, not image selection.
        // CLI self-tests and headless UI smoke only construct forms, never Show().
        Shown += (_, _) =>
        {
            if (_adFirstStepShown) return;
            _adFirstStepShown = true;
            BeginInvoke(new Action(ConnectAndReadAd));
        };
    }

    private void ConfigureLayout()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            Padding = new Padding(18, 12, 18, 12), BackColor = Bg
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 188));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 140));
        Controls.Add(root);

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 61));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(new Label {
            Text = "LockScreen Campaign Manager", AutoSize = false,
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = false, MinimumSize = new Size(530, 52),
            Font = new Font("Segoe UI Semibold", 19f, FontStyle.Bold), ForeColor = UiStyle.Navy
        }, 0, 0);
        header.Controls.Add(new Label {
            Text = "1 Connect & Read AD    2 Configure campaign    3 Preview & deploy GPO",
            AutoSize = true, ForeColor = UiStyle.Muted
        }, 0, 1);
        _companyLabel.Dock = DockStyle.Fill;
        _companyLabel.ForeColor = UiStyle.Accent;
        _companyLabel.Font = new Font("Segoe UI Semibold", 9.5f);
        _companyLabel.AutoEllipsis = true;
        _companyLabel.Text = "ACTIVE COMPANY: Unassigned — local campaign";
        header.Controls.Add(_companyLabel, 0, 2);
        var adFirstStep = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true,
            BackColor = UiStyle.SoftBlue, Padding = new Padding(5, 5, 7, 4)
        };
        _adConnectButton.Text = "1  Connect to DC & Read AD";
        _adConnectButton.MinimumSize = new Size(218, 36);
        _adConnectButton.AutoSize = true;
        _adConnectButton.BackColor = UiStyle.Accent;
        _adConnectButton.ForeColor = Color.White;
        _adConnectButton.Tag = "PrimaryAction";
        _adConnectButton.Click += (_, _) => ConnectAndReadAd();
        adFirstStep.Controls.Add(_adConnectButton);
        _adConnectionStatus.AutoSize = true;
        _adConnectionStatus.MinimumSize = new Size(300, 31);
        _adConnectionStatus.TextAlign = ContentAlignment.MiddleLeft;
        _adConnectionStatus.Margin = new Padding(7, 2, 0, 0);
        adFirstStep.Controls.Add(_adConnectionStatus);
        UiStyle.ApplyStatus(_adConnectionStatus,
            "NOT CONNECTED — offline editing only.", UiStatusKind.Neutral);
        header.Controls.Add(adFirstStep, 0, 3);
        root.Controls.Add(header, 0, 0);

        var settings = PanelBox();
        var settingsFlow = new FlowLayoutPanel {
            Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true, Padding = new Padding(12, 12, 0, 4)
        };
        settings.Controls.Add(settingsFlow);
        _name.Width = 235;
        _name.TextChanged += (_, _) => OnDataChanged();
        settingsFlow.Controls.Add(WithLabel("Campaign name", _name, 244));
        _startDate.Format = DateTimePickerFormat.Custom;
        _startDate.CustomFormat = "dd.MM.yyyy";
        _startDate.Width = 135;
        _startDate.ValueChanged += (_, _) => OnDataChanged();
        settingsFlow.Controls.Add(WithLabel("Start date", _startDate, 150));
        _startTime.Format = DateTimePickerFormat.Custom;
        _startTime.CustomFormat = "HH:mm";
        _startTime.ShowUpDown = true;
        _startTime.Width = 100;
        _startTime.ValueChanged += (_, _) => OnDataChanged();
        settingsFlow.Controls.Add(WithLabel("Change time", _startTime, 114));
        _lastHours.Minimum = 1;
        _lastHours.Maximum = 72;
        _lastHours.Width = 98;
        _lastHours.ValueChanged += (_, _) => OnDataChanged();
        settingsFlow.Controls.Add(WithLabel("Last slide (hours)", _lastHours, 170));
        _timeZone.DropDownStyle = ComboBoxStyle.DropDown;
        _timeZone.Width = 173;
        _timeZone.DropDownWidth = 310;
        _timeZone.Items.AddRange(TimeZoneInfo.GetSystemTimeZones()
            .OrderBy(zone => zone.BaseUtcOffset)
            .ThenBy(zone => zone.Id, StringComparer.OrdinalIgnoreCase)
            .Select(zone => (object)zone.Id).ToArray());
        _timeZone.TextChanged += (_, _) => OnDataChanged();
        settingsFlow.Controls.Add(WithLabel("Windows time zone", _timeZone, 198));
        root.Controls.Add(settings, 0, 1);

        var middle = new SplitContainer {
            Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 9,
            BackColor = Bg
        };
        Shown += (_, _) =>
        {
            if (middle.Width > 800)
            {
                middle.SplitterDistance = (int)(middle.Width * 0.61);
                middle.Panel1MinSize = 450;
                middle.Panel2MinSize = 290;
            }
        };
        root.Controls.Add(middle, 0, 2);
        var left = PanelBox();
        var leftLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 102));
        left.Controls.Add(leftLayout);
        middle.Panel1.Controls.Add(left);
        leftLayout.Controls.Add(new Label {
            Text = "Images and schedule", AutoSize = true,
            Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(36, 47, 62)
        }, 0, 0);
        ConfigureGrid();
        leftLayout.Controls.Add(_table, 0, 1);
        var imageButtons = new FlowLayoutPanel {
            Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true,
            Padding = new Padding(0, 7, 0, 0)
        };
        imageButtons.Controls.Add(MakeButton("Schedule...", OpenScheduleEditor, 156, primary: true));
        imageButtons.Controls.Add(MakeButton("Add Files...", AddImages, 133));
        imageButtons.Controls.Add(MakeButton("Add Folder...", AddImagesFromFolder, 147));
        imageButtons.Controls.Add(MakeButton("Replace", ReplaceImage, 105));
        imageButtons.Controls.Add(MakeButton("Remove", RemoveImage, 90));
        imageButtons.Controls.Add(MakeButton("↑", () => MoveImage(-1), 40));
        imageButtons.Controls.Add(MakeButton("↓", () => MoveImage(+1), 40));
        leftLayout.Controls.Add(imageButtons, 0, 2);

        var right = PanelBox();
        var rightLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rightLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 53));
        right.Controls.Add(rightLayout);
        middle.Panel2.Controls.Add(right);
        rightLayout.Controls.Add(new Label {
            Text = "Preview", AutoSize = true,
            Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(36, 47, 62)
        }, 0, 0);
        _preview.Dock = DockStyle.Fill;
        _preview.BackColor = Color.FromArgb(27, 34, 45);
        _preview.SizeMode = PictureBoxSizeMode.Zoom;
        _preview.BorderStyle = BorderStyle.FixedSingle;
        rightLayout.Controls.Add(_preview, 0, 1);
        _previewName.Dock = DockStyle.Fill;
        _previewName.ForeColor = Color.DimGray;
        _previewName.TextAlign = ContentAlignment.MiddleLeft;
        _previewName.AutoEllipsis = true;
        rightLayout.Controls.Add(_previewName, 0, 2);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _summary.Dock = DockStyle.Fill;
        _summary.ForeColor = Color.FromArgb(50, 62, 77);
        _summary.Font = new Font("Segoe UI Semibold", 9.5f);
        footer.Controls.Add(_summary, 0, 0);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true };
        actions.Controls.Add(MakeButton("Companies", OpenCompanyProfiles, 142));
        actions.Controls.Add(MakeButton("New campaign", StartNewCampaign, 163));
        actions.Controls.Add(MakeButton("Save project", SaveProject, 158));
        actions.Controls.Add(MakeButton("Open project", OpenProject, 148));
        actions.Controls.Add(MakeButton("Schedule", OpenScheduleEditor, 128));
        actions.Controls.Add(MakeButton("Validate", ValidateCurrent, 110));
        actions.Controls.Add(MakeButton("Export for GPO", ExportPackage, 165));
        actions.Controls.Add(MakeButton("3  GPO Preview / Deploy", OpenDomainDeployment, 205));
        actions.Controls.Add(MakeButton("Client feedback", OpenFeedbackSettings, 164));
        actions.Controls.Add(MakeButton("Settings", OpenUiSettings, 124));
        actions.Controls.Add(MakeButton("Diagnostics", OpenDiagnostics, 135));
        actions.Controls.Add(MakeButton("Updates", OpenUniversalUpdater, 120));
        actions.Controls.Add(MakeButton("History", OpenCampaignHistory, 118));
        _status.Text = "Ready";
        _status.ForeColor = Color.DimGray;
        _status.AutoSize = true;
        _status.Margin = new Padding(22, 9, 0, 0);
        actions.Controls.Add(_status);
        footer.Controls.Add(actions, 0, 1);
        root.Controls.Add(footer, 0, 3);
    }

    private static Panel PanelBox() => new() {
        Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(1),
        BorderStyle = BorderStyle.FixedSingle, Margin = new Padding(0, 0, 0, 12)
    };

    private static Control WithLabel(string label, Control control, int width)
    {
        var box = new TableLayoutPanel { Width = width, Height = 75, ColumnCount = 1, RowCount = 2, Margin = new Padding(0, 0, 10, 6) };
        box.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        box.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        box.Controls.Add(new Label { Text = label, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(80, 88, 99), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        control.Margin = new Padding(0);
        control.Anchor = AnchorStyles.Left | AnchorStyles.Top;
        box.Controls.Add(control, 0, 1);
        return box;
    }

    private static Button MakeButton(string label, Action action, int width, bool primary = false)
    {
        var button = new Button {
            Text = label, Width = width, Height = 36, FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Blue : Color.White,
            ForeColor = primary ? Color.White : Color.FromArgb(39, 49, 61),
            Margin = new Padding(0, 0, 7, 0), Cursor = Cursors.Hand
        };
        button.FlatAppearance.BorderColor = primary ? Blue : Border;
        button.FlatAppearance.BorderSize = 1;
        button.Click += (_, _) => action();
        return button;
    }

    private void ConfigureGrid()
    {
        _table.Dock = DockStyle.Fill;
        _table.AllowUserToAddRows = false;
        _table.AllowUserToDeleteRows = false;
        _table.AllowUserToResizeRows = false;
        _table.MultiSelect = false;
        _table.ReadOnly = true;
        _table.RowHeadersVisible = false;
        _table.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _table.BackgroundColor = Color.White;
        _table.BorderStyle = BorderStyle.FixedSingle;
        _table.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        _table.RowTemplate.Height = 40;
        _table.EnableHeadersVisualStyles = false;
        _table.ColumnHeadersHeight = 38;
        _table.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(240, 244, 249);
        _table.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(36, 47, 62);
        _table.DefaultCellStyle.SelectionBackColor = Color.FromArgb(222, 236, 251);
        _table.DefaultCellStyle.SelectionForeColor = Color.Black;
        _table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "№", Width = 42, SortMode = DataGridViewColumnSortMode.NotSortable });
        _table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Date / time", Width = 164, SortMode = DataGridViewColumnSortMode.NotSortable });
        _table.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Image", AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.NotSortable });
        _table.SelectionChanged += (_, _) => UpdatePreview();
        _table.CellDoubleClick += (_, _) => ReplaceImage();
    }

    private void OnDataChanged()
    {
        if (_loading) return;
        ReadControls();
        _projectDirty = true;
        UpdateRows();
    }

    private void ReadControls()
    {
        _project.Name = _name.Text.Trim();
        if (_project.ScheduleMode != CampaignScheduleMode.SpecificDates)
            _project.StartLocal = DateTime.SpecifyKind(
                _startDate.Value.Date + _startTime.Value.TimeOfDay,
                DateTimeKind.Unspecified);
        _project.LastImageHours = (int)_lastHours.Value;
        _project.TimeZoneId = _timeZone.Text.Trim();
    }

    private void SetProject(CampaignProject project)
    {
        _loading = true;
        try
        {
            _project = project;
            _name.Text = project.Name;
            _startDate.Value = project.StartLocal.Date;
            _startTime.Value = DateTime.Today.Add(project.StartLocal.TimeOfDay);
            _lastHours.Value = Math.Clamp(project.LastImageHours, 1, 72);
            _timeZone.Text = project.TimeZoneId;
        }
        finally { _loading = false; }
        _startDate.Enabled = _startTime.Enabled =
            project.ScheduleMode != CampaignScheduleMode.SpecificDates;
        _projectDirty = false;
        UpdateCompanyBanner();
        UpdateRows();
    }

    private int SelectedIndex => _table.SelectedRows.Count == 0 ? -1 : _table.SelectedRows[0].Index;

    private void UpdateRows(int selected = -1)
    {
        if (selected < 0) selected = SelectedIndex;
        _table.Rows.Clear();
        for (int i = 0; i < _project.ImagePaths.Count; i++)
        {
            string path = _project.ImagePaths[i];
            string at;
            try { at = CampaignSchedule.At(_project, i).ToString("dd.MM.yyyy  HH:mm"); }
            catch (Exception) { at = "Invalid date"; }
            int index = _table.Rows.Add((i + 1).ToString("D2"),
                at, Path.GetFileName(path));
            _table.Rows[index].Cells[2].ToolTipText = path;
        }
        if (_table.Rows.Count > 0)
        {
            selected = Math.Clamp(selected, 0, _table.Rows.Count - 1);
            _table.Rows[selected].Selected = true;
            _table.CurrentCell = _table.Rows[selected].Cells[0];
        }
        UpdatePreview();
        string end;
        try { end = _project.ImagePaths.Count == 0
            ? "—" : CampaignSchedule.End(_project).ToString("dd.MM.yyyy HH:mm"); }
        catch (Exception) { end = "Invalid schedule"; }
        _summary.Text = $"Images: {_project.ImagePaths.Count}   •   " +
            $"Schedule: {_project.ScheduleMode}   •   " +
            $"Randomize/device: {(_project.RandomizePerDevice ? "Yes" : "No")}   •   " +
            $"Start: {_project.StartLocal:dd.MM.yyyy HH:mm}   →   {end}   •   Restore on completion";
    }

    private void UpdatePreview()
    {
        var old = _preview.Image;
        _preview.Image = null;
        old?.Dispose();
        int idx = SelectedIndex;
        if (idx < 0 || idx >= _project.ImagePaths.Count) { _previewName.Text = "Select an image on the left"; return; }
        string path = _project.ImagePaths[idx];
        _previewName.Text = Path.GetFileName(path) + Environment.NewLine + path;
        if (!File.Exists(path)) { _previewName.Text = "Not found: " + path; return; }
        try
        {
            using var source = Image.FromFile(path);
            _preview.Image = new Bitmap(source);
        }
        catch (Exception ex)
        {
            _previewName.Text = "Image error: " + ex.Message;
            AppLog.Write(ex);
        }
    }

    private void OpenScheduleEditor()
    {
        ReadControls();
        try
        {
            // Schedule first: configure days or interval, then import folder.
            using var editor = new CampaignScheduleDialog(_project);
            if (editor.ShowDialog(this) != DialogResult.OK ||
                editor.ResultProject is not { } edited) return;
            _project = edited;
            _loading = true;
            try
            {
                _startDate.Value = edited.StartLocal.Date;
                _startTime.Value = DateTime.Today.Add(edited.StartLocal.TimeOfDay);
                _lastHours.Value = edited.LastImageHours;
            }
            finally { _loading = false; }
            _startDate.Enabled = _startTime.Enabled =
                edited.ScheduleMode != CampaignScheduleMode.SpecificDates;
            _projectDirty = true;
            UpdateRows();
            _status.Text = "Schedule updated locally. Export to apply through GPO.";
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Campaign Schedule", ex); }
    }

    private void AddImages()
    {
        using var picker = new OpenFileDialog {
            Title = "Add multiple lock screen image files", Multiselect = true,
            Filter = "Images (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp"
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try { AppendImages(picker.FileNames); }
        catch (Exception ex) { UiErrorDialog.Show(this, "Add image files", ex); }
    }

    private void AddImagesFromFolder()
    {
        using var picker = new FolderBrowserDialog {
            Description = "Select a folder containing lock screen JPG, PNG or BMP images.",
            UseDescriptionForTitle = true, ShowNewFolderButton = false
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var files = ImageFilenameMatcher.ScanFolder(picker.SelectedPath);
            AppendImages(files);
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Add image folder", ex); }
    }

    private void AppendImages(IEnumerable<string> files)
    {
        string[] selected = files.ToArray();
        if (selected.Length == 0) return;
        if (_project.ImagePaths.Count + selected.Length > CampaignSchedule.MaxImages)
        {
            ShowError($"Maximum {CampaignSchedule.MaxImages} images per campaign.");
            return;
        }
        var known = new HashSet<string>(_project.ImagePaths,
            StringComparer.OrdinalIgnoreCase);
        if (selected.Any(path =>
                !ImageFilenameMatcher.IsSupportedImage(path) ||
                !File.Exists(path) || !known.Add(Path.GetFullPath(path))))
            throw new InvalidDataException(
                "Selected images contain a missing, unsupported or duplicate file.");
        var unsafeFile = selected.Select(path => new FileInfo(path))
            .FirstOrDefault(file => file.Length == 0 ||
                (file.Attributes & FileAttributes.ReparsePoint) != 0);
        if (unsafeFile is not null)
            throw new InvalidDataException(
                "Empty files and filesystem links cannot be imported.");
        int originalCount = _project.ImagePaths.Count;
        _project.ImagePaths.AddRange(selected);
        if (_project.ScheduleMode == CampaignScheduleMode.SpecificDates)
        {
            _project.SpecificDatesLocal ??= new();
            for (int i = 0; i < selected.Length; i++)
            {
                DateTime next = _project.SpecificDatesLocal.Count > 0
                    ? _project.SpecificDatesLocal[^1].AddDays(1)
                    : _project.StartLocal;
                _project.SpecificDatesLocal.Add(next);
            }
        }
        _projectDirty = true;
        UpdateRows(originalCount);
        _status.Text = $"{selected.Length} image(s) added. Review schedule before deployment.";
    }

    private void ReplaceImage()
    {
        int idx = SelectedIndex;
        if (idx < 0) return;
        using var picker = new OpenFileDialog { Title = "Replace image", Filter = "Images (*.jpg;*.jpeg;*.png;*.bmp)|*.jpg;*.jpeg;*.png;*.bmp" };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        _project.ImagePaths[idx] = picker.FileName;
        _projectDirty = true;
        UpdateRows(idx);
    }

    private void RemoveImage()
    {
        int idx = SelectedIndex;
        if (idx < 0) return;
        _project.ImagePaths.RemoveAt(idx);
        if (_project.ScheduleMode == CampaignScheduleMode.SpecificDates &&
            idx < _project.SpecificDatesLocal.Count)
        {
            _project.SpecificDatesLocal.RemoveAt(idx);
            if (_project.SpecificDatesLocal.Count > 0)
                _project.StartLocal = _project.SpecificDatesLocal[0];
            _loading = true;
            try
            {
                _startDate.Value = _project.StartLocal.Date;
                _startTime.Value = DateTime.Today.Add(_project.StartLocal.TimeOfDay);
            }
            finally { _loading = false; }
        }
        _projectDirty = true;
        UpdateRows(Math.Max(0, idx - 1));
    }

    private void MoveImage(int step)
    {
        int idx = SelectedIndex, target = idx + step;
        if (idx < 0 || target < 0 || target >= _project.ImagePaths.Count) return;
        (_project.ImagePaths[idx], _project.ImagePaths[target]) = (_project.ImagePaths[target], _project.ImagePaths[idx]);
        _projectDirty = true;
        UpdateRows(target);
    }

    private void ValidateCurrent()
    {
        try
        {
            ReadControls();
            CampaignSchedule.Validate(_project, requireImages: true);
            string first = CampaignSchedule.At(_project, 0).ToString("dd.MM.yyyy HH:mm");
            string last = CampaignSchedule.End(_project).ToString("dd.MM.yyyy HH:mm");
            MessageBox.Show(this,
                $"Configuration is valid.\n\nImages: {_project.ImagePaths.Count}\nStart: {first}\nCleanup: {last}\nTime zone: {_project.TimeZoneId}",
                "Campaign validation", MessageBoxButtons.OK, MessageBoxIcon.Information);
            _status.Text = "Configuration validated";
        }
        catch (CampaignValidationException ex) { ShowCampaignValidation(ex.Message); }
        catch (FileNotFoundException) {
            ShowCampaignValidation("A selected image is missing. Replace it or choose an existing file.");
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Campaign editor", ex); }
    }

    private void SaveProject()
    {
        ReadControls();
        using var dialog = new SaveFileDialog { Title = "Save project", Filter = "Campaign project (*.json)|*.json", FileName = CampaignSchedule.BuildId(_project) + ".project.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            File.WriteAllText(dialog.FileName, JsonSerializer.Serialize(_project, CampaignExporter.JsonOptions) + Environment.NewLine, new UTF8Encoding(false));
            _projectDirty = false;
            _status.Text = "Project saved";
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Campaign editor", ex); }
    }

    private void OpenProject()
    {
        using var dialog = new OpenFileDialog { Title = "Open project", Filter = "Campaign project (*.json)|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK || !ConfirmDiscardChanges()) return;
        try
        {
            var model = JsonSerializer.Deserialize<CampaignProject>(File.ReadAllText(dialog.FileName), CampaignExporter.JsonOptions)
                ?? throw new InvalidOperationException("Project is empty.");
            model.ImagePaths ??= new();
            if (model.ImagePaths.Count > CampaignSchedule.MaxImages)
                throw new InvalidOperationException("Too many images in this project.");
            // A saved project belongs to exactly one local company workspace.
            // Unknown/deleted companies are blocked rather than silently
            // taking defaults from a different domain.
            CompanyProfile? newOwner = null;
            if (!string.IsNullOrEmpty(model.CompanyProfileId))
            {
                newOwner = CompanyProfilesStore.All().FirstOrDefault(x =>
                    x.Id.Equals(model.CompanyProfileId, StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException(
                        "The company for this project is missing. Re-create the original profile first.");
            }
            if (!string.Equals(_company?.Id ?? "", newOwner?.Id ?? "",
                    StringComparison.OrdinalIgnoreCase))
            {
                if (MessageBox.Show(this,
                    "The selected project belongs to " + (newOwner?.Name ?? "an unassigned workspace") +
                    ". Switch company before opening? Saved credentials are never transferred.",
                    "Switch company workspace", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
            }
            CompanyProfilesStore.SetActive(newOwner?.Id);
            _company = newOwner;
            SetAdSnapshot(null);
            SetProject(model);
            _status.Text = "Project opened";
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Campaign editor", ex); }
    }

    private void SetAdSnapshot(AdDirectorySnapshot? snapshot)
    {
        _adSnapshot = snapshot;
        _adScope = AdConnectionScope.From(_company);
        if (snapshot is null)
        {
            UiStyle.ApplyStatus(_adConnectionStatus,
                "NOT CONNECTED — select Step 1 before GPO deployment.",
                UiStatusKind.Warning);
            return;
        }
        UiStyle.ApplyStatus(_adConnectionStatus,
            "READ ONLY / CONNECTED: " + snapshot.Domain + " | " + snapshot.Dc +
            $" | {snapshot.OrganizationalUnits.Count} OUs | " +
            $"{snapshot.ComputerGroups.Count} groups | {snapshot.GroupPolicies.Count} GPOs" +
            (snapshot.Capped ? " | PARTIAL INVENTORY" : ""),
            snapshot.Capped ? UiStatusKind.Warning : UiStatusKind.Success);
        _status.Text = "AD discovery complete (read-only). Continue with campaign configuration.";
    }

    private void ConnectAndReadAd()
    {
        if (IsDisposed || Disposing) return;
        // Never retain a previous authorized inventory during a new attempt.
        SetAdSnapshot(null);
        try
        {
            using var dialog = new AdConnectionStartDialog(_company);
            if (dialog.ShowDialog(this) == DialogResult.OK && dialog.Snapshot is { } incoming)
            {
                SetAdSnapshot(incoming);
            }
            else
            {
                // A cancelled/rejected connection never upgrades an offline session.
                SetAdSnapshot(null);
                _status.Text = dialog.WorkOfflineChosen
                    ? "Working offline. GPO deployment requires a fresh AD connection."
                    : "AD connection was not completed. No directory permissions inferred.";
            }
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Connect and read AD", ex); }
    }

    private void OpenDomainDeployment()
    {
        if (!_adScope.Matches(_adSnapshot, _company, DateTimeOffset.UtcNow))
        {
            ConnectAndReadAd();
            if (!_adScope.Matches(_adSnapshot, _company, DateTimeOffset.UtcNow)) return;
        }
        ReadControls();
        try
        {
            CampaignSchedule.Validate(_project, requireImages: true);
            // Copy the editing model: the AD wizard never changes image order or dates.
            var snapshot = new CampaignProject {
                Name = _project.Name, CompanyProfileId = _project.CompanyProfileId,
                StartLocal = _project.StartLocal,
                LastImageHours = _project.LastImageHours, TimeZoneId = _project.TimeZoneId,
                EnableFeedback = _project.EnableFeedback, FeedbackShare = _project.FeedbackShare,
                FeedbackIntervalMinutes = _project.FeedbackIntervalMinutes,
                ImagePaths = new List<string>(_project.ImagePaths),
                ScheduleMode = _project.ScheduleMode,
                WeekdayMask = _project.WeekdayMask,
                SpecificDatesLocal = new List<DateTime>(_project.SpecificDatesLocal),
                RandomizePerDevice = _project.RandomizePerDevice,
                IntervalMinutes = _project.IntervalMinutes
            };
            using var wizard = new DomainDeploymentForm(snapshot, _company, _adSnapshot);
            wizard.ShowDialog(this);
        }
        catch (CampaignValidationException ex) { ShowCampaignValidation(ex.Message); }
        catch (FileNotFoundException) {
            ShowCampaignValidation("A selected image is missing. Replace it or choose an existing file.");
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Campaign editor", ex); }
    }

    private void ExportPackage()
    {
        ReadControls();
        try
        {
            CampaignSchedule.Validate(_project, requireImages: true);
            var now = TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById(_project.TimeZoneId));
            if (now >= CampaignSchedule.End(_project))
            {
                ShowError("The campaign has already ended. Select a future start date.");
                return;
            }
            using var dialog = new FolderBrowserDialog { Description = "Where should the campaign package be exported?", UseDescriptionForTitle = true };
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            Cursor = Cursors.WaitCursor;
            string path;
            try { path = CampaignExporter.Export(_project, dialog.SelectedPath); }
            finally { Cursor = Cursors.Default; }
            _status.Text = "Export complete";
            if (MessageBox.Show(this,
                "Ready. GPO deployment package created:\n" + path + "\n\nOpen folder?",
                "Export completed", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
        }
        catch (CampaignValidationException ex) { Cursor = Cursors.Default; ShowCampaignValidation(ex.Message); }
        catch (FileNotFoundException) {
            Cursor = Cursors.Default;
            ShowCampaignValidation("A selected image is missing. Replace it or choose an existing file.");
        }
        catch (Exception ex) { Cursor = Cursors.Default; UiErrorDialog.Show(this, "Export campaign", ex); }
    }

    private void OpenFeedbackSettings()
    {
        ReadControls();
        using var dialog = new FeedbackSettingsDialog(_project);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _projectDirty = true;
            _status.Text = _project.EnableFeedback ? "Client feedback enabled" : "Client feedback disabled";
        }
    }

    private bool ConfirmDiscardChanges()
    {
        return !_projectDirty || MessageBox.Show(this,
            "The current campaign has unsaved changes." + Environment.NewLine +
            "Discard those changes and switch to a different campaign/company?",
            "Unsaved campaign", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) == DialogResult.Yes;
    }

    private void UpdateCompanyBanner()
    {
        // Never carry a trusted directory snapshot across company identities.
        // Company profiles only prefill a domain and are not an authorization.
        if (_adSnapshot is not null &&
            !_adScope.Matches(_adSnapshot, _company, DateTimeOffset.UtcNow))
            SetAdSnapshot(null);
        _companyLabel.Text = _company is null
            ? "ACTIVE COMPANY: Unassigned — local campaign"
            : "ACTIVE COMPANY: " + _company.Name + "   |   AD: " + _company.Domain +
              "   |   Workspace only (no GPO changes)";
        _companyLabel.AutoEllipsis = true;
    }

    private void StartNewCampaign()
    {
        if (!ConfirmDiscardChanges()) return;
        SetProject(CompanyProfilesStore.NewCampaign(_company));
        _status.Text = _company is null
            ? "New unassigned campaign"
            : "New campaign for " + _company.Name;
    }

    private void OpenCompanyProfiles()
    {
        try
        {
            using var dialog = new CompanyProfilesDialog(_company?.Id);
            if (dialog.ShowDialog(this) != DialogResult.OK)
            {
                // The active profile may have been edited without switching.
                if (_company is not null)
                {
                    _company = CompanyProfilesStore.All().FirstOrDefault(x =>
                        x.Id.Equals(_company.Id, StringComparison.OrdinalIgnoreCase));
                    UpdateCompanyBanner();
                }
                return;
            }
            if (dialog.UnassignedChosen)
            {
                if (_company is null || !ConfirmDiscardChanges()) return;
                CompanyProfilesStore.SetActive(null);
                _company = null;
                SetProject(CompanyProfilesStore.NewCampaign(null));
                _status.Text = "Switched to unassigned workspace. No AD scope was reused.";
                return;
            }
            if (dialog.ChosenCompany is not { } selected) return;
            if (string.Equals(_company?.Id, selected.Id, StringComparison.OrdinalIgnoreCase))
            {
                _company = selected;
                UpdateCompanyBanner();
                _status.Text = "Company profile settings updated. Existing campaign unchanged.";
                return;
            }
            if (!ConfirmDiscardChanges()) return;
            CompanyProfilesStore.SetActive(selected.Id);
            _company = selected;
            SetProject(CompanyProfilesStore.NewCampaign(selected));
            _status.Text = "Selected company " + selected.Name +
                " — fresh campaign defaults loaded; AD not connected.";
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Company profiles", ex); }
    }

    private void OpenUiSettings()
    {
        using var form = new UiSettingsDialog();
        form.ShowDialog(this);
    }

    private void OpenCampaignHistory()
    {
        using var form = new CampaignHistoryForm();
        form.ShowDialog(this);
    }

    private void OpenUniversalUpdater()
    {
        using var form = new UniversalUpdaterWindow();
        form.ShowDialog(this);
    }

    private void OpenDiagnostics()
    {
        using var form = new DiagnosticsWindow();
        form.ShowDialog(this);
    }

    // An incomplete campaign is an expected user action, not a runtime error:
    // never log it as UniversalDiagnostics and never show a GitHub Issue prompt.
    private void ShowCampaignValidation(string message)
    {
        _status.Text = "Campaign needs attention: " + message;
        if (string.Equals(message, "Add at least one image.", StringComparison.Ordinal))
        {
            if (MessageBox.Show(this,
                    "This campaign does not contain any images.\n\nChoose images now?",
                    "Campaign needs images", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information, MessageBoxDefaultButton.Button1) == DialogResult.Yes)
            {
                AddImages();
            }
            return;
        }
        MessageBox.Show(this, message, "Check campaign settings",
            MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private void ShowError(string message)
    {
        _status.Text = "Error: see details";
        MessageBox.Show(this, message, "LockScreen Campaign Manager", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void RestoreWindow()
    {
        try
        {
            var path = Path.Combine(AppLog.DirectoryPath, "window.json");
            if (!File.Exists(path)) return;
            var state = JsonSerializer.Deserialize<SavedWindowState>(File.ReadAllText(path));
            if (state is null || state.Width < MinimumSize.Width || state.Height < MinimumSize.Height) return;
            var bounds = new Rectangle(state.Left, state.Top, state.Width, state.Height);
            if (!Screen.AllScreens.Any(s => s.WorkingArea.IntersectsWith(bounds))) return;
            StartPosition = FormStartPosition.Manual;
            Bounds = bounds;
            if (state.Maximized) this.WindowState = FormWindowState.Maximized;
        }
        catch (Exception ex) { AppLog.Write(ex); }
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (e.CloseReason == CloseReason.UserClosing && _projectDirty &&
            MessageBox.Show(this,
                "The current campaign has unsaved changes. Close anyway?",
                "Unsaved campaign", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
        {
            e.Cancel = true;
            return;
        }
        try
        {
            Directory.CreateDirectory(AppLog.DirectoryPath);
            var bounds = this.WindowState == FormWindowState.Normal ? Bounds : RestoreBounds;
            var state = new SavedWindowState {
                Left = bounds.Left, Top = bounds.Top, Width = bounds.Width, Height = bounds.Height,
                Maximized = this.WindowState == FormWindowState.Maximized
            };
            File.WriteAllText(Path.Combine(AppLog.DirectoryPath, "window.json"), JsonSerializer.Serialize(state));
        }
        catch (Exception ex) { AppLog.Write(ex); }
        _preview.Image?.Dispose();
        base.OnFormClosing(e);
    }

    private sealed class SavedWindowState
    {
        public int Left { get; set; }
        public int Top { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public bool Maximized { get; set; }
    }
}
