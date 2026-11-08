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
    private readonly Button _usersButton = new();
    private readonly Label _connectionStatus = new();
    private readonly Label _progress = new();
    private readonly List<DirectoryRow> _allOus = new();
    private readonly List<DirectoryRow> _allGroups = new();
    private string _connectedDomain = string.Empty;
    private string _connectedDc = string.Empty;
    private string? _lastPlanKey;
    private bool _running;
    private int _lastConflictHigh;
    private int _lastConflictWarnings;
    private bool _suppressConnectionChanged;
    private readonly Color _accent = UiStyle.Accent;

    private sealed record DirectoryRow(string Name, string Dn, string Sam = "")
    {
        public override string ToString() => string.IsNullOrWhiteSpace(Sam)
            ? Name + "   |   " + Dn
            : Name + " (" + Sam + ")   |   " + Dn;
    }

    private sealed class SavedConnection
    {
        public string Domain { get; set; } = "";
        public string DomainController { get; set; } = "";
        public string UserName { get; set; } = "";
        public bool UseAlternate { get; set; }
    }

    internal DomainDeploymentForm(CampaignProject project, CompanyProfile? company = null,
        AdDirectorySnapshot? readOnlyInventory = null)
    {
        _project = project;
        if (company is not null && !string.Equals(project.CompanyProfileId,
                company.Id, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                "The selected company does not match this campaign project.");
        _company = company;
        Text = "LockScreen Campaign Manager - Active Directory Deployment" +
            (_company is null ? "" : " — " + _company.Name);
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(1180, 800);
        MinimumSize = new Size(930, 680);
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiStyle.Canvas;
        BuildControls();
        LoadConnection();
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
        _gpoBox.Text = "LockScreen - " + CampaignSchedule.BuildId(_project);
        _currentRadio.Checked = true;
        SetCredentialFields();
        InvalidatePlan();
        UiStyle.Apply(this);
        UiStyle.ApplyStatus(_connectionStatus,
            readOnlyInventory is null
                ? "NOT CONNECTED — Connect / Refresh before GPO Preview."
                : "STEP 1 READ: " + readOnlyInventory.Domain + " / " +
                  readOnlyInventory.Dc +
                  ". Reconnect here to verify OU/group and permissions before deployment.",
            UiStatusKind.Neutral);
        UiWindowPlacement.Attach(this, "domain-deployment");
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
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5, Padding = new Padding(18) };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 155));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 89));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 111));
        Controls.Add(layout);
        layout.Controls.Add(new Label { Text = "Active Directory / GPO Deployment", Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 16, FontStyle.Bold), ForeColor = Color.FromArgb(30, 44, 65) }, 0, 0);

        var connection = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(10), BorderStyle = BorderStyle.FixedSingle };
        var connLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        connLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        connLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        connection.Controls.Add(connLayout);
        var connFields = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        _domainBox.Width = 228;
        _domainBox.TextChanged += (_, _) => ConnectionChanged();
        _dcBox.Width = 210;
        _dcBox.DropDownStyle = ComboBoxStyle.DropDown;
        _dcBox.TextChanged += (_, _) => ConnectionChanged();
        _gpoBox.Width = 370;
        _gpoBox.TextChanged += (_, _) => InvalidatePlan();
        connFields.Controls.Add(Field("DNS domain (empty = auto detect)", _domainBox, 245));
        connFields.Controls.Add(Field("Writable domain controller", _dcBox, 230));
        connFields.Controls.Add(Field("New GPO name", _gpoBox, 390));
        connLayout.Controls.Add(connFields, 0, 0);
        var accounts = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        _currentRadio.Text = "Current Windows account";
        _currentRadio.AutoSize = true;
        _currentRadio.Padding = new Padding(0, 10, 0, 0);
        _alternateRadio.Text = "Other domain account";
        _alternateRadio.AutoSize = true;
        _alternateRadio.Padding = new Padding(0, 10, 0, 0);
        _currentRadio.CheckedChanged += (_, _) => SetCredentialFields();
        _alternateRadio.CheckedChanged += (_, _) => SetCredentialFields();
        accounts.Controls.Add(_currentRadio);
        accounts.Controls.Add(_alternateRadio);
        _userBox.Width = 190;
        _userBox.PlaceholderText = @"DOMAIN\user or user@domain";
        _userBox.TextChanged += (_, _) => ConnectionChanged();
        _passwordBox.Width = 145;
        _passwordBox.UseSystemPasswordChar = true;
        _passwordBox.TextChanged += (_, _) => ConnectionChanged();
        accounts.Controls.Add(Field("AD user", _userBox, 200, 52));
        accounts.Controls.Add(Field("Password (session only)", _passwordBox, 155, 52));
        _usersButton.Text = "Find user";
        _usersButton.Width = 90;
        _usersButton.Height = 31;
        _usersButton.Margin = new Padding(0, 17, 10, 0);
        _usersButton.Click += async (_, _) => await FindUserAsync();
        accounts.Controls.Add(_usersButton);
        _connectButton.Text = "Connect / Refresh";
        _connectButton.Width = 155;
        _connectButton.Height = 31;
        _connectButton.Margin = new Padding(0, 17, 10, 0);
        _connectButton.Click += async (_, _) => await ConnectAsync();
        accounts.Controls.Add(_connectButton);
        connLayout.Controls.Add(accounts, 0, 1);
        layout.Controls.Add(connection, 0, 1);

        var notification = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(6) };
        notification.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        notification.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _connectionStatus.Dock = DockStyle.Fill;
        _connectionStatus.Text = "Not connected. Native LDAP + Windows GPO COM; no RSAT or WinRM.";
        _connectionStatus.ForeColor = Color.DarkSlateGray;
        notification.Controls.Add(_connectionStatus, 0, 0);
        notification.Controls.Add(new Label { Text = "Security Filtering targets COMPUTER accounts in the selected security group, within the linked OU. Preview is read-only.",
            Dock = DockStyle.Fill, ForeColor = Color.DarkSlateGray, AutoEllipsis = true }, 0, 1);
        layout.Controls.Add(notification, 0, 2);

        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 8,
            BackColor = UiStyle.Canvas };
        Shown += (_, _) => {
            if (split.Width > 750)
            {
                split.Panel1MinSize = 300;
                split.Panel2MinSize = 300;
                split.SplitterDistance = Math.Clamp(split.Width / 2, 300,
                    split.Width - split.Panel2MinSize - split.SplitterWidth);
            }
        };
        layout.Controls.Add(split, 0, 3);
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
        _groupFilter.Dock = DockStyle.Fill;
        _groupFilter.PlaceholderText = "Search computer security group...";
        _groupFilter.TextChanged += (_, _) => FilterGroups();
        _groups.Dock = DockStyle.Fill;
        _groups.IntegralHeight = false;
        _groups.HorizontalScrollbar = true;
        _groups.SelectedIndexChanged += (_, _) => InvalidatePlan();
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

        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1, Padding = new Padding(0, 14, 0, 0) };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        _planButton = Button("Preview (no changes)", 212, () => _ = PlanAsync(), true);
        _deployButton = Button("Create GPO + copy to DC", 224, () => _ = DeployAsync());
        buttons.Controls.Add(_planButton);
        buttons.Controls.Add(_deployButton);
        buttons.Controls.Add(Button("GPO Management / Conflicts / Push Now", 285, OpenGpoOperations));
        buttons.Controls.Add(Button("Close", 96, Close));
        footer.Controls.Add(buttons, 0, 0);
        _progress.Dock = DockStyle.Fill;
        _progress.ForeColor = Color.DimGray;
        _progress.Text = "No action taken in Active Directory.";
        footer.Controls.Add(_progress, 0, 1);
        layout.Controls.Add(footer, 0, 4);
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
        _passwordBox.Enabled = alternate;
        _usersButton.Enabled = alternate;
        ConnectionChanged();
    }

    private void ConnectionChanged()
    {
        if (_suppressConnectionChanged) return;
        _connectedDomain = string.Empty;
        _connectedDc = string.Empty;
        UiStyle.ApplyStatus(_connectionStatus,
            "CHANGED — click Connect / Refresh to verify this domain/DC and list OU/groups.",
            UiStatusKind.Warning);
        _allOus.Clear();
        _allGroups.Clear();
        FilterOus();
        FilterGroups();
        InvalidatePlan();
    }

    private void InvalidatePlan()
    {
        _lastPlanKey = null;
        _lastConflictHigh = 0;
        _lastConflictWarnings = 0;
        _deployButton.Enabled = false;
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
                (writable ? " | Writable" : " | Read-only DC"),
                writable ? UiStatusKind.Success : UiStatusKind.Warning);
            _progress.Text = !writable
                ? "Read-only DC detected. Select a writable domain controller before deployment."
                : _company is not null &&
                    ((_company.DefaultOuDn.Length > 0 && _ous.SelectedItem is null) ||
                     (_company.DefaultGroupDn.Length > 0 && _groups.SelectedItem is null))
                    ? "Company OU/group default was not found; select a verified AD entry before Preview."
                    : "LDAP verified. No RSAT/WinRM. A new GPO still requires Preview and confirmation.";
            SaveConnection();
            InvalidatePlan();
        }
        catch (Exception ex) { DisplayError(ex); }
    }

    private void FilterOus()
    {
        string query = _ouFilter.Text.Trim();
        _ous.BeginUpdate();
        try
        {
            _ous.Items.Clear();
            foreach (var row in _allOus.Where(x => string.IsNullOrEmpty(query) || x.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)))
                _ous.Items.Add(row);
        }
        finally { _ous.EndUpdate(); }
    }

    private void FilterGroups()
    {
        string query = _groupFilter.Text.Trim();
        _groups.BeginUpdate();
        try
        {
            _groups.Items.Clear();
            foreach (var row in _allGroups.Where(x => string.IsNullOrEmpty(query) || x.ToString().Contains(query, StringComparison.OrdinalIgnoreCase)))
                _groups.Items.Add(row);
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
                _progress.Text = "Preview passed. Click Deploy to create the GPO.";
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
            }
            finally { try { Directory.Delete(parent, true); } catch (Exception ex) { AppLog.Write(ex); } }
        }
        catch (Exception ex) { DisplayError(ex); }
    }

    private async Task FindUserAsync()
    {
        if (_running) return;
        var search = Microsoft.VisualBasic.Interaction.InputBox("AD account name (at least 2 characters)",
            "Find a domain user", _userBox.Text);
        if (search.Length < 2) return;
        try
        {
            string domain = _domainBox.Text.Trim();
            string dc = _dcBox.Text.Trim();
            var lookupCredentials = string.IsNullOrWhiteSpace(_userBox.Text) || string.IsNullOrEmpty(_passwordBox.Text)
                ? new DomainCredentials(domain, "", "") : Credentials();
            var response = await RunBusy("Searching AD users...", () =>
                DomainPowerShellRunner.Execute(DomainOperation.Discovery, "Users",
                    new { domain, dc, search }, lookupCredentials));
            var rows = response.GetProperty("data").EnumerateArray().Select(x =>
                new DirectoryRow(x.GetProperty("name").GetString()!, x.GetProperty("dn").GetString()!, x.GetProperty("sam").GetString()!)).ToArray();
            using var chooser = new Form { Text = "Select AD account", StartPosition = FormStartPosition.CenterParent,
                Width = 650, Height = 440, MinimumSize = new Size(450, 350) };
            var list = new ListBox { Dock = DockStyle.Fill };
            list.Items.AddRange(rows);
            var select = new Button { Dock = DockStyle.Bottom, Text = "Select account", Height = 45 };
            select.Click += (_, _) => { if (list.SelectedItem is not null) chooser.DialogResult = DialogResult.OK; };
            chooser.Controls.Add(list); chooser.Controls.Add(select);
            if (chooser.ShowDialog(this) == DialogResult.OK && list.SelectedItem is DirectoryRow user)
            {
                _alternateRadio.Checked = true;
                _userBox.Text = user.Sam;
            }
        }
        catch (Exception ex) { DisplayError(ex); }
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
