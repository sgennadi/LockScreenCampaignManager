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
        bool capped = data.GetProperty("capped").GetBoolean();
        DateTimeOffset when = data.GetProperty("observedUtc").GetDateTimeOffset().ToUniversalTime();
        var now = DateTimeOffset.UtcNow;
        if (when < now.AddMinutes(-10) || when > now.AddMinutes(5))
            throw new InvalidDataException("AD inventory timestamp is stale or in the future.");

        return new AdDirectorySnapshot(domain, dc, writable, when, capped,
            Names(data, "controllers"), Names(data, "ous"),
            Names(data, "groups"), Names(data, "gpos"));
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
    private readonly RadioButton _current = new();
    private readonly RadioButton _alternate = new();
    private readonly TextBox _user = new();
    private readonly TextBox _password = new();
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
    private readonly TextBox _review = new();
    private readonly ToolTip _tips = new() { AutoPopDelay = 22000, InitialDelay = 300, ReshowDelay = 200 };
    private readonly System.Windows.Forms.Timer _dnsDebounce = new() { Interval = 650 };
    private int _discoveryRevision;
    private int _page;
    private bool _busy;
    private bool _mutating;
    internal AdDirectorySnapshot? Snapshot { get; private set; }
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
        }
        finally { _mutating = false; }

        _domain.TextChanged += (_, _) => { InvalidateSnapshot(); ScheduleDcDiscovery(); };
        _dc.TextChanged += (_, _) => { InvalidateSnapshot(); _tips.SetToolTip(_dc, _dc.Text); };
        _user.TextChanged += (_, _) => InvalidateSnapshot();
        _password.TextChanged += (_, _) => InvalidateSnapshot();
        _current.CheckedChanged += (_, _) => { SetCredentialMode(); InvalidateSnapshot(); };
        _alternate.CheckedChanged += (_, _) => { SetCredentialMode(); InvalidateSnapshot(); };
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
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 264));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        Controls.Add(root);

        var title = new TableLayoutPanel {
            Dock = DockStyle.Fill, RowCount = 2, BackColor = UiStyle.Navy,
            Padding = new Padding(20, 12, 16, 8)
        };
        title.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
        title.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        title.Controls.Add(new Label {
            Text = "LockScreen Campaign Manager  |  AD Connection Wizard",
            Dock = DockStyle.Fill, ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 17f, FontStyle.Bold),
            AutoEllipsis = false
        }, 0, 0);
        title.Controls.Add(new Label {
            Text = "Read-only LDAP inventory first. No GPO changes, RSAT or WinRM.",
            Dock = DockStyle.Fill, ForeColor = Color.FromArgb(223,238,255),
            Font = new Font("Segoe UI", 10f)
        }, 0, 1);
        root.Controls.Add(title, 0, 0);
        root.SetColumnSpan(title, 2);

        var rail = new TableLayoutPanel {
            Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1,
            Padding = new Padding(7, 14, 12, 10), BackColor = UiStyle.SoftBlue
        };
        rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 92));
        rail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        string[] steps = [
            "1  Connect to Domain Controller",
            "2  Read Active Directory",
            "3  Review and Confirm"
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
            Text = "Next: configure images, schedule and GPO deployment. " +
                "Each deployment independently revalidates the selected AD scope.",
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
        connection.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        connection.RowStyles.Add(new RowStyle(SizeType.Absolute, 165));
        connection.RowStyles.Add(new RowStyle(SizeType.Absolute, 85));
        connection.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        connection.Controls.Add(new Label {
            Text = "Connect to Domain Controller" + Environment.NewLine +
                "Detect the local AD DNS domain and select a DC discovered through DNS SRV.",
            Dock = DockStyle.Fill, ForeColor = UiStyle.Navy,
            Font = new Font("Segoe UI Semibold", 12f)
        }, 0, 0);
        var servers = new FlowLayoutPanel {
            Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true,
            BackColor = UiStyle.SoftBlue, Padding = new Padding(10, 12, 6, 5)
        };
        _domain.PlaceholderText = "ad.company.example";
        _dc.DropDownStyle = ComboBoxStyle.DropDown;
        _dc.DropDownWidth = 750;
        _dc.MaxDropDownItems = 18;
        _dc.IntegralHeight = false;
        _dc.Text = "";
        servers.Controls.Add(Field("AD DNS domain (detected automatically)", _domain, 380));
        servers.Controls.Add(Field("Domain controller (DNS SRV auto-discovery)", _dc, 475));
        connection.Controls.Add(servers, 0, 1);

        var authentication = new FlowLayoutPanel {
            Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true,
            BackColor = UiStyle.Surface, Padding = new Padding(6, 9, 8, 0)
        };
        _current.Text = "Current Windows account";
        _alternate.Text = "Other domain account (session only)";
        _current.AutoSize = true;
        _alternate.AutoSize = true;
        authentication.Controls.Add(_current);
        authentication.Controls.Add(_alternate);
        _user.PlaceholderText = @"DOMAIN\user / user@domain";
        _password.UseSystemPasswordChar = true;
        authentication.Controls.Add(Field("AD user", _user, 315));
        authentication.Controls.Add(Field("Password — never saved", _password, 285));
        connection.Controls.Add(authentication, 0, 2);
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
            Text = "Review and Confirm Scope" + Environment.NewLine +
                "Finish makes this read-only inventory available to the campaign editor. " +
                "GPO deployment will separately validate permissions and selected targets.",
            Dock = DockStyle.Fill, ForeColor = UiStyle.Navy,
            Font = new Font("Segoe UI Semibold", 12f)
        }, 0, 0);
        _review.Multiline = true;
        _review.ReadOnly = true;
        _review.WordWrap = true;
        _review.ScrollBars = ScrollBars.Vertical;
        _review.Dock = DockStyle.Fill;
        _review.BackColor = UiStyle.SoftGreen;
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
        _continue.Text = "Finish";
        _continue.Width = 125; _continue.Height = 38;
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
        _connect.Text = "Connect & Read AD";
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
            if (candidates.Count == 0)
                _tips.SetToolTip(_dc, "No DC DNS SRV answers. Check DNS or enter a DC manually.");
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "DNS domain controller discovery");
        }
    }

    private void SetCredentialMode()
    {
        _user.Enabled = _password.Enabled = _alternate.Checked;
        if (!_alternate.Checked) _password.Clear();
    }

    private void InvalidateSnapshot()
    {
        if (_mutating) return;
        Snapshot = null;
        _next.Enabled = _continue.Enabled = false;
        if (_page > 0) ShowPage(0);
        UiStyle.ApplyStatus(_state,
            "CONNECTION CHANGED — read AD again to continue.", UiStatusKind.Warning);
    }

    private async Task ConnectAsync()
    {
        if (_busy) return;
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
            string dc = _dc.Text.Trim();
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
            _mutating = true;
            try
            {
                _domain.Text = snapshot.Domain;
                _dc.Text = snapshot.Dc;
                _dc.Items.Clear();
                foreach (var name in snapshot.DomainControllers.Distinct(
                    StringComparer.OrdinalIgnoreCase).Take(100))
                    _dc.Items.Add(name);
                _dc.DropDownWidth = Math.Max(750, snapshot.DomainControllers.Count == 0
                    ? 750 : snapshot.DomainControllers.Max(name =>
                        TextRenderer.MeasureText(name, _dc.Font).Width + 65));
                _tips.SetToolTip(_dc, snapshot.Dc);
            }
            finally { _mutating = false; }

            _inventory.Text = BuildInventory(snapshot);
            _review.Text = "DOMAIN: " + snapshot.Domain + Environment.NewLine +
                "CONNECTED DC: " + snapshot.Dc + Environment.NewLine +
                "DC WRITABLE: " + (snapshot.Writable ? "Yes" : "No (RODC)") +
                Environment.NewLine + "COMPANY: " + (_company?.Name ?? "(unassigned)") +
                Environment.NewLine + "DEFAULT OU: " + (_company?.DefaultOuDn ?? "(not selected)") +
                Environment.NewLine + "COMPUTER GROUP: " + (_company?.DefaultGroupDn ?? "(not selected)") +
                Environment.NewLine + "READ UTC: " + snapshot.ObservedUtc.ToString("O") +
                Environment.NewLine + Environment.NewLine +
                $"Inventory: {snapshot.DomainControllers.Count} DCs, " +
                $"{snapshot.OrganizationalUnits.Count} OUs, " +
                $"{snapshot.ComputerGroups.Count} security groups, " +
                $"{snapshot.GroupPolicies.Count} GPOs." +
                Environment.NewLine + Environment.NewLine +
                "No changes have been made to AD or Group Policy.";
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
            UiStyle.ApplyStatus(_state,
                "NOT CONNECTED — read failed: " +
                UniversalDiagnosticsEmbedded.Clean(ex.Message), UiStatusKind.Error);
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

    private static string BuildInventory(AdDirectorySnapshot snapshot)
    {
        var text = new System.Text.StringBuilder();
        text.AppendLine("READ-ONLY ACTIVE DIRECTORY INVENTORY");
        text.AppendLine($"Domain: {snapshot.Domain}  |  DC: {snapshot.Dc}" +
            (snapshot.Writable ? " (Writable)" : " (Read-only)"));
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
