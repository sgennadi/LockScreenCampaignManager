using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

internal sealed partial class GpoOperationsForm
{
    private readonly List<PcRow> _agentInventory = new();
    private readonly TextBox _agentSearch = new();
    private readonly CheckBox _agentReachableOnly = new();
    private readonly CheckBox _agentAutoProbe = new();
    private readonly Label _agentProbeStatus = new();
    private readonly TextBox _agentSelectedDetails = new();
    private Button? _agentRefreshReportsButton;
    private bool _agentEvidenceLoading;
    private int _agentEvidenceGeneration;
    private readonly System.Windows.Forms.Timer _agentProbeTimer = new() { Interval = 60000 };
    private Button? _agentProbeButton;
    private TabPage? _agentManagementTab;
    private TabPage? _agentHealthTab;
    private TabControl? _agentTabControl;
    private bool _agentProbing;
    private int _agentInventoryGeneration;
    private bool _openAgentManagementInitially;

    internal void OpenAgentManagementInitially() =>
        _openAgentManagementInitially = true;

    private void SetupAgentManagement(FlowLayoutPanel toolbar)
    {
        _agentSearch.PlaceholderText = "Filter computer or SAM...";
        _agentSearch.Width = 192;
        _agentSearch.Margin = new Padding(5, 7, 7, 3);
        _agentSearch.TextChanged += (_, _) => DrawAgentInventory();
        toolbar.Controls.Add(_agentSearch);

        _agentReachableOnly.Text = "RPC reachable only";
        _agentReachableOnly.AutoSize = true;
        _agentReachableOnly.Margin = new Padding(4, 14, 7, 0);
        _agentReachableOnly.CheckedChanged += (_, _) => DrawAgentInventory();
        toolbar.Controls.Add(_agentReachableOnly);

        _agentProbeButton = ActionButton(
            "Check RPC reachability", () => _ = ProbeVisibleAgentHostsAsync(), 204);
        toolbar.Controls.Add(_agentProbeButton);
        toolbar.Controls.Add(ActionButton("Select reachable (max 25)", SelectReachableAgents, 243));
        _agentRefreshReportsButton = ActionButton(
            "Refresh Agent Reports", () => _ = RefreshAgentEvidenceAsync(), 210);
        toolbar.Controls.Add(_agentRefreshReportsButton);
        toolbar.Controls.Add(ActionButton("Open Client Health", OpenAgentHealth, 187));

        _agentAutoProbe.Text = "Auto-check every 60 sec";
        _agentAutoProbe.AutoSize = true;
        _agentAutoProbe.Margin = new Padding(4, 14, 7, 0);
        _agentAutoProbe.CheckedChanged += (_, _) => UpdateAgentProbeTimer();
        toolbar.Controls.Add(_agentAutoProbe);

        _agentProbeTimer.Tick += (_, _) =>
        {
            if (!_busy && !_agentProbing && _agentInventory.Count > 0 &&
                _agentTabControl?.SelectedTab == _agentManagementTab)
                _ = ProbeVisibleAgentHostsAsync();
        };
    }

