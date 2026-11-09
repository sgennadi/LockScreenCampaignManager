using System.Drawing;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

internal sealed class DomainDeploymentForm : Form
{
    private readonly CampaignProject _project;
    private readonly CompanyProfile? _company;
    private readonly TextBox _domainBox = new();
    private readonly ComboBox _dcBox = new();
    private readonly TextBox _userBox = new();
    private readonly TextBox _passwordBox = new();
    private readonly Label _otherAccountHint = new();
    private readonly TextBox _gpoBox = new();
    private readonly RadioButton _currentRadio = new();
    private readonly RadioButton _alternateRadio = new();
    private readonly TextBox _ouFilter = new();
    private readonly TextBox _groupFilter = new();
    private readonly ListBox _ous = new();
    private readonly ListBox _groups = new();
    private readonly TextBox _details = new();
    private readonly Button _connectButton = new();
    private Button _planButton = new();
    private Button _deployButton = new();
    private Button _rightsButton = new();
    private string _rightsStatus = "NOT_CHECKED";
    private readonly Button _usersButton = new();
    private readonly Label _connectionStatus = new();
    private readonly Label _rightsBanner = new();
    private readonly Label _progress = new();
    private readonly ToolTip _directoryTooltip = new() { AutoPopDelay = 12000, InitialDelay = 450, ReshowDelay = 150 };
    private SplitContainer? _targetSplit;
    private TableLayoutPanel? _deploymentLayout;
    private Control? _connectionPanel;
    private readonly List<DirectoryRow> _allOus = new();
    private readonly List<DirectoryRow> _allGroups = new();
    private string _connectedDomain = string.Empty;
    private string _connectedDc = string.Empty;
    private string? _lastPlanKey;
    private bool _running;
    private bool _connectedWritable;
    private int _lastConflictHigh;
    private int _lastConflictWarnings;
    private bool _suppressConnectionChanged;
    private readonly Color _accent = UiStyle.Accent;
    private readonly bool _advancedInitially;
    private bool _advancedMode;
    private int _wizardStep;
    private readonly Button _wizardModeButton = new();
    private readonly Button _advancedModeButton = new();
    private readonly Button _wizardNext = new();
    private readonly Button _wizardBack = new();
    private readonly Label _wizardHint = new();
    private FlowLayoutPanel? _deploymentActionRow;
    private Button? _gpoOperationsButton;

    private sealed record DirectoryRow(string Name, string Dn, string Sam = "")
    {
        // Keep long DNs out of the list rows; they remain searchable and
        // available through item tooltips and the deployment preview.
        public override string ToString() => string.IsNullOrWhiteSpace(Sam)
            ? Name
            : Name + " (" + Sam + ")";
    }

    private sealed class SavedConnection
    {
        public string Domain { get; set; } = "";
        public string DomainController { get; set; } = "";
        public string UserName { get; set; } = "";
        public bool UseAlternate { get; set; }
    }

    internal DomainDeploymentForm(CampaignProject project, CompanyProfile? company = null,
        AdDirectorySnapshot? readOnlyInventory = null, bool advanced = false,
        DomainCredentials? authenticatedSession = null)
    {
        _project = project;
        _advancedInitially = advanced;
        if (company is not null && !string.Equals(project.CompanyProfileId,
                company.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The selected company does not match this campaign project.");
        _company = company;
        Text = "LockScreen Campaign Manager - Active Directory Deployment" +
            (_company is null ? "" : " — " + _company.Name);
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1280, 870);
        MinimumSize = new Size(1060, 770);
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiStyle.Canvas;
        BuildControls();
        _suppressConnectionChanged = true;
        try { LoadConnection(); }
        finally { _suppressConnectionChanged = false; }
        if (readOnlyInventory is not null)
        {
            // Step 1 is a read-only inventory; it never grants permission to
            // edit AD or skips this window's independent, fresh Connect action.
            if (company is not null &&
                !string.Equals(company.Domain, readOnlyInventory.Domain,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Read-only AD inventory is for another company/domain.");
            _domainBox.Text = readOnlyInventory.Domain;
            _dcBox.Text = readOnlyInventory.Dc;
        }
        _gpoBox.Text = CampaignSchedule.DefaultGpoName(_project);
        if (authenticatedSession is { IsAlternate: true })
        {
            _suppressConnectionChanged = true;
            _alternateRadio.Checked = true;
            _userBox.Text = authenticatedSession.UserName;
            _passwordBox.Text = authenticatedSession.Password;
            _suppressConnectionChanged = false;
        }
        else _currentRadio.Checked = true;
        SetCredentialFields();
        InvalidatePlan();
        UiStyle.Apply(this);
        SetDeploymentMode(_advancedInitially);
        UiStyle.ApplyStatus(_connectionStatus,
            readOnlyInventory is null
                ? "NOT CONNECTED — Connect / Refresh before GPO Preview."
                : "STEP 1 READ: " + readOnlyInventory.Domain + " / " +
                  readOnlyInventory.Dc +
                  ". Session credentials reused; write permission checks follow OU selection.",
            UiStatusKind.Neutral);
        UiWindowPlacement.Attach(this, "domain-deployment");
        FormClosed += (_, _) => _passwordBox.Clear();
        if (authenticatedSession is not null && readOnlyInventory is not null)
            Shown += async (_, _) => await ConnectAsync();
    }

    private static Control Field(string title, Control child, int width, int height = 76)
    {
        var p = new TableLayoutPanel { ColumnCount = 1, RowCount = 2, Width = width, Height = height, Margin = new Padding(0, 0, 12, 0) };
        p.RowStyles.Add(new RowStyle(SizeType.Absolute, 26));
        p.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        p.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill, ForeColor = Color.FromArgb(85, 96, 110), TextAlign = ContentAlignment.MiddleLeft }, 0, 0);
        child.Dock = DockStyle.Fill;
        p.Controls.Add(child, 0, 1);
        return p;
    }

    private static TableLayoutPanel ResponsiveField(string title, Control editor)
    {
        var holder = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1,
            RowCount = 2, Margin = new Padding(2, 3, 10, 4) };
        holder.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        holder.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        holder.Controls.Add(new Label { Text = title, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft, ForeColor = UiStyle.Muted,
            AutoEllipsis = true }, 0, 0);
        editor.Dock = DockStyle.Fill;
        editor.Margin = Padding.Empty;
        holder.Controls.Add(editor, 0, 1);
        return holder;
    }

