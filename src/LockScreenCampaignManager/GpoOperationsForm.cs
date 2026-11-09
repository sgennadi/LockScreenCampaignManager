using System.Drawing;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Safety-first admin console. No Active Directory writes without a read-only preview,
/// exact GUID confirmation and explicit user action. No RSAT and no WinRM.
/// </summary>
internal sealed partial class GpoOperationsForm : Form
{
    private readonly string _domain;
    private readonly string _dc;
    private readonly string _ouDn;
    private readonly string _groupDn;
    private readonly DomainCredentials _credentials;
    private readonly CampaignProject _project;
    private readonly ListBox _policies = new();
    private readonly ComboBox _lifecycleAction = new();
    private readonly TextBox _lifecycleInfo = new();
    private readonly TextBox _computerName = new();
    private readonly TextBox _conflicts = new();
    private readonly CheckedListBox _computers = new();
    private readonly TextBox _pushInfo = new();
    private readonly ComboBox _pushPurpose = new();
    private readonly Label _status = new();
    private ClientFeedbackPanel? _clientHealth;
    private DeviceRepairDashboard? _deviceDashboard;
    private DcConsistencyPanel? _dcConsistency;
    private Button _applyGpo = new();
    private Button _push = new();
    private Button _lifecyclePreview = new();
    private Button _pushPreview = new();
    private bool _busy;
    private string? _gpoPlanHash;
    private string? _gpoPlanAction;
    private string? _gpoPlanId;
    private string? _pushPlanHash;
    private string? _pushPlanId;
    private string? _pushPlanPurpose;
    private string[] _pushPlanHosts = [];
    private string? _tempPackageRoot;
    private string? _tempPackage;
    private readonly string _version = AppVersion.Current;

    private sealed record GpoRow(string Id, string Name, string CampaignId, int Links)
    {
        public override string ToString() => $"{Name}  |  {CampaignId}  |  {Links} link(s)  |  {Id}";
    }
    private sealed record PcRow(string Name, string Sam,
        bool? RpcReachable = null, DateTimeOffset? CheckedUtc = null)
    {
        public override string ToString() => $"{Name}   ({Sam})" +
            (RpcReachable.HasValue
                ? (RpcReachable.Value ? "   |   RPC 135: reachable" : "   |   RPC 135: not reachable") +
                  "   |   checked " + CheckedUtc?.ToLocalTime().ToString("HH:mm:ss")
                : "   |   RPC: not checked");
    }

