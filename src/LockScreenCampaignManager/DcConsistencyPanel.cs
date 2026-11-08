using System.Drawing;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// No domain queries until explicit button action. DCs are discovered from the
/// currently selected domain, and the operator explicitly chooses up to 12.
/// </summary>
internal sealed class DcConsistencyPanel : UserControl
{
    private sealed record DcChoice(string Name, bool Writable)
    {
        public override string ToString() =>
            Name + (Writable ? "  (writable)" : "  (RODC / unknown)");
    }

    private readonly string _domain;
    private readonly string _dc;
    private readonly DomainCredentials _credentials;
    private readonly Func<(string CampaignId, string GpoId)> _selection;
    private readonly Func<bool> _parentBusy;
    private readonly Action<bool> _setParentBusy;
    private readonly CheckedListBox _controllers = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _details = new();
    private readonly Label _summary = new();
    private readonly Label _status = new();
    private readonly Button _discover = new();
    private readonly Button _scan = new();
    private Button _export = new();
    private readonly List<string> _discovered = [];
    private DateTimeOffset _discoveryUtc;
    private bool _busy;
    private DcConsistencyReport? _report;

    internal DcConsistencyPanel(string domain, string dc, DomainCredentials credentials,
        Func<(string CampaignId, string GpoId)> selection,
        Func<bool>? parentBusy = null, Action<bool>? setParentBusy = null)
    {
        _domain = domain;
        _dc = dc;
        _credentials = credentials;
        _selection = selection;
        _parentBusy = parentBusy ?? (() => false);
        _setParentBusy = setParentBusy ?? (_ => { });
        Dock = DockStyle.Fill;
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = UiStyle.Canvas;
        Build();
        // The parent GPO Operations Form applies shared DPI/style tokens to
        // all child controls; UiStyle.Apply accepts Form, not UserControl.
    }

    internal void InvalidateSelectedCampaign()
    {
        _report = null;
        _grid.Rows.Clear();
        _details.Text = "Selected managed campaign changed; run a new DC scan.";
        _summary.Text = "Campaign selection changed. No prior audit may be reused.";
        UpdateActions();
    }

    internal TabPage AsTabPage()
    {
        var tab = new TabPage("DC Consistency") { BackColor = UiStyle.Canvas };
        tab.Controls.Add(this);
        return tab;
    }

    private static Button MakeButton(string label, int width, Action action)
    {
        var result = new Button {
            Text = label, Width = width, Height = 40,
            Margin = new Padding(0, 4, 12, 4), FlatStyle = FlatStyle.Flat,
            BackColor = UiStyle.Surface, ForeColor = UiStyle.Foreground
        };
        result.Click += (_, _) => action();
        return result;
    }