    private Button Button(string text, int width, Action action, bool primary = false)
    {
        var b = new Button { Text = text, Width = width, Height = 39, Margin = new Padding(0, 0, 9, 5),
            FlatStyle = FlatStyle.Flat, BackColor = primary ? _accent : Color.White,
            ForeColor = primary ? Color.White : Color.FromArgb(35, 44, 55), Cursor = Cursors.Hand };
        b.FlatAppearance.BorderColor = primary ? _accent : Color.LightGray;
        b.Click += (_, _) => action();
        return b;
    }

    private void BuildControls()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6, Padding = new Padding(18) };
        _deploymentLayout = layout;
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 222));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Active Directory / GPO Deployment", Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 16, FontStyle.Bold), ForeColor = Color.FromArgb(30, 44, 65) }, 0, 0);
        var navigation = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112));
        navigation.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
        _wizardHint.Dock = DockStyle.Fill;
        _wizardHint.TextAlign = ContentAlignment.MiddleLeft;
        _wizardHint.Margin = new Padding(2, 0, 8, 0);
        _wizardHint.AutoEllipsis = true;
        _wizardHint.ForeColor = UiStyle.Navy;
        navigation.Controls.Add(_wizardHint, 0, 0);
        _wizardModeButton.Text = "Wizard";
        _wizardModeButton.Dock = DockStyle.Fill;
        _wizardModeButton.Click += (_, _) => SetDeploymentMode(false);
        navigation.Controls.Add(_wizardModeButton, 1, 0);
        _advancedModeButton.Text = "Advanced";
        _advancedModeButton.Dock = DockStyle.Fill;
        _advancedModeButton.Click += (_, _) => SetDeploymentMode(true);
        navigation.Controls.Add(_advancedModeButton, 2, 0);
        layout.Controls.Add(navigation, 0, 1);

        var connection = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(10), BorderStyle = BorderStyle.FixedSingle };
        _connectionPanel = connection;
        var connLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        connLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        connLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        connection.Controls.Add(connLayout);
        var connFields = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        connFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        connFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        connFields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        _domainBox.TextChanged += (_, _) => ConnectionChanged();
        _dcBox.DropDownStyle = ComboBoxStyle.DropDown;
        _dcBox.DropDownWidth = 670;
        _dcBox.TextChanged += (_, _) => ConnectionChanged();
        _gpoBox.TextChanged += (_, _) => InvalidatePlan();
        connFields.Controls.Add(ResponsiveField("AD DNS domain", _domainBox), 0, 0);
        connFields.Controls.Add(ResponsiveField("Writable DC", _dcBox), 1, 0);
        connFields.Controls.Add(ResponsiveField("New GPO name", _gpoBox), 2, 0);
        connLayout.Controls.Add(connFields, 0, 0);
        var accounts = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2 };
        accounts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        accounts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        accounts.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
        accounts.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        accounts.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _currentRadio.Text = "Current Windows account";
        _currentRadio.AutoSize = true;
        _alternateRadio.Text = "Other AD account";
        _alternateRadio.AutoSize = true;
        _currentRadio.CheckedChanged += (_, _) => SetCredentialFields();
        _alternateRadio.CheckedChanged += (_, _) =>
        {
            SetCredentialFields();
            if (_alternateRadio.Checked && !_suppressConnectionChanged)
                EditAlternateAccount(revertToCurrentOnCancel: true);
        };
        accounts.Controls.Add(_currentRadio, 0, 0);
        accounts.Controls.Add(_alternateRadio, 1, 0);
        _userBox.PlaceholderText = "Choose account using the Wizard...";
        _userBox.ReadOnly = true;
        _userBox.TextChanged += (_, _) => ConnectionChanged();
        // Password is collected exclusively on the second Wizard page.
        // This hidden in-memory text box supports existing credential routing
        // without exposing a second, inconsistent password editor.
        _passwordBox.UseSystemPasswordChar = true;
        _passwordBox.TextChanged += (_, _) => ConnectionChanged();
        _otherAccountHint.Dock = DockStyle.Fill;
        _otherAccountHint.TextAlign = ContentAlignment.MiddleLeft;
        _otherAccountHint.ForeColor = UiStyle.Muted;
        _otherAccountHint.AutoEllipsis = true;
        accounts.Controls.Add(ResponsiveField("AD user", _userBox), 0, 1);
        accounts.Controls.Add(ResponsiveField("Password", _otherAccountHint), 1, 1);
        var accountActions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        _usersButton.Text = "Select AD user...";
        _usersButton.Width = 168;
        _usersButton.Height = 34;
        _usersButton.Click += (_, _) => EditAlternateAccount();
        accountActions.Controls.Add(_usersButton);
        _connectButton.Text = "Connect";
        _connectButton.Width = 122;
        _connectButton.Height = 34;
        _connectButton.Click += async (_, _) => await ConnectAsync();
        accountActions.Controls.Add(_connectButton);
        accounts.Controls.Add(accountActions, 2, 1);
        connLayout.Controls.Add(accounts, 0, 1);
        layout.Controls.Add(connection, 0, 2);

        var notification = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = new Padding(6, 5, 6, 5) };
        notification.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        notification.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
        notification.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _connectionStatus.Dock = DockStyle.Fill;
        _connectionStatus.AutoEllipsis = true;
        _connectionStatus.TextAlign = ContentAlignment.MiddleLeft;
        _connectionStatus.Text = "Not connected. Native LDAP + Windows GPO COM; no RSAT or WinRM.";
        _connectionStatus.ForeColor = Color.DarkSlateGray;
        notification.Controls.Add(_connectionStatus, 0, 0);
        _rightsBanner.Dock = DockStyle.Fill;
        _rightsBanner.AutoEllipsis = true;
        _rightsBanner.TextAlign = ContentAlignment.MiddleLeft;
        _rightsBanner.Text = "GPO RIGHTS: Not checked. Preview does not change Active Directory.";
        notification.Controls.Add(_rightsBanner, 0, 1);
        notification.Controls.Add(new Label { Text = "Security filtering uses COMPUTER accounts from the selected security group, within the linked OU.",
            Dock = DockStyle.Fill, ForeColor = Color.DarkSlateGray, AutoEllipsis = true }, 0, 2);
        layout.Controls.Add(notification, 0, 3);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 8,
            BackColor = UiStyle.Canvas };
        _targetSplit = split;
        Shown += (_, _) => {
            if (split.Width > 750)
            {
                split.Panel1MinSize = 300;
                split.Panel2MinSize = 300;
                split.SplitterDistance = Math.Clamp(split.Width / 2, 300,
                    split.Width - split.Panel2MinSize - split.SplitterWidth);
            }
        };
        layout.Controls.Add(split, 0, 4);
        var lists = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1 };
        lists.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        lists.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        lists.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        lists.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        split.Panel1.Controls.Add(lists);
        _ouFilter.Dock = DockStyle.Fill;
        _ouFilter.PlaceholderText = "Search OU...";
        _ouFilter.TextChanged += (_, _) => FilterOus();
        _ous.Dock = DockStyle.Fill;
        _ous.IntegralHeight = false;
        _ous.HorizontalScrollbar = true;
        _ous.SelectedIndexChanged += (_, _) => InvalidatePlan();
        _ous.MouseMove += (_, e) => ShowDirectoryTooltip(_ous, e);
        _groupFilter.Dock = DockStyle.Fill;
        _groupFilter.PlaceholderText = "Search computer security group...";
        _groupFilter.TextChanged += (_, _) => FilterGroups();
        _groups.Dock = DockStyle.Fill;
        _groups.IntegralHeight = false;
        _groups.HorizontalScrollbar = true;
        _groups.SelectedIndexChanged += (_, _) => InvalidatePlan();
        _groups.MouseMove += (_, e) => ShowDirectoryTooltip(_groups, e);
        lists.Controls.Add(_ouFilter, 0, 0);
        lists.Controls.Add(_ous, 0, 1);
        lists.Controls.Add(_groupFilter, 0, 2);
        lists.Controls.Add(_groups, 0, 3);
        _details.Dock = DockStyle.Fill;
        _details.Multiline = true;
        _details.ScrollBars = ScrollBars.Vertical;
        _details.ReadOnly = true;
        _details.BackColor = Color.White;
        _details.Font = new Font("Consolas", 9f);
        _details.Text = "Select a domain, OU and computer security group; then click Preview.";
        split.Panel2.Controls.Add(_details);

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(0, 7, 0, 0) };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true,
            Padding = new Padding(0, 3, 0, 0) };
        _deploymentActionRow = buttons;
        _planButton = Button("Preview / check conflicts", 213, () => _ = PlanAsync(), true);
        _deployButton = Button("Deploy GPO + files", 190, () => _ = DeployAsync());
        _wizardBack.Text = "< Back";
        _wizardBack.Width = 105;
        _wizardBack.Height = 39;
        _wizardBack.Click += (_, _) => { _wizardStep = Math.Max(0, _wizardStep - 1); UpdateDeploymentSteps(); };
        _wizardNext.Text = "Next >";
        _wizardNext.Width = 110;
        _wizardNext.Height = 39;
        _wizardNext.Click += async (_, _) => await NextDeploymentStepAsync();
        buttons.Controls.Add(_wizardBack);
        buttons.Controls.Add(_wizardNext);
        _rightsButton = Button("Check GPO rights", 178, () => _ = CheckRightsAsync());
        buttons.Controls.Add(_rightsButton);
        buttons.Controls.Add(_planButton);
        buttons.Controls.Add(_deployButton);
        _gpoOperationsButton = Button("GPO operations...", 182, OpenGpoOperations);
        buttons.Controls.Add(_gpoOperationsButton);
        buttons.Controls.Add(Button("Close", 96, Close));
        footer.Controls.Add(buttons, 0, 0);
        _progress.Dock = DockStyle.Fill;
        _progress.AutoEllipsis = true;
        _progress.TextAlign = ContentAlignment.MiddleLeft;
        _progress.ForeColor = Color.DimGray;
        _progress.Text = "No action taken in Active Directory.";
        footer.Controls.Add(_progress, 0, 1);
        layout.Controls.Add(footer, 0, 5);
    }

    private bool HasSelectedTargets =>
        !string.IsNullOrWhiteSpace(_connectedDomain) && _connectedWritable &&
        _ous.SelectedItem is DirectoryRow && _groups.SelectedItem is DirectoryRow &&
        !string.IsNullOrWhiteSpace(_gpoBox.Text);

    private void SetDeploymentMode(bool advanced)
    {
        _advancedMode = advanced;
        _wizardModeButton.Enabled = advanced;
        _advancedModeButton.Enabled = !advanced;
        _wizardModeButton.BackColor = advanced ? UiStyle.Surface : UiStyle.Accent;
        _wizardModeButton.ForeColor = advanced ? UiStyle.Foreground : Color.White;
        _advancedModeButton.BackColor = advanced ? UiStyle.Accent : UiStyle.Surface;
        _advancedModeButton.ForeColor = advanced ? Color.White : UiStyle.Foreground;
        UpdateDeploymentSteps();
    }

    private async Task NextDeploymentStepAsync()
    {
        if (_running) return;
        if (_wizardStep == 0 && (string.IsNullOrEmpty(_connectedDomain) || !_connectedWritable))
        {
            _progress.Text = "Connect to a writable domain controller first.";
            return;
        }
        if (_wizardStep == 1 && !HasSelectedTargets)
        {
            _progress.Text = "Select an OU, a computer group and a new GPO name.";
            return;
        }
        if (_wizardStep == 1)
        {
            // Read-only ACL hints are advisory and can be incomplete. Never
            // interrupt an otherwise safe Preview with a blocking Yes/No.
            // The actual AD/SYSVOL write retains its own confirmation and
            // independently verifies delegated rights during deployment.
            await CheckRightsAsync();
            if (_running || _wizardStep != 1 || !HasSelectedTargets) return;
        }
        _wizardStep = Math.Min(2, _wizardStep + 1);
        UpdateDeploymentSteps();
    }

    private async Task CheckRightsAsync()
    {
        if (_running || !HasSelectedTargets) return;
        try
        {
            var selected = (DirectoryRow)_ous.SelectedItem!;
            string domain = _connectedDomain, dc = _connectedDc;
            string login = _alternateRadio.Checked ? _userBox.Text.Trim() : "";
            var credentials = Credentials();
            var response = await RunBusy("Read-only: checking GPO creation and OU-link ACEs...", () =>
                DomainPowerShellRunner.Execute(DomainOperation.Discovery, "Permissions",
                    new { domain, dc, ouDn = selected.Dn, user = login }, credentials));
            var data = response.GetProperty("data");
            string create = data.GetProperty("gpoCreateAce").GetString() ?? "UNKNOWN";
            string link = data.GetProperty("ouGpoLinkAce").GetString() ?? "UNKNOWN";
            bool complete = data.TryGetProperty("groupTokenComplete", out var token) &&
                token.ValueKind == JsonValueKind.True;
            _rightsStatus = create == "POTENTIAL_ALLOW" && link == "POTENTIAL_ALLOW" && complete
                ? "POTENTIAL_ALLOW"
                : create == "POSSIBLE_DENY" || link == "POSSIBLE_DENY"
                    ? "POSSIBLE_DENY"
                    : !complete || create.StartsWith("UNKNOWN", StringComparison.Ordinal) ||
                      link.StartsWith("UNKNOWN", StringComparison.Ordinal)
                        ? "UNKNOWN"
                        : "NO_MATCHING_ALLOW";
            _progress.Text = "GPO create: " + create + " | OU link: " + link +
                " | group SIDs complete: " + (complete ? "YES" : "NO") +
                " | SYSVOL write: NOT TESTED. These are advisory read-only ACL hints.";
            UiStyle.ApplyStatus(_rightsBanner, _rightsStatus switch
            {
                "POTENTIAL_ALLOW" => "GPO RIGHTS: Potentially allowed (read-only hints). SYSVOL and COM not tested.",
                "POSSIBLE_DENY" => "GPO RIGHTS: Possible deny ACE. Preview is safe; deployment can fail.",
                "UNKNOWN" => "GPO RIGHTS: Unknown — account group membership or ACL incomplete. Preview is safe.",
                _ => "GPO RIGHTS: Permission not confirmed (no matching allow). Preview is safe."
            }, _rightsStatus == "POTENTIAL_ALLOW" ? UiStatusKind.Success : UiStatusKind.Warning);
        }
        catch (Exception ex)
        {
            _rightsStatus = "UNAVAILABLE";
            _progress.Text = "Read-only GPO rights could not be determined. Preview remains available; " +
                "deployment still requires independent permission checks.";
            UiStyle.ApplyStatus(_rightsBanner,
                "GPO RIGHTS: Read-only check unavailable. Preview is safe; write rights unverified.",
                UiStatusKind.Warning);
            AppLog.Write(ex, "Read-only GPO permission preflight");
        }
        UpdateDeploymentSteps();
    }

    private void UpdateDeploymentSteps()
    {
        if (_deploymentActionRow is null) return;
        _wizardHint.Text = _advancedMode
            ? "Advanced mode  •  Choose targets, Preview, then Deploy."
            : _wizardStep switch
            {
                0 => "Step 1 / 3  Connect to a writable DC (credentials are session-only).",
                1 => "Step 2 / 3  Select target OU and computer group, check GPO name.",
                _ => "Step 3 / 3  Preview permissions/conflicts; confirm deployment."
            };
        // Connection fields belong on step 1 only. Keep their selected values
        // in memory while giving target and preview pages the full height.
        bool connectionVisible = _advancedMode || _wizardStep == 0;
        if (_connectionPanel is not null) _connectionPanel.Visible = connectionVisible;
        if (_deploymentLayout is not null)
        {
            _deploymentLayout.RowStyles[2].SizeType = SizeType.Absolute;
            _deploymentLayout.RowStyles[2].Height = connectionVisible ? 222 : 0;
        }
        // Use the full content width for target selection and for read-only
        // preview, instead of leaving half the window blank on each page.
        if (_targetSplit is not null)
        {
            _targetSplit.Panel1Collapsed = false;
            _targetSplit.Panel2Collapsed = false;
            if (!_advancedMode && _wizardStep == 1) _targetSplit.Panel2Collapsed = true;
            else if (!_advancedMode && _wizardStep == 2) _targetSplit.Panel1Collapsed = true;
        }
        // Guided mode exposes only the controls needed for the current step.
        // Advanced mode keeps the complete manual editing surface.
        bool credentialsStep = _advancedMode || _wizardStep == 0;
        bool targetStep = _advancedMode || _wizardStep == 1;
        _domainBox.Enabled = _dcBox.Enabled = credentialsStep && !_running;
        _currentRadio.Enabled = _alternateRadio.Enabled = credentialsStep && !_running;
        _userBox.Enabled = _passwordBox.Enabled = credentialsStep &&
            _alternateRadio.Checked && !_running;
        _usersButton.Enabled = credentialsStep && _alternateRadio.Checked && !_running;
        _connectButton.Enabled = credentialsStep && !_running;
        _gpoBox.Enabled = _ous.Enabled = _groups.Enabled = targetStep && !_running;
        _ouFilter.Enabled = _groupFilter.Enabled = targetStep && !_running;
        _wizardBack.Visible = !_advancedMode && _wizardStep > 0;
        _wizardNext.Visible = !_advancedMode && _wizardStep < 2;
        _wizardBack.Enabled = !_running;
        _wizardNext.Enabled = !_running && (_wizardStep == 0
            ? !string.IsNullOrEmpty(_connectedDomain) && _connectedWritable
            : HasSelectedTargets);
        _rightsButton.Visible = _advancedMode || _wizardStep >= 1;
        _rightsButton.Enabled = !_running && HasSelectedTargets;
        _planButton.Visible = _advancedMode || _wizardStep == 2;
        _planButton.Enabled = !_running && HasSelectedTargets;
        _deployButton.Visible = _advancedMode || _wizardStep == 2;
        _deployButton.Enabled = !_running && HasSelectedTargets &&
            _lastPlanKey is not null && _lastPlanKey == PlanKey();
        if (_gpoOperationsButton is not null)
            _gpoOperationsButton.Visible = _advancedMode;
    }

    private void OpenGpoOperations()
    {
        if (_running) return;
        try
        {
            var selection = Selection();
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
            using var form = new GpoOperationsForm(selection.domain, selection.dc,
                selection.ou.Dn, selection.group.Dn, Credentials(), snapshot);
            form.ShowDialog(this);
            // Scope or GPO state may have changed in the child window.
            InvalidatePlan();
        }
        catch (Exception ex) { DisplayError(ex); }
    }

    private void SetCredentialFields()
    {
        bool alternate = _alternateRadio.Checked;
        _userBox.Enabled = alternate;
        _usersButton.Enabled = alternate;
        _otherAccountHint.Text = !alternate ? "Use current Windows credentials" :
            _passwordBox.Text.Length > 0 ? "Set in Wizard (session only)" :
            "Select user to enter password";
        ConnectionChanged();
    }

    private void ConnectionChanged()
    {
        if (_suppressConnectionChanged) return;
        _connectedDomain = string.Empty;
        _connectedDc = string.Empty;
        _connectedWritable = false;
        UiStyle.ApplyStatus(_connectionStatus,
            "CHANGED — click Connect / Refresh to verify this domain/DC and list OU/groups.",
            UiStatusKind.Warning);
        _allOus.Clear();
        _allGroups.Clear();
        FilterOus();
        FilterGroups();
        InvalidatePlan();
        UpdateDeploymentSteps();
    }

    private void InvalidatePlan()
    {
        _lastPlanKey = null;
        _rightsStatus = "NOT_CHECKED";
        UiStyle.ApplyStatus(_rightsBanner,
            "GPO RIGHTS: Not checked. Preview does not change Active Directory.",
            UiStatusKind.Neutral);
        _lastConflictHigh = 0;
        _lastConflictWarnings = 0;
        _deployButton.Enabled = false;
        UpdateDeploymentSteps();
    }

    private DomainCredentials Credentials() => _alternateRadio.Checked
        ? new DomainCredentials(_domainBox.Text.Trim(), _userBox.Text.Trim(), _passwordBox.Text)
        : new DomainCredentials(_domainBox.Text.Trim(), string.Empty, string.Empty);

    private object Request(string mode, string search = "") => new {
        domain = _domainBox.Text.Trim(), dc = _dcBox.Text.Trim(), search
    };

    private async Task<T> RunBusy<T>(string label, Func<T> worker)
    {
        if (_running) throw new InvalidOperationException("An operation is already running.");
        _running = true;
        _connectButton.Enabled = _planButton.Enabled = _deployButton.Enabled = _usersButton.Enabled = false;
        _progress.Text = label;
        UseWaitCursor = true;
        try { return await Task.Run(worker); }
        finally
        {
            _running = false;
            _connectButton.Enabled = _planButton.Enabled = _usersButton.Enabled = true;
            _deployButton.Enabled = _lastPlanKey is not null;
            UseWaitCursor = false;
            UpdateDeploymentSteps();
        }
    }

    private async Task ConnectAsync()
    {
        if (_running) return;
        try
        {
            var creds = Credentials();
            string domain = _domainBox.Text.Trim();
            string dc = _dcBox.Text.Trim();
            var response = await RunBusy("Connecting to Active Directory...", () =>
                DomainPowerShellRunner.Execute(DomainOperation.Discovery, "Connect", new { domain, dc }, creds));
            var connected = response.GetProperty("data");
            var actualDomain = connected.GetProperty("domain").GetString()!;
            var actualDc = connected.GetProperty("dc").GetString()!;
            if (_company is not null && !string.Equals(
                    actualDomain, _company.Domain, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "Connected AD domain differs from the selected company. " +
                    "Choose the correct domain; company profiles never authorize cross-domain operations.");
            _suppressConnectionChanged = true;
            try
            {
                _domainBox.Text = actualDomain;
                _dcBox.Text = actualDc;
            }
            finally { _suppressConnectionChanged = false; }
            _connectedDomain = actualDomain;
            _connectedDc = actualDc;
            bool writable = connected.GetProperty("dcWritable").GetBoolean();
            bool writableKnown = connected.TryGetProperty("dcWritableKnown", out var known)
                && known.ValueKind == System.Text.Json.JsonValueKind.True;
            _connectedWritable = writable && writableKnown;
            var dcRows = await RunBusy("Enumerating writable DCs...", () =>
                DomainPowerShellRunner.Execute(DomainOperation.Discovery, "DomainControllers",
                    new { domain=actualDomain, dc=actualDc }, creds));
            _suppressConnectionChanged = true;
            try
            {
                _dcBox.Items.Clear();
                foreach (var item in dcRows.GetProperty("data").EnumerateArray())
                {
                    if (item.GetProperty("writable").GetBoolean())
                        _dcBox.Items.Add(item.GetProperty("name").GetString()!);
                }
                _dcBox.Text = actualDc;
            }
            finally { _suppressConnectionChanged = false; }
            var ousResponse = await RunBusy("Reading organizational units...", () =>
                DomainPowerShellRunner.Execute(DomainOperation.Discovery, "OUs",
                    new { domain=actualDomain, dc=actualDc }, creds));
            var groupsResponse = await RunBusy("Reading security groups...", () =>
                DomainPowerShellRunner.Execute(DomainOperation.Discovery, "Groups",
                    new { domain=actualDomain, dc=actualDc }, creds));
            _allOus.Clear();
            foreach (var item in ousResponse.GetProperty("data").EnumerateArray())
                _allOus.Add(new DirectoryRow(item.GetProperty("name").GetString()!, item.GetProperty("dn").GetString()!));
            _allGroups.Clear();
            foreach (var item in groupsResponse.GetProperty("data").EnumerateArray())
                _allGroups.Add(new DirectoryRow(item.GetProperty("name").GetString()!, item.GetProperty("dn").GetString()!, item.GetProperty("sam").GetString()!));
            FilterOus();
            FilterGroups();
            if (_company is not null)
            {
                // The profile merely suggests existing AD entries. Select only
                // exact DNs returned by the fresh LDAP discovery. Do not invent
                // a row from profile text or bypass the normal Preview step.
                foreach (var row in _ous.Items.OfType<DirectoryRow>())
                    if (row.Dn.Equals(_company.DefaultOuDn,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        _ous.SelectedItem = row;
                        break;
                    }
                foreach (var row in _groups.Items.OfType<DirectoryRow>())
                    if (row.Dn.Equals(_company.DefaultGroupDn,
                            StringComparison.OrdinalIgnoreCase))
                    {
                        _groups.SelectedItem = row;
                        break;
                    }
                if ((_company.DefaultOuDn.Length > 0 && _ous.SelectedItem is null) ||
                    (_company.DefaultGroupDn.Length > 0 && _groups.SelectedItem is null))
                    _progress.Text = "Some saved company OU/group defaults were not found in AD; select them manually.";
            }
            UiStyle.ApplyStatus(_connectionStatus,
                $"CONNECTED / LDAP: {actualDomain} | DC: {actualDc} | " +
                $"OU: {_allOus.Count} | Groups: {_allGroups.Count}" +
                (!writableKnown ? " | Write capability UNKNOWN" :
                    writable ? " | Writable DC" : " | Confirmed RODC"),
                _connectedWritable ? UiStatusKind.Success : UiStatusKind.Warning);
            _progress.Text = !writableKnown
                ? "DC writable status could not be verified. Select another DC or inspect LDAP diagnostics."
                : !writable
                    ? "Confirmed read-only DC. Select a writable domain controller before deployment."
                : _company is not null &&
                    ((_company.DefaultOuDn.Length > 0 && _ous.SelectedItem is null) ||
                     (_company.DefaultGroupDn.Length > 0 && _groups.SelectedItem is null))
                    ? "Company OU/group default was not found; select a verified AD entry before Preview."
                    : "LDAP verified. No RSAT/WinRM. A new GPO still requires Preview and confirmation.";
            SaveConnection();
            InvalidatePlan();
            UpdateDeploymentSteps();
        }
        catch (Exception ex) { DisplayError(ex); }
    }

    private void ShowDirectoryTooltip(ListBox list, MouseEventArgs e)
    {
        int index = list.IndexFromPoint(e.Location);
        string hint = index >= 0 && index < list.Items.Count &&
            list.Items[index] is DirectoryRow row
                ? row.ToString() + Environment.NewLine + row.Dn : string.Empty;
        if (!string.Equals(_directoryTooltip.GetToolTip(list), hint, StringComparison.Ordinal))
            _directoryTooltip.SetToolTip(list, hint);
    }

    private static bool MatchesDirectoryQuery(DirectoryRow row, string query) =>
        string.IsNullOrWhiteSpace(query) ||
        row.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        row.Dn.Contains(query, StringComparison.OrdinalIgnoreCase) ||
        row.Sam.Contains(query, StringComparison.OrdinalIgnoreCase);

    private void FilterOus()
    {
        string query = _ouFilter.Text.Trim();
        _ous.BeginUpdate();
        try
        {
            var selection = _ous.SelectedItem as DirectoryRow;
            _ous.Items.Clear();
            foreach (var row in _allOus.Where(x => MatchesDirectoryQuery(x, query)))
                _ous.Items.Add(row);
            if (selection is not null && _ous.Items.Contains(selection))
                _ous.SelectedItem = selection;
        }
        finally { _ous.EndUpdate(); }
    }

    private void FilterGroups()
    {
        string query = _groupFilter.Text.Trim();
        _groups.BeginUpdate();
        try
        {
            var selection = _groups.SelectedItem as DirectoryRow;
            _groups.Items.Clear();
            foreach (var row in _allGroups.Where(x => MatchesDirectoryQuery(x, query)))
                _groups.Items.Add(row);
            if (selection is not null && _groups.Items.Contains(selection))
                _groups.SelectedItem = selection;
        }
        finally { _groups.EndUpdate(); }
    }

    private (string domain,string dc,DirectoryRow ou,DirectoryRow group,string gpoName) Selection()
    {
        if (string.IsNullOrWhiteSpace(_connectedDomain) || string.IsNullOrWhiteSpace(_connectedDc))
            throw new InvalidOperationException("Connect to a writable AD domain controller first.");
        if (_ous.SelectedItem is not DirectoryRow ou) throw new InvalidOperationException("Select the target organizational unit.");
        if (_groups.SelectedItem is not DirectoryRow group) throw new InvalidOperationException("Select a computer security group.");
        if (string.IsNullOrWhiteSpace(_gpoBox.Text)) throw new InvalidOperationException("Enter a GPO name.");
        return (_connectedDomain, _connectedDc, ou, group, _gpoBox.Text.Trim());
    }

    private string PlanKey()
    {
        var x = Selection();
        return string.Join("|", x.domain, x.dc, x.ou.Dn, x.group.Dn, x.gpoName,
            CampaignSchedule.BuildId(_project));
    }

    private (string parent,string package) MakeTemporaryPackage()
    {
        CampaignSchedule.Validate(_project, true);
        string parent = Path.Combine(Path.GetTempPath(), "LSCM-Package-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(parent);
        try { return (parent, CampaignExporter.Export(_project, parent)); }
        catch { Directory.Delete(parent, true); throw; }
    }

    private async Task<JsonElement?> PlanAsync()
    {
        if (_running) return null;
        try
        {
            var x = Selection();
            var creds = Credentials();
            if (_project.EnableFeedback)
            {
                string inbox = FeedbackPath.CampaignDirectory(_project.FeedbackShare,
                    CampaignSchedule.BuildId(_project));
                if (!Directory.Exists(inbox))
                    throw new DirectoryNotFoundException("Prepare the SMB feedback directory first: " + inbox);
            }
            var (parent, package) = MakeTemporaryPackage();
            try
            {
                var result = await RunBusy("Checking permissions, domain, OU, GPO name and image hashes...", () =>
                    DomainPowerShellRunner.Execute(DomainOperation.Deployment, "Plan", new {
                        domain = x.domain, dc = x.dc, ouDn = x.ou.Dn,
                        groupDn = x.group.Dn, gpoName = x.gpoName, packagePath = package
                    }, creds, timeoutMilliseconds: 240000));
                var d = result.GetProperty("data");
                // Fail closed: deployment preview must also perform a read-only
                // conflict scan of domain + OU-linked computer policies.
                var conflictResponse = await RunBusy("Scanning inherited Lock Screen policies...", () =>
                    DomainPowerShellRunner.Execute(DomainOperation.ConflictScan, "Scan", new {
                        domain = x.domain, dc = x.dc, ouDn = x.ou.Dn, computer = ""
                    }, creds, timeoutMilliseconds: 240000));
                var scan = conflictResponse.GetProperty("data");
                _lastConflictHigh = scan.GetProperty("highRisk").GetInt32();
                _lastConflictWarnings = scan.GetProperty("warningsCount").GetInt32();
                string storageSummary = "Smart SYSVOL Storage: duplicate audit unavailable. Open GPO Operations to inspect.";
                try
                {
                    var storageResponse = await RunBusy(
                        "Checking existing campaign image hashes in SYSVOL...", () =>
                        DomainPowerShellRunner.Execute(DomainOperation.Storage, "Scan", new {
                            domain = x.domain, dc = x.dc, packagePath = package
                        }, creds, timeoutMilliseconds: 240000));
                    var storageData = storageResponse.GetProperty("data");
                    var match = storageData.GetProperty("comparison");
                    if (match.ValueKind == JsonValueKind.Object)
                        storageSummary =
                            $"SYSVOL duplicate images: {match.GetProperty("duplicateImages").GetInt32()} of {match.GetProperty("imageCount").GetInt32()}\r\n" +
                            $"Bytes already stored (independent copies): {match.GetProperty("duplicateBytes").GetInt64():N0} of {match.GetProperty("totalBytes").GetInt64():N0}.\r\n" +
                            "No images are linked, shared, skipped or overwritten.";
                }
                catch (Exception storageError)
                {
                    // Optional deduplication preview must never claim zero duplicates
                    // when the storage audit was incomplete.
                    AppLog.Write(storageError);
                }
                string conflictSummary = string.Join("\r\n", scan.GetProperty("matches").EnumerateArray()
                    .Take(20).Select(hit =>
                        $"{hit.GetProperty("risk").GetString()} | {hit.GetProperty("gpo").GetString()} | {hit.GetProperty("name").GetString()} = {hit.GetProperty("value").GetString()}"));
                string coverageNotes = string.Join("\r\n",
                    scan.GetProperty("notes").EnumerateArray().Take(15)
                        .Select(item => item.GetString() ?? ""));
                if (coverageNotes.Length != 0)
                    conflictSummary += "\r\nCOVERAGE WARNINGS (verify in GPMC/gpresult):\r\n" + coverageNotes;
                _details.Text = $"PREVIEW - NO CHANGES MADE\r\n\r\nDomain: {x.domain}\r\nWritable DC: {x.dc}\r\n" +
                    $"DC UTC time: {d.GetProperty("dcUtc").GetString()}  |  Local/DC offset: {d.GetProperty("clockSkewSeconds").GetInt32()} seconds\r\n" +
                    $"Time source: selected AD DC (RootDSE currentTime), schedule interpreted in chosen campaign time zone.\r\n" +
                    $"OU: {x.ou.Dn}\r\nComputer group: {x.group.Dn}\r\n" +
                    $"Direct group members: {d.GetProperty("directGroupMembers").GetInt32()}\r\n" +
                    $"GPO: {x.gpoName}\r\nImages: {d.GetProperty("imageCount").GetInt32()}\r\n" +
                    $"Files on DC: {d.GetProperty("sysvolOnDc").GetString()}\r\n" +
                    $"Client startup script: {d.GetProperty("clientScriptPath").GetString()}\r\n" +
                    $"Security filtering: selected group receives Apply; Authenticated Users retains Read only.\r\n" +
                    $"GPO is linked to the OU. No GPO domain-wide link.\r\n" +
                    $"Cleaning up: local agent restores registry; domain GPO and SYSVOL require administrator removal.\r\n" +
                    $"\r\nCONFLICT AUDIT: {_lastConflictHigh} high-risk, {_lastConflictWarnings} warning matches.\r\n" +
                    $"{conflictSummary}\r\n" +
                    $"\r\nSMART SYSVOL STORAGE:\r\n{storageSummary}\r\n" +
                    $"Audit is not full RSoP: check local/site GPO, security, WMI filters and gpresult.\r\n";
                _lastPlanKey = PlanKey();
                _deployButton.Enabled = true;
                UpdateDeploymentSteps();
                _progress.Text = "Preview validated campaign and GPO scope. " +
                    "AD write rights and SYSVOL still require verification during deployment.";
                return d;
            }
            finally { try { Directory.Delete(parent, true); } catch (Exception ex) { AppLog.Write(ex); } }
        }
        catch (Exception ex) { InvalidatePlan(); DisplayError(ex); return null; }
    }

    private async Task DeployAsync()
    {
        if (_running) return;
        var plan = await PlanAsync();
        if (plan is null) return;
        try
        {
            var x = Selection();
            if (_lastPlanKey != PlanKey()) throw new InvalidOperationException("Scope changed. Preview again.");
            if (_lastConflictHigh > 0 && MessageBox.Show(this,
                    $"Detected {_lastConflictHigh} potentially conflicting Lock Screen policy settings.\n" +
                    "Do not deploy without confirming resultant policy on a test computer. Continue anyway?",
                    "Conflicting GPOs detected", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            if (MessageBox.Show(this,
                    $"CREATE A NEW GPO AND COPY IMAGES INTO SYSVOL?\n\nDomain: {x.domain}\nDC: {x.dc}\nOU: {x.ou.Dn}\n" +
                    $"Computer security group: {x.group.Sam}\nGPO: {x.gpoName}\n\n" +
                    "No changes are made to other GPOs. Confirm you have a tested campaign and delegated rights.",
                    "Confirm Active Directory deployment", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            var creds = Credentials();
            var (parent, package) = MakeTemporaryPackage();
            try
            {
                var response = await RunBusy("Creating GPO, uploading files to DC and applying OU / security filters...", () =>
                    DomainPowerShellRunner.Execute(DomainOperation.Deployment, "Deploy", new {
                        domain = x.domain, dc = x.dc, ouDn = x.ou.Dn,
                        groupDn = x.group.Dn, gpoName = x.gpoName, packagePath = package
                    }, creds, timeoutMilliseconds: 300000));
                var data = response.GetProperty("data");
                string gpoId = data.GetProperty("gpoId").GetString()!;
                string receiptDir = Path.Combine(AppLog.DirectoryPath, "Deployments");
                Directory.CreateDirectory(receiptDir);
                string receipt = Path.Combine(receiptDir, CampaignSchedule.BuildId(_project) + ".json");
                File.WriteAllText(receipt, data.GetRawText(), new UTF8Encoding(false));
                _details.Text = $"DEPLOYMENT SUCCESS\r\n\r\nDomain: {x.domain}\r\nGPO: {x.gpoName}\r\nGPO ID: {gpoId}\r\n" +
                    $"OU: {x.ou.Dn}\r\nComputer group: {x.group.Sam}\r\n" +
                    $"Source: {data.GetProperty("sysvolOnDc").GetString()}\r\n" +
                    $"Receipt: {receipt}\r\n\r\n" +
                    "NEXT: Verify GPO in GPMC and test a single Windows 11 Enterprise/Education computer.\r\n";
                _progress.Text = "GPO created through Windows native COM/LDAP. Test on a single computer first.";
                CampaignHistoryStore.TryRecordEvent(CampaignSchedule.BuildId(_project), "GPO Deploy",
                    "New GPO created on the selected DC. Verify the GPO and its application on endpoints.");
                _lastPlanKey = null;
                _deployButton.Enabled = false;
                UpdateDeploymentSteps();
            }
            finally { try { Directory.Delete(parent, true); } catch (Exception ex) { AppLog.Write(ex); } }
        }
        catch (Exception ex) { DisplayError(ex); }
    }

    private void EditAlternateAccount(bool revertToCurrentOnCancel = false)
    {
        if (_running || IsDisposed || Disposing) return;
        string previous = _userBox.Text.Trim();
        using var wizard = new AdAlternateAccountWizard(
            _domainBox.Text.Trim(), _dcBox.Text.Trim(), previous,
            _passwordBox.Text, rememberUsername: false,
            saveEncryptedPassword: false, allowSaving: false);
        if (wizard.ShowDialog(this) != DialogResult.OK)
        {
            // Cancel must not leave a newly selected alternate account with
            // an empty password. An existing authenticated session survives.
            if (revertToCurrentOnCancel || _passwordBox.Text.Length == 0)
            {
                _suppressConnectionChanged = true;
                try { _currentRadio.Checked = true; }
                finally { _suppressConnectionChanged = false; }
                SetCredentialFields();
            }
            return;
        }
        _suppressConnectionChanged = true;
        try
        {
            _alternateRadio.Checked = true;
            _userBox.Text = wizard.SelectedUsername;
            _passwordBox.Text = wizard.SelectedPassword;
        }
        finally { _suppressConnectionChanged = false; }
        SetCredentialFields();
    }

    private void DisplayError(Exception ex)
    {
        _progress.Text = "Failed: " + ex.Message;
        _details.Text = "ERROR\r\n\r\n" + UniversalDiagnosticsEmbedded.Clean(ex.Message);
        UiErrorDialog.Show(this, "AD/GPO Deployment", ex);
    }

    private void SaveConnection()
    {
        // Company workspaces must never overwrite or reuse the legacy global
        // AD username/DC cache. Credentials are never persisted.
        if (_company is not null) return;
        try
        {
            Directory.CreateDirectory(AppLog.DirectoryPath);
            var settings = new SavedConnection { Domain = _connectedDomain, DomainController = _connectedDc,
                UserName = _alternateRadio.Checked ? _userBox.Text.Trim() : string.Empty,
                UseAlternate = _alternateRadio.Checked };
            File.WriteAllText(Path.Combine(AppLog.DirectoryPath, "connection.json"), JsonSerializer.Serialize(settings));
        }
        catch (Exception ex) { AppLog.Write(ex); }
    }

    private void LoadConnection()
    {
        // Prefill ONLY non-secret company defaults. Fresh Connect + LDAP
        // enumeration always verify the real scope before deployment.
        if (_company is not null)
        {
            _domainBox.Text = _company.Domain;
            _dcBox.Text = _company.PreferredDc;
            _userBox.Clear();
            _passwordBox.Clear();
            _currentRadio.Checked = true;
            _alternateRadio.Checked = false;
            return;
        }
        try
        {
            string path = Path.Combine(AppLog.DirectoryPath, "connection.json");
            if (File.Exists(path))
            {
                var saved = JsonSerializer.Deserialize<SavedConnection>(File.ReadAllText(path));
                if (saved is not null)
                {
                    _domainBox.Text = saved.Domain;
                    _dcBox.Text = saved.DomainController;
                    _userBox.Text = saved.UserName;
                    _alternateRadio.Checked = saved.UseAlternate;
                    _currentRadio.Checked = !saved.UseAlternate;
                    return;
                }
            }
            string suffix = IPGlobalProperties.GetIPGlobalProperties().DomainName;
            if (suffix.Contains('.')) _domainBox.Text = suffix;
        }
        catch (Exception ex) { AppLog.Write(ex); }
    }
}
