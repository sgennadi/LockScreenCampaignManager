using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Read-only closure review. Agent "Completed" is a self-report emitted
/// BEFORE the final local file/task removal. This screen never offers an
/// automatic fleet-cleanup action or mistakes GPO unlink for local cleanup.
/// </summary>
internal sealed class CampaignClosureAuditDialog : Form
{
    private readonly CampaignClosureAudit _audit;
    private readonly CampaignGpoLinkEvidence _gpo;
    private readonly Label _summary = new();
    private readonly Label _policy = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _details = new();
    private readonly TextBox _search = new();
    private readonly ComboBox _category = new();
    private readonly Label _footer = new();
    private readonly List<ClosureAuditRow> _visible = [];
    private readonly Dictionary<string, PostCleanupObservation> _remote =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly PostCleanupScope? _scope;
    private readonly Func<IReadOnlyList<string>, Task<IReadOnlyList<PostCleanupObservation>>>? _scan;
    private readonly IReadOnlyCollection<string> _eligible;
    private readonly Button _verify = new();
    private bool _verifying;

    internal CampaignClosureAuditDialog(CampaignClosureAudit audit,
        CampaignGpoLinkEvidence gpo,
        PostCleanupScope? scope = null,
        IReadOnlyCollection<string>? eligible = null,
        IReadOnlyList<PostCleanupObservation>? previouslyObserved = null,
        Func<IReadOnlyList<string>, Task<IReadOnlyList<PostCleanupObservation>>>? scan = null)
    {
        _audit = audit ?? throw new ArgumentNullException(nameof(audit));
        _gpo = gpo ?? throw new ArgumentNullException(nameof(gpo));
        _scope = scope;
        _scan = scan;
        _eligible = eligible ?? [];
        foreach (var item in previouslyObserved ?? []) _remote[item.Computer] = item;
        Text = "LockScreen Campaign Manager - Campaign closure audit";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        MinimumSize = new Size(900, 680);
        Size = new Size(1280, 870);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 7,
            Padding = new Padding(16), BackColor = UiStyle.Canvas
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 65));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 83));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 62));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 38));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        Controls.Add(root);
        root.Controls.Add(new Label
        {
            Text = "Campaign closure audit",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 16f),
            ForeColor = UiStyle.Foreground
        }, 0, 0);
        _summary.Dock = DockStyle.Fill;
        _summary.ForeColor = UiStyle.Foreground;
        _summary.AutoEllipsis = true;
        _summary.Text = $"AD devices: {_audit.Total}  |  Agent-reported Completed: " +
            $"{_audit.ReportedComplete}  |  Repair tracked: {_audit.RepairTracked}\r\n" +
            $"Active/pending: {_audit.Outstanding}  |  No report: {_audit.NoReport}  |  " +
            $"Review: {_audit.NeedsReview}  |  Other: {_audit.Unexpected}";
        root.Controls.Add(_summary, 0, 1);
        _policy.Dock = DockStyle.Fill;
        _policy.AutoEllipsis = true;
        _policy.ForeColor = UiStyle.Muted;
        _policy.Text = _gpo.Available
            ? $"DC: {_gpo.DomainController}  |  {_gpo.DirectOuLink}  |  " +
              $"{_gpo.MachineConfiguration}  |  Other domain/site links: {_gpo.OtherLinks}\r\n" +
              "GPO link state is not endpoint RSoP. Local agent/tasks can remain active."
            : $"GPO state: UNKNOWN on {_gpo.DomainController}. " +
              "Selected DC read failed; do not infer link removal.\r\n" + _gpo.Caution;
        root.Controls.Add(_policy, 0, 2);

        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true
        };
        filters.Controls.Add(new Label
        {
            Text = "State:", AutoSize = true, Margin = new Padding(2, 14, 6, 0)
        });
        _category.DropDownStyle = ComboBoxStyle.DropDownList;
        _category.Width = 220;
        _category.Items.AddRange(new object[]
        {
            "All", "Reported completion", "Repair unresolved",
            "Active or pending", "Needs review", "No report", "Unclassified"
        });
        _category.SelectedIndex = 0;
        _category.SelectedIndexChanged += (_, _) => Draw();
        filters.Controls.Add(_category);
        _search.Width = 260;
        _search.PlaceholderText = "Search computer...";
        _search.Margin = new Padding(16, 7, 7, 0);
        _search.TextChanged += (_, _) => Draw();
        filters.Controls.Add(_search);
        root.Controls.Add(filters, 0, 3);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = true;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn
            { HeaderText = "Computer", FillWeight = 155 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
            { HeaderText = "Closure category", FillWeight = 143 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
            { HeaderText = "Agent stage", FillWeight = 100 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
            { HeaderText = "Repair queue", FillWeight = 131 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
            { HeaderText = "Last report", FillWeight = 115 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
            { HeaderText = "Post-cleanup check", FillWeight = 160 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
            { HeaderText = "Last observed", FillWeight = 120 });
        _grid.SelectionChanged += (_, _) => DrawDetails();
        root.Controls.Add(_grid, 0, 4);
        _details.Dock = DockStyle.Fill;
        _details.Multiline = true;
        _details.ReadOnly = true;
        _details.WordWrap = true;
        _details.ScrollBars = ScrollBars.Vertical;
        _details.BackColor = UiStyle.Surface;
        root.Controls.Add(_details, 0, 5);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2
        };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true
        };
        _verify.Text = "Verify selected (read-only)";
        _verify.Width = 238; _verify.Height = 38;
        _verify.Margin = new Padding(0, 4, 10, 4);
        _verify.BackColor = UiStyle.Surface;
        _verify.FlatStyle = FlatStyle.Flat;
        _verify.Enabled = _scope is not null && _scan is not null;
        _verify.Click += (_, _) => _ = VerifySelectedAsync();
        actions.Controls.Add(_verify);
        actions.Controls.Add(MakeButton("Export visible CSV", ExportVisible, 170));
        actions.Controls.Add(MakeButton("Close", Close, 100));
        footer.Controls.Add(actions, 0, 0);
        _footer.Dock = DockStyle.Fill;
        _footer.ForeColor = UiStyle.Muted;
        _footer.AutoEllipsis = true;
        footer.Controls.Add(_footer, 0, 1);
        root.Controls.Add(footer, 0, 6);

        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "campaign-closure");
        FormClosing += (_, e) => { if (_verifying) e.Cancel = true; };
        Draw();
    }

    private static Button MakeButton(string name, Action action, int width)
    {
        var result = new Button
        {
            Text = name, Width = width, Height = 38,
            Margin = new Padding(0, 4, 10, 4),
            BackColor = UiStyle.Surface, FlatStyle = FlatStyle.Flat
        };
        result.Click += (_, _) => action();
        return result;
    }

    private void Draw()
    {
        string category = _category.SelectedItem as string ?? "All";
        string query = _search.Text.Trim();
        _visible.Clear();
        _visible.AddRange(_audit.Rows.Where(x =>
            (category == "All" || x.Category == category) &&
            (query.Length == 0 ||
             x.ComputerFqdn.Contains(query, StringComparison.OrdinalIgnoreCase))));
        _grid.SuspendLayout();
        try
        {
            _grid.Rows.Clear();
            foreach (var item in _visible)
            {
                _remote.TryGetValue(item.ComputerFqdn, out var remote);
                int index = _grid.Rows.Add(item.ComputerFqdn, item.Category,
                    item.AgentState, item.RepairState, item.LastReport,
                    remote is null ? "Not checked" : remote.DisplayState,
                    remote is null ? "—" : remote.ObservedUtc.LocalDateTime.ToString("yyyy-MM-dd HH:mm:ss"));
                if (item.Category == "Repair unresolved" ||
                    item.Category == "Needs review")
                    _grid.Rows[index].DefaultCellStyle.ForeColor = Color.DarkRed;
            }
            _grid.ClearSelection();
            if (_grid.Rows.Count > 0)
            {
                _grid.Rows[0].Selected = true;
                _grid.CurrentCell = _grid.Rows[0].Cells[0];
            }
        }
        finally { _grid.ResumeLayout(); }
        DrawDetails();
        int verified = _remote.Values.Count(x => x.IsObservedVerified);
        int unknown = _remote.Values.Count(x => x.Status == "Unknown");
        int pending = _remote.Values.Count(x => x.Status == "Pending");
        _footer.Text = $"Showing {_visible.Count}/{_audit.Total}; last observed clean: {verified}, " +
            $"pending: {pending}, unknown: {unknown}, not checked: {_audit.Total - _remote.Count}. " +
            "Results are historical observations; no automatic rechecks or remote changes.";
    }

    private void DrawDetails()
    {
        if (_grid.SelectedRows.Count != 1 ||
            _grid.SelectedRows[0].Index >= _visible.Count)
        {
            _details.Text = "Select a verified AD computer to inspect its closure assessment.";
            return;
        }
        var item = _visible[_grid.SelectedRows[0].Index];
        _remote.TryGetValue(item.ComputerFqdn, out var observation);
        _details.Text =
            $"Computer: {item.ComputerFqdn}\r\n" +
            $"Category: {item.Category}\r\n" +
            $"Agent stage: {item.AgentState}\r\n" +
            $"Repair state: {item.RepairState}\r\n" +
            $"Last report: {item.LastReport}\r\n\r\n" +
            $"Assessment: {item.Explanation}\r\n\r\n" +
            $"Next action: {item.NextAction}\r\n\r\n" +
            (observation is null ? "Post-cleanup: Not checked on this PC.\r\n" :
                $"Post-cleanup: {observation.DisplayState} at " +
                $"{observation.ObservedUtc.LocalDateTime:yyyy-MM-dd HH:mm:ss}\r\n" +
                $"Reason: {observation.Reason}\r\n" +
                $"Receipt: {observation.ReceiptValid}, Tasks gone: {observation.TasksGone}, " +
                $"Files gone: {observation.LocalFolderGone}, Owner released: " +
                $"{observation.OwnerReleased}, HKLM match: {observation.RegistryRestored}\r\n") +
            "This is a read-only operational review, not proof of " +
            "physical lock screen rendering, local cleanup, or GPO removal.";
    }


    private async Task VerifySelectedAsync()
    {
        if (_verifying || _scope is null || _scan is null) return;
        var hosts = _grid.SelectedRows.Cast<DataGridViewRow>()
            .Where(x => x.Index >= 0 && x.Index < _visible.Count)
            .Select(x => _visible[x.Index].ComputerFqdn)
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (hosts.Length is < 1 or > 25)
        {
            MessageBox.Show(this, "Select between 1 and 25 verified AD computers (Ctrl+click for multiple).",
                "Post-cleanup verification", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        if (MessageBox.Show(this,
            "Perform READ-ONLY SMB, Task Scheduler RPC and Remote Registry reads " +
            $"on {hosts.Length} selected verified computer(s)?\r\n\r\n" +
            "No remote task, registry or GPO will be modified. Offline/unreachable " +
            "computers remain Unknown until another explicit verification.",
            "Verify post-cleanup", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;

        _verifying = true;
        _verify.Enabled = false;
        _footer.Text = "Reading remote cleanup evidence; no changes will be made...";
        try
        {
            var results = await _scan(hosts);
            if (IsDisposed) return;
            PostCleanupVerificationStore.Save(_scope, _eligible, results);
            foreach (var result in results) _remote[result.Computer] = result;
            Draw();
            _footer.Text = $"Completed read-only verification for {results.Count} device(s). " +
                "Observations saved locally; offline states remain for a manual retry.";
        }
        catch (Exception ex)
        {
            if (!IsDisposed) UiErrorDialog.Show(this, "Post-cleanup verification", ex);
        }
        finally
        {
            _verifying = false;
            if (!IsDisposed) _verify.Enabled = _scan is not null;
        }
    }

    private static string Csv(string value)
    {
        var safe = value.Replace('\r', ' ').Replace('\n', ' ')
            .Replace('\t', ' ');
        var trimmed = safe.TrimStart();
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0]))
            safe = "'" + safe;
        return "\"" + safe.Replace("\"", "\"\"") + "\"";
    }

    private void ExportVisible()
    {
        if (_visible.Count == 0) return;
        if (MessageBox.Show(this,
            "Export the visible closure assessment to a local CSV? It includes " +
            "computer FQDNs, the selected campaign and GPO IDs, and operational " +
            "information. Review it before sharing. A report is not proof of " +
            "endpoint cleanup.",
            "Export private closure audit", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        using var dialog = new SaveFileDialog
        {
            Title = "Export campaign closure audit",
            Filter = "CSV UTF-8 (*.csv)|*.csv",
            FileName = "LSCM-Closure-Audit-" +
                DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var csv = new StringBuilder(
                "Computer,ClosureCategory,AgentStage,RepairState,LastReport,Assessment,NextAction,Campaign,GpoId,GpoDirectLink,GpoMachineConfig,GpoAvailable,PostCleanupState,PostCleanupReason,PostCleanupObservedUtc\r\n");
            foreach (var item in _visible)
            {
                _remote.TryGetValue(item.ComputerFqdn, out var last);
                csv.AppendJoin(',', new[]
                {
                    item.ComputerFqdn, item.Category, item.AgentState,
                    item.RepairState, item.LastReport, item.Explanation,
                    item.NextAction, _audit.CampaignId, _audit.GpoId,
                    _gpo.DirectOuLink, _gpo.MachineConfiguration,
                    _gpo.Available ? "Yes" : "No",
                    last is null ? "NotChecked" : last.Status,
                    last?.Reason ?? "NotChecked", last?.ObservedUtc.ToUniversalTime().ToString("o") ?? ""
                }.Select(Csv)).Append("\r\n");
            }
            File.WriteAllText(dialog.FileName, csv.ToString(), new UTF8Encoding(true));
            _footer.Text = "Local CSV saved: " + dialog.FileName;
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Export closure audit", ex); }
    }
}
