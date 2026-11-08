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
    private bool _busy;
    private bool _mutating;
    internal AdDirectorySnapshot? Snapshot { get; private set; }
    internal bool WorkOfflineChosen { get; private set; }

    internal AdConnectionStartDialog(CompanyProfile? company = null)
    {
        _company = company;
        Text = "Step 1 — Connect to DC & Read AD";
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(770, 565);
        Size = new Size(920, 680);
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

        _domain.TextChanged += (_, _) => InvalidateSnapshot();
        _dc.TextChanged += (_, _) => InvalidateSnapshot();
        _user.TextChanged += (_, _) => InvalidateSnapshot();
        _password.TextChanged += (_, _) => InvalidateSnapshot();
        _current.CheckedChanged += (_, _) => { SetCredentialMode(); InvalidateSnapshot(); };
        _alternate.CheckedChanged += (_, _) => { SetCredentialMode(); InvalidateSnapshot(); };
        SetCredentialMode();
        UiStyle.Apply(this);
        UiStyle.ApplyStatus(_state, "NOT CONNECTED — no directory read has been performed.", UiStatusKind.Neutral);
        AcceptButton = _connect;
        CancelButton = _offline;
        UiWindowPlacement.Attach(this, "ad-start-connection");
        FormClosed += (_, _) => _password.Clear();
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
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6,
            Padding = new Padding(18), BackColor = UiStyle.Canvas
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 59));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 107));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        Controls.Add(root);

        var title = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        title.RowStyles.Add(new RowStyle(SizeType.Absolute, 33));
        title.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        title.Controls.Add(new Label
        {
            Text = "1  Connect to DC & Read Active Directory",
            Dock = DockStyle.Fill, ForeColor = UiStyle.Navy,
            Font = new Font("Segoe UI Semibold", 16, FontStyle.Bold)
        }, 0, 0);
        title.Controls.Add(new Label
        {
            Text = "Read-only LDAP inventory first. No GPO changes, RSAT or WinRM.",
            Dock = DockStyle.Fill, ForeColor = UiStyle.Muted
        }, 0, 1);
        root.Controls.Add(title, 0, 0);

        var servers = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true,
            BackColor = UiStyle.SoftBlue, Padding = new Padding(9, 7, 8, 0)
        };
        _domain.PlaceholderText = "ad.company.example";
        _dc.DropDownStyle = ComboBoxStyle.DropDown;
        _dc.Text = "";
        servers.Controls.Add(Field("AD DNS domain (blank = detect local)", _domain, 270));
        servers.Controls.Add(Field("Domain controller (blank = discover)", _dc, 280));
        root.Controls.Add(servers, 0, 1);

        var authentication = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true,
            BackColor = UiStyle.Surface, Padding = new Padding(7, 4, 8, 0)
        };
        _current.Text = "Current Windows account";
        _alternate.Text = "Other domain account (session only)";
        _current.AutoSize = true;
        _alternate.AutoSize = true;
        authentication.Controls.Add(_current);
        authentication.Controls.Add(_alternate);
        _user.PlaceholderText = @"DOMAIN\user / user@domain";
        _password.UseSystemPasswordChar = true;
        authentication.Controls.Add(Field("AD user", _user, 225));
        authentication.Controls.Add(Field("Password — never saved", _password, 195));
        root.Controls.Add(authentication, 0, 2);

        _state.Dock = DockStyle.Fill;
        _state.TextAlign = ContentAlignment.MiddleLeft;
        root.Controls.Add(_state, 0, 3);
        _inventory.Multiline = true;
        _inventory.ReadOnly = true;
        _inventory.ScrollBars = ScrollBars.Both;
        _inventory.WordWrap = false;
        _inventory.Dock = DockStyle.Fill;
        _inventory.BackColor = UiStyle.Surface;
        _inventory.ForeColor = UiStyle.Foreground;
        _inventory.Font = new Font("Consolas", 9.5f);
        _inventory.Text = "AD directory inventory is not available yet.\r\n\r\n" +
            "First connect to a domain controller. A successful read will list:\r\n" +
            " - DCs and writable/read-only status\r\n" +
            " - Organizational Units\r\n" +
            " - Computer security groups\r\n" +
            " - Existing GPO names\r\n\r\n" +
            "Work offline is optional. It does not enable deployment.";
        root.Controls.Add(_inventory, 0, 4);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 9, 0, 0)
        };
        _connect.Text = "Connect & Read AD";
        _connect.AutoSize = true;
        _connect.MinimumSize = new Size(180, 37);
        _connect.BackColor = UiStyle.Accent;
        _connect.ForeColor = Color.White;
        _connect.Tag = "PrimaryAction";
        _connect.Click += async (_, _) => await ConnectAsync();
        footer.Controls.Add(_connect);
        _continue.Text = "2  Continue to Campaigns";
        _continue.MinimumSize = new Size(225, 37);
        _continue.AutoSize = true;
        _continue.Enabled = false;
        _continue.Click += (_, _) =>
        {
            if (Snapshot is null) return;
            DialogResult = DialogResult.OK;
            Close();
        };
        footer.Controls.Add(_continue);
        _offline.Text = "Work offline (no AD)";
        _offline.AutoSize = true;
        _offline.MinimumSize = new Size(175, 37);
        _offline.Click += (_, _) =>
        {
            if (_busy) return;
            WorkOfflineChosen = true;
            DialogResult = DialogResult.Cancel;
            Close();
        };
        footer.Controls.Add(_offline);
        root.Controls.Add(footer, 0, 5);
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
        _continue.Enabled = false;
        UiStyle.ApplyStatus(_state,
            "CONNECTION CHANGED — read AD again to continue.", UiStatusKind.Warning);
    }

    private async Task ConnectAsync()
    {
        if (_busy) return;
        _busy = true;
        Snapshot = null;
        _continue.Enabled = false;
        _connect.Enabled = _offline.Enabled = false;
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
            }
            finally { _mutating = false; }

            _inventory.Text = BuildInventory(snapshot);
            _continue.Enabled = true;
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
