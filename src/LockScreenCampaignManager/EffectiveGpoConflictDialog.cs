using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// An English, DPI-safe read-only report for one AD-verified computer.
/// It never invokes gpupdate, executes remote code or changes GPO filtering.
/// </summary>
internal sealed class EffectiveGpoConflictDialog : Form
{
    private readonly EffectiveGpoReport _report;
    private readonly ComboBox _filter = new();
    private readonly TextBox _search = new();
    private readonly DataGridView _grid = new();
    private readonly TextBox _details = new();
    private readonly Label _footer = new();
    private readonly List<EffectiveGpoPolicy> _visible = [];
    private RsopEvidence? _rsop;
    private EndpointLockScreenObservation? _endpoint;
    private EndpointSourceEvidence? _sourceEvidence;
    private readonly Func<Task<EndpointLockScreenObservation>>? _verifyEndpoint;
    private readonly Func<Task<EndpointEvidenceBundle>>? _verifyEvidence;
    private readonly Button _evidenceButton;
    private readonly Button _verifyButton;
    private readonly Button _clearEndpoint;
    private readonly Button _clearRsop;

    internal EffectiveGpoConflictDialog(EffectiveGpoReport report,
        Func<Task<EndpointLockScreenObservation>>? verifyEndpoint = null,
        Func<Task<EndpointEvidenceBundle>>? verifyEvidence = null)
    {
        _report = report ?? throw new ArgumentNullException(nameof(report));
        _verifyEndpoint = verifyEndpoint;
        _verifyEvidence = verifyEvidence;
        Text = "LockScreen Campaign Manager - Effective GPO candidates";
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(1000, 680);
        Size = new Size(1350, 860);
        StartPosition = FormStartPosition.CenterParent;
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 6,
            Padding = new Padding(16), BackColor = UiStyle.Canvas
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 80));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 60));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 40));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 95));
        Controls.Add(root);
        root.Controls.Add(new Label
        {
            Text = "Effective GPO candidates — not an RSoP",
            Dock = DockStyle.Fill, AutoEllipsis = true,
            Font = new Font("Segoe UI Semibold", 16),
            ForeColor = UiStyle.Foreground
        }, 0, 0);
        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill, AutoEllipsis = true,
            Text = $"Computer: {_report.Computer}  |  DC: {_report.Dc}\r\n" +
                $"Computer membership: {_report.Membership}  |  " +
                $"Token groups: {_report.TokenStatus}  |  Site association: UNKNOWN\r\n" +
                $"GPO candidates: {_report.Policies.Count}  |  " +
                $"Potential high overlaps: {_report.PotentialHigh}  |  " +
                $"Requires verification: {_report.NeedsVerification}  |  " +
                $"OU/link excluded: {_report.Excluded}",
            ForeColor = UiStyle.Foreground
        }, 0, 1);

        var bar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true
        };
        bar.Controls.Add(new Label { Text = "Assessment:",
            AutoSize = true, Margin = new Padding(1, 13, 8, 0) });
        _filter.Width = 265;
        _filter.DropDownStyle = ComboBoxStyle.DropDownList;
        _filter.Items.AddRange([
            "All", "Potentially applicable", "Needs RSoP / verification",
            "Security deny candidate", "Site applicability unknown",
            "Excluded by link/config"
        ]);
        _filter.SelectedIndex = 0;
        _filter.SelectedIndexChanged += (_, _) => Draw();
        bar.Controls.Add(_filter);
        _search.Width = 280;
        _search.Margin = new Padding(16, 7, 7, 0);
        _search.PlaceholderText = "Search GPO or policy setting...";
        _search.TextChanged += (_, _) => Draw();
        bar.Controls.Add(_search);
        bar.Controls.Add(MakeButton("Import gpresult XML", 195, ImportRsopXml));
        _clearRsop = MakeButton("Clear imported RSoP", 178, ClearImportedRsop);
        _clearRsop.Enabled = false;
        bar.Controls.Add(_clearRsop);
        root.Controls.Add(bar, 0, 2);

        _grid.Dock = DockStyle.Fill;
        _grid.ReadOnly = true;
        _grid.AllowUserToAddRows = false;
        _grid.AllowUserToDeleteRows = false;
        _grid.RowHeadersVisible = false;
        _grid.MultiSelect = false;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "GPO", FillWeight = 140 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Assessment", FillWeight = 155 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Link", FillWeight = 110 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Security", FillWeight = 160 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "WMI", FillWeight = 105 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "High values", FillWeight = 65 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "Imported RSoP", FillWeight = 155 });
        _grid.SelectionChanged += (_, _) => ShowDetails();
        root.Controls.Add(_grid, 0, 3);
        _details.Dock = DockStyle.Fill;
        _details.ReadOnly = true;
        _details.Multiline = true;
        _details.ScrollBars = ScrollBars.Vertical;
        _details.WordWrap = false;
        _details.BackColor = UiStyle.Surface;
        _details.Font = new Font("Consolas", 9);
        root.Controls.Add(_details, 0, 4);

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
        _verifyButton = MakeButton("Verify endpoint HKLM", 205,
            () => _ = VerifyEndpointAsync());
        _verifyButton.Enabled = _verifyEndpoint is not null;
        actions.Controls.Add(_verifyButton);
        _evidenceButton = MakeButton("Verify RSoP + image", 206,
            () => _ = VerifyEvidenceAsync());
        _evidenceButton.Enabled = _verifyEvidence is not null;
        actions.Controls.Add(_evidenceButton);
        _clearEndpoint = MakeButton("Clear HKLM snapshot", 185, ClearEndpoint);
        _clearEndpoint.Enabled = false;
        actions.Controls.Add(_clearEndpoint);
        actions.Controls.Add(MakeButton("Winning policy analysis", 225, ShowWinningPolicyAnalysis));
        actions.Controls.Add(MakeButton("RSoP-only GPOs", 165, ShowRsopOnly));
        actions.Controls.Add(MakeButton("Export visible CSV", 170, ExportVisible));
        actions.Controls.Add(MakeButton("Close", 110, Close));
        footer.Controls.Add(actions, 0, 0);
        _footer.Dock = DockStyle.Fill;
        _footer.AutoEllipsis = true;
        _footer.ForeColor = UiStyle.Muted;
        footer.Controls.Add(_footer, 0, 1);
        root.Controls.Add(footer, 0, 5);
        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "effective-gpo-conflicts");
        Draw();
    }

    private static Button MakeButton(string title, int width, Action action)
    {
        var result = new Button
        {
            Text = title, Width = width, Height = 38, FlatStyle = FlatStyle.Flat,
            BackColor = UiStyle.Surface, Margin = new Padding(0, 4, 11, 4)
        };
        result.Click += (_, _) => action();
        return result;
    }

    private void Draw()
    {
        string selected = _filter.SelectedItem as string ?? "All";
        string query = _search.Text.Trim();
        _visible.Clear();
        _visible.AddRange(_report.Policies.Where(p =>
            (selected == "All" || p.Category == selected) &&
            (query.Length == 0 ||
                p.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                p.Settings.Any(s =>
                    s.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    s.Key.Contains(query, StringComparison.OrdinalIgnoreCase)))));
        _grid.Rows.Clear();
        foreach (var policy in _visible)
        {
            string reported = _rsop is null ? "Not imported" :
                _rsop.ObservationFor(policy.Id) +
                (_rsop.IsHistorical ? " (historical)" : " (snapshot)");
            int row = _grid.Rows.Add(policy.Name, policy.Category, policy.Link,
                policy.Security, policy.Wmi, policy.HighSettings, reported);
            _grid.Rows[row].Tag = policy;
            if (policy.Category == "Security deny candidate" ||
                policy.Category == "Needs RSoP / verification")
                _grid.Rows[row].DefaultCellStyle.ForeColor = Color.DarkRed;
        }
        _grid.ClearSelection();
        if (_grid.Rows.Count > 0)
        {
            _grid.Rows[0].Selected = true;
            _grid.CurrentCell = _grid.Rows[0].Cells[0];
        }
        ShowDetails();
        var uniqueCandidateIds = new HashSet<string>(_report.Policies.Select(
            p => Guid.Parse(p.Id).ToString("D")), StringComparer.OrdinalIgnoreCase);
        int importedOnly = _rsop?.Policies.Count(x => !uniqueCandidateIds.Contains(x.Id)) ?? 0;
        _footer.Text = $"Showing {_visible.Count}/{_report.Policies.Count} AD candidates. " +
            (_rsop is null ? "No RSoP imported. " :
                $"Client XML: {_rsop.ReadUtc.LocalDateTime:yyyy-MM-dd HH:mm}, " +
                $"unmatched imported GPOs: {importedOnly}, " +
                (_rsop.IsHistorical ? "HISTORICAL (>24h). " : "point-in-time only. ")) +
            (_report.SiteInventoryComplete ? "" : "Site inventory incomplete. ") +
            (_endpoint is null ? "No remote HKLM snapshot. " :
                "Read-only HKLM " + _endpoint.RegistryStatus + " (" +
                _endpoint.OsStatus + ") at " +
                _endpoint.ObservedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm") + ". ") +
            (_sourceEvidence is null ? "No CSE/image source check. " :
                "CSE: " + _sourceEvidence.RsopStatus + " | JPEG: " +
                _sourceEvidence.Image.Status + ". ") +
            "Neither RSoP XML nor remote HKLM proves a winning GPO or displayed lock screen.";
    }

    private void ShowDetails()
    {
        if (_grid.SelectedRows.Count != 1 ||
            _grid.SelectedRows[0].Tag is not EffectiveGpoPolicy p)
        {
            _details.Text = "Choose a GPO candidate to inspect details.";
            return;
        }
        var sb = new StringBuilder();
        sb.AppendLine($"GPO: {p.Name}  ({p.Id})");
        sb.AppendLine($"Assessment from selected DC: {p.Category}");
        sb.AppendLine($"Imported client RSoP: {_rsop?.ObservationFor(p.Id) ?? "Not imported"}");
        if (_rsop is not null)
        {
            sb.AppendLine($"Client reason: {_rsop.DetailFor(p.Id)}");
            sb.AppendLine($"Client snapshot UTC: {_rsop.ReadUtc:o}" +
                (_rsop.IsHistorical ? " (historical)" : ""));
            sb.AppendLine($"XML SHA-256: {_rsop.Sha256}");
            sb.AppendLine("Imported file is unsigned and local; actual value precedence is NOT proven.");
        }
        if (_endpoint is not null)
        {
            sb.AppendLine("Observed remote HKLM: " + _endpoint.RegistryStatus +
                " | OS: " + _endpoint.OsStatus +
                " | UTC: " + _endpoint.ObservedUtc.ToString("o"));
            sb.AppendLine("Read-only endpoint observation does NOT attribute a winning GPO.");
        }
        if (_sourceEvidence is not null)
        {
            sb.AppendLine("Registry Policy CSE (cached): " + _sourceEvidence.RsopStatus);
            sb.AppendLine("Managed campaign image hash: " + _sourceEvidence.Image.Status);
            sb.AppendLine("Image file: " + _sourceEvidence.Image.FileName);
            sb.AppendLine("No physical lock screen display is proven.");
        }
        sb.AppendLine($"Actual AD computer container: {_report.ActualComputerContainer}");
        sb.AppendLine($"Linked at: {p.LinkedAt}");
        sb.AppendLine($"Site DN: {(p.Site.Length == 0 ? "Not a Site link" : p.Site + " (client site UNKNOWN)")}");
        sb.AppendLine($"Link: {p.Link}, GPMC order: {p.LinkOrder}, scope depth: {p.ScopeDepth}, Enforced: {p.Enforced}, Blocked below: {p.BlockedBelow}");
        sb.AppendLine($"Computer configuration disabled: {p.MachinePartDisabled}");
        sb.AppendLine($"Security ACL hint: {p.Security}");
        sb.AppendLine($"WMI: {p.Wmi}");
        sb.AppendLine($"Machine Registry.pol / GPP XML: {p.StaticRead}");
        sb.AppendLine($"Startup script present: {p.StartupScript}; dynamic effects unknown");
        sb.AppendLine();
        sb.AppendLine($"Potentially conflicting registry entries ({p.Settings.Count}):");
        foreach (var setting in p.Settings)
            sb.AppendLine($"  [{setting.Risk}] {setting.Name} = {setting.Value}" +
                $"\r\n    {setting.Key}  ({setting.Source})");
        sb.AppendLine();
        foreach (var warn in _report.Warnings.Take(15))
            sb.AppendLine("WARNING: " + warn);
        sb.AppendLine(_rsop is null
            ? "Run gpresult /scope computer /x to obtain optional client-side evidence."
            : "Imported RSoP is untrusted point-in-time evidence, not a signed attestation.");
        _details.Text = sb.ToString();
    }


    private void ImportRsopXml()
    {
        using var choose = new OpenFileDialog
        {
            Title = "Import an existing local gpresult /scope computer /x XML",
            Filter = "gpresult XML (*.xml)|*.xml",
            CheckFileExists = true, CheckPathExists = true,
            Multiselect = false, RestoreDirectory = true
        };
        if (choose.ShowDialog(this) != DialogResult.OK) return;
        if (MessageBox.Show(this,
            "Read the selected local gpresult XML? It can contain private computer, " +
            "domain, account, GPO and security settings. Only the current window will " +
            "receive parsed GPO IDs and flags, and the original file will NOT be copied " +
            "or uploaded. A user-supplied XML is NOT cryptographically authenticated.",
            "Import private RSoP report", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        try
        {
            // Parse into a local variable first; failed imports NEVER reuse stale
            // evidence from the old report or replace it with partly parsed data.
            RsopEvidence incoming = RsopEvidence.LoadLocalXml(choose.FileName,
                _report.Computer, _report.Domain);
            if (incoming.IsHistorical && MessageBox.Show(this,
                $"The selected client RSoP snapshot is from " +
                $"{incoming.ReadUtc.LocalDateTime:yyyy-MM-dd HH:mm:ss}. " +
                "It is more than 24 hours old. Use it as historical context only?",
                "Historical RSoP", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;
            _rsop = incoming;
            _clearRsop.Enabled = true;
            Draw();
        }
        catch (Exception ex)
        {
            // Keep any previously reviewed import unchanged on parse failure.
            UiErrorDialog.Show(this, "Import gpresult XML", ex);
        }
    }

    private void ClearImportedRsop()
    {
        if (_rsop is null) return;
        _rsop = null;
        _clearRsop.Enabled = false;
        Draw();
    }

    private void ShowWinningPolicyAnalysis()
    {
        try
        {
            var analysis = WinningPolicyAnalysis.Build(_report, _rsop, _endpoint, _sourceEvidence);
            using var window = new WinningPolicyDialog(analysis);
            window.ShowDialog(this);
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Winning Policy Analysis", ex); }
    }

    private async Task VerifyEndpointAsync()
    {
        if (_verifyEndpoint is null || !_verifyButton.Enabled) return;
        if (MessageBox.Show(this,
            "Read only the native 64-bit HKLM Lock Screen and Spotlight registry " +
            "values on the AD-verified computer " + _report.Computer +
            " using Remote Registry? The manager first rechecks domain/DC/OU, " +
            "security group and managed GPO identity. This does NOT run " +
            "gpupdate, enable services, execute remote scripts, install tasks, " +
            "change GPOs, or prove the physical lock screen. " +
            "Unavailable and access-denied PCs remain Unknown. Proceed?",
            "Verify endpoint (read-only)", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        _verifyButton.Enabled = false;
        try
        {
            // Preserve the prior snapshot if the request fails or is rejected.
            var incoming = await _verifyEndpoint();
            _endpoint = incoming;
            _sourceEvidence = null; // Never combine new HKLM data with stale CSE/file evidence.
            _clearEndpoint.Enabled = true;
            Draw();
        }
        catch (Exception ex)
        {
            UiErrorDialog.Show(this, "Read endpoint Lock Screen settings", ex);
        }
        finally { _verifyButton.Enabled = true; }
    }

    private void ClearEndpoint()
    {
        _endpoint = null;
        _sourceEvidence = null;
        _clearEndpoint.Enabled = false;
        Draw();
    }

    private async Task VerifyEvidenceAsync()
    {
        if (_verifyEvidence is null || !_evidenceButton.Enabled) return;
        if (MessageBox.Show(this,
            "Perform one explicit READ-ONLY check of " + _report.Computer +
            ": revalidate computer/OU/group/managed GPO on the selected DC, " +
            "read native 64-bit HKLM, query cached computer Registry Policy RSoP " +
            "via WMI/DCOM, and SHA-256 the managed campaign JPEG via read-only " +
            "C$ SMB. Requires existing Remote Registry/WMI/SMB admin access. " +
            "Never starts services, runs commands on the client, alters settings " +
            "or proves what is physically displayed. Proceed?",
            "Verify RSoP source + managed image", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        _evidenceButton.Enabled = false;
        _verifyButton.Enabled = false;
        try
        {
            var incoming = await _verifyEvidence();
            _endpoint = incoming.Registry;
            _sourceEvidence = incoming.Source;
            _clearEndpoint.Enabled = true;
            Draw();
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Verify RSoP + image (read-only)", ex); }
        finally
        {
            _evidenceButton.Enabled = _verifyEvidence is not null;
            _verifyButton.Enabled = _verifyEndpoint is not null;
        }
    }

    private void ShowRsopOnly()
    {
        if (_rsop is null)
        {
            MessageBox.Show(this, "Import a local gpresult XML first.",
                "No RSoP evidence", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var expected = new HashSet<string>(_report.Policies.Select(
            x => Guid.Parse(x.Id).ToString("D")), StringComparer.OrdinalIgnoreCase);
        var outside = _rsop.Policies.Where(x => !expected.Contains(x.Id)).ToArray();
        if (outside.Length == 0)
        {
            MessageBox.Show(this, "Every identified GPO in the imported report " +
                "is present in the candidate inventory. This does NOT prove that " +
                "the candidate scan is complete, or that all policy settings applied.",
                "RSoP-only GPOs", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var detail = new Form
        {
            Text = "Client XML GPOs absent from selected DC candidate list",
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(1000, 650), MinimumSize = new Size(700, 450),
            AutoScaleMode = AutoScaleMode.Dpi, BackColor = UiStyle.Surface
        };
        var body = new TextBox
        {
            Dock = DockStyle.Fill, ReadOnly = true, Multiline = true,
            ScrollBars = ScrollBars.Both, WordWrap = false,
            Font = new Font("Consolas", 9),
            Text = "This list is historical/imported-only GPO evidence, NOT " +
                "proof that the AD candidate scanner omitted a currently applicable GPO." +
                Environment.NewLine + Environment.NewLine +
                string.Join(Environment.NewLine, outside.Select(x =>
                    x.Id + "  " + x.Name.Replace("\r", " ").Replace("\n", " ") +
                    "  |  " + x.Observation)) +
                Environment.NewLine +
                $"Entries without parseable GUIDs: {_rsop.UnidentifiedEntries}"
        };
        detail.Controls.Add(body);
        detail.ShowDialog(this);
    }

    private static string Csv(string value)
    {
        string safe = value.Replace('\r', ' ').Replace('\n', ' ').Replace('\t', ' ');
        string trimmed = safe.TrimStart();
        if (trimmed.Length > 0 && "=+-@".Contains(trimmed[0]))
            safe = "'" + safe;
        return "\"" + safe.Replace("\"", "\"\"") + "\"";
    }

    private void ExportVisible()
    {
        if (_visible.Count == 0) return;
        if (MessageBox.Show(this,
            "Export the visible GPO candidate assessment? It includes private " +
            "DC, computer, GPO names/IDs and registry settings. This is NOT an " +
            "actual endpoint Resultant Set of Policy (RSoP). Review before sharing.",
            "Export private effective GPO analysis", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        using var save = new SaveFileDialog
        {
            Title = "Export GPO candidates (local CSV)",
            Filter = "CSV UTF-8 (*.csv)|*.csv",
            FileName = "LSCM-Effective-GPO-" + DateTime.Now.ToString(
                "yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".csv"
        };
        if (save.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var sb = new StringBuilder(
                "Computer,DomainController,ManagedCampaign,ManagedGpo,Gpo,Guid,LinkedAt,GpmcLinkOrder,ScopeDepth,ActualComputerContainer,Site,Category,Link,Security,Wmi,StaticRead,StartupScript,HighOverlapSettings,ObservedUtc,ClientRsopObservation,ClientRsopReason,ClientSnapshotUtc,ClientXmlSha256,ClientSnapshotHistorical\r\n");
            foreach (var p in _visible)
            {
                var values = new[]
                {
                    _report.Computer, _report.Dc, _report.CampaignId,
                    _report.ManagedGpoId, p.Name, p.Id, p.LinkedAt,
                    p.LinkOrder.ToString(CultureInfo.InvariantCulture),
                    p.ScopeDepth.ToString(CultureInfo.InvariantCulture),
                    _report.ActualComputerContainer, p.Site,
                    p.Category, p.Link, p.Security, p.Wmi, p.StaticRead,
                    p.StartupScript ? "Yes" : "No",
                    p.HighSettings.ToString(CultureInfo.InvariantCulture),
                    _report.AnalyzedUtc.ToUniversalTime().ToString("o"),
                    _rsop?.ObservationFor(p.Id) ?? "Not imported",
                    _rsop?.DetailFor(p.Id) ?? "No XML evidence imported",
                    _rsop?.ReadUtc.ToUniversalTime().ToString("o") ?? "",
                    _rsop?.Sha256 ?? "",
                    _rsop is null ? "" : _rsop.IsHistorical ? "Yes" : "No"
                };
                sb.AppendJoin(',', values.Select(Csv)).Append("\r\n");
            }
            File.WriteAllText(save.FileName, sb.ToString(), new UTF8Encoding(true));
            _footer.Text = "Private read-only report exported locally. No automatic upload.";
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Export effective GPO analysis", ex); }
    }
}
