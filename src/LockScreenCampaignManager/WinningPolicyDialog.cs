using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Read-only per-setting priority comparison. Never labels a projected
/// leader as a verified effective policy or changes the endpoint/domain.
/// </summary>
internal sealed class WinningPolicyDialog : Form
{
    private readonly WinningPolicyAnalysis _analysis;
    private readonly ComboBox _filter = new();
    private readonly TextBox _search = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _details = new();
    private readonly Label _footer = new();
    private readonly List<WinningPolicyRow> _visible = [];

    internal WinningPolicyDialog(WinningPolicyAnalysis analysis)
    {
        _analysis = analysis ?? throw new ArgumentNullException(nameof(analysis));
        Text = "LockScreen Campaign Manager - Winning Policy Analysis";
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(970, 680);
        Size = new Size(1320, 850);
        StartPosition = FormStartPosition.CenterParent;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6,
            Padding = new Padding(16), BackColor = UiStyle.Canvas
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Text = "Winning Policy Analysis - static priority, NOT verified RSoP",
            Dock = DockStyle.Fill, AutoEllipsis = true,
            Font = new Font("Segoe UI Semibold", 15),
            ForeColor = UiStyle.Foreground
        }, 0, 0);
        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill, AutoEllipsis = true,
            ForeColor = UiStyle.Foreground,
            Text = "Computer: " + _analysis.Computer + "  |  DC: " + _analysis.Dc +
                "\r\nActual AD container: " + _analysis.ActualComputerContainer +
                "\r\nSettings: " + _analysis.Rows.Count +
                "  |  Conflicting values: " + _analysis.ConflictingSettings +
                "  |  Calculated priority leaders: " + _analysis.PriorityLeaders +
                "  |  Verified winners: 0 (NOT PROVEN)" +
                "\r\nEndpoint HKLM: " + _analysis.EndpointStatus +
                "  |  OS: " + _analysis.EndpointOs +
                "  |  Value matches: " + _analysis.EndpointValueMatches +
                "  |  Value differs: " + _analysis.EndpointValueDiffers +
                "\r\nRSoP Registry CSE: " + _analysis.RsopCseStatus +
                "  |  CSE reported sources: " + _analysis.CseReportedWinners +
                "  |  Managed image SHA-256: " + _analysis.ImageHashStatus
        }, 0, 1);

        var filters = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = true, WrapContents = false
        };
        filters.Controls.Add(new Label
        {
            Text = "Show:", AutoSize = true,
            Margin = new Padding(1, 13, 8, 0)
        });
        _filter.Width = 240;
        _filter.DropDownStyle = ComboBoxStyle.DropDownList;
        _filter.Items.AddRange([
            "All", "Conflicting values", "Ranked leaders", "Unranked",
            "Endpoint value matches", "Endpoint value differs"
        ]);
        _filter.SelectedIndex = 0;
        _filter.SelectedIndexChanged += (_, _) => Draw();
        filters.Controls.Add(_filter);
        _search.Width = 380;
        _search.Margin = new Padding(16, 6, 8, 0);
        _search.PlaceholderText = "Search setting, registry key, GPO or value...";
        _search.TextChanged += (_, _) => Draw();
        filters.Controls.Add(_search);
        root.Controls.Add(filters, 0, 2);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.MultiSelect = false;
        _grid.RowHeadersVisible = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Setting", FillWeight = 90 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Registry key", FillWeight = 180 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Static priority leader", FillWeight = 130 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value", FillWeight = 125 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Assessment", FillWeight = 145 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Observed HKLM", FillWeight = 125 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Value comparison", FillWeight = 150 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "CSE source (cached)", FillWeight = 140 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Sources", FillWeight = 58 });
        _grid.SelectionChanged += (_, _) => ShowDetails();
        root.Controls.Add(_grid, 0, 3);

        _details.Dock = DockStyle.Fill;
        _details.ReadOnly = true;
        _details.Multiline = true;
        _details.ScrollBars = ScrollBars.Both;
        _details.WordWrap = false;
        _details.BackColor = UiStyle.Surface;
        _details.Font = new Font("Consolas", 9);
        root.Controls.Add(_details, 0, 4);

        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2
        };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true
        };
        actions.Controls.Add(MakeButton("Export visible CSV", 195, ExportVisible));
        actions.Controls.Add(MakeButton("Close", 115, Close));
        footer.Controls.Add(actions, 0, 0);
        _footer.Dock = DockStyle.Fill;
        _footer.AutoEllipsis = true;
        _footer.ForeColor = UiStyle.Muted;
        footer.Controls.Add(_footer, 0, 1);
        root.Controls.Add(footer, 0, 5);
        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "winning-policy-analysis");
        Draw();
    }

    private static Button MakeButton(string label, int width, Action action)
    {
        var button = new Button
        {
            Text = label, Width = width, Height = 38,
            FlatStyle = FlatStyle.Flat, BackColor = UiStyle.Surface,
            Margin = new Padding(0, 4, 10, 4)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private void Draw()
    {
        string filter = _filter.SelectedItem as string ?? "All";
        string query = _search.Text.Trim();
        _visible.Clear();
        _visible.AddRange(_analysis.Rows.Where(x =>
            (filter == "All" ||
             (filter == "Conflicting values" && x.HasConflict) ||
             (filter == "Ranked leaders" && x.SuggestedGpoId.Length > 0) ||
             (filter == "Unranked" && x.SuggestedGpoId.Length == 0) ||
             (filter == "Endpoint value matches" &&
                 x.EndpointComparison == "Matches static estimate (source unverified)") ||
             (filter == "Endpoint value differs" &&
                 x.EndpointComparison == "Differs from static estimate (source unknown)")) &&
            (query.Length == 0 ||
             x.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             x.Key.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             x.SuggestedValue.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             x.EndpointValue.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             x.EndpointComparison.Contains(query, StringComparison.OrdinalIgnoreCase) ||
             x.Sources.Any(s => s.Gpo.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                s.Value.Contains(query, StringComparison.OrdinalIgnoreCase)))));
        _grid.Rows.Clear();
        foreach (var row in _visible)
        {
            int index = _grid.Rows.Add(row.Name, row.Key,
                row.SuggestedGpo.Length == 0 ? "Unknown" : row.SuggestedGpo,
                row.SuggestedValue, row.Assessment,
                row.EndpointState == "Present" ? row.EndpointValue : row.EndpointState,
                row.EndpointComparison,
                row.RsopCseGpoId.Length > 0 ? row.RsopCseGpoId : row.RsopCseStatus,
                row.Sources.Count);
            _grid.Rows[index].Tag = row;
            if (row.EndpointComparison == "Differs from static estimate (source unknown)" ||
                row.HasConflict)
                _grid.Rows[index].DefaultCellStyle.ForeColor = Color.DarkRed;
        }
        _grid.ClearSelection();
        if (_grid.Rows.Count != 0)
        {
            _grid.Rows[0].Selected = true;
            _grid.CurrentCell = _grid.Rows[0].Cells[0];
        }
        ShowDetails();
        _footer.Text = "Showing " + _visible.Count + "/" + _analysis.Rows.Count +
            " settings. AD snapshot: " + _analysis.AdObservedUtc.ToLocalTime().ToString(
                "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) +
            (_analysis.ClientObservedUtc is { } utc
                ? " | Imported client XML: " + utc.ToLocalTime().ToString(
                    "yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                : " | No client XML") +
            (_analysis.EndpointObservedUtc is { } endpointTime
                ? " | HKLM: " + _analysis.EndpointStatus + " " +
                    endpointTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm",
                        CultureInfo.InvariantCulture)
                : " | HKLM not checked") +
            " | CSE " + _analysis.RsopCseStatus +
            " | JPEG " + _analysis.ImageHashStatus +
            ". Neither CSE nor SHA-256 proves physical screen display.";
    }

    private void ShowDetails()
    {
        if (_grid.SelectedRows.Count != 1 ||
            _grid.SelectedRows[0].Tag is not WinningPolicyRow row)
        {
            _details.Text = "Select a setting to inspect precedence and verification gaps.";
            return;
        }
        var sb = new StringBuilder();
        sb.AppendLine("Registry: " + row.Key + "\\" + row.Name);
        sb.AppendLine("Assessment: " + row.Assessment);
        sb.AppendLine("Estimated priority leader: " +
            (row.SuggestedGpo.Length == 0 ? "None - order unknown" :
                row.SuggestedGpo + " (" + row.SuggestedGpoId + ")"));
        sb.AppendLine("Estimated value: " + row.SuggestedValue);
        sb.AppendLine("Endpoint observed: " + row.EndpointState +
            " (" + row.EndpointKind + ")");
        sb.AppendLine("Endpoint value: " + row.EndpointValue);
        sb.AppendLine("Value comparison: " + row.EndpointComparison);
        sb.AppendLine("Endpoint OS: " + _analysis.EndpointOs +
            " | Remote HKLM status: " + _analysis.EndpointStatus +
            " | UTC: " + _analysis.EndpointObservedUtc?.ToString("o"));
        sb.AppendLine("Registry Policy CSE: " + row.RsopCseStatus);
        sb.AppendLine("Reported CSE source GPO (cached): " +
            (row.RsopCseGpoId.Length > 0 ? row.RsopCseGpoId : "UNKNOWN"));
        sb.AppendLine("CSE detail: " + row.RsopCseExplanation);
        sb.AppendLine("Image hash: " + _analysis.ImageHashStatus +
            " | file: " + _analysis.ImageFileName);
        sb.AppendLine("Expected SHA256: " + _analysis.ImageExpectedSha256);
        sb.AppendLine("Observed SHA256: " + _analysis.ImageActualSha256);
        sb.AppendLine("Hash note: " + _analysis.ImageNote);
        sb.AppendLine("No proof the file is physically displayed on the lock screen.");
        sb.AppendLine("Reason: " + row.Explanation);
        sb.AppendLine();
        sb.AppendLine("Candidate sources (GPO link instances, NOT confirmed processing order):");
        foreach (var source in row.Sources)
        {
            sb.AppendLine("  " + source.Gpo + " [" + source.GpoId + "]");
            sb.AppendLine("    Value: " + source.Value + " | " + source.Source);
            sb.AppendLine("    Link: " + source.LinkedAt + " | GPMC order: " +
                source.LinkOrder + " | depth: " + source.ScopeDepth +
                " | Enforced: " + source.Enforced);
            sb.AppendLine("    AD: " + source.Category + " | RSoP XML: " +
                source.ClientObservation + " | Rank eligible: " + source.EligibleByLink);
        }
        sb.AppendLine();
        foreach (string warning in _analysis.Limitations)
            sb.AppendLine("LIMITATION: " + warning);
        sb.AppendLine("VERIFIED WINNING POLICY: UNKNOWN. This report never makes a read-only estimate authoritative.");
        _details.Text = sb.ToString();
    }

    private static string Csv(string input)
    {
        string safe = input.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        if ("=+-@".Contains(safe.TrimStart().FirstOrDefault()) &&
            safe.TrimStart().Length != 0)
            safe = "'" + safe;
        return "\"" + safe.Replace("\"", "\"\"") + "\"";
    }

    private void ExportVisible()
    {
        if (_visible.Count == 0) return;
        if (MessageBox.Show(this, "Export private AD/GPO identifiers, registry keys, values " +
            "and computer details to a local CSV? Static priority is not an actual client " +
            "winning policy. No network upload is performed.",
            "Export private policy priority", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        using var save = new SaveFileDialog
        {
            Title = "Export Winning Policy Analysis (unverified)",
            Filter = "CSV UTF-8 (*.csv)|*.csv",
            FileName = "LSCM-Winning-Policy-" + DateTime.Now.ToString(
                "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv"
        };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var sb = new StringBuilder(
                "Computer,DomainController,ObservedUtc,SettingKey,SettingName,Assessment,StaticLeader,LeaderGuid,EstimatedValue,EndpointObservedUtc,EndpointRegistryStatus,EndpointOs,EndpointState,EndpointKind,EndpointValue,EndpointComparison,RsopCseStatus,RsopCseGpoId,ImageHashStatus,ImageFileName,ImageExpectedSha256,ImageActualSha256,Conflict,SourceCount,AllSources,Explanation,VerifiedWinningPolicy\r\n");
            foreach (var row in _visible)
            {
                var fields = new[]
                {
                    _analysis.Computer, _analysis.Dc,
                    _analysis.AdObservedUtc.ToUniversalTime().ToString("o"),
                    row.Key, row.Name, row.Assessment, row.SuggestedGpo,
                    row.SuggestedGpoId, row.SuggestedValue,
                    _analysis.EndpointObservedUtc?.ToUniversalTime().ToString("o") ?? "",
                    _analysis.EndpointStatus, _analysis.EndpointOs,
                    row.EndpointState, row.EndpointKind, row.EndpointValue, row.EndpointComparison,
                    row.RsopCseStatus, row.RsopCseGpoId,
                    _analysis.ImageHashStatus, _analysis.ImageFileName,
                    _analysis.ImageExpectedSha256, _analysis.ImageActualSha256,
                    row.HasConflict ? "Yes" : "No",
                    row.Sources.Count.ToString(CultureInfo.InvariantCulture),
                    string.Join("; ", row.Sources.Select(s =>
                        s.Gpo + " [" + s.GpoId + "]=" + s.Value +
                        " (order " + s.LinkOrder + ", depth " + s.ScopeDepth +
                        ", enforced " + s.Enforced + ", client " + s.ClientObservation + ")")),
                    row.Explanation, "No"
                };
                sb.AppendJoin(',', fields.Select(Csv)).Append("\r\n");
            }
            File.WriteAllText(save.FileName, sb.ToString(), new UTF8Encoding(true));
            _footer.Text = "Read-only report exported locally; never uploaded automatically.";
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Export Winning Policy Analysis", ex); }
    }
}