    internal GpoOperationsForm(string domain, string dc, string ouDn, string groupDn,
        DomainCredentials credentials, CampaignProject project)
    {
        _domain = domain;
        _dc = dc;
        _ouDn = ouDn;
        _groupDn = groupDn;
        _credentials = credentials;
        _project = project;
        Text = "LockScreen Campaign Manager - GPO Operations " + _version;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(920, 670);
        Size = new Size(1250, 820);
        BackColor = Color.FromArgb(247, 248, 251);
        Build();
        InitializeDeferred();
        Shown += async (_, _) => await ReloadAsync();
        FormClosing += (_, e) => {
            if (!_busy) return;
            e.Cancel = true;
            MessageBox.Show(this,
                "An AD or remote PC operation is still running. Check its result before closing.",
                "GPO Operations", MessageBoxButtons.OK, MessageBoxIcon.Information);
        };
        FormClosed += (_, _) => { CloseDeferred(); StopAgentManagement(); RemoveTempPackage(); };
        // Centralized UI font/DPI and Windows 11 styling; same tokens as the main window.
        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "gpo-operations");
    }

    private static Button ActionButton(string title, Action action, int width, bool primary = false)
    {
        var b = new Button { Text = title, Width = width, Height = 41,
            BackColor = primary ? UiStyle.Accent : UiStyle.Surface,
            ForeColor = primary ? Color.White : UiStyle.Foreground,
            FlatStyle = FlatStyle.Flat, Margin = new Padding(0, 4, 8, 5) };
        b.Click += (_, _) => action();
        return b;
    }
    private static TableLayoutPanel Panel(int rows = 3)
    {
        return new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1,
            RowCount = rows, Padding = new Padding(12), BackColor = UiStyle.Surface };
    }
    private static TextBox Output() => new()
    {
        Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
        ScrollBars = ScrollBars.Both, WordWrap = false,
        Font = new Font("Consolas", 9.2f), BackColor = UiStyle.Surface
    };
    private static Label Note(string text) => new()
    {
        Dock = DockStyle.Fill, Text = text, ForeColor = UiStyle.Muted,
        TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
    };

    private void Build()
    {
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1,
            RowCount = 3, Padding = new Padding(16), BackColor = UiStyle.Canvas };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        Controls.Add(root);
        var headline = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        headline.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        headline.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        headline.Controls.Add(new Label { Text = "Domain campaign management", Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 17f), ForeColor = UiStyle.Foreground }, 0, 0);
        headline.Controls.Add(Note("Domain: " + _domain + "  |  DC: " + _dc + "  |  OU: " + _ouDn), 0, 1);
        root.Controls.Add(headline, 0, 0);

        var tabs = new TabControl { Dock = DockStyle.Fill, Padding = new Point(15, 7) };
        _agentTabControl = tabs;
        tabs.SelectedIndexChanged += (_, _) => OnAgentManagementTabChanged();
        root.Controls.Add(tabs, 0, 1);
        tabs.TabPages.Add(BuildLifecycleTab());
        tabs.TabPages.Add(BuildConflictTab());
        tabs.TabPages.Add(BuildPushTab());
        tabs.TabPages.Add(new ComputerGroupAssignmentPanel(
            _domain, _dc, _ouDn, _groupDn, _credentials,
            () => _busy, running => _busy = running).AsTabPage());
        tabs.TabPages.Add(new SysvolStoragePanel(
            _domain, _dc, _project, _credentials,
            () => _busy, running => _busy = running).AsTabPage());
        _dcConsistency = new DcConsistencyPanel(
            _domain, _dc, _credentials,
            () => _policies.SelectedItem is GpoRow policy
                ? (policy.CampaignId, policy.Id) : ("", ""),
            () => _busy, running => _busy = running);
        tabs.TabPages.Add(_dcConsistency.AsTabPage());
        _clientHealth = new ClientFeedbackPanel(_domain, _dc, _ouDn, _groupDn, _credentials, _project, RepairFromHealthAsync);
        var clientHealthTab = _clientHealth.AsTabPage();
        _agentHealthTab = clientHealthTab;
        _deviceDashboard = new DeviceRepairDashboard(
            _domain, _dc, _ouDn, _groupDn, _credentials, _project,
            () => _policies.SelectedItem is GpoRow policy
                ? (policy.CampaignId, policy.Id) : ("", ""),
            () => _clientHealth.FeedbackShareRoot,
            RepairFromHealthAsync,
            () => tabs.SelectedTab = clientHealthTab);
        tabs.TabPages.Add(_deviceDashboard.AsTabPage());
        tabs.TabPages.Add(clientHealthTab);
        tabs.TabPages.Add(BuildDeferredTab());
        tabs.TabPages.Add(BuildGmsaTab());
        _status.Text = "Read-only until explicit Preview + Apply. No automatic AD changes.";
        _status.Dock = DockStyle.Fill;
        _status.ForeColor = UiStyle.Muted;
        _status.AutoEllipsis = true;
        root.Controls.Add(_status, 0, 2);
    }

    private TabPage BuildLifecycleTab()
    {
        var page = new TabPage("GPO: Disable / Reuse / Delete") { BackColor = UiStyle.Canvas };
        var layout = Panel(4);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 64));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        page.Controls.Add(layout);
        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false };
        top.Controls.Add(ActionButton("Refresh GPO list", () => _ = ReloadAsync(), 180));
        top.Controls.Add(Note("Only policies containing our agent and campaign.json are managed."));
        layout.Controls.Add(top, 0, 0);
        _policies.Dock = DockStyle.Fill;
        _policies.IntegralHeight = false;
        _policies.HorizontalScrollbar = true;
        _policies.SelectedIndexChanged += (_, _) => {
            InvalidatePlans();
            var managed = _policies.SelectedItem as GpoRow;
            if (managed is not null && _clientHealth is not null)
                _clientHealth.SetSelectedManagedCampaign(managed.CampaignId);
            _deviceDashboard?.SetManagedCampaign(managed?.CampaignId, managed?.Id);
            _dcConsistency?.InvalidateSelectedCampaign();
        };
        layout.Controls.Add(_policies, 0, 1);
        _lifecycleInfo.Dock = DockStyle.Fill;
        _lifecycleInfo.Multiline = true;
        _lifecycleInfo.ScrollBars = ScrollBars.Both;
        _lifecycleInfo.ReadOnly = true;
        _lifecycleInfo.WordWrap = false;
        _lifecycleInfo.Font = new Font("Consolas", 9.2f);
        _lifecycleInfo.Text = "Select a GPO and an action, then click Preview.";
        layout.Controls.Add(_lifecycleInfo, 0, 2);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false };
        _lifecycleAction.DropDownStyle = ComboBoxStyle.DropDownList;
        _lifecycleAction.Width = 230;
        _lifecycleAction.Items.AddRange(new object[] {
            "Disable", "Enable", "Unlink", "Relink", "Reuse", "Delete"
        });
        _lifecycleAction.SelectedIndex = 0;
        _lifecycleAction.SelectedIndexChanged += (_, _) => InvalidatePlans();
        actions.Controls.Add(_lifecycleAction);
        _lifecyclePreview = ActionButton("Preview", () => _ = PreviewLifecycleAsync(), 145);
        actions.Controls.Add(_lifecyclePreview);
        _applyGpo = ActionButton("Apply", () => _ = ApplyLifecycleAsync(), 160, primary: true);
        _applyGpo.Enabled = false;
        actions.Controls.Add(_applyGpo);
        footer.Controls.Add(actions, 0, 0);
        footer.Controls.Add(Note("Disable does not stop local tasks. Unlink keeps the GPO; Relink reconnects it. Reuse changes the schedule. Remove all links before Delete."), 0, 1);
        layout.Controls.Add(footer, 0, 3);
        return page;
    }

    private TabPage BuildConflictTab()
    {
        var page = new TabPage("Lock Screen conflicts") { BackColor = UiStyle.Canvas };
        var layout = Panel(3);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
        page.Controls.Add(layout);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true };
        toolbar.Controls.Add(new Label { Text = "Computer for optional HKLM inspection:",
            AutoSize = true, Margin = new Padding(0, 13, 7, 0) });
        _computerName.Width = 255;
        _computerName.PlaceholderText = "PC01.example.com";
        _computerName.Margin = new Padding(0, 9, 9, 0);
        toolbar.Controls.Add(_computerName);
        toolbar.Controls.Add(ActionButton("Scan OU + GPO", () => _ = ScanConflictsAsync(), 220, primary: true));
        toolbar.Controls.Add(ActionButton("Analyze effective (PC)",
            () => _ = AnalyzeEffectiveConflictsAsync(), 235));
        layout.Controls.Add(toolbar, 0, 0);
        _conflicts.Dock = DockStyle.Fill;
        _conflicts.Multiline = true;
        _conflicts.ReadOnly = true;
        _conflicts.ScrollBars = ScrollBars.Both;
        _conflicts.WordWrap = false;
        _conflicts.Font = new Font("Consolas", 9.2f);
        _conflicts.Text = "LockScreenImage, NoChangingLockScreen, NoLockScreen, Spotlight and related policies will appear here.\r\n"+
            "The scan considers Block Inheritance, disabled links, Enforced, and GPO computer settings.\r\n"+
            "For accurate RSoP, also run gpresult on the target computer.\r\n" +
            "Analyze effective (PC) requires a selected managed GPO and an AD computer in the OU + group.";
        layout.Controls.Add(_conflicts, 0, 1);
        layout.Controls.Add(Note("Scan OU reads linked GPOs and optionally HKLM. Analyze effective (PC) checks AD OU/group, ACL hints, WMI presence and possible Site GPOs. Neither is RSoP."), 0, 2);
        return page;
    }

    private TabPage BuildPushTab()
    {
        var page = new TabPage("Agent Management / Live") { BackColor = UiStyle.Canvas };
        _agentManagementTab = page;
        var layout = Panel(4);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 47));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 53));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 101));
        page.Controls.Add(layout);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true };
        toolbar.Controls.Add(ActionButton("Computers from OU + group", () => _ = LoadComputersAsync(), 250, primary: true));
        toolbar.Controls.Add(ActionButton("Select first 25", () => {
            for (int i = 0; i < _computers.Items.Count; i++) _computers.SetItemChecked(i, i < 25);
            InvalidatePlans();
        }, 180));
        toolbar.Controls.Add(ActionButton("Clear selection", () => {
            for (int i = 0; i < _computers.Items.Count; i++) _computers.SetItemChecked(i, false);
            InvalidatePlans();
        }, 135));
        SetupAgentManagement(toolbar);
        toolbar.Controls.Add(Note("Up to 25 PCs per run. RPC TCP/135 reachability is NOT proof of Task Scheduler access or agent health."));
        layout.Controls.Add(toolbar, 0, 0);
        _computers.Dock = DockStyle.Fill;
        _computers.IntegralHeight = false;
        _computers.CheckOnClick = true;
        _computers.HorizontalScrollbar = true;
        _computers.ItemCheck += (_, _) => BeginInvoke((Action)InvalidatePlans);
        layout.Controls.Add(AgentComputerList(), 0, 1);
        _pushInfo.Dock = DockStyle.Fill;
        _pushInfo.Multiline = true;
        _pushInfo.ReadOnly = true;
        _pushInfo.ScrollBars = ScrollBars.Both;
        _pushInfo.WordWrap = false;
        _pushInfo.Font = new Font("Consolas", 9.2f);
        _pushInfo.Text = "Agent Management: Run now acts only on verified AD OU/group computers.\r\n"+
            "RPC reachability is a network hint; use Client Health / Report for actual agent evidence.\r\n"+
            "The production client is PS1. Native EXE is still laboratory-only and cannot be launched here.\r\n\r\n"+
            "Push: install the agent immediately (images follow the configured schedule).\r\n"+
            "Repair: verify and restore files from the trusted SYSVOL package using SHA-256.\r\n"+
            "Cleanup: restore previous settings and remove the local agent on selected PCs.\r\n"+
            "Report: request a fresh JSON status via RPC without modifying the registry.";
        layout.Controls.Add(_pushInfo, 0, 2);
        var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        _pushPurpose.DropDownStyle = ComboBoxStyle.DropDownList;
        _pushPurpose.Width = 210;
        _pushPurpose.Items.AddRange(new object[] { "Push", "Repair", "Cleanup", "Report" });
        _pushPurpose.SelectedIndex = 0;
        _pushPurpose.SelectedIndexChanged += (_, _) => InvalidatePlans();
        actions.Controls.Add(_pushPurpose);
        _pushPreview = ActionButton("Preview", () => _ = PreviewPushAsync(), 145);
        _push = ActionButton("Run on selected", () => _ = ExecutePushAsync(), 225, primary: true);
        _push.Enabled = false;
        actions.Controls.Add(_pushPreview);
        actions.Controls.Add(_push);
        footer.Controls.Add(actions, 0, 0);
        footer.Controls.Add(Note("Offline PCs or blocked RPC may not respond. GPO Startup remains active for those endpoints."), 0, 1);
        layout.Controls.Add(footer, 0, 3);
        return page;
    }

    private string SelectedId => _policies.SelectedItem is GpoRow row ? row.Id : "";
    private string SelectedAction => _lifecycleAction.SelectedItem as string ?? "";
    private string SelectedPushPurpose => _pushPurpose.SelectedItem as string ?? "Push";
    private string[] SelectedComputerNames() => _computers.CheckedItems.Cast<PcRow>().Select(x => x.Name).ToArray();
    private void InvalidatePlans()
    {
        _gpoPlanHash = _gpoPlanAction = _gpoPlanId = null;
        _pushPlanHash = _pushPlanId = _pushPlanPurpose = null;
        _pushPlanHosts = [];
        _applyGpo.Enabled = _push.Enabled = false;
    }
    private async Task<JsonElement> ExecuteAsync(DomainOperation operation, string mode, object request)
    {
        if (_busy) throw new InvalidOperationException("Operation already running.");
        _busy = true;
        _status.Text = "Running: " + mode;
        UseWaitCursor = true;
        try
        {
            var result = await Task.Run(() => DomainPowerShellRunner.Execute(operation, mode, request,
                _credentials, mode is "Push" or "Repair" or "Cleanup" or "Report" ? 45 * 60 * 1000 : 4 * 60 * 1000));
            return result.GetProperty("data").Clone();
        }
        finally
        {
            _busy = false;
            UseWaitCursor = false;
        }
    }
    private static string Pretty(JsonElement data)
    {
        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    }
    private void Fail(Exception ex, string operation)
    {
        _status.Text = "Failed: " + UniversalDiagnosticsEmbedded.Clean(ex.Message, 250);
        UiErrorDialog.Show(this, operation, ex);
    }
    private async Task ReloadAsync()
    {
        if (_busy) return;
        InvalidatePlans();
        try
        {
            var result = await ExecuteAsync(DomainOperation.Lifecycle, "List", new { domain = _domain, dc = _dc });
            _policies.BeginUpdate();
            try
            {
                _policies.Items.Clear();
                foreach (JsonElement e in result.EnumerateArray())
                {
                    _policies.Items.Add(new GpoRow(e.GetProperty("id").GetString() ?? "",
                        e.GetProperty("name").GetString() ?? "", e.GetProperty("campaignId").GetString() ?? "",
                        e.GetProperty("linkCount").GetInt32()));
                }
                if (_policies.Items.Count > 0) _policies.SelectedIndex = 0;
            }
            finally { _policies.EndUpdate(); }
            _status.Text = "Managed GPOs: " + _policies.Items.Count;
        }
        catch (Exception ex) { Fail(ex, "List managed GPOs"); }
    }
    private void RemoveTempPackage()
    {
        var folder = _tempPackageRoot;
        _tempPackageRoot = _tempPackage = null;
        if (string.IsNullOrWhiteSpace(folder)) return;
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception ex) { AppLog.Write(ex, "Cleanup export temp"); }
    }
    private string CreateReusePackage()
    {
        if (!string.IsNullOrWhiteSpace(_tempPackage) && Directory.Exists(_tempPackage)) return _tempPackage;
        RemoveTempPackage();
        _tempPackageRoot = Path.Combine(Path.GetTempPath(), "LSCM-REUSE-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempPackageRoot);
        _tempPackage = CampaignExporter.Export(_project, _tempPackageRoot);
        return _tempPackage;
    }
    private async Task PreviewLifecycleAsync()
    {
        if (_busy) return;
        InvalidatePlans();
        try
        {
            if (string.IsNullOrWhiteSpace(SelectedId)) throw new InvalidOperationException("Select a managed GPO first.");
            string action = SelectedAction;
            string package = action == "Reuse" ? CreateReusePackage() : "";
            var plan = await ExecuteAsync(DomainOperation.Lifecycle, "Preview",
                new { domain = _domain, dc = _dc, ouDn = _ouDn, gpoId = SelectedId,
                    action, packagePath = package });
            _lifecycleInfo.Text = Pretty(plan);
            _gpoPlanHash = plan.GetProperty("hash").GetString();
            _gpoPlanAction = action;
            _gpoPlanId = SelectedId;
            _applyGpo.Enabled = true;
            _status.Text = "Plan ready. No domain changes made.";
        }
        catch (Exception ex) { Fail(ex, "Preview GPO lifecycle"); }
    }
    private static bool ConfirmGuid(IWin32Window parent, string operation, string guid)
    {
        using var dialog = new Form { Text = "Confirm GPO change", StartPosition = FormStartPosition.CenterParent,
            AutoScaleMode = AutoScaleMode.Dpi, Size = new Size(690, 310), MinimumSize = new Size(550, 270) };
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(18), RowCount = 3, ColumnCount = 1 };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 95));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label { Dock = DockStyle.Fill, Text =
            "Operation: " + operation + "\n\nEnter the full GPO GUID to confirm:\n" + guid }, 0, 0);
        var edit = new TextBox { Dock = DockStyle.Fill };
        panel.Controls.Add(edit, 0, 1);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var confirm = ActionButton("Confirm", () => { if (edit.Text.Trim().Equals(guid, StringComparison.OrdinalIgnoreCase)) dialog.DialogResult = DialogResult.OK; }, 170, true);
        confirm.Enabled = false;
        edit.TextChanged += (_, _) => confirm.Enabled = edit.Text.Trim().Equals(guid, StringComparison.OrdinalIgnoreCase);
        buttons.Controls.Add(confirm);
        buttons.Controls.Add(ActionButton("Cancel", () => dialog.DialogResult = DialogResult.Cancel, 110));
        panel.Controls.Add(buttons, 0, 2);
        dialog.Controls.Add(panel);
        UiStyle.Apply(dialog);
        return dialog.ShowDialog(parent) == DialogResult.OK;
    }
    private async Task ApplyLifecycleAsync()
    {
        if (_busy || string.IsNullOrWhiteSpace(_gpoPlanHash) || SelectedAction != _gpoPlanAction || SelectedId != _gpoPlanId)
            return;
        var planHash = _gpoPlanHash;
        string id = SelectedId;
        string action = SelectedAction;
        if (!ConfirmGuid(this, action, id)) return;
        // Explicit preview token is revalidated in PowerShell under the same chosen DC.
        try
        {
            var data = await ExecuteAsync(DomainOperation.Lifecycle, "Apply", new {
                domain = _domain, dc = _dc, ouDn = _ouDn, gpoId = id, action,
                packagePath = action == "Reuse" ? _tempPackage : "",
                planHash, approveGuid = id
            });
            _lifecycleInfo.Text = Pretty(data);
            _status.Text = "GPO action confirmed. Wait for AD/SYSVOL replication.";
            string campaignForEvent = _policies.SelectedItem is GpoRow selected
                ? (action == "Reuse" ? CampaignSchedule.BuildId(_project) : selected.CampaignId)
                : CampaignSchedule.BuildId(_project);
            CampaignHistoryStore.TryRecordEvent(campaignForEvent, "GPO " + action,
                "The action was confirmed and executed through the selected DC.");
            InvalidatePlans();
            RemoveTempPackage();
            await ReloadAsync();
        }
        catch (Exception ex) { InvalidatePlans(); Fail(ex, "Apply GPO lifecycle"); }
    }
    private async Task AnalyzeEffectiveConflictsAsync()
    {
        if (_busy) return;
        if (_policies.SelectedItem is not GpoRow selected)
        {
            _status.Text = "Select a managed GPO on the GPO tab first.";
            return;
        }
        string fqdn = _computerName.Text.Trim();
        if (!fqdn.EndsWith("." + _domain, StringComparison.OrdinalIgnoreCase) ||
            fqdn.Length > 253 || fqdn.Length < 3 ||
            !System.Text.RegularExpressions.Regex.IsMatch(fqdn,
                @"\A[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+\z"))
        {
            _status.Text = "Enter a domain computer FQDN, e.g. PC01." + _domain;
            return;
        }
        try
        {
            var data = await ExecuteAsync(DomainOperation.ConflictScan, "Effective",
                new
                {
                    domain = _domain, dc = _dc, ouDn = _ouDn, groupDn = _groupDn,
                    computer = fqdn, campaignId = selected.CampaignId,
                    managedGpoId = selected.Id
                });
            if (!ReferenceEquals(_policies.SelectedItem, selected))
                throw new InvalidOperationException(
                    "Managed GPO changed during scan. Select it again and retry.");
            var report = EffectiveGpoReport.Parse(data, _domain, _dc,
                _ouDn, _groupDn, fqdn, selected.CampaignId, selected.Id,
                DateTimeOffset.UtcNow);
            _conflicts.Text =
                $"Effective candidates for {report.Computer}\r\n" +
                $"Managed campaign: {report.CampaignId}\r\n" +
                $"GPO links: {report.Policies.Count}, potential high overlaps: " +
                $"{report.PotentialHigh}, needing verification: " +
                $"{report.NeedsVerification}\r\n" +
                $"Site association: UNKNOWN, tokenGroups: {report.TokenStatus}.\r\n" +
                "No policy application is proven. Run gpresult on the client for RSoP.";
            _status.Text = "Read-only effective candidate assessment finished; " +
                "WMI, Site, security and client RSoP uncertainties are shown.";
            using var window = new EffectiveGpoConflictDialog(report, async () =>
            {
                // Every explicitly approved read revalidates the managed GPO
                // and AD computer scope on the named DC. Never reuse the
                // earlier AD report as authorization for an endpoint read.
                var fresh = await ExecuteAsync(DomainOperation.ConflictScan,
                    "VerifyEndpoint", new
                    {
                        domain = report.Domain, dc = report.Dc,
                        ouDn = report.OuDn, groupDn = report.GroupDn,
                        computer = report.Computer, campaignId = report.CampaignId,
                        managedGpoId = report.ManagedGpoId
                    });
                return EndpointLockScreenObservation.Parse(fresh, report,
                    DateTimeOffset.UtcNow);
            }, async () =>
            {
                // A fresh scoped read for BOTH cached CSE source and JPEG hash.
                // An earlier endpoint snapshot never authorizes this operation.
                var fresh = await ExecuteAsync(DomainOperation.ConflictScan,
                    "VerifyEvidence", new
                    {
                        domain = report.Domain, dc = report.Dc,
                        ouDn = report.OuDn, groupDn = report.GroupDn,
                        computer = report.Computer, campaignId = report.CampaignId,
                        managedGpoId = report.ManagedGpoId
                    });
                var observedAt = DateTimeOffset.UtcNow;
                var registry = EndpointLockScreenObservation.Parse(
                    fresh, report, observedAt);
                var sources = EndpointSourceEvidence.Parse(
                    fresh, report, registry, observedAt);
                return new EndpointEvidenceBundle(registry, sources);
            });
            window.ShowDialog(this);
        }
        catch (Exception ex) { Fail(ex, "Analyze effective GPO candidates"); }
    }

    private async Task ScanConflictsAsync()
    {
        if (_busy) return;
        try
        {
            var data = await ExecuteAsync(DomainOperation.ConflictScan, "Scan", new {
                domain = _domain, dc = _dc, ouDn = _ouDn, computer = _computerName.Text.Trim()
            });
            _conflicts.Text = Pretty(data);
            int high = data.GetProperty("highRisk").GetInt32();
            _status.Text = $"Read-only scan: {high} high-risk candidates. Scope/RSoP limitations are listed in the report.";
        }
        catch (Exception ex) { Fail(ex, "Scan GPO / registry conflicts"); }
    }
    private async Task LoadComputersAsync()
    {
        if (_busy) return;
        InvalidatePlans();
        try
        {
            var data = await ExecuteAsync(DomainOperation.Immediate, "List", new {
                domain = _domain, dc = _dc, ouDn = _ouDn, groupDn = _groupDn
            });
            _agentInventoryGeneration++;
            _agentInventory.Clear();
            foreach (JsonElement e in data.EnumerateArray())
                _agentInventory.Add(new PcRow(e.GetProperty("name").GetString() ?? "",
                    e.GetProperty("sam").GetString() ?? ""));
            DrawAgentInventory();
            UiStyle.ApplyStatus(_agentProbeStatus,
                "Loaded " + _agentInventory.Count +
                " AD-verified computers. Check RPC reachability to find contactable endpoints.",
                UiStatusKind.Neutral);
            _status.Text = $"Eligible computers: {_agentInventory.Count} (nested group membership).";
            // Entering Agent Management performs an explicitly scoped,
            // read-only RPC reachability sample without waiting for the user
            // to navigate to another panel. No remote task is created.
            if (_agentTabControl?.SelectedTab == _agentManagementTab &&
                _agentInventory.Count > 0)
                await ProbeVisibleAgentHostsAsync();
        }
        catch (Exception ex) { Fail(ex, "List eligible computers"); }
    }
    private async Task RepairFromHealthAsync(IReadOnlyList<string> shortNames)
    {
        if (_busy) throw new InvalidOperationException("Another operation is already running.");
        if (_policies.SelectedItem is not GpoRow managed)
            throw new InvalidOperationException("Select a managed GPO on the GPO tab first.");
        if (_clientHealth is null ||
            !string.Equals(managed.CampaignId, _clientHealth.SelectedCampaignId, StringComparison.Ordinal))
            throw new InvalidOperationException("The Client Health tab and selected GPO refer to different campaigns.");
        if (shortNames.Count is < 1 or > 25 || shortNames.Any(n => !FeedbackPath.IsValidHost(n)))
            throw new InvalidOperationException("Select 1–25 valid computer names.");

        // Never trust a client-uploaded hostname to authorize RPC. Re-discover the
        // domain OU/group intersection and resolve each selected short name to exactly
        // one AD DNS computer name, then let the remote PowerShell revalidate again.
        var listed = await ExecuteAsync(DomainOperation.Immediate, "List", new {
            domain = _domain, dc = _dc, ouDn = _ouDn, groupDn = _groupDn
        });
        var eligible = listed.EnumerateArray().Select(x => x.GetProperty("name").GetString() ?? "")
            .Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        var requested = new List<string>();
        foreach (string shortName in shortNames.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var matching = eligible.Where(x => string.Equals(x.Split('.')[0], shortName,
                StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matching.Length != 1)
                throw new InvalidOperationException("No unique match found in the selected OU and group: " + shortName);
            requested.Add(matching[0]);
        }
        EnsureNoInFlightRepair(managed, requested);
        var plan = await ExecuteAsync(DomainOperation.Immediate, "Preview", new {
            domain = _domain, dc = _dc, ouDn = _ouDn, groupDn = _groupDn,
            gpoId = managed.Id, computers = requested, purpose = "Repair"
        });
        if (!string.Equals(plan.GetProperty("campaignId").GetString(), managed.CampaignId,
            StringComparison.Ordinal))
            throw new InvalidOperationException("Source campaign changed since AD selection. Refresh the GPO list.");
        string planHash = plan.GetProperty("hash").GetString() ?? "";
        if (string.IsNullOrWhiteSpace(planHash))
            throw new InvalidOperationException("Remote preview did not supply a plan hash.");
        if (MessageBox.Show(this,
            $"Repair will be attempted on {requested.Count} computers via Task Scheduler RPC.\n\n" +
            "Clients will verify local files against the trusted SYSVOL package using SHA-256.\n" +
            "The original registry backup will not be overwritten.\n\n" +
            (_gmsaForNewRepair.Checked
                ? "gMSA 24/7 mode: confirmed results will enter a protected " +
                  "machine-wide queue that continues after the administrator signs out. "
                : "Results will remain in the per-user Deferred Repair queue. " +
                  "When the per-user Background Worker is enabled, the GUI can be closed. ") +
            "Maximum 14 days / 20 attempts; confirmation through Client Health. " +
            "Passwords are not stored.\n\n" +
            "Use test PCs only until domain validation is completed. Continue?",
            "Repair Selected — Preview", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        if (!ConfirmGuid(this, "Repair " + requested.Count + " computers" +
            (_gmsaForNewRepair.Checked ? " [gMSA 24/7]" : " [user queue]"), managed.Id)) return;
        bool approvedGmsaRoute = _gmsaForNewRepair.Checked;
        ReserveGmsaBeforeManualRepair(managed, requested, approvedGmsaRoute);
        // Atomic lease under the shared queue mutex: an old background Queued
        // request cannot race with this newly approved manual execution.
        DeferredRepairQueue.SuspendBackgroundForApprovedRepair(
            _domain, managed.Id, managed.CampaignId, requested);
        DateTimeOffset submittedUtc = DateTimeOffset.UtcNow;
        var data = await ExecuteAsync(DomainOperation.Immediate, "Repair", new {
            domain = _domain, dc = _dc, ouDn = _ouDn, groupDn = _groupDn,
            gpoId = managed.Id, computers = requested, purpose = "Repair",
            planHash, approveGuid = managed.Id
        });
        int successful = data.GetProperty("successful").GetInt32();
        int pending = data.GetProperty("pending").GetInt32();
        int failed = data.GetProperty("failed").GetInt32();
        var tracked = TrackApprovedRepairResults(data, managed, requested, submittedUtc,
            approvedGmsaRoute);
        _pushInfo.Text = Pretty(data);
        _status.Text = tracked.Tracked
            ? $"Repair attempted: {successful} task success, {pending} pending, {failed} failed. " +
              $"Offline queued: {tracked.Offline}; health confirmation: {tracked.AwaitingHealth}; " +
              $"uncertain tasks: {tracked.AwaitingTask}; failed: {tracked.NeedsAttention}. Check Client Health."
            : "WARNING: Remote Repair may have executed but queue could not be saved; inspect clients before retry.";
        CampaignHistoryStore.TryRecordEvent(managed.CampaignId, "Repair Selected",
            $"Requested: {requested.Count}; task success: {successful}; pending: {pending}; failed: {failed}.");
        MessageBox.Show(this, $"Repair request sent to {requested.Count} computers.\n" +
            $"Successful tasks: {successful}\nPending: {pending}\nFailures: {failed}\n" +
            (tracked.Tracked
                ? $"Deferred offline: {tracked.Offline}\n" +
                  $"Awaiting client verification: {tracked.AwaitingHealth + tracked.AwaitingTask}\n" +
                  $"Require manual review: {tracked.NeedsAttention}\n\n"
                : "WARNING: result queue was not saved; inspect the PCs manually.\n\n") +
            "Check fresh Client Health reports and actual Lock Screen application.",
            "Repair result", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    private async Task PreviewPushAsync()
    {
        if (_busy) return;
        _pushPlanHash = null;
        _push.Enabled = false;
        try
        {
            string id = SelectedId;
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Choose a managed GPO in the first tab.");
            string[] hosts = SelectedComputerNames();
            string purpose = SelectedPushPurpose;
            var data = await ExecuteAsync(DomainOperation.Immediate, "Preview", new {
                domain = _domain, dc = _dc, ouDn = _ouDn, groupDn = _groupDn,
                gpoId = id, computers = hosts, purpose
            });
            _pushInfo.Text = Pretty(data);
            _pushPlanHash = data.GetProperty("hash").GetString();
            _pushPlanId = id;
            _pushPlanPurpose = purpose;
            _pushPlanHosts = hosts;
            _push.Enabled = true;
            _status.Text = $"Remote execution preview ready for {hosts.Length} PCs. No tasks created yet.";
        }
        catch (Exception ex) { Fail(ex, "Preview immediate remote execution"); }
    }
    private async Task ExecutePushAsync()
    {
        if (_busy || string.IsNullOrWhiteSpace(_pushPlanHash) || _pushPlanId != SelectedId ||
            _pushPlanPurpose != SelectedPushPurpose || !_pushPlanHosts.SequenceEqual(SelectedComputerNames())) return;
        string id = SelectedId;
        if (!ConfirmGuid(this, SelectedPushPurpose + " on " + _pushPlanHosts.Length +
            " selected PCs" + (SelectedPushPurpose == "Repair" && _gmsaForNewRepair.Checked
                ? " [gMSA 24/7]" : ""), id)) return;
        try
        {
            string mode = SelectedPushPurpose;
            bool approvedGmsaRoute = mode == "Repair" && _gmsaForNewRepair.Checked;
            if (mode == "Repair" && _policies.SelectedItem is GpoRow repairGpo)
            {
                EnsureNoInFlightRepair(repairGpo, _pushPlanHosts);
                ReserveGmsaBeforeManualRepair(repairGpo, _pushPlanHosts,
                    approvedGmsaRoute);
                DeferredRepairQueue.SuspendBackgroundForApprovedRepair(
                    _domain, repairGpo.Id, repairGpo.CampaignId, _pushPlanHosts);
            }
            DateTimeOffset submittedUtc = DateTimeOffset.UtcNow;
            var data = await ExecuteAsync(DomainOperation.Immediate, mode, new {
                domain = _domain, dc = _dc, ouDn = _ouDn, groupDn = _groupDn,
                gpoId = id, computers = _pushPlanHosts, purpose = mode,
                planHash = _pushPlanHash, approveGuid = id
            });
            (int Offline, int AwaitingHealth, int AwaitingTask, int NeedsAttention, bool Tracked) tracked =
                mode == "Repair" && _policies.SelectedItem is GpoRow selectedGpo
                    ? TrackApprovedRepairResults(data, selectedGpo, _pushPlanHosts, submittedUtc,
                        approvedGmsaRoute)
                    : (0, 0, 0, 0, true);
            _pushInfo.Text = Pretty(data);
            _status.Text = tracked.Tracked
                ? $"Remote task attempted. Offline queued: {tracked.Offline}; " +
                  $"awaiting healthy feedback: {tracked.AwaitingHealth + tracked.AwaitingTask}; " +
                  $"failed/manual review: {tracked.NeedsAttention}. " +
                  "Inspect actual lock screen on test clients."
                : "WARNING: Repair ran, but result tracking failed; inspect PCs before retry.";
            int successful = data.GetProperty("successful").GetInt32();
            int pending = data.GetProperty("pending").GetInt32();
            int failed = data.GetProperty("failed").GetInt32();
            string historyId = _policies.SelectedItem is GpoRow selected ? selected.CampaignId : CampaignSchedule.BuildId(_project);
            CampaignHistoryStore.TryRecordEvent(historyId, "Remote " + mode,
                $"Target PCs: {_pushPlanHosts.Length}; task success: {successful}; pending: {pending}; failed: {failed}. " +
                "Task execution does not prove that the lock-screen image is visible.");
            InvalidatePlans();
        }
        catch (Exception ex) { InvalidatePlans(); Fail(ex, "Remote Task Scheduler deployment"); }
    }
}
