using System.Windows.Forms;
using System.Diagnostics;
using System.Drawing;
using System.Text;
using System.Text.Json;

namespace LockScreenCampaignManager;

public sealed partial class MainForm : Form
{
    private CampaignProject _project = new();
    private CompanyProfile? _company;
    private bool _projectDirty;
    private readonly Label _companyLabel = new();
    private readonly Label _adConnectionStatus = new();
    private readonly ToolTip _connectionTooltip = new() { AutoPopDelay = 14000, InitialDelay = 300, ReshowDelay = 100 };
    private readonly Button _adConnectButton = new();
    private AdDirectorySnapshot? _adSnapshot;
    private DomainCredentials? _sessionAdCredentials;
    private AdConnectionScope _adScope = AdConnectionScope.From(null);
    private readonly TextBox _name = new();
    private readonly DateTimePicker _startDate = new();
    private readonly DateTimePicker _startTime = new();
    private readonly NumericUpDown _lastHours = new();
    private readonly ComboBox _timeZone = new();
    private readonly DataGridView _table = new();
    private readonly PictureBox _preview = new();
    private readonly Label _previewName = new();
    private readonly Label _summary = new();
    private SplitContainer? _imagePreviewSplit;
    private int _savedSplitPercent = 61;
    private readonly Label _status = new();
    private readonly Label _workflowHint = new();
    private readonly TableLayoutPanel _workflowButtons = new();
    private readonly FlowLayoutPanel _advancedButtons = new();
    private readonly FlowLayoutPanel _quickButtons = new();
    private readonly Button _imageScheduleButton = new();
    private Button? _arrangeImagesButton;
    private bool _startupWizardShown;
    private readonly Button _wizardMode = new();
    private readonly Button _advancedModeButton = new();
    private readonly Button _workflowNext = new();
    private readonly Button _workflowBack = new();
    private readonly Button _workflowAction = new();
    private TableLayoutPanel? _footerLayout;
    private TableLayoutPanel? _workflowBar;
    private int _workflowStep;
    private bool _advancedMode;

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
        MinimumSize = new Size(1050, 720);
        Size = new Size(1430, 910);
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
        SetWorkflowMode(false);
        // Start guided onboarding after the main form is visible. Cancel keeps
        // the local editor open; no automatic AD authentication is attempted.
        Shown += (_, _) => {
            if (_startupWizardShown) return;
            _startupWizardShown = true;
            BeginInvoke(new Action(ShowStartupWizardChoice));
        };
    }

    private void ConfigureLayout()
    {
        // An outer scrolling viewport is essential for laptop displays,
        // Remote Desktop and high-DPI sessions: a section must never disappear
        // when minimum sensible content height exceeds the monitor viewport.
        var viewport = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.None, ColumnCount = 1, RowCount = 5,
            Padding = new Padding(18, 12, 18, 12), BackColor = Bg,
            Location = Point.Empty
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 171));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 98));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 106));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        viewport.Controls.Add(root);
        Controls.Add(viewport);
        void FitContentToViewport()
        {
            if (viewport.IsDisposed || root.IsDisposed) return;
            var visible = viewport.ClientSize;
            // Avoid forcing a tall virtual canvas on normal laptop windows:
            // this used to push the entire wizard footer below the viewport.
            var required = new Size(Math.Max(990, visible.Width),
                Math.Max(790, visible.Height));
            if (root.Size != required) root.Size = required;
        }
        viewport.ClientSizeChanged += (_, _) => FitContentToViewport();
        Shown += (_, _) => FitContentToViewport();
        FitContentToViewport();

        var header = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 31));
        header.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        header.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        header.Controls.Add(new Label {
            Text = "LockScreen Campaign Manager", AutoSize = false,
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = false, MinimumSize = new Size(530, 52),
            Font = new Font("Segoe UI Semibold", 19f, FontStyle.Bold), ForeColor = UiStyle.Navy
        }, 0, 0);
        header.Controls.Add(new Label {
            Text = "1 AD sign-in / Skip    2 Images    3 Arrange    4 Schedule    5 Save    6 Deploy",
            AutoSize = false, Dock = DockStyle.Fill, AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiStyle.Muted,
            Margin = new Padding(3, 0, 0, 0)
        }, 0, 1);
        _companyLabel.Dock = DockStyle.Fill;
        _companyLabel.ForeColor = UiStyle.Accent;
        _companyLabel.Font = new Font("Segoe UI Semibold", 9.5f);
        _companyLabel.AutoEllipsis = true;
        _companyLabel.TextAlign = ContentAlignment.MiddleLeft;
        _companyLabel.Margin = new Padding(3, 0, 0, 0);
        _companyLabel.Text = "LOCAL PROJECT — No organization selected; no AD privileges required";
        header.Controls.Add(_companyLabel, 0, 2);
        var adFirstStep = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1,
            BackColor = UiStyle.SoftBlue, Padding = new Padding(7, 5, 7, 5)
        };
        adFirstStep.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
        adFirstStep.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        adFirstStep.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 121));
        adFirstStep.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 147));
        _adConnectButton.Text = "Connect to AD...";
        _adConnectButton.MinimumSize = Size.Empty;
        _adConnectButton.Dock = DockStyle.Fill;
        _adConnectButton.Margin = new Padding(6, 1, 14, 1);
        _adConnectButton.BackColor = UiStyle.Surface;
        _adConnectButton.ForeColor = UiStyle.Navy;
        _adConnectButton.Tag = "NoIcon";
        _adConnectButton.Click += (_, _) => ConnectAndReadAd();
        adFirstStep.Controls.Add(_adConnectButton, 0, 0);
        _adConnectionStatus.AutoSize = false;
        _adConnectionStatus.Dock = DockStyle.Fill;
        _adConnectionStatus.MinimumSize = Size.Empty;
        _adConnectionStatus.TextAlign = ContentAlignment.MiddleLeft;
        _adConnectionStatus.Margin = new Padding(9, 1, 14, 1);
        _adConnectionStatus.AutoEllipsis = true;
        adFirstStep.Controls.Add(_adConnectionStatus, 1, 0);
        _wizardMode.Text = "Wizard";
        _wizardMode.Width = 95;
        _wizardMode.Height = 35;
        _wizardMode.Click += (_, _) => { _workflowStep = NextWizardPreparationStep(); SetWorkflowMode(false); };
        _advancedModeButton.Text = "Advanced";
        _advancedModeButton.Width = 110;
        _advancedModeButton.Height = 35;
        _advancedModeButton.Click += (_, _) => SetWorkflowMode(true);
        _wizardMode.Dock = DockStyle.Fill;
        _wizardMode.Tag = "NoIcon";
        _wizardMode.AutoEllipsis = true;
        _wizardMode.Margin = new Padding(5, 1, 5, 1);
        _advancedModeButton.Dock = DockStyle.Fill;
        _advancedModeButton.Tag = "NoIcon";
        _advancedModeButton.AutoEllipsis = true;
        _advancedModeButton.Margin = new Padding(5, 1, 3, 1);
        adFirstStep.Controls.Add(_wizardMode, 2, 0);
        adFirstStep.Controls.Add(_advancedModeButton, 3, 0);
        UiStyle.ApplyStatus(_adConnectionStatus,
            "Offline — AD not connected", UiStatusKind.Neutral);
        _connectionTooltip.SetToolTip(_adConnectionStatus,
            "Active Directory is optional while creating and saving a campaign.");
        header.Controls.Add(adFirstStep, 0, 3);
        root.Controls.Add(header, 0, 0);

        var settings = PanelBox();
        var settingsFlow = new FlowLayoutPanel {
            Dock = DockStyle.Fill, AutoScroll = true, FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true, Padding = new Padding(12, 9, 0, 7)
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
        _imagePreviewSplit = middle;
        Shown += (_, _) => {
            if (middle.Width < 600) return;
            int available = middle.Width - middle.SplitterWidth;
            int minLeft = Math.Min(350, Math.Max(150, available / 3));
            int minRight = Math.Min(260, Math.Max(150, available / 3));
            middle.Panel1MinSize = minLeft;
            middle.Panel2MinSize = minRight;
            int proposed = (int)Math.Round(available * Math.Clamp(_savedSplitPercent, 30, 80) / 100.0);
            middle.SplitterDistance = Math.Clamp(proposed, minLeft, available - minRight);
        };
        middle.SplitterMoved += (_, _) => {
            int available = middle.Width - middle.SplitterWidth;
            if (available > 0) _savedSplitPercent = Math.Clamp(
                (int)Math.Round(100.0 * middle.SplitterDistance / available), 30, 80);
        };
        root.Controls.Add(middle, 0, 3);
        var left = PanelBox();
        var leftLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(12) };
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        leftLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 118));
        left.Controls.Add(leftLayout);
        middle.Panel1.Controls.Add(left);
        leftLayout.Controls.Add(new Label {
            Text = "Images and schedule", AutoSize = true,
            Font = new Font("Segoe UI Semibold", 11f, FontStyle.Bold), ForeColor = Color.FromArgb(36, 47, 62)
        }, 0, 0);
        ConfigureGrid();
        leftLayout.Controls.Add(_table, 0, 1);
        // Fixed 2x2 toolbar: no wrapped-offscreen buttons at 125-200% DPI.
        var imageButtons = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
            Padding = new Padding(1, 3, 1, 3)
        };
        imageButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        imageButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        imageButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        imageButtons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        var addFiles = MakeButton("1  Add files...", AddImages, 145, primary: true);
        var addFolder = MakeButton("Add folder...", AddImagesFromFolder, 145);
        _imageScheduleButton.Text = "3  Schedule...";
        _imageScheduleButton.Click += (_, _) => OpenScheduleEditor();
        foreach (var button in new[] { addFiles, addFolder, _imageScheduleButton })
        {
            button.Tag = "NoIcon";
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(3);
            button.AutoEllipsis = true;
        }
        imageButtons.Controls.Add(addFiles, 0, 0);
        imageButtons.Controls.Add(addFolder, 1, 0);
        _arrangeImagesButton = AddActionMenu(imageButtons, "2  Arrange / edit", new (string Label, Action Execute, Func<bool>? Allowed)[] {
            ("Replace selected image...", ReplaceImage, () => SelectedIndex >= 0),
            ("Remove selected image", RemoveImage, () => SelectedIndex >= 0),
            ("Move image up", () => MoveImage(-1), () => SelectedIndex > 0),
            ("Move image down", () => MoveImage(+1),
                () => SelectedIndex >= 0 && SelectedIndex < _project.ImagePaths.Count - 1),
            ("Sort images A-Z...", SortImagesByFilename, () => _project.ImagePaths.Count > 1),
            ("Auto match dates / weekdays...", AutoMatchImageNames,
                () => _project.ImagePaths.Count > 0)
        });
        _arrangeImagesButton.Dock = DockStyle.Fill;
        _arrangeImagesButton.Margin = new Padding(3);
        _arrangeImagesButton.AutoEllipsis = true;
        imageButtons.SetCellPosition(_arrangeImagesButton,
            new TableLayoutPanelCellPosition(0, 1));
        imageButtons.Controls.Add(_imageScheduleButton, 1, 1);
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

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
        _footerLayout = footer;
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _summary.Dock = DockStyle.Fill;
        _summary.ForeColor = UiStyle.Foreground;
        _summary.Font = new Font("Segoe UI Semibold", 9.5f);
        footer.Controls.Add(_summary, 0, 0);

        var flowBar = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Padding = new Padding(14, 8, 14, 8), BackColor = UiStyle.SoftBlue,
            Margin = new Padding(0, 3, 0, 8)
        };
        flowBar.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
        flowBar.RowStyles.Add(new RowStyle(SizeType.Percent, 52));
        _workflowBar = flowBar;
        flowBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _workflowHint.Dock = DockStyle.Fill;
        _workflowHint.TextAlign = ContentAlignment.MiddleLeft;
        _workflowHint.AutoEllipsis = true;
        _workflowHint.ForeColor = UiStyle.Navy;
        _workflowHint.Font = new Font("Segoe UI Semibold", 11f);
        _workflowHint.Padding = new Padding(7, 0, 0, 0);
        flowBar.Controls.Add(_workflowHint, 0, 0);
        _workflowButtons.Dock = DockStyle.Fill;
        _workflowButtons.ColumnCount = 3;
        _workflowButtons.RowCount = 1;
        _workflowButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 114));
        _workflowButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 152));
        _workflowButtons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _workflowNext.Text = "Next";
        _workflowNext.Width = 83;
        _workflowNext.Height = 36;
        _workflowNext.Click += (_, _) => AdvanceWorkflow();
        _workflowBack.Text = "Back";
        _workflowBack.Width = 75;
        _workflowBack.Height = 36;
        _workflowBack.Click += (_, _) => { _workflowStep = Math.Max(0, _workflowStep - 1); UpdateWorkflow(); };
        _workflowAction.Width = 215;
        _workflowAction.Height = 36;
        _workflowAction.Click += (_, _) => RunWorkflowAction();
        foreach (var button in new[] { _workflowBack, _workflowNext, _workflowAction })
        {
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(2);
            button.Tag = "NoIcon";
        }
        _workflowButtons.Controls.Add(_workflowBack, 0, 0);
        _workflowButtons.Controls.Add(_workflowNext, 1, 0);
        _workflowButtons.Controls.Add(_workflowAction, 2, 0);
        flowBar.Controls.Add(_workflowButtons, 0, 1);
        root.Controls.Add(flowBar, 0, 2);
        _advancedButtons.Dock = DockStyle.Fill;
        _advancedButtons.WrapContents = true;
        _advancedButtons.Padding = new Padding(4, 8, 4, 0);
        _advancedButtons.AutoScroll = true;
        var actions = _advancedButtons;
        AddActionMenu(actions, "Project", new (string Label, Action Execute, Func<bool>? Allowed)[] {
            ("New campaign...", StartNewCampaign, null),
            ("Open project...", OpenProject, null),
            ("Save project...", SaveProject, null),
            ("Companies...", OpenCompanyProfiles, null),
            ("History...", OpenCampaignHistory, null)
        });
        AddActionMenu(actions, "Images / schedule", new (string Label, Action Execute, Func<bool>? Allowed)[] {
            ("Add files...", AddImages, null),
            ("Add folder...", AddImagesFromFolder, null),
            ("Edit schedule...", OpenScheduleEditor, () => _project.ImagePaths.Count > 0),
            ("Replace selected...", ReplaceImage, () => SelectedIndex >= 0),
            ("Remove selected", RemoveImage, () => SelectedIndex >= 0),
            ("Move up", () => MoveImage(-1), () => SelectedIndex > 0),
            ("Move down", () => MoveImage(+1), () => SelectedIndex >= 0 && SelectedIndex < _project.ImagePaths.Count - 1),
            ("Sort by filename...", SortImagesByFilename, () => _project.ImagePaths.Count > 1),
            ("Auto match filenames...", AutoMatchImageNames, () => _project.ImagePaths.Count > 0)
        });
        AddActionMenu(actions, "Review / export", new (string Label, Action Execute, Func<bool>? Allowed)[] {
            ("Validate campaign...", ValidateCurrent, () => _project.ImagePaths.Count > 0),
            ("Export package...", ExportPackage, () => _project.ImagePaths.Count > 0)
        });
        AddActionMenu(actions, "AD / GPO", new (string Label, Action Execute, Func<bool>? Allowed)[] {
            ("Connect AD account...", ConnectAndReadAd, null),
            ("Deploy GPO...", () => OpenDomainDeployment(true), () => AdReady && _project.ImagePaths.Count > 0),
            ("Client feedback...", OpenFeedbackSettings, null)
        });
        AddActionMenu(actions, "Tools", new (string Label, Action Execute, Func<bool>? Allowed)[] {
            ("Settings...", OpenUiSettings, null),
            ("Diagnostics...", OpenDiagnostics, null),
            ("Check updates...", OpenUniversalUpdater, null)
        });
        _status.Text = "Ready — add images or open a project";
        _status.ForeColor = UiStyle.Muted;
        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.AutoEllipsis = true;
        _status.Margin = new Padding(8, 5, 0, 0);
        _quickButtons.Dock = DockStyle.Fill;
        _quickButtons.Padding = new Padding(4, 8, 4, 0);
        _quickButtons.AutoScroll = true;
        _quickButtons.WrapContents = true;
        AddActionMenu(_quickButtons, "Project", new (string Label, Action Execute, Func<bool>? Allowed)[] {
            ("New campaign...", StartNewCampaign, null),
            ("Open project...", OpenProject, null),
            ("Save project...", SaveProject, null),
            ("Companies...", OpenCompanyProfiles, null)
        });
        AddActionMenu(_quickButtons, "Help / tools", new (string Label, Action Execute, Func<bool>? Allowed)[] {
            ("Settings...", OpenUiSettings, null),
            ("Diagnostics...", OpenDiagnostics, null),
            ("Check updates...", OpenUniversalUpdater, null),
            ("History...", OpenCampaignHistory, null)
        });
        var toolsHost = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        toolsHost.Controls.Add(actions);
        toolsHost.Controls.Add(_quickButtons);
        footer.Controls.Add(_status, 0, 1);
        footer.Controls.Add(toolsHost, 0, 2);
        root.Controls.Add(footer, 0, 4);
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

    private static Button AddActionMenu(Control host, string title,
        (string Label, Action Execute, Func<bool>? Allowed)[] entries)
    {
        var button = new Button {
            Text = title + "  ▾", Width = Math.Max(148, title.Length * 9 + 45),
            Height = 38, Margin = new Padding(0, 0, 9, 5), Cursor = Cursors.Hand,
            Tag = "NoIcon"
        };
        var menu = new ContextMenuStrip { ShowImageMargin = false };
        foreach (var entry in entries)
        {
            var item = new ToolStripMenuItem(entry.Label);
            item.Click += (_, _) => entry.Execute();
            menu.Items.Add(item);
        }
        menu.Opening += (_, _) => {
            for (int i = 0; i < entries.Length; i++)
                menu.Items[i].Enabled = entries[i].Allowed?.Invoke() ?? true;
        };
        button.Click += (_, _) => menu.Show(button, 0, button.Height);
        button.ContextMenuStrip = menu;
        host.Controls.Add(button);
        return button;
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
        _table.SelectionChanged += (_, _) => { UpdatePreview(); UpdateWorkflow(); };
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
        _wizardScheduleConfigured = false;
        _workflowStep = 0;
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
        UpdateWorkflow();
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
            // Edit the schedule after choosing images; preserve the selected order.
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
            WizardScheduleSaved();
            if (_advancedMode)
                _status.Text = "Schedule saved locally. Validate and save the project when ready.";
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
            int before = _project.ImagePaths.Count;
            AppendImages(files);
            if (_project.ImagePaths.Count > before &&
                MessageBox.Show(this,
                    "Images imported. Arrange their order now before setting the schedule?",
                    "Next: arrange images", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information) == DialogResult.Yes)
                _arrangeImagesButton?.PerformClick();
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
        _status.Text = $"{selected.Length} image(s) imported. Next: Arrange / edit, then Schedule.";
        WizardImagesImported();
        UpdateWorkflow();
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

    private void SaveProject() => TrySaveProject();

    private bool TrySaveProject()
    {
        ReadControls();
        using var dialog = new SaveFileDialog {
            Title = "Save campaign project", Filter = "Campaign project (*.json)|*.json",
            FileName = CampaignSchedule.BuildId(_project) + ".project.json"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return false;
        try
        {
            File.WriteAllText(dialog.FileName,
                JsonSerializer.Serialize(_project, CampaignExporter.JsonOptions) +
                Environment.NewLine, new UTF8Encoding(false));
            _projectDirty = false;
            if (!_advancedMode && _workflowStep == 4) _workflowStep = 5;
            _status.Text = "Project saved. You may deploy through AD or finish offline.";
            UpdateWorkflow();
            return true;
        }
        catch (Exception ex)
        {
            UiErrorDialog.Show(this, "Save campaign project", ex);
            return false;
        }
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
        if (snapshot is null) _sessionAdCredentials = null;
        _adScope = AdConnectionScope.From(_company);
        if (snapshot is null)
        {
            UiStyle.ApplyStatus(_adConnectionStatus,
                "Offline — AD not connected", UiStatusKind.Neutral);
            _connectionTooltip.SetToolTip(_adConnectionStatus,
                "No AD session. You can arrange images, schedule and save locally.");
            UpdateWorkflow();
            return;
        }
        UiStyle.ApplyStatus(_adConnectionStatus,
            (snapshot.Capped ? "AD partial: " : "AD connected: ") + snapshot.Domain,
            snapshot.Capped ? UiStatusKind.Warning : UiStatusKind.Success);
        _connectionTooltip.SetToolTip(_adConnectionStatus,
            "Read-only AD inventory" + Environment.NewLine +
            "Domain: " + snapshot.Domain + Environment.NewLine +
            "DC: " + snapshot.Dc + Environment.NewLine +
            $"OUs: {snapshot.OrganizationalUnits.Count}; Groups: {snapshot.ComputerGroups.Count}; " +
            $"GPOs: {snapshot.GroupPolicies.Count}" +
            (snapshot.Capped ? Environment.NewLine + "Inventory is partial" : ""));
        _status.Text = "AD discovery complete (read-only). Continue with campaign configuration.";
        UpdateWorkflow();
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
                _sessionAdCredentials = dialog.ConnectedCredentials;
                if (!_advancedMode && _workflowStep == 0)
                    _workflowStep = NextWizardPreparationStep();
                _status.Text = "AD read complete. GPO write permissions must be checked before deployment.";
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

    private void OpenDomainDeployment(bool advanced = false)
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
            using var wizard = new DomainDeploymentForm(snapshot, _company, _adSnapshot,
                advanced, _sessionAdCredentials);
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
        // Creating a campaign is a deliberate action, not an invisible reset.
        // Show a named, editable draft before discarding the existing project.
        try
        {
            var draft = CompanyProfilesStore.NewCampaign(_company);
            using var dialog = new Form
            {
                Text = "Create new campaign",
                StartPosition = FormStartPosition.CenterParent,
                ShowInTaskbar = false,
                AutoScaleMode = AutoScaleMode.Dpi,
                Font = new Font("Segoe UI", 10f),
                MinimumSize = new Size(510, 345),
                Size = new Size(600, 375),
                BackColor = UiStyle.Canvas
            };
            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7,
                Padding = new Padding(22, 16, 22, 14)
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(new Label
            {
                Text = "New lock screen campaign",
                Dock = DockStyle.Fill,
                Font = new Font("Segoe UI Semibold", 15f, FontStyle.Bold),
                ForeColor = UiStyle.Navy
            }, 0, 0);
            layout.Controls.Add(new Label
            {
                Text = _company is null
                    ? "Company: Unassigned (local campaign)"
                    : "Company: " + _company.Name + "  |  Domain: " + _company.Domain,
                Dock = DockStyle.Fill, AutoEllipsis = true,
                ForeColor = UiStyle.Muted
            }, 0, 1);
            layout.Controls.Add(new Label { Text = "Campaign name", Dock = DockStyle.Fill }, 0, 2);
            var nameBox = new TextBox { Dock = DockStyle.Fill, MaxLength = 100,
                Text = draft.Name };
            layout.Controls.Add(nameBox, 0, 3);
            layout.Controls.Add(new Label { Text = "Start date and time", Dock = DockStyle.Fill }, 0, 4);
            var start = new DateTimePicker
            {
                Dock = DockStyle.Left, Width = 260, Format = DateTimePickerFormat.Custom,
                CustomFormat = "dd.MM.yyyy HH:mm", ShowUpDown = true,
                Value = draft.StartLocal
            };
            layout.Controls.Add(start, 0, 5);
            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false, Padding = new Padding(0, 10, 0, 0)
            };
            var create = new Button { Text = "Create campaign", Width = 155,
                Height = 38, BackColor = UiStyle.Accent, ForeColor = Color.White };
            var cancel = new Button { Text = "Cancel", Width = 110,
                Height = 38, DialogResult = DialogResult.Cancel };
            actions.Controls.Add(create);
            actions.Controls.Add(cancel);
            layout.Controls.Add(actions, 0, 6);
            dialog.Controls.Add(layout);
            dialog.AcceptButton = create;
            dialog.CancelButton = cancel;
            create.Click += (_, _) =>
            {
                if (string.IsNullOrWhiteSpace(nameBox.Text))
                {
                    MessageBox.Show(dialog, "Enter a campaign name.",
                        "New campaign", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    nameBox.Focus();
                    return;
                }
                dialog.DialogResult = DialogResult.OK;
                dialog.Close();
            };
            UiStyle.Apply(dialog);
            if (dialog.ShowDialog(this) != DialogResult.OK) return;
            if (!ConfirmDiscardChanges()) return;
            draft.Name = nameBox.Text.Trim();
            draft.StartLocal = DateTime.SpecifyKind(start.Value, DateTimeKind.Unspecified);
            CampaignSchedule.Validate(draft, requireImages: false);
            SetProject(draft);
            _status.Text = "Created campaign: " + draft.Name + " (not saved yet)";
            _name.Focus();
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "New campaign", ex); }
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
            var screens = Screen.AllScreens.Select(s => s.WorkingArea).ToArray();
            if (screens.Length == 0) return;
            var safeBounds = UiWindowPlacement.ClampToScreens(bounds, MinimumSize, screens);
            StartPosition = FormStartPosition.Manual;
            Bounds = safeBounds;
            _savedSplitPercent = Math.Clamp(state.ImagePreviewSplitPercent, 30, 80);
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
                Maximized = this.WindowState == FormWindowState.Maximized,
                ImagePreviewSplitPercent = _savedSplitPercent
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
        public int ImagePreviewSplitPercent { get; set; } = 61;
    }
}