    private Control AgentComputerList()
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1, Padding = Padding.Empty
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 49));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 108));
        _agentProbeStatus.Dock = DockStyle.Fill;
        _agentProbeStatus.TextAlign = ContentAlignment.MiddleLeft;
        _agentProbeStatus.AutoEllipsis = true;
        UiStyle.ApplyStatus(_agentProbeStatus,
            "Load AD computers, then check RPC reachability. TCP/135 is not proof that the agent is running.",
            UiStatusKind.Neutral);
        panel.Controls.Add(_agentProbeStatus, 0, 0);
        panel.Controls.Add(_computers, 0, 1);
        _agentSelectedDetails.Dock = DockStyle.Fill;
        _agentSelectedDetails.Multiline = true;
        _agentSelectedDetails.ReadOnly = true;
        _agentSelectedDetails.ScrollBars = ScrollBars.Vertical;
        _agentSelectedDetails.BackColor = UiStyle.Surface;
        _agentSelectedDetails.Font = new Font("Segoe UI", 9f);
        _agentSelectedDetails.Text = "Select a verified computer to see RPC hints, client-reported health and latest command outcome.";
        panel.Controls.Add(_agentSelectedDetails, 0, 2);
        return panel;
    }

    private void OnAgentManagementTabChanged()
    {
        UpdateAgentProbeTimer();
        if (_agentTabControl?.SelectedTab == _agentManagementTab &&
            _agentInventory.Count == 0 && !_busy)
            _ = LoadComputersAsync();
    }

    private void UpdateAgentProbeTimer()
    {
        bool enabled = _agentAutoProbe.Checked &&
            _agentTabControl?.SelectedTab == _agentManagementTab;
        _agentProbeTimer.Enabled = enabled;
    }

    private void StopAgentManagement()
    {
        _agentProbeTimer.Stop();
        _agentProbeTimer.Dispose();
    }

    private void OpenAgentHealth()
    {
        if (_agentTabControl is not null && _agentHealthTab is not null)
            _agentTabControl.SelectedTab = _agentHealthTab;
    }

    private void DrawAgentInventory()
    {
        if (_computers.IsDisposed) return;
        string query = _agentSearch.Text.Trim();
        var selected = _computers.CheckedItems.Cast<PcRow>()
            .Select(x => x.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        string? highlighted = (_computers.SelectedItem as PcRow)?.Name;
        var visible = _agentInventory.Where(x =>
                (query.Length == 0 || x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                 x.Sam.Contains(query, StringComparison.OrdinalIgnoreCase)) &&
                (!_agentReachableOnly.Checked || x.RpcReachable == true))
            .OrderByDescending(x => x.RpcReachable == true)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        _computers.BeginUpdate();
        try
        {
            _computers.Items.Clear();
            foreach (var pc in visible)
                _computers.Items.Add(pc, selected.Contains(pc.Name));
        }
        finally { _computers.EndUpdate(); }
        if (highlighted is not null)
        {
            for (int i = 0; i < _computers.Items.Count; i++)
                if (_computers.Items[i] is PcRow row &&
                    string.Equals(row.Name, highlighted, StringComparison.OrdinalIgnoreCase))
                { _computers.SelectedIndex = i; break; }
        }
        if (!selected.SetEquals(_computers.CheckedItems.Cast<PcRow>().Select(x => x.Name)))
            InvalidatePlans();
        DisplaySelectedAgentDetails();
    }

    private void SelectReachableAgents()
    {
        int count = 0;
        _computers.BeginUpdate();
        try
        {
            for (int i = 0; i < _computers.Items.Count; i++)
            {
                bool select = _computers.Items[i] is PcRow pc &&
                    pc.RpcReachable == true && count < 25;
                _computers.SetItemChecked(i, select);
                if (select) count++;
            }
        }
        finally { _computers.EndUpdate(); }
        InvalidatePlans();
        _status.Text = count == 0
            ? "No RPC-reachable computer in the visible list. Check reachability or clear the filter."
            : "Selected " + count + " RPC-reachable AD computers. Preview and confirm any remote action.";
    }

    private async Task ProbeVisibleAgentHostsAsync()
    {
        if (_busy || _agentProbing || _agentInventory.Count == 0 || IsDisposed || Disposing)
            return;
        var visible = _computers.Items.Cast<PcRow>().Select(x => x.Name).ToArray();
        var hosts = visible.Take(AgentReachabilityProbe.MaximumPerScan).ToArray();
        int generation = _agentInventoryGeneration;
        _agentProbing = true;
        if (_agentProbeButton is not null) _agentProbeButton.Enabled = false;
        UiStyle.ApplyStatus(_agentProbeStatus,
            "Checking RPC TCP/135 on " + hosts.Length + " verified AD computers (read-only)...",
            UiStatusKind.Busy);
        try
        {
            var status = await AgentReachabilityProbe.CheckAsync(hosts, _domain);
            if (IsDisposed || Disposing || generation != _agentInventoryGeneration) return;
            var updates = status.ToDictionary(x => x.Host, StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < _agentInventory.Count; i++)
            {
                var pc = _agentInventory[i];
                if (updates.TryGetValue(pc.Name, out var evidence))
                    _agentInventory[i] = pc with {
                        RpcReachable = evidence.RpcEndpointReachable,
                        CheckedUtc = evidence.CheckedUtc
                    };
            }
            DrawAgentInventory();
            int reachable = status.Count(x => x.RpcEndpointReachable);
            string limit = visible.Length > hosts.Length
                ? " | " + (visible.Length - hosts.Length) + " not checked; filter to check other PCs" : "";
            UiStyle.ApplyStatus(_agentProbeStatus,
                "RPC TCP/135 reachable: " + reachable + "/" + hosts.Length +
                " | checked " + DateTime.Now.ToString("HH:mm:ss") + limit +
                " | verify agent via Report / Client Health",
                reachable > 0 ? UiStatusKind.Success : UiStatusKind.Warning);
        }
        catch (Exception ex)
        {
            if (!IsDisposed && !Disposing)
                UiStyle.ApplyStatus(_agentProbeStatus,
                    "RPC reachability check unavailable. Remote computer status is UNKNOWN.",
                    UiStatusKind.Warning);
            AppLog.Write(ex, "Agent Management TCP/135 reachability");
        }
        finally
        {
            _agentProbing = false;
            if (!IsDisposed && !Disposing && _agentProbeButton is not null)
                _agentProbeButton.Enabled = true;
        }
    }
}
