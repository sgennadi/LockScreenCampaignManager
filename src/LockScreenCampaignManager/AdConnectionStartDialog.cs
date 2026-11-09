using System.Drawing;
using System.Net.NetworkInformation;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// BitKeyBridge-style first step: make a read-only, fresh connection to a DC
/// and inspect one bounded LDAP directory snapshot before editing/deploying.
/// This contains no credentials; only harmless directory metadata in memory.
/// The GPO deployment wizard MUST re-validate scope and credentials later.
/// </summary>
internal sealed record AdDirectorySnapshot(
    string Domain, string Dc, bool Writable, DateTimeOffset ObservedUtc,
    bool Capped, IReadOnlyList<string> DomainControllers,
    IReadOnlyList<string> OrganizationalUnits, IReadOnlyList<string> ComputerGroups,
    IReadOnlyList<string> GroupPolicies)
{
    internal bool WritableKnown { get; init; } = true;

    private static string StringField(JsonElement obj, string key, int max = 260)
    {
        if (!obj.TryGetProperty(key, out var x) || x.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("AD snapshot field missing: " + key);
        string s = x.GetString()?.Trim() ?? "";
        if (s.Length > max || s.Any(char.IsControl))
            throw new InvalidDataException("Invalid AD snapshot field: " + key);
        return s;
    }

    private static IReadOnlyList<string> Names(JsonElement item, string collection,
        string field = "name")
    {
        var array = item.GetProperty(collection);
        if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > 5000)
            throw new InvalidDataException("Invalid or oversized AD snapshot: " + collection);
        var result = new List<string>();
        foreach (var entry in array.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("Invalid AD snapshot entry.");
            var name = StringField(entry, field, 1024);
            if (name.Length > 0) result.Add(name);
        }
        return result;
    }

    internal static AdDirectorySnapshot Parse(JsonElement root, string requestedDomain,
        CompanyProfile? company = null)
    {
        var data = root.GetProperty("data");
        if (data.ValueKind != JsonValueKind.Object ||
            !data.GetProperty("readOnly").GetBoolean())
            throw new InvalidDataException("Not an authorized read-only AD snapshot.");

        var domain = StringField(data, "domain").ToLowerInvariant();
        var dc = StringField(data, "dc");
        if (!Regex.IsMatch(domain,
            @"\A(?=.{3,253}\z)[a-z0-9-]+(?:\.[a-z0-9-]+)+\z",
            RegexOptions.CultureInvariant) ||
            !Regex.IsMatch(dc,
                @"\A[a-zA-Z0-9-]+(?:\.[a-zA-Z0-9-]+)+\z",
                RegexOptions.CultureInvariant))
            throw new InvalidDataException("AD returned invalid domain or DC name.");
        if (!string.IsNullOrWhiteSpace(requestedDomain) &&
            !string.Equals(domain, requestedDomain.Trim(), StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Connected to a different domain than requested.");
        if (company is not null &&
            !string.Equals(domain, company.Domain, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("AD domain differs from selected company. No scope is trusted.");

        string baseDn = StringField(data, "baseDn", 1024);
        string expectedDn = "DC=" + string.Join(",DC=", domain.Split('.'));
        if (!string.Equals(baseDn, expectedDn, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("AD RootDSE naming context is not the requested domain.");

        bool writable = data.GetProperty("dcWritable").GetBoolean();
        bool writableKnown = data.TryGetProperty("dcWritableKnown", out var knownField)
            ? knownField.GetBoolean() : true; // Backward compatibility for older fixtures.
        bool capped = data.GetProperty("capped").GetBoolean();
        DateTimeOffset when = data.GetProperty("observedUtc").GetDateTimeOffset().ToUniversalTime();
        var now = DateTimeOffset.UtcNow;
        if (when < now.AddMinutes(-10) || when > now.AddMinutes(5))
            throw new InvalidDataException("AD inventory timestamp is stale or in the future.");

        return new AdDirectorySnapshot(domain, dc, writable, when, capped,
            Names(data, "controllers"), Names(data, "ous"),
            Names(data, "groups"), Names(data, "gpos"))
        { WritableKnown = writableKnown };
    }
}


internal sealed record AdConnectionScope(
    string CompanyId, string Domain, string PreferredDc,
    string DefaultOuDn, string DefaultGroupDn)
{
    // Keep the read-only directory snapshot tied to the precise company
    // defaults that were visible when the connection was accepted.
    private static string Normalize(string? value) => (value ?? "").Trim().ToUpperInvariant();

    internal static AdConnectionScope From(CompanyProfile? company) => new(
        Normalize(company?.Id), Normalize(company?.Domain),
        Normalize(company?.PreferredDc), Normalize(company?.DefaultOuDn),
        Normalize(company?.DefaultGroupDn));

    internal bool Matches(AdDirectorySnapshot? snapshot, CompanyProfile? company,
        DateTimeOffset utcNow) =>
        snapshot is not null &&
        this == From(company) &&
        snapshot.ObservedUtc >= utcNow.AddMinutes(-20) &&
        snapshot.ObservedUtc <= utcNow.AddMinutes(5) &&
        (company is null || string.Equals(snapshot.Domain, company.Domain.Trim(),
            StringComparison.OrdinalIgnoreCase));
}

internal sealed class AdConnectionStartDialog : Form
{
    private readonly CompanyProfile? _company;
    private readonly TextBox _domain = new();
    private readonly ComboBox _dc = new();
    private readonly Button _refreshDc = new();
    private readonly Label _dcDiscoveryStatus = new();
    private readonly CheckBox _advancedDc = new();
    private readonly TableLayoutPanel _serverOptions = new();
    private readonly RadioButton _current = new();
    private readonly RadioButton _alternate = new();
    private readonly Label _activeIdentity = new();
    private readonly Button _editOtherAccount = new();
    private readonly TextBox _user = new();
    private readonly TextBox _password = new();
    private readonly CheckBox _rememberUsername = new();
    private readonly CheckBox _saveEncryptedPassword = new();
    private readonly Button _forgetCredential = new();
    private string _loadedCredentialDomain = "";
    private readonly Button _connect = new();
    private readonly Button _continue = new();
    private readonly Button _offline = new();
    private readonly Label _state = new();
    private readonly TextBox _inventory = new();
    private readonly Button _back = new();
    private readonly Button _next = new();
    private readonly Label[] _railSteps = new Label[3];
    private readonly Panel _stepHost = new();
    private readonly Panel _connectionPage = new();
    private readonly Panel _inventoryPage = new();
    private readonly Panel _reviewPage = new();
    private readonly RichTextBox _review = new();
    private readonly ToolTip _tips = new() { AutoPopDelay = 22000, InitialDelay = 300, ReshowDelay = 200 };
    private readonly System.Windows.Forms.Timer _dnsDebounce = new() { Interval = 650 };
    private int _discoveryRevision;
    private int _page;
    private bool _busy;
    private bool _mutating;
    internal AdDirectorySnapshot? Snapshot { get; private set; }
    // Session-only account for subsequent deployment: no password in projects or diagnostics.
    internal DomainCredentials? ConnectedCredentials { get; private set; }
    internal bool WorkOfflineChosen { get; private set; }

    internal AdConnectionStartDialog(CompanyProfile? company = null)
    {
        _company = company;
        Text = "LockScreen Campaign Manager - AD Connection Wizard";
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(1050, 690);
        Size = new Size(1190, 795);
        BackColor = UiStyle.Canvas;
        BuildLayout();

        _mutating = true;
        try
        {
            _domain.Text = company?.Domain ??
                IPGlobalProperties.GetIPGlobalProperties().DomainName;
            _dc.Text = company?.PreferredDc ?? "";
            _current.Checked = true;
            _advancedDc.Checked = !string.IsNullOrWhiteSpace(company?.PreferredDc);
            LoadSavedCredentials();
        }
        finally { _mutating = false; }

        _domain.TextChanged += (_, _) => {
            if (!_mutating && !string.Equals(_loadedCredentialDomain, _domain.Text.Trim(),
                    StringComparison.OrdinalIgnoreCase))
            {
                _loadedCredentialDomain = "";
                _password.Clear();
                _saveEncryptedPassword.Checked = false;
            }
            InvalidateSnapshot(); ScheduleDcDiscovery();
        };
        _dc.TextChanged += (_, _) => { InvalidateSnapshot(); _tips.SetToolTip(_dc, _dc.Text); };
        _user.TextChanged += (_, _) => InvalidateSnapshot();
        _password.TextChanged += (_, _) => InvalidateSnapshot();
        _current.CheckedChanged += (_, _) => { SetCredentialMode(); InvalidateSnapshot(); };
        _alternate.CheckedChanged += (_, _) => {
            SetCredentialMode();
            if (_mutating) return;
            if (_alternate.Checked)
            {
                if (_password.Text.Length == 0) LoadSavedCredentials();
                if (!EditAlternateAccount())
                {
                    _mutating = true;
                    try { _current.Checked = true; }
                    finally { _mutating = false; }
                    SetCredentialMode();
                }
            }
            InvalidateSnapshot();
        };
        _rememberUsername.CheckedChanged += (_, _) => {
            if (!_rememberUsername.Checked) _saveEncryptedPassword.Checked = false;
            SetCredentialMode();
        };
        _saveEncryptedPassword.CheckedChanged += (_, _) => {
            if (_saveEncryptedPassword.Checked) _rememberUsername.Checked = true;
            SetCredentialMode();
        };
        SetCredentialMode();
        UiStyle.Apply(this);
        UiStyle.ApplyStatus(_state, "NOT CONNECTED — no directory read has been performed.", UiStatusKind.Neutral);
        AcceptButton = _connect;
        CancelButton = _offline;
        _dnsDebounce.Tick += async (_, _) => {
            _dnsDebounce.Stop();
            await DiscoverDcListAsync();
        };
        Shown += (_, _) => ScheduleDcDiscovery();
        UiWindowPlacement.Attach(this, "ad-start-connection");
        FormClosed += (_, _) => {
            _dnsDebounce.Stop();
            _dnsDebounce.Dispose();
            _tips.Dispose();
            _password.Clear();
        };
    }

    private static TableLayoutPanel Field(string caption, Control child, int logicalWidth)
    {
        var holder = new TableLayoutPanel
        {
            RowCount = 2, ColumnCount = 1, AutoSize = true,
            Width = logicalWidth, Margin = new Padding(0, 0, 12, 6)
        };
        holder.RowStyles.Add(new RowStyle(SizeType.Absolute, 27));
        holder.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        holder.Controls.Add(new Label
        {
            Text = caption, Dock = DockStyle.Fill,
            ForeColor = UiStyle.Muted,
            TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        child.Dock = DockStyle.Fill;
        holder.Controls.Add(child, 0, 1);
        return holder;
    }

    private static TableLayoutPanel ConnectionField(string caption, Control editor)
    {
        var field = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = new Padding(2, 1, 10, 2)
        };
        field.RowStyles.Add(new RowStyle(SizeType.Absolute, 25));
        field.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        field.Controls.Add(new Label {
            Text = caption, Dock = DockStyle.Fill,
            ForeColor = UiStyle.Muted, TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true, Margin = Padding.Empty
        }, 0, 0);
        editor.Dock = DockStyle.Top;
        editor.Margin = new Padding(0, 1, 0, 0);
        field.Controls.Add(editor, 0, 1);
        return field;
    }

    private static Button ActionButton(string text, int width, bool primary = false)
    {
        return new Button
        {
            Text = text, MinimumSize = new Size(width, 36),
            AutoSize = true, AutoSizeMode = AutoSizeMode.GrowOnly,
            BackColor = primary ? UiStyle.Accent : UiStyle.Surface,
            ForeColor = primary ? Color.White : UiStyle.Foreground,
            Tag = primary ? "PrimaryAction" : null,
            Margin = new Padding(0, 1, 10, 7),
            Cursor = Cursors.Hand
        };
    }

    private void BuildLayout()
    {
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3,
            Padding = new Padding(16), BackColor = UiStyle.Canvas
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 206));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 128));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        Controls.Add(root);

        var title = new TableLayoutPanel {
            Dock = DockStyle.Fill, RowCount = 2, BackColor = UiStyle.Navy,
            Padding = new Padding(20, 12, 16, 8)
        };
        title.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        title.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
        var headerTitle = new Label {
            Name = "AdWizardHeaderTitle",
            Text = "AD Connection Wizard",
            Dock = DockStyle.Fill, ForeColor = Color.White,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI Semibold", 17f, FontStyle.Bold),
            AutoEllipsis = true
        };
        var headerSubtitle = new Label {
            Name = "AdWizardHeaderSubtitle",
            Text = "Read-only LDAP inventory first. No GPO changes, RSAT or WinRM.",
            Dock = DockStyle.Fill, ForeColor = Color.FromArgb(223,238,255),
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 10f),
            AutoEllipsis = true
        };
        title.Controls.Add(headerTitle, 0, 0);
        title.Controls.Add(headerSubtitle, 0, 1);
        root.Controls.Add(title, 0, 0);
        root.SetColumnSpan(title, 2);
        void FitAdHeader() => UiStyle.FitWizardHeader(root, title,
            headerTitle, headerSubtitle, 128);
        headerTitle.FontChanged += (_, _) => FitAdHeader();
        headerSubtitle.FontChanged += (_, _) => FitAdHeader();
        Shown += (_, _) => FitAdHeader();
        DpiChanged += (_, _) => BeginInvoke(new Action(FitAdHeader));
        FitAdHeader();

        var rail = new TableLayoutPanel {
            Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1,
            Padding = new Padding(7, 14, 12, 10), BackColor = UiStyle.SoftBlue
        };
        rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        rail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        string[] steps = [
            "1  Sign in",
            "2  Read AD",
            "3  Review"
        ];
        for (int i = 0; i < steps.Length; i++)
        {
            _railSteps[i] = new Label {
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                Text = steps[i], AutoEllipsis = false,
                Font = new Font("Segoe UI Semibold", 10f),
                Margin = new Padding(4, 3, 2, 4),
                Padding = new Padding(12, 4, 7, 4)
            };
            rail.Controls.Add(_railSteps[i], 0, i);
        }
        rail.Controls.Add(new Label {
            Text = "Reading AD does not require GPO deployment rights. " +
                "You can prepare and save a campaign without changing AD.",
            Dock = DockStyle.Fill, ForeColor = UiStyle.Muted, Padding = new Padding(10, 12, 10, 6),
            Font = new Font("Segoe UI", 9f)
        }, 0, 3);
        root.Controls.Add(rail, 0, 1);

        _stepHost.Dock = DockStyle.Fill;
        _stepHost.Padding = new Padding(20, 15, 18, 8);
        _stepHost.BackColor = UiStyle.Surface;
        root.Controls.Add(_stepHost, 1, 1);

        _connectionPage.Dock = DockStyle.Fill;
        var connection = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5
        };
        connection.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        connection.RowStyles.Add(new RowStyle(SizeType.Absolute, 226));
        connection.RowStyles.Add(new RowStyle(SizeType.Absolute, 153));
        connection.RowStyles.Add(new RowStyle(SizeType.Absolute, 85));
        connection.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        connection.Controls.Add(new Label {
            Text = "Connect to Active Directory" + Environment.NewLine +
                "Choose your Windows account or sign in with another AD user. Domain controllers are detected automatically.",
            Dock = DockStyle.Fill, ForeColor = UiStyle.Navy,
            Font = new Font("Segoe UI Semibold", 12f)
        }, 0, 0);
        // Current Windows identity is visible; alternate credentials are
        // entered in a separate, DPI-safe dialog rather than cramped inline fields.
        var authentication = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3,
            BackColor = UiStyle.SoftBlue, Padding = new Padding(18, 14, 18, 10)
        };
        authentication.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        authentication.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        authentication.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        authentication.RowStyles.Add(new RowStyle(SizeType.Absolute, 69));
        authentication.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        _current.Text = "Use current Windows account";
        _alternate.Text = "Use another AD account...";
        _current.AutoSize = true;
        _alternate.AutoSize = true;
        _current.Margin = new Padding(2, 6, 8, 2);
        _alternate.Margin = new Padding(8, 6, 2, 2);
        authentication.Controls.Add(_current, 0, 0);
        authentication.Controls.Add(_alternate, 1, 0);

        _activeIdentity.Dock = DockStyle.Fill;
        _activeIdentity.TextAlign = ContentAlignment.MiddleLeft;
        _activeIdentity.AutoEllipsis = true;
        _activeIdentity.ForeColor = UiStyle.Navy;
        _activeIdentity.Padding = new Padding(7, 0, 4, 0);
        authentication.Controls.Add(_activeIdentity, 0, 1);
        _editOtherAccount.Text = "Enter / change AD credentials...";
        _editOtherAccount.Dock = DockStyle.Fill;
        _editOtherAccount.Margin = new Padding(14, 12, 16, 12);
        _editOtherAccount.Tag = "NoIcon";
        _editOtherAccount.Click += (_, _) => {
            if (EditAlternateAccount()) InvalidateSnapshot();
        };
        authentication.Controls.Add(_editOtherAccount, 1, 1);

        _user.PlaceholderText = @"DOMAIN\user or user@domain";
        _password.UseSystemPasswordChar = true;
        _domain.PlaceholderText = "example.com";
        var domainField = ConnectionField("Active Directory DNS domain", _domain);
        authentication.Controls.Add(domainField, 0, 2);
        authentication.SetColumnSpan(domainField, 2);
        // Credential persistence is configured inside the alternate-account
        // dialog only, next to the matching username/password fields.
        _rememberUsername.Text = "Remember username";
        _saveEncryptedPassword.Text = "Save password securely";
        _forgetCredential.Text = "Forget saved login";
        _forgetCredential.Click += (_, _) => ForgetCredentials();
        connection.Controls.Add(authentication, 0, 1);

        // The operator is signing in, not selecting a server. DC override is
        // advanced and never required for normal automatic domain connection.
        _dc.DropDownStyle = ComboBoxStyle.DropDown;
        _dc.DropDownWidth = 750;
        _dc.MaxDropDownItems = 18;
        _dc.IntegralHeight = false;
        _serverOptions.Dock = DockStyle.Fill;
        _serverOptions.ColumnCount = 2;
        _serverOptions.RowCount = 2;
        _serverOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _serverOptions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 138));
        _serverOptions.RowStyles.Add(new RowStyle(SizeType.Absolute, 67));
        _serverOptions.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _serverOptions.Padding = new Padding(12, 0, 12, 3);
        _serverOptions.Controls.Add(ConnectionField("Domain controller (optional)", _dc), 0, 0);
        _refreshDc.Text = "Find DCs";
        _refreshDc.Dock = DockStyle.Fill;
        _refreshDc.Margin = new Padding(6, 24, 0, 9);
        _refreshDc.Click += async (_, _) => await DiscoverDcListAsync();
        _serverOptions.Controls.Add(_refreshDc, 1, 0);
        _dcDiscoveryStatus.Dock = DockStyle.Fill;
        _dcDiscoveryStatus.AutoEllipsis = true;
        _dcDiscoveryStatus.ForeColor = UiStyle.Muted;
        _dcDiscoveryStatus.Text = "Automatic selection is recommended. Manual DC is optional.";
        _dcDiscoveryStatus.Padding = new Padding(2, 4, 0, 0);
        _serverOptions.Controls.Add(_dcDiscoveryStatus, 0, 1);
        _serverOptions.SetColumnSpan(_dcDiscoveryStatus, 2);


        _advancedDc.Text = "Advanced connection options  •  Specify domain controller";
        _advancedDc.AutoSize = true;
        _advancedDc.Dock = DockStyle.Left;
        _advancedDc.Margin = new Padding(10, 9, 0, 4);
        _advancedDc.CheckedChanged += (_, _) => {
            _serverOptions.Visible = _advancedDc.Checked;
            if (_advancedDc.Checked && !_mutating)
            {
                // Never discard the discovered controller list when Advanced
                // is collapsed. Rechecking must reuse the existing list and
                // refresh DNS if the domain has changed or no DCs are cached.
                if (_dc.Items.Count == 0)
                    ScheduleDcDiscovery();
                else
                    _dcDiscoveryStatus.Text = _dc.Items.Count +
                        " controller(s) available. Select one or use Find DCs to refresh.";
            }
            // When Advanced is off the connection already ignores _dc.Text.
            // Clearing it here caused a lost selection and an empty-looking
            // dropdown when the operator enabled Advanced again.
        };
        var serverPanel = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            BackColor = UiStyle.Surface
        };
        serverPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        serverPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        serverPanel.Controls.Add(_advancedDc, 0, 0);
        serverPanel.Controls.Add(_serverOptions, 0, 1);
        connection.Controls.Add(serverPanel, 0, 2);
        _serverOptions.Visible = false;

        _state.Dock = DockStyle.Fill;
        _state.TextAlign = ContentAlignment.MiddleLeft;
        connection.Controls.Add(_state, 0, 3);
        connection.Controls.Add(new Label {
            Text = "Connection uses signed and sealed LDAP for read-only inventory. " +
                "No GPO changes happen in this wizard. After connecting you can inspect " +
                "discovered controllers, OUs, computer groups and GPOs before finishing.",
            Dock = DockStyle.Fill, ForeColor = UiStyle.Muted,
            Padding = new Padding(9, 8, 9, 0)
        }, 0, 4);
        _connectionPage.Controls.Add(connection);
        _stepHost.Controls.Add(_connectionPage);

        _inventoryPage.Dock = DockStyle.Fill;
        var inventoryLayout = new TableLayoutPanel {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1
        };
        inventoryLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 87));
        inventoryLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        inventoryLayout.Controls.Add(new Label {
            Text = "Read Active Directory Inventory" + Environment.NewLine +
                "Review discovered domain controllers, OUs, groups and GPOs. " +
                "No changes were written to AD.",
            Dock = DockStyle.Fill, ForeColor = UiStyle.Navy,
            Font = new Font("Segoe UI Semibold", 12f)
        }, 0, 0);
        _inventory.Multiline = true;
        _inventory.ReadOnly = true;
        _inventory.ScrollBars = ScrollBars.Both;
        _inventory.WordWrap = false;
        _inventory.Dock = DockStyle.Fill;
        _inventory.BackColor = UiStyle.Surface;
        _inventory.ForeColor = UiStyle.Foreground;
        _inventory.Font = new Font("Consolas", 10f);
        _inventory.Text = "Connect to AD first to read directory inventory.";
        inventoryLayout.Controls.Add(_inventory, 0, 1);
        _inventoryPage.Controls.Add(inventoryLayout);
        _stepHost.Controls.Add(_inventoryPage);

        _reviewPage.Dock = DockStyle.Fill;
        var reviewLayout = new TableLayoutPanel {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1
        };
        reviewLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
        reviewLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        reviewLayout.Controls.Add(new Label {
            Text = "Review AD Connection & Permissions" + Environment.NewLine +
                "The initial ACL assessment is read-only, not proof of write access. " +
                "OU/GPO and SYSVOL rights are rechecked before deployment.",
            Dock = DockStyle.Fill, ForeColor = UiStyle.Navy,
            Font = new Font("Segoe UI Semibold", 12f)
        }, 0, 0);
        _review.Multiline = true;
        _review.ReadOnly = true;
        _review.WordWrap = true;
        _review.ScrollBars = RichTextBoxScrollBars.Vertical;
        _review.HideSelection = false;
        _review.BorderStyle = BorderStyle.None;
        _review.Dock = DockStyle.Fill;
        _review.BackColor = UiStyle.Surface; // Neutral: connection does not prove write rights.
        _review.Font = new Font("Segoe UI", 11f);
        reviewLayout.Controls.Add(_review, 0, 1);
        _reviewPage.Controls.Add(reviewLayout);
        _stepHost.Controls.Add(_reviewPage);

        var footer = new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, AutoScroll = true, Padding = new Padding(4, 11, 4, 3)
        };
        _offline.Text = "Work offline (no AD)";
        _offline.Width = 190; _offline.Height = 38;
        _offline.Click += (_, _) => {
            if (_busy) return;
            WorkOfflineChosen = true;
            DialogResult = DialogResult.Cancel;
            Close();
        };
        footer.Controls.Add(_offline);
        _continue.Text = "Open campaign editor";
        _continue.Width = 201; _continue.Height = 38;
        _continue.Enabled = false;
        _continue.Click += (_, _) => {
            if (Snapshot is null || _page != 2) return;
            DialogResult = DialogResult.OK;
            Close();
        };
        footer.Controls.Add(_continue);
        _next.Text = "Next >";
        _next.Width = 125; _next.Height = 38;
        _next.Click += (_, _) => { if (Snapshot is not null) ShowPage(2); };
        footer.Controls.Add(_next);
        _connect.Text = "Sign in & Read AD";
        _connect.Width = 205; _connect.Height = 38;
        _connect.BackColor = UiStyle.Accent;
        _connect.ForeColor = Color.White;
        _connect.Tag = "PrimaryAction";
        _connect.Click += async (_, _) => await ConnectAsync();
        footer.Controls.Add(_connect);
        _back.Text = "< Back";
        _back.Width = 125; _back.Height = 38;
        _back.Click += (_, _) => ShowPage(Math.Max(0, _page - 1));
        footer.Controls.Add(_back);
        root.Controls.Add(footer, 0, 2);
        root.SetColumnSpan(footer, 2);
        ShowPage(0);
    }

    private void ShowPage(int page)
    {
        _page = Math.Clamp(page, 0, 2);
        _connectionPage.Visible = _page == 0;
        _inventoryPage.Visible = _page == 1;
        _reviewPage.Visible = _page == 2;
        (_page switch { 0 => _connectionPage, 1 => _inventoryPage, _ => _reviewPage }).BringToFront();
        for (int i = 0; i < _railSteps.Length; i++)
        {
            _railSteps[i].ForeColor = i == _page ? Color.White : i < _page ? UiStyle.Green : UiStyle.Navy;
            _railSteps[i].BackColor = i == _page ? UiStyle.Accent : UiStyle.SoftBlue;
        }
        _back.Enabled = _page > 0 && !_busy;
        _connect.Visible = _page == 0;
        _next.Visible = _page == 1;
        _next.Enabled = Snapshot is not null && !_busy;
        _continue.Visible = _page == 2;
        _continue.Enabled = Snapshot is not null && !_busy;
        AcceptButton = _page switch { 0 => _connect, 1 => _next, _ => _continue };
    }

    private void ScheduleDcDiscovery()
    {
        if (_busy || IsDisposed || Disposing) return;
        _discoveryRevision++;
        _dnsDebounce.Stop();
        _dnsDebounce.Start();
    }

    private async Task DiscoverDcListAsync()
    {
        string domain = _domain.Text.Trim();
        int revision = _discoveryRevision;
        if (domain.Length == 0) domain = DcDnsDiscovery.LocalDnsDomain();
        string expected = domain;
        if (string.IsNullOrWhiteSpace(expected)) return;
        try
        {
            _refreshDc.Enabled = false;
            _dcDiscoveryStatus.Text = "Searching DNS and Windows DC Locator...";
            var candidates = await Task.Run(() => DcDnsDiscovery.FindControllers(expected));
            if (IsDisposed || Disposing || _busy || revision != _discoveryRevision ||
                !string.Equals(_domain.Text.Trim(), expected, StringComparison.OrdinalIgnoreCase))
                return;
            string selected = _dc.Text.Trim();
            _mutating = true;
            try
            {
                _dc.BeginUpdate();
                _dc.Items.Clear();
                foreach (string value in candidates) _dc.Items.Add(value);
                if (selected.Length > 0 && !_dc.Items.Contains(selected))
                    _dc.Items.Insert(0, selected);
                _dc.Text = selected.Length > 0 ? selected :
                    candidates.Count > 0 ? candidates[0] : "";
                int longest = candidates.Count == 0 ? 750 : candidates
                    .Select(name => TextRenderer.MeasureText(name, _dc.Font).Width + 65).Max();
                _dc.DropDownWidth = Math.Clamp(longest, 750, 1100);
                _tips.SetToolTip(_dc, _dc.Text);
            }
            finally
            {
                _dc.EndUpdate();
                _mutating = false;
            }
            _dcDiscoveryStatus.Text = candidates.Count > 0
                ? $"{candidates.Count} controller(s) discovered. Select one or enter another DC."
                : "No DC found. Check DNS / network / VPN, or enter an explicit DC FQDN.";
            if (candidates.Count == 0)
                _tips.SetToolTip(_dc, "No DNS SRV / Netlogon DC result. Check DNS and domain reachability.");
        }
        catch (Exception ex)
        {
            _dcDiscoveryStatus.Text = "Discovery failed. Check DNS/network or enter DC manually.";
            AppLog.Write(ex, "DNS domain controller discovery");
        }
        finally
        {
            if (!IsDisposed && !Disposing) _refreshDc.Enabled = true;
        }
    }

    private static string CurrentWindowsIdentity()
    {
        try { return System.Security.Principal.WindowsIdentity.GetCurrent().Name; }
        catch { return Environment.UserDomainName + @"\" + Environment.UserName; }
    }

    private bool EditAlternateAccount()
    {
        if (_busy) return false;
        // Share the same two-page account lookup/password Wizard with GPO
        // Deployment. Account search is read-only; password is entered after
        // a specific account has been chosen.
        using var chooser = new AdAlternateAccountWizard(
            _domain.Text.Trim(),
            _advancedDc.Checked ? _dc.Text.Trim() : "",
            _user.Text, _password.Text, _rememberUsername.Checked,
            _saveEncryptedPassword.Checked, allowSaving: true,
            forgetSavedLogin: ForgetCredentials);
        if (chooser.ShowDialog(this) != DialogResult.OK) return false;
        _mutating = true;
        try
        {
            _user.Text = chooser.SelectedUsername;
            _password.Text = chooser.SelectedPassword;
            _rememberUsername.Checked = chooser.RememberUsername;
            _saveEncryptedPassword.Checked = chooser.SaveEncryptedPassword;
        }
        finally { _mutating = false; }
        SetCredentialMode();
        return true;
    }

    private void SetCredentialMode()
    {
        _user.Enabled = _password.Enabled = _alternate.Checked;
        _rememberUsername.Enabled = _saveEncryptedPassword.Enabled = _alternate.Checked;
        _editOtherAccount.Enabled = _alternate.Checked;
        _activeIdentity.Text = _alternate.Checked
            ? "Selected AD account: " + (string.IsNullOrWhiteSpace(_user.Text)
                ? "Not configured" : _user.Text.Trim())
            : "Signed-in Windows identity: " + CurrentWindowsIdentity();
        _tips.SetToolTip(_activeIdentity, _activeIdentity.Text);
        if (!_alternate.Checked) _password.Clear();
    }

    private void LoadSavedCredentials()
    {
        string domain = _domain.Text.Trim();
        if (string.IsNullOrWhiteSpace(domain)) return;
        try
        {
            var saved = AdCredentialVault.Read(domain);
            if (saved is null) return;
            _loadedCredentialDomain = domain;
            _user.Text = saved.Username;
            _rememberUsername.Checked = saved.Username.Length > 0;
            _saveEncryptedPassword.Checked = saved.HasPassword;
            // Never automatically switch the Windows identity to a stored one.
            // The operator must explicitly choose "Use another AD account".
            if (saved.HasPassword && _alternate.Checked) _password.Text = saved.Password;
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Read saved AD credential metadata");
        }
    }

    private void ForgetCredentials()
    {
        try
        {
            AdCredentialVault.Delete(_domain.Text.Trim());
            _loadedCredentialDomain = "";
            _mutating = true;
            try
            {
                _password.Clear();
                _user.Clear();
                _saveEncryptedPassword.Checked = false;
                _rememberUsername.Checked = false;
            }
            finally { _mutating = false; }
            InvalidateSnapshot();
            UiStyle.ApplyStatus(_state,
                "Saved AD credentials removed from Windows Credential Manager.",
                UiStatusKind.Success);
        }
        catch (Exception ex)
        {
            UiStyle.ApplyStatus(_state,
                "Unable to remove saved credentials: " +
                UniversalDiagnosticsEmbedded.Clean(ex.Message), UiStatusKind.Error);
        }
    }

    private void InvalidateSnapshot()
    {
        if (_mutating) return;
        Snapshot = null;
        ConnectedCredentials = null;
        _next.Enabled = _continue.Enabled = false;
        if (_page > 0) ShowPage(0);
        UiStyle.ApplyStatus(_state,
            "CONNECTION CHANGED — read AD again to continue.", UiStatusKind.Warning);
    }

    private async Task ConnectAsync()
    {
        if (_busy) return;
        if (_alternate.Checked && (string.IsNullOrWhiteSpace(_user.Text) ||
            string.IsNullOrEmpty(_password.Text)))
        {
            UiStyle.ApplyStatus(_state,
                "Enter both AD username and password, or use your current Windows account.",
                UiStatusKind.Warning);
            if (string.IsNullOrWhiteSpace(_user.Text)) _user.Focus();
            else _password.Focus();
            return;
        }
        _busy = true;
        Snapshot = null;
        _next.Enabled = _continue.Enabled = false;
        _dnsDebounce.Stop();
        _discoveryRevision++;
        _connect.Enabled = _offline.Enabled = _back.Enabled = false;
        _domain.Enabled = _dc.Enabled = _current.Enabled = _alternate.Enabled = false;
        _user.Enabled = _password.Enabled = false;
        UiStyle.ApplyStatus(_state,
            "CONNECTING — signed LDAP read in progress...", UiStatusKind.Busy);
        try
        {
            string domain = _domain.Text.Trim();
            string dc = _advancedDc.Checked ? _dc.Text.Trim() : "";
            var credential = _alternate.Checked
                ? new DomainCredentials(domain, _user.Text.Trim(), _password.Text)
                : new DomainCredentials(domain, "", "");
            var result = await Task.Run(() =>
                DomainPowerShellRunner.Execute(DomainOperation.Discovery, "Snapshot",
                    new { domain, dc }, credential));
            if (IsDisposed || Disposing) return;
            var snapshot = AdDirectorySnapshot.Parse(result, domain, _company);
            if (dc.Length > 0 &&
                !string.Equals(dc, snapshot.Dc, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(dc.Split('.')[0], snapshot.Dc.Split('.')[0],
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("DC returned a different server than requested.");
            Snapshot = snapshot;
            ConnectedCredentials = credential;
            _mutating = true;
            try
            {
                _domain.Text = snapshot.Domain;
                _advancedDc.Checked = false;
                _dc.Items.Clear();
                foreach (var name in snapshot.DomainControllers.Distinct(
                    StringComparer.OrdinalIgnoreCase).Take(100))
                    _dc.Items.Add(name);
                if (!snapshot.DomainControllers.Contains(snapshot.Dc,
                    StringComparer.OrdinalIgnoreCase))
                    _dc.Items.Insert(0, snapshot.Dc);
                _dc.Text = snapshot.Dc;
                _dc.DropDownWidth = Math.Max(750, snapshot.DomainControllers.Count == 0
                    ? 750 : snapshot.DomainControllers.Max(name =>
                        TextRenderer.MeasureText(name, _dc.Font).Width + 65));
                _tips.SetToolTip(_dc, snapshot.Dc);
            }
            finally { _mutating = false; }

            _inventory.Text = BuildInventory(snapshot);
            // Advisory, read-only ACL assessment. Never mistake an ACL hint
            // for an authorization to deploy: COM/SYSVOL and scoped rights
            // still require fresh deployment preflight.
            string permissionSummary;
            if (!snapshot.WritableKnown)
                permissionSummary = "WRITE ACCESS: NOT CONFIRMED" +
                    Environment.NewLine +
                    "GPO CREATE: Unknown - cannot verify DC write capability." +
                    Environment.NewLine +
                    "OU LINK: Requires writable DC verification and selected OU." +
                    Environment.NewLine +
                    "SYSVOL WRITE: Not verified (no files created).";
            else if (!snapshot.Writable)
                permissionSummary = "WRITE ACCESS: NOT CONFIRMED" +
                    Environment.NewLine +
                    "GPO CREATE: Unavailable on confirmed read-only DC." +
                    Environment.NewLine +
                    "OU LINK: Select a writable DC and an OU.";
            else
            {
                try
                {
                    var permissionResult = await Task.Run(() =>
                        DomainPowerShellRunner.Execute(DomainOperation.Discovery, "Permissions",
                            new { domain = snapshot.Domain, dc = snapshot.Dc,
                                ouDn = _company?.DefaultOuDn ?? "" },
                            credential));
                    var permissionData = permissionResult.GetProperty("data");
                    string creationHint = permissionData.GetProperty("gpoCreateAce").GetString()
                        ?? "UNKNOWN";
                    string linkHint = permissionData.GetProperty("ouGpoLinkAce").GetString()
                        ?? "UNKNOWN";
                    string selectedOu = _company?.DefaultOuDn ?? "";
                    bool groupsComplete = permissionData.TryGetProperty("groupTokenComplete",
                        out var tokenValue) && tokenValue.ValueKind == JsonValueKind.True;
                    permissionSummary =
                        "WRITE ACCESS: NOT CONFIRMED (no write operations performed)" +
                        Environment.NewLine +
                        "ACCOUNT GROUP SIDs: " + (groupsComplete ? "Loaded" : "Incomplete / unknown") +
                        Environment.NewLine +
                        "GPO CREATE: " + DescribeAclHint(creationHint) +
                        Environment.NewLine +
                        (selectedOu.Length > 0
                            ? "DEFAULT OU LINK: " + DescribeAclHint(linkHint)
                            : "OU LINK: Requires OU selection") + Environment.NewLine +
                        "GPO EDIT / DELETE: Requires target GPO selection" +
                        Environment.NewLine +
                        "SYSVOL WRITE: Unknown (no files created)";
                }
                catch (Exception permissionError)
                {
                    AppLog.Write(permissionError, "AD permission assessment");
                    permissionSummary = "GPO CREATION ACL: Unknown (assessment unavailable)" +
                        Environment.NewLine + "OU LINKING: Requires OU selection" +
                        Environment.NewLine + "SYSVOL WRITE: Not verified" +
                        Environment.NewLine +
                        "Check Diagnostics for permission assessment details.";
                }
            }
            if (IsDisposed || Disposing) return;
            SetReviewText("DOMAIN: " + snapshot.Domain + Environment.NewLine +
                "CONNECTED DC: " + snapshot.Dc + Environment.NewLine +
                "DC WRITABLE: " + (!snapshot.WritableKnown
                    ? "Unknown (NTDS Settings status unavailable)"
                    : snapshot.Writable ? "Yes (controller capability)" : "No (RODC verified)") +
                Environment.NewLine + "READ UTC: " + snapshot.ObservedUtc.ToString("O") +
                Environment.NewLine + Environment.NewLine +
                $"Read-only inventory: {snapshot.DomainControllers.Count} DCs, " +
                $"{snapshot.OrganizationalUnits.Count} OUs, " +
                $"{snapshot.ComputerGroups.Count} security groups, " +
                $"{snapshot.GroupPolicies.Count} GPOs." +
                Environment.NewLine + Environment.NewLine +
                "No changes have been made to AD or Group Policy." +
                Environment.NewLine + Environment.NewLine +
                "You can edit and save campaigns locally without administrator rights." +
                Environment.NewLine +
                Environment.NewLine + "PERMISSION ASSESSMENT (READ-ONLY)" +
                Environment.NewLine + permissionSummary +
                Environment.NewLine + Environment.NewLine +
                "ACL hints are not proof of effective write rights. " +
                "Deployment independently verifies the exact OU/GPO and SYSVOL scope.");
            // Commit persistence only after successful signed+sealed LDAP authentication.
            if (_alternate.Checked)
            {
                try
                {
                    AdCredentialVault.Save(snapshot.Domain, _user.Text,
                        _password.Text, _rememberUsername.Checked,
                        _saveEncryptedPassword.Checked);
                    _loadedCredentialDomain = snapshot.Domain;
                }
                catch (Exception storageError)
                {
                    // An authenticated session remains valid if optional persistence fails.
                    AppLog.Write(storageError, "Save AD credential preference");
                    MessageBox.Show(this,
                        "AD connected, but Windows could not save the requested credentials. " +
                        "Check Windows Credential Manager permissions.",
                        "Credential saving", MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            }
            // Clearing the transient password is NOT a change of AD scope.
            // Suppress TextChanged invalidation so the freshly verified
            // directory snapshot stays valid for Review and Finish.
            _mutating = true;
            try { _password.Clear(); }
            finally { _mutating = false; }
            ShowPage(1);
            UiStyle.ApplyStatus(_state,
                $"CONNECTED / READ ONLY — {snapshot.Domain} | {snapshot.Dc} | " +
                (snapshot.Writable ? "Writable DC" : "Read-only DC") + " | " +
                snapshot.ObservedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"),
                snapshot.Capped ? UiStatusKind.Warning : UiStatusKind.Success);
        }
        catch (Exception ex)
        {
            if (IsDisposed || Disposing) return;
            string details = UniversalDiagnosticsEmbedded.Clean(ex.Message);
            bool invalidCredentials = details.Contains("credential is invalid",
                    StringComparison.OrdinalIgnoreCase) ||
                details.Contains("invalid credentials", StringComparison.OrdinalIgnoreCase) ||
                details.Contains("logon failure", StringComparison.OrdinalIgnoreCase);
            UiStyle.ApplyStatus(_state,
                invalidCredentials
                    ? "Sign-in failed. Check the AD username, password and selected domain."
                    : "AD connection failed. Check the domain, DC and network. Details in Diagnostics.",
                UiStatusKind.Error);
            _inventory.Text = "No AD data accepted. Check domain, DNS, DC availability, " +
                "permissions and your Windows/alternate account.\r\n" +
                "No AD/GPO settings were changed.\r\n\r\n" +
                UniversalDiagnosticsEmbedded.Clean(ex.Message);
        }
        finally
        {
            if (!IsDisposed && !Disposing)
            {
                _connect.Enabled = _offline.Enabled = true;
                _domain.Enabled = _dc.Enabled = _current.Enabled = _alternate.Enabled = true;
                _busy = false;
                SetCredentialMode();
                ShowPage(_page);
            }
        }
    }

    private void SetReviewText(string text)
    {
        // Use RichTextBox for readable per-status emphasis without changing
        // report wording or implying that an ACL hint is a write test.
        _review.SuspendLayout();
        try
        {
            _review.Text = text;
            string contents = _review.Text;
            var navy = UiStyle.Navy;
            var green = Color.FromArgb(20, 120, 78);
            var amber = Color.FromArgb(161, 95, 19);
            var red = Color.FromArgb(175, 47, 47);
            var muted = UiStyle.Muted;

            void Paint(int index, int length, Color color)
            {
                if (index < 0 || length <= 0 || index + length > contents.Length) return;
                _review.Select(index, length);
                _review.SelectionColor = color;
            }

            int start = 0;
            foreach (string line in _review.Lines)
            {
                int index = contents.IndexOf(line, start, StringComparison.Ordinal);
                if (index < 0) continue;
                start = index + line.Length;
                int colon = line.IndexOf(':');
                if (colon is > 0 and < 38)
                    Paint(index, colon + 1, navy);
                if (line.StartsWith("PERMISSION ASSESSMENT", StringComparison.Ordinal) ||
                    line.StartsWith("Read-only inventory:", StringComparison.Ordinal))
                    Paint(index, line.Length, navy);

                Color? statusColor = null;
                if (line.StartsWith("No changes have been made", StringComparison.Ordinal) ||
                    line.Contains("Loaded", StringComparison.Ordinal) ||
                    line.Contains("Yes (controller capability)", StringComparison.Ordinal))
                    statusColor = green;
                if (line.Contains("NOT CONFIRMED", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Unknown", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Not verified", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Requires", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("not a proof", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("not proof", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("Potential Allow", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("No matching Allow", StringComparison.OrdinalIgnoreCase))
                    statusColor = amber;
                if (line.Contains("Deny", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("No (RODC verified)", StringComparison.Ordinal) ||
                    line.Contains("Unavailable on confirmed read-only DC", StringComparison.Ordinal))
                    statusColor = red;
                if (statusColor is not null)
                {
                    int valueOffset = colon is >= 0 and < 38 ? colon + 1 : 0;
                    Paint(index + valueOffset, line.Length - valueOffset, statusColor.Value);
                }
                else if (line.StartsWith("ACL hints", StringComparison.Ordinal))
                    Paint(index, line.Length, muted);
            }
            _review.Select(0, 0);
            _review.ScrollToCaret();
        }
        finally { _review.ResumeLayout(); }
    }

    private static string DescribeAclHint(string hint) => hint switch
    {
        "POTENTIAL_ALLOW" => "Potential Allow (ACL only; not verified)",
        "POSSIBLE_DENY" => "Possible Deny (review delegated rights)",
        "NO_MATCHING_ALLOW" => "No matching Allow ACE (not a proven denial)",
        "REQUIRES_OU_SELECTION" => "Requires OU selection",
        "UNKNOWN_INCOMPLETE_TOKEN" => "Unknown (AD security token incomplete)",
        _ => "Unknown (permission assessment unavailable)"
    };

    private static string BuildInventory(AdDirectorySnapshot snapshot)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("READ-ONLY ACTIVE DIRECTORY INVENTORY");
        text.AppendLine($"Domain: {snapshot.Domain}  |  DC: {snapshot.Dc}" +
            (!snapshot.WritableKnown ? " (Unknown DC write capability)" :
                snapshot.Writable ? " (Writable)" : " (Read-only DC)"));
        text.AppendLine($"Observed (UTC): {snapshot.ObservedUtc:yyyy-MM-dd HH:mm:ss}");
        text.AppendLine($"Domain Controllers: {snapshot.DomainControllers.Count}  |  " +
            $"OUs: {snapshot.OrganizationalUnits.Count}  |  " +
            $"Security groups: {snapshot.ComputerGroups.Count}  |  " +
            $"GPOs: {snapshot.GroupPolicies.Count}");
        if (snapshot.Capped)
            text.AppendLine("WARNING: at least one list reached the limit; inventory may be incomplete.");
        static void Section(System.Text.StringBuilder sb, string title, IReadOnlyList<string> values)
        {
            sb.AppendLine();
            sb.AppendLine(title + ":");
            foreach (var value in values.Take(30)) sb.AppendLine("  " + value);
            if (values.Count > 30) sb.AppendLine($"  ... and {values.Count - 30} more");
        }
        Section(text, "Domain controllers", snapshot.DomainControllers);
        Section(text, "Organizational units", snapshot.OrganizationalUnits);
        Section(text, "Computer security groups", snapshot.ComputerGroups);
        Section(text, "Existing GPO names", snapshot.GroupPolicies);
        text.AppendLine();
        text.AppendLine("Read-only discovery completed. GPO operations still require fresh scoped validation.");
        return text.ToString();
    }
}
