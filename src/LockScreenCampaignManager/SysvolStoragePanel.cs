using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Safe read-only SYSVOL audit and separately approved deletion of an expired,
/// unreferenced campaign source folder. No GPO changes, links or file reuse.
/// </summary>
internal sealed class SysvolStoragePanel : UserControl
{
    private sealed record CampaignRow(string Id, bool Valid, bool Eligible,
        int Images, long ImageBytes, int Duplicates, long DuplicateBytes,
        int GpoCount, string EndUtc, string Reason);

    private readonly string _domain;
    private readonly string _dc;
    private readonly CampaignProject _project;
    private readonly DomainCredentials _credentials;
    private readonly Func<bool> _parentBusy;
    private readonly Action<bool> _setParentBusy;
    private readonly ListView _campaigns = new();
    private readonly TextBox _details = new();
    private readonly Label _status = new();
    private readonly Label _scope = new();
    private Button _scan = new();
    private Button _compare = new();
    private Button _preview = new();
    private Button _delete = new();
    private bool _busy;
    private bool _clockSafe;
    private bool _storageWritable;
    private string? _approvedId;
    private string? _planHash;
    private string? _previewUtc;

    internal SysvolStoragePanel(string domain, string dc, CampaignProject project,
        DomainCredentials credentials, Func<bool>? parentBusy = null,
        Action<bool>? setParentBusy = null)
    {
        _domain = domain;
        _dc = dc;
        _project = project;
        _credentials = credentials;
        _parentBusy = parentBusy ?? (() => false);
        _setParentBusy = setParentBusy ?? (_ => { });
        Dock = DockStyle.Fill;
        BackColor = UiStyle.Canvas;
        Build();
    }

    internal TabPage AsTabPage()
    {
        var tab = new TabPage("Smart SYSVOL Storage") { BackColor = UiStyle.Canvas };
        tab.Controls.Add(this);
        return tab;
    }

    private static Button MakeButton(string text, int width, Action onClick, bool primary = false)
    {
        var button = new Button
        {
            Text = text, Width = width, Height = 40, AutoEllipsis = true,
            BackColor = primary ? UiStyle.Accent : UiStyle.Surface,
            ForeColor = primary ? Color.White : UiStyle.Foreground,
            Margin = new Padding(3, 4, 8, 4),
        };
        button.Click += (_, _) => onClick();
        return button;
    }