    private void Build()
    {
        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7,
            Padding = new Padding(12), BackColor = UiStyle.Surface
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 78));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        Controls.Add(layout);

        var help = new Label {
            Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = UiStyle.Muted,
            Text = "Read-only comparison across named domain controllers. Select a managed GPO " +
                "on the GPO tab, discover AD DCs, then check up to 12 explicitly selected DCs. " +
                "The chosen DC is the baseline. Results are observations, not DFSR convergence proof."
        };
        layout.Controls.Add(help, 0, 0);

        var actions = new FlowLayoutPanel {
            Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true
        };
        _discover.Text = "Discover DCs";
        _discover.Width = 155; _discover.Height = 40;
        _discover.FlatStyle = FlatStyle.Flat;
        _discover.BackColor = UiStyle.Surface;
        _discover.Margin = new Padding(0, 4, 12, 4);
        _discover.Click += async (_, _) => await DiscoverAsync();
        _scan.Text = "Compare selected DCs";
        _scan.Width = 235; _scan.Height = 40;
        _scan.FlatStyle = FlatStyle.Flat;
        _scan.BackColor = UiStyle.Surface;
        _scan.Margin = new Padding(0, 4, 12, 4);
        _scan.Click += async (_, _) => await CompareAsync();
        _export = MakeButton("Export safe CSV", 170, ExportVisible);
        actions.Controls.AddRange([_discover, _scan, _export]);
        layout.Controls.Add(actions, 0, 1);

        _controllers.Dock = DockStyle.Fill;
        _controllers.CheckOnClick = true;
        _controllers.MultiColumn = true;
        _controllers.ColumnWidth = 280;
        _controllers.HorizontalScrollbar = true;
        _controllers.BorderStyle = BorderStyle.FixedSingle;
        _controllers.IntegralHeight = false;
        _controllers.ItemCheck += (_, _) => {
            if (_busy) return;
            _report = null;
            _grid.Rows.Clear();
            _details.Text = "DC selection changed; prior audit is no longer current.";
            _summary.Text = "Selection changed: run a new read-only scan.";
            _export.Enabled = false;
        };
        layout.Controls.Add(_controllers, 0, 2);

        _summary.Dock = DockStyle.Fill;
        _summary.AutoEllipsis = true;
        _summary.ForeColor = UiStyle.Foreground;
        _summary.Text = "No DC consistency scan yet. No domain changes will be made.";
        layout.Controls.Add(_summary, 0, 3);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Domain controller", FillWeight = 172 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Result", FillWeight = 95 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Source read", FillWeight = 85 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "UTC offset", FillWeight = 85 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "AD / GPT", FillWeight = 85 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Images", FillWeight = 63 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Reason", FillWeight = 140 });
        _grid.SelectionChanged += (_, _) => ShowSelected();
        layout.Controls.Add(_grid, 0, 4);

        _details.Dock = DockStyle.Fill;
        _details.Multiline = true;
        _details.ReadOnly = true;
        _details.WordWrap = true;
        _details.ScrollBars = ScrollBars.Vertical;
        _details.Font = new Font("Consolas", 9f);
        _details.BackColor = UiStyle.Surface;
        _details.Text = "Choose a managed GPO and discover the participating domain controllers.";
        layout.Controls.Add(_details, 0, 5);

        _status.Dock = DockStyle.Fill;
        _status.ForeColor = UiStyle.Muted;
        _status.AutoEllipsis = true;
        _status.Text = "No remote changes, no RSAT, no WinRM, no remote scheduled task.";
        layout.Controls.Add(_status, 0, 6);
        UpdateActions();
    }

    private void UpdateActions()
    {
        if (IsDisposed) return;
        _discover.Enabled = !_busy && !_parentBusy();
        _scan.Enabled = !_busy && !_parentBusy() && _discovered.Count > 0;
        _export.Enabled = !_busy && _report is not null;
    }

    private async Task DiscoverAsync()
    {
        if (_busy || _parentBusy()) return;
        _busy = true;
        _setParentBusy(true);
        UpdateActions();
        try
        {
            _status.Text = "Reading AD domain controller inventory, without modifying AD...";
            var response = await Task.Run(() => DomainPowerShellRunner.Execute(
                DomainOperation.Discovery, "DomainControllers",
                new { domain = _domain, dc = _dc }, _credentials, 240000));
            var array = response.GetProperty("data");
            if (array.ValueKind != JsonValueKind.Array ||
                array.GetArrayLength() is < 1 or > 5000)
                throw new InvalidDataException("Invalid AD domain controller inventory.");
            var choices = new List<DcChoice>();
            var unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in array.EnumerateArray())
            {
                var name = (item.GetProperty("name").GetString() ?? "").Trim();
                if (!name.EndsWith("." + _domain, StringComparison.OrdinalIgnoreCase) ||
                    name.Length > 253 ||
                    !System.Text.RegularExpressions.Regex.IsMatch(name,
                        @"\A[A-Za-z0-9-]+(?:\.[A-Za-z0-9-]+)+\z") ||
                    !unique.Add(name))
                    throw new InvalidDataException("Unsafe, duplicate or out-of-domain controller.");
                choices.Add(new DcChoice(name, item.GetProperty("writable").GetBoolean()));
            }
            if (!unique.Contains(_dc))
                throw new InvalidDataException(
                    "Selected DC is absent from the discovered AD list; refuse to infer a baseline.");
            _discovered.Clear();
            _discovered.AddRange(choices.Select(x => x.Name));
            _discoveryUtc = DateTimeOffset.UtcNow;
            _report = null;
            _grid.Rows.Clear();
            _controllers.BeginUpdate();
            try
            {
                _controllers.Items.Clear();
                int selected = 0;
                foreach (var dc in choices.OrderBy(x =>
                    !x.Name.Equals(_dc, StringComparison.OrdinalIgnoreCase))
                    .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase))
                {
                    // Baseline is listed first so an oversized domain can
                    // never accidentally drop it from the default 12.
                    bool check = selected < 12;
                    _controllers.Items.Add(dc, check);
                    if (check) selected++;
                }
            }
            finally { _controllers.EndUpdate(); }
            _summary.Text = $"AD discovered {_discovered.Count} controller(s). " +
                $"Selected baseline: {_dc}; choose at most 12 DCs.";
            _status.Text = _discovered.Count > 12
                ? "Partial batch: more than 12 DCs. Select the other DCs for a separate scan."
                : "DC list loaded from selected domain; click Compare selected DCs.";
        }
        catch (Exception ex)
        {
            _discovered.Clear();
            _controllers.Items.Clear();
            _report = null;
            _summary.Text = "Domain controller inventory unavailable; no scan allowed.";
            _status.Text = UniversalDiagnosticsEmbedded.Clean(ex.Message, 180);
            AppLog.Write(ex, "Read-only DC discovery");
        }
        finally
        {
            _busy = false;
            _setParentBusy(false);
            UpdateActions();
        }
    }

    private async Task CompareAsync()
    {
        if (_busy || _parentBusy() || _discovered.Count == 0) return;
        var selected = _controllers.CheckedItems.Cast<DcChoice>()
            .Select(x => x.Name).ToArray();
        if (selected.Length is < 1 or > 12 ||
            !selected.Contains(_dc, StringComparer.OrdinalIgnoreCase))
        {
            _status.Text = "Select 1-12 discovered DCs, including the selected baseline DC.";
            return;
        }
        if (_discoveryUtc.AddMinutes(10) < DateTimeOffset.UtcNow)
        {
            _status.Text = "AD discovery is over 10 minutes old; discover DCs again.";
            return;
        }
        var current = _selection();
        if (string.IsNullOrWhiteSpace(current.CampaignId) ||
            !Guid.TryParse(current.GpoId, out _))
        {
            _status.Text = "Select a managed LockScreen GPO on the GPO tab first.";
            return;
        }
        _busy = true;
        _setParentBusy(true);
        UpdateActions();
        _report = null;
        _grid.Rows.Clear();
        _status.Text = "Checking RootDSE UTC, AD/GPT and SHA-256 on explicitly selected DCs...";
        try
        {
            var response = await Task.Run(() => DomainPowerShellRunner.Execute(
                DomainOperation.DcConsistency, "Scan",
                new {
                    domain = _domain, dc = _dc, campaignId = current.CampaignId,
                    gpoId = current.GpoId, controllers = selected
                }, _credentials, timeoutMilliseconds: 20 * 60 * 1000));
            var report = DcConsistencyReport.Parse(
                response.GetProperty("data"), _domain, _dc,
                current.CampaignId, current.GpoId,
                _discovered, selected, DateTimeOffset.UtcNow);
            if (IsDisposed) return;
            if (_selection() != current ||
                !selected.SequenceEqual(_controllers.CheckedItems.Cast<DcChoice>()
                    .Select(x => x.Name), StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    "GPO or DC selection changed during audit; repeat the scan.");
            _report = report;
            Render();
        }
        catch (Exception ex)
        {
            if (!IsDisposed)
            {
                _report = null;
                _summary.Text = "DC consistency not verified. No changes made.";
                _details.Text = "Read failed: no false success or automatic retry.";
                _status.Text = UniversalDiagnosticsEmbedded.Clean(ex.Message, 170);
                AppLog.Write(ex, "DC consistency audit");
            }
        }
        finally
        {
            _busy = false;
            _setParentBusy(false);
            UpdateActions();
        }
    }

    private void Render()
    {
        if (_report is null) return;
        _summary.Text = $"Result: {_report.Overall}  |  Verified matches: {_report.Matching}  |  " +
            $"Mismatched: {_report.Mismatch}  |  Clock warnings: {_report.ClockWarning}  |  " +
            $"Unreadable/invalid: {_report.Unavailable + _report.Invalid}  |  " +
            $"Scanned {_report.Rows.Count}/{_report.DiscoveredCount} discovered DCs.";
        _grid.Rows.Clear();
        foreach (var row in _report.Rows)
        {
            int index = _grid.Rows.Add(
                row.Node.Dc + (row.Node.Dc.Equals(_dc, StringComparison.OrdinalIgnoreCase)
                    ? "  [baseline]" : ""),
                row.Assessment, row.Node.ReadStatus,
                row.Node.DcUtc.HasValue ? row.Node.OffsetSeconds.ToString(
                    "+0;-0;0", CultureInfo.InvariantCulture) + " sec" : "Unknown",
                row.Node.ReadStatus == "Readable"
                    ? $"{row.Node.AdVersion}/{row.Node.GptVersion}" : "Unknown",
                row.Node.ReadStatus == "Readable"
                    ? row.Node.Images.Count.ToString(CultureInfo.InvariantCulture) : "—",
                row.Node.Reason);
            _grid.Rows[index].Tag = row;
            if (row.Assessment is "Mismatch" or "Invalid" or "Clock drift")
                _grid.Rows[index].DefaultCellStyle.ForeColor = Color.DarkRed;
        }
        _grid.ClearSelection();
        if (_grid.Rows.Count > 0)
        {
            _grid.Rows[0].Selected = true;
            _grid.CurrentCell = _grid.Rows[0].Cells[0];
        }
        ShowSelected();
        _status.Text = "Read-only scan at " +
            _report.ScannedUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss") +
            ". Results may change after DFSR/AD replication; inspect errors manually.";
    }

    private void ShowSelected()
    {
        if (_grid.SelectedRows.Count != 1 ||
            _grid.SelectedRows[0].Tag is not DcConsistencyRow row)
        {
            _details.Text = "Select one controller to view audit evidence.";
            return;
        }
        var node = row.Node;
        var sb = new StringBuilder();
        sb.AppendLine("DC: " + node.Dc);
        sb.AppendLine("Assessment: " + row.Assessment + " — " + row.Explanation);
        sb.AppendLine("Read status: " + node.ReadStatus + " / " + node.Reason);
        sb.AppendLine("RODC: " + node.Rodc);
        sb.AppendLine("DC UTC: " + (node.DcUtc?.ToString("o") ?? "Unknown"));
        sb.AppendLine("Local sample UTC: " + (node.SampledUtc?.ToString("o") ?? "Unknown"));
        sb.AppendLine("UTC offset: " + (node.DcUtc.HasValue
            ? node.OffsetSeconds.ToString(CultureInfo.InvariantCulture) + " seconds" : "Unknown"));
        sb.AppendLine("AD / GPT: " + node.AdVersion + " / " + node.GptVersion);
        sb.AppendLine("campaign.json: " + (node.ManifestHash.Length > 0 ? node.ManifestHash : "Unknown"));
        sb.AppendLine("Agent: " + (node.AgentHash.Length > 0 ? node.AgentHash : "Unknown"));
        sb.AppendLine("gpt.ini: " + (node.GptIniHash.Length > 0 ? node.GptIniHash : "Unknown"));
        sb.AppendLine("psscripts.ini: " + (node.PsScriptsHash.Length > 0 ? node.PsScriptsHash : "Unknown"));
        foreach (var image in node.Images)
            sb.AppendLine($"{image.FileName}  {image.Bytes:N0} bytes  SHA-256 {image.Sha256}");
        sb.AppendLine("Observation only. No client RSoP, DFSR state or remote changes.");
        _details.Text = sb.ToString();
    }

    private static string Csv(string value)
    {
        string safe = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        string trim = safe.TrimStart();
        if (trim.Length > 0 && "=+-@".Contains(trim[0])) safe = "'" + safe;
        return "\"" + safe.Replace("\"", "\"\"") + "\"";
    }

    private void ExportVisible()
    {
        if (_report is null) return;
        if (MessageBox.Show(FindForm(),
            "Export local DC comparison? Contains DC hostnames, GPO/campaign ID, " +
            "versions and file hashes. Review before sharing outside the domain. " +
            "The report is not proof of DFS Replication completion.",
            "Export private DC audit", MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        using var save = new SaveFileDialog {
            Filter = "CSV UTF-8 (*.csv)|*.csv",
            Title = "Save DC Consistency Audit (private local file)",
            FileName = "LSCM-DC-Consistency-" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".csv"
        };
        if (save.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            var sb = new StringBuilder(
                "Domain,CampaignId,GpoId,SelectedBaseline,DC,Assessment,ReadStatus,Reason,DCUtc,SampledUtc,OffsetSeconds,RODC,AdVersion,GptVersion,ManifestSHA256,AgentSHA256,GptIniSHA256,StartupSHA256,ImageCount,Images,Overall,DiscoveredCount,ScannedCount\r\n");
            foreach (var row in _report.Rows)
            {
                var node = row.Node;
                string images = string.Join("; ", node.Images.Select(x =>
                    $"{x.FileName}:{x.Sha256}:{x.Bytes}"));
                sb.AppendJoin(',', new[] {
                    _report.Domain, _report.CampaignId, _report.GpoId,
                    _report.SelectedDc, node.Dc, row.Assessment,
                    node.ReadStatus, node.Reason, node.DcUtc?.ToString("o") ?? "",
                    node.SampledUtc?.ToString("o") ?? "",
                    node.OffsetSeconds.ToString(CultureInfo.InvariantCulture),
                    node.Rodc, node.AdVersion.ToString(CultureInfo.InvariantCulture),
                    node.GptVersion.ToString(CultureInfo.InvariantCulture),
                    node.ManifestHash, node.AgentHash, node.GptIniHash,
                    node.PsScriptsHash, node.Images.Count.ToString(CultureInfo.InvariantCulture),
                    images, _report.Overall, _report.DiscoveredCount.ToString(
                        CultureInfo.InvariantCulture), _report.Rows.Count.ToString(
                        CultureInfo.InvariantCulture)
                }.Select(Csv)).Append("\r\n");
            }
            File.WriteAllText(save.FileName, sb.ToString(), new UTF8Encoding(true));
            _status.Text = "Private read-only DC audit saved locally. No automatic upload.";
        }
        catch (Exception ex) { UiErrorDialog.Show(FindForm(), "Export DC audit", ex); }
    }
}