    private void Build()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            BackColor = UiStyle.Canvas, Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 72));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 173));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
        Controls.Add(root);

        _scope.Dock = DockStyle.Fill;
        _scope.ForeColor = UiStyle.Foreground;
        _scope.Text = "Smart SYSVOL Storage — read-only duplicate inventory and manual cleanup" +
            Environment.NewLine + "Domain: " + _domain + "  |  Selected DC: " + _dc +
            Environment.NewLine +
            "Separate campaign folders are retained; identical images are never linked or overwritten.";
        _scope.AutoEllipsis = true;
        root.Controls.Add(_scope, 0, 0);

        var commands = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true
        };
        _scan = MakeButton("Scan SYSVOL", 160, () => _ = ScanAsync(false), true);
        _compare = MakeButton("Compare current campaign", 240,
            () => _ = ScanAsync(true));
        _preview = MakeButton("Preview cleanup", 177,
            () => _ = PreviewCleanupAsync());
        _preview.Enabled = false;
        _delete = MakeButton("Delete source folder", 199,
            () => _ = DeleteSourceAsync());
        _delete.Enabled = false;
        commands.Controls.AddRange(new Control[] { _scan, _compare, _preview, _delete });
        root.Controls.Add(commands, 0, 1);

        _campaigns.Dock = DockStyle.Fill;
        _campaigns.View = View.Details;
        _campaigns.FullRowSelect = true;
        _campaigns.MultiSelect = false;
        _campaigns.HideSelection = false;
        _campaigns.Columns.Add("Campaign ID", 235);
        _campaigns.Columns.Add("End (UTC)", 165);
        _campaigns.Columns.Add("Images", 68);
        _campaigns.Columns.Add("Duplicate", 85);
        _campaigns.Columns.Add("GPO refs", 81);
        _campaigns.Columns.Add("Cleanup", 130);
        _campaigns.SelectedIndexChanged += (_, _) =>
        {
            InvalidatePlan();
            if (Selected is { } item)
            {
                _details.Text =
                    $"Campaign: {item.Id}\r\nEnd UTC: {item.EndUtc}\r\n" +
                    $"Verified: {item.Valid} | Images: {item.Images} ({item.ImageBytes:N0} bytes)\r\n" +
                    $"Already stored in other campaigns: {item.Duplicates} images ({item.DuplicateBytes:N0} bytes)\r\n" +
                    $"GPO references: {item.GpoCount} | Cleanup eligible: {item.Eligible}\r\n" +
                    item.Reason;
            }
        };
        root.Controls.Add(_campaigns, 0, 2);

        _details.Dock = DockStyle.Fill;
        _details.Multiline = true;
        _details.ReadOnly = true;
        _details.ScrollBars = ScrollBars.Both;
        _details.WordWrap = false;
        _details.Font = new Font("Consolas", 9.1f);
        _details.BackColor = UiStyle.Surface;
        _details.Text =
            "Scan SYSVOL to view existing campaign folders and SHA-256 duplicates.\r\n" +
            "Compare current campaign creates a temporary LOCAL export only.\r\n" +
            "Cleanup needs a fresh Preview and exact typed campaign ID.\r\n" +
            "A 30-day grace period after campaign end and no GPO references are required.\r\n" +
            "Before deletion, verify client shutdown/cleanup and SYSVOL replication.";
        root.Controls.Add(_details, 0, 3);

        _status.Text =
            "Nothing has changed on the DC. A missing or uncertain GPO scan blocks cleanup.";
        _status.Dock = DockStyle.Fill;
        _status.AutoEllipsis = true;
        _status.ForeColor = UiStyle.Muted;
        root.Controls.Add(_status, 0, 4);
    }

    private CampaignRow? Selected => _campaigns.SelectedItems.Count == 1
        ? _campaigns.SelectedItems[0].Tag as CampaignRow : null;

    private void InvalidatePlan()
    {
        _approvedId = _planHash = _previewUtc = null;
        _delete.Enabled = false;
        _preview.Enabled = !_busy && _clockSafe && _storageWritable &&
            Selected is { Valid: true, Eligible: true };
    }

    private async Task<JsonElement> RunAsync(string mode, object request)
    {
        if (_busy || _parentBusy())
            throw new InvalidOperationException("An AD / SYSVOL operation is already running.");
        _busy = true;
        _setParentBusy(true);
        _scan.Enabled = _compare.Enabled = _preview.Enabled = _delete.Enabled = false;
        UseWaitCursor = true;
        try
        {
            return await Task.Run(() => DomainPowerShellRunner.Execute(
                    DomainOperation.Storage, mode, request, _credentials,
                    timeoutMilliseconds: 300000)
                .GetProperty("data").Clone());
        }
        finally
        {
            _busy = false;
            _setParentBusy(false);
            UseWaitCursor = false;
            _scan.Enabled = _compare.Enabled = true;
            _preview.Enabled = _clockSafe && _storageWritable &&
                Selected is { Valid: true, Eligible: true };
        }
    }

    private async Task ScanAsync(bool compare)
    {
        if (_busy || _parentBusy()) return;
        InvalidatePlan();
        _clockSafe = _storageWritable = false;
        string? temporaryRoot = null;
        try
        {
            string packagePath = "";
            if (compare)
            {
                _status.Text = "Building temporary local export for read-only hash comparison...";
                temporaryRoot = Path.Combine(Path.GetTempPath(),
                    "LSCM-SYSVOL-COMPARE-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(temporaryRoot);
                packagePath = CampaignExporter.Export(_project, temporaryRoot);
            }
            _status.Text = "Scanning campaign hashes and GPO references on selected DC...";
            var data = await RunAsync("Scan", new {
                domain = _domain, dc = _dc, packagePath
            });
            int offset = data.GetProperty("clockSkewSeconds").GetInt32();
            _clockSafe = Math.Abs((long)offset) <= 300 &&
                data.GetProperty("gpoAuditComplete").GetBoolean();
            _storageWritable = data.GetProperty("writable").GetBoolean();
            _campaigns.BeginUpdate();
            try
            {
                _campaigns.Items.Clear();
                long totalDuplicateBytes = 0;
                int totalDuplicates = 0;
                foreach (var folder in data.GetProperty("campaigns").EnumerateArray())
                {
                    string id = folder.GetProperty("campaignId").GetString() ?? "";
                    bool valid = folder.GetProperty("valid").GetBoolean();
                    bool eligible = folder.GetProperty("cleanupEligible").GetBoolean();
                    int images = folder.GetProperty("imageCount").GetInt32();
                    long bytes = folder.GetProperty("imageBytes").GetInt64();
                    int duplicates = folder.GetProperty("duplicateImages").GetInt32();
                    long duplicateBytes = folder.GetProperty("duplicateBytes").GetInt64();
                    int gpos = folder.GetProperty("gpoCount").GetInt32();
                    string end = folder.GetProperty("endUtc").GetString() ?? "";
                    string reason = folder.GetProperty("reason").GetString() ?? "";
                    totalDuplicates += duplicates;
                    totalDuplicateBytes += duplicateBytes;
                    var row = new CampaignRow(id, valid, eligible, images, bytes,
                        duplicates, duplicateBytes, gpos, end, reason);
                    _campaigns.Items.Add(new ListViewItem(new[] {
                        id, end, images.ToString(), duplicates.ToString(),
                        gpos.ToString(), eligible ? "Review eligible" :
                            valid ? "Protected" : "Invalid / inspect"
                    }) { Tag = row });
                }
                _details.Text =
                    $"SYSVOL: {data.GetProperty("root").GetString()}\r\n" +
                    $"Verified DC UTC: {data.GetProperty("dcUtc").GetString()} | Local/DC offset: {offset} seconds\r\n" +
                    $"Full GPO reference audit: {data.GetProperty("gpoAuditComplete").GetBoolean()}\r\n" +
                    $"Campaigns inspected: {_campaigns.Items.Count}\r\n" +
                    $"Identical copied JPEGs across campaigns: {totalDuplicates} ({totalDuplicateBytes:N0} bytes)\r\n" +
                    $"Domain GPOs audited: {data.GetProperty("gpoAudited").GetInt32()}\r\n" +
                    "Duplicate files are only reported. No hardlinks or changes were made.";
                var comparison = data.GetProperty("comparison");
                if (comparison.ValueKind == JsonValueKind.Object)
                {
                    _details.Text += "\r\n\r\nCURRENT CAMPAIGN COMPARISON:\r\n" +
                        $"Images: {comparison.GetProperty("imageCount").GetInt32()}; " +
                        $"already stored on DC: {comparison.GetProperty("duplicateImages").GetInt32()}\r\n" +
                        $"Duplicate bytes: {comparison.GetProperty("duplicateBytes").GetInt64():N0} " +
                        $"of {comparison.GetProperty("totalBytes").GetInt64():N0} bytes.\r\n" +
                        "New campaign will still retain its own immutable files.";
                }
            }
            finally { _campaigns.EndUpdate(); }
            _status.Text = "Read-only SYSVOL scan complete. " +
                (!_clockSafe ? "Cleanup disabled: incomplete GPO audit or >5 minute clock skew." :
                 !_storageWritable ? "Cleanup disabled: writable DC not confirmed." :
                 "Writable DC and clock verified; cleanup still requires Preview.");
        }
        catch (Exception ex)
        {
            _status.Text = "SYSVOL audit failed; no files were changed.";
            UiErrorDialog.Show(this, "Smart SYSVOL Storage", ex);
        }
        finally
        {
            if (temporaryRoot is not null)
            {
                try { Directory.Delete(temporaryRoot, recursive: true); }
                catch (Exception ex) { AppLog.Write(ex); }
            }
        }
    }

    private async Task PreviewCleanupAsync()
    {
        if (_busy || _parentBusy() || Selected is not { Valid: true, Eligible: true } row)
            return;
        InvalidatePlan();
        try
        {
            _status.Text = "Rechecking source hashes, expiration and EVERY domain GPO...";
            var plan = await RunAsync("Preview", new {
                domain = _domain, dc = _dc, campaignId = row.Id
            });
            if (!string.Equals(plan.GetProperty("campaignId").GetString(), row.Id,
                    StringComparison.Ordinal))
                throw new InvalidDataException("SYSVOL campaign identity changed.");
            _approvedId = row.Id;
            _planHash = plan.GetProperty("planHash").GetString();
            _previewUtc = plan.GetProperty("previewUtc").GetString();
            _details.Text = "READ-ONLY CLEANUP PREVIEW\r\n" +
                $"Campaign: {row.Id}\r\n" +
                $"Exact source: {plan.GetProperty("path").GetString()}\r\n" +
                $"Images: {plan.GetProperty("imageCount").GetInt32()}\r\n" +
                $"Total image bytes: {plan.GetProperty("imageBytes").GetInt64():N0}\r\n" +
                "No GPO reference found on selected DC.\r\n" +
                "30-day grace period passed. Preview expires in 5 minutes.\r\n" +
                "Offline computers and delayed SYSVOL replication require manual review.\r\n" +
                "No GPO, AD object or client task will be changed.";
            _delete.Enabled = true;
            _status.Text = "Preview passed. Explicitly confirm the exact campaign ID to delete only its SYSVOL source.";
        }
        catch (Exception ex)
        {
            InvalidatePlan();
            _status.Text = "Cleanup preview rejected; no changes made.";
            UiErrorDialog.Show(this, "Preview SYSVOL cleanup", ex);
        }
    }

    private static bool ConfirmDeletion(IWin32Window owner, string id)
    {
        using var dialog = new Form
        {
            Text = "Confirm permanent SYSVOL source deletion",
            AutoScaleMode = AutoScaleMode.Dpi,
            StartPosition = FormStartPosition.CenterParent,
            Size = new Size(760, 325), MinimumSize = new Size(610, 290),
            Font = new Font("Segoe UI", 9.5f)
        };
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 3, ColumnCount = 1,
            Padding = new Padding(14)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 125));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 51));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill, AutoEllipsis = true,
            Text = $"DELETE ONLY SYSVOL SOURCE: {id}\r\n" +
                "No active GPO reference was found, but offline clients may still need files.\r\n" +
                "Check SYSVOL replication and client cleanup first.\r\n" +
                "Type the EXACT campaign ID to approve irreversible deletion:"
        }, 0, 0);
        var answer = new TextBox { Dock = DockStyle.Fill };
        root.Controls.Add(answer, 0, 1);
        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = true,
            WrapContents = false, FlowDirection = FlowDirection.RightToLeft
        };
        var accept = MakeButton("Delete source folder", 210,
            () => dialog.DialogResult = DialogResult.OK, true);
        accept.Enabled = false;
        answer.TextChanged += (_, _) => accept.Enabled =
            string.Equals(answer.Text, id, StringComparison.Ordinal);
        actions.Controls.Add(accept);
        actions.Controls.Add(MakeButton("Cancel", 120,
            () => dialog.DialogResult = DialogResult.Cancel));
        root.Controls.Add(actions, 0, 2);
        dialog.Controls.Add(root);
        UiStyle.Apply(dialog);
        return dialog.ShowDialog(owner) == DialogResult.OK;
    }

    private async Task DeleteSourceAsync()
    {
        if (_busy || _parentBusy() || Selected is not { } row ||
            _approvedId is null || _planHash is null || _previewUtc is null ||
            !string.Equals(_approvedId, row.Id, StringComparison.Ordinal))
            return;
        string id = row.Id, stamp = _previewUtc, hash = _planHash;
        if (!ConfirmDeletion(this, id)) return;
        InvalidatePlan();
        try
        {
            _status.Text = "Revalidating and deleting only the approved source directory...";
            var data = await RunAsync("Cleanup", new {
                domain = _domain, dc = _dc, campaignId = id,
                previewUtc = stamp, planHash = hash, approveId = id
            });
            if (!data.GetProperty("deleted").GetBoolean() ||
                !string.Equals(data.GetProperty("campaignId").GetString(), id,
                    StringComparison.Ordinal))
                throw new InvalidDataException("Cleanup completion could not be verified.");
            await ScanAsync(false);
            _status.Text = "Source cleanup verified for " + id + ". Refresh inventory to confirm DFSR replication.";
        }
        catch (Exception ex)
        {
            _status.Text = "Cleanup failed or could not be verified; inspect SYSVOL before retry.";
            UiErrorDialog.Show(this, "Delete SYSVOL campaign source", ex);
        }
    }
}
