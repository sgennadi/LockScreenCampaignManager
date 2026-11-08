using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Explicit, single-computer direct group membership management. Uses the
/// selected company OU and GPO security group only. No RSAT / PowerShell file
/// supplied by the user, no client RPC and no implicit AD writes.
/// </summary>
internal sealed class ComputerGroupAssignmentPanel : UserControl
{
    private sealed record ComputerRow(string Name, string Sam, string Dn,
        string Guid, string Membership)
    {
        public override string ToString() => Name + " [" + Membership + "]";
    }

    private readonly string _domain;
    private readonly string _dc;
    private readonly string _ouDn;
    private readonly string _groupDn;
    private readonly DomainCredentials _credentials;
    private readonly Func<bool> _parentBusy;
    private readonly Action<bool> _setParentBusy;
    private readonly TextBox _search = new();
    private readonly ListView _computers = new();
    private readonly ComboBox _action = new();
    private readonly TextBox _preview = new();
    private Button _find = new();
    private Button _refresh = new();
    private Button _previewButton = new();
    private Button _apply = new();
    private readonly Label _status = new();
    private readonly Label _scope = new();
    private JsonElement? _approvedPlan;
    private string? _approvedDn;
    private string? _approvedAction;
    private bool _busy;

    internal ComputerGroupAssignmentPanel(string domain, string dc, string ouDn,
        string groupDn, DomainCredentials credentials,
        Func<bool>? parentBusy = null, Action<bool>? setParentBusy = null)
    {
        _domain = domain; _dc = dc; _ouDn = ouDn; _groupDn = groupDn;
        _credentials = credentials;
        _parentBusy = parentBusy ?? (() => false);
        _setParentBusy = setParentBusy ?? (_ => { });
        BackColor = UiStyle.Canvas;
        Dock = DockStyle.Fill;
        Build();
    }

    internal TabPage AsTabPage()
    {
        var page = new TabPage("Computer & Group Assignment") {
            BackColor = UiStyle.Canvas
        };
        page.Controls.Add(this);
        return page;
    }

    private static Button Button(string text, int width, Action action, bool primary = false)
    {
        var result = new Button {
            Text = text, Width = width, Height = 39,
            BackColor = primary ? UiStyle.Accent : UiStyle.Surface,
            ForeColor = primary ? Color.White : UiStyle.Foreground,
            AutoEllipsis = true, Margin = new Padding(2, 4, 8, 4)
        };
        result.Click += (_, _) => action();
        return result;
    }

    private void Build()
    {
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            Padding = new Padding(12)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 83));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 60));
        Controls.Add(root);

        _scope.Dock = DockStyle.Fill;
        _scope.ForeColor = UiStyle.Foreground;
        _scope.Text = "Computer & Group Assignment (single computer, signed AD connection)" +
            Environment.NewLine + "Domain: " + _domain + "    |    Group: " + _groupDn +
            Environment.NewLine + "Scope: " + _ouDn;
        _scope.AutoEllipsis = true;
        root.Controls.Add(_scope, 0, 0);

        var find = new FlowLayoutPanel {
            Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true
        };
        find.Controls.Add(new Label {
            Text = "Find computer:", AutoSize = true,
            Margin = new Padding(2, 14, 7, 0)
        });
        _search.Width = 260;
        _search.PlaceholderText = "Computer name (or leave empty for first 150)...";
        _search.Margin = new Padding(0, 7, 12, 0);
        _search.TextChanged += (_, _) => InvalidatePlan();
        find.Controls.Add(_search);
        _find = Button("Find computers", 175, () => _ = SearchAsync(), true);
        _refresh = Button("Refresh membership", 181, () => _ = SearchAsync());
        find.Controls.Add(_find);
        find.Controls.Add(_refresh);
        root.Controls.Add(find, 0, 1);

        _computers.Dock = DockStyle.Fill;
        _computers.View = View.Details;
        _computers.FullRowSelect = true;
        _computers.HideSelection = false;
        _computers.MultiSelect = false;
        _computers.GridLines = true;
        _computers.Columns.Add("Computer", 230);
        _computers.Columns.Add("SAM account", 190);
        _computers.Columns.Add("Group membership", 180);
        _computers.Columns.Add("Computer DN", 420);
        _computers.SelectedIndexChanged += (_, _) => {
            InvalidatePlan();
            if (Selected?.Membership == "Primary")
                _status.Text = "Primary AD group: direct Add/Remove is not supported.";
        };
        root.Controls.Add(_computers, 0, 2);

        var details = new TableLayoutPanel {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1
        };
        details.RowStyles.Add(new RowStyle(SizeType.Absolute, 51));
        details.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var actions = new FlowLayoutPanel {
            Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true
        };
        _action.DropDownStyle = ComboBoxStyle.DropDownList;
        _action.Width = 205;
        _action.Items.AddRange(new object[] { "Add to group", "Remove from group" });
        _action.SelectedIndex = 0;
        _action.SelectedIndexChanged += (_, _) => InvalidatePlan();
        actions.Controls.Add(_action);
        _previewButton = Button("Preview membership change", 236,
            () => _ = PreviewAsync());
        _apply = Button("Apply group change", 208, () => _ = ApplyAsync(), true);
        _apply.Enabled = false;
        actions.Controls.Add(_previewButton);
        actions.Controls.Add(_apply);
        details.Controls.Add(actions, 0, 0);
        _preview.Dock = DockStyle.Fill;
        _preview.Multiline = true;
        _preview.ReadOnly = true;
        _preview.ScrollBars = ScrollBars.Vertical;
        _preview.BackColor = UiStyle.Surface;
        _preview.Text = "Search the selected OU, choose a computer, then Preview." +
            Environment.NewLine +
            "Direct group membership is modified only after preview and typed confirmation.";
        details.Controls.Add(_preview, 0, 1);
        root.Controls.Add(details, 0, 3);
        _status.Text = "Read-only search. Primary group cannot be changed; nested membership cannot be removed here.";
        _status.Dock = DockStyle.Fill;
        _status.ForeColor = UiStyle.Muted;
        _status.AutoEllipsis = true;
        root.Controls.Add(_status, 0, 4);
    }

    private ComputerRow? Selected =>
        _computers.SelectedItems.Count == 1
            ? _computers.SelectedItems[0].Tag as ComputerRow : null;

    private string ActionName =>
        _action.SelectedIndex == 1 ? "Remove" : "Add";

    private void InvalidatePlan()
    {
        _approvedPlan = null;
        _approvedAction = _approvedDn = null;
        _apply.Enabled = false;
    }

    private async Task<JsonElement> RunAsync(string mode, object request)
    {
        if (_busy || _parentBusy())
            throw new InvalidOperationException("An AD operation is already running.");
        _busy = true;
        _setParentBusy(true);
        _find.Enabled = _refresh.Enabled = _previewButton.Enabled = _apply.Enabled = false;
        UseWaitCursor = true;
        try
        {
            return await Task.Run(() =>
                DomainPowerShellRunner.Execute(DomainOperation.Assignment, mode,
                    request, _credentials, timeoutMilliseconds: 240000)
                    .GetProperty("data").Clone());
        }
        finally
        {
            _busy = false;
            _setParentBusy(false);
            UseWaitCursor = false;
            _find.Enabled = _refresh.Enabled = _previewButton.Enabled = true;
        }
    }

    private object RequestScope() => new {
        domain = _domain, dc = _dc, ouDn = _ouDn, groupDn = _groupDn
    };

    private async Task SearchAsync()
    {
        if (_busy || _parentBusy()) return;
        InvalidatePlan();
        try
        {
            _status.Text = "Searching AD on the selected DC...";
            var data = await RunAsync("Search", new {
                domain = _domain, dc = _dc, ouDn = _ouDn,
                groupDn = _groupDn, search = _search.Text.Trim()
            });
            _computers.BeginUpdate();
            try
            {
                _computers.Items.Clear();
                foreach (var pc in data.GetProperty("computers").EnumerateArray())
                {
                    var row = new ComputerRow(
                        pc.GetProperty("name").GetString() ?? "",
                        pc.GetProperty("sam").GetString() ?? "",
                        pc.GetProperty("dn").GetString() ?? "",
                        pc.GetProperty("guid").GetString() ?? "",
                        pc.GetProperty("membership").GetString() ?? "Unknown");
                    _computers.Items.Add(new ListViewItem(new[] {
                        row.Name, row.Sam, row.Membership, row.Dn
                    }) { Tag = row });
                }
            }
            finally { _computers.EndUpdate(); }
            _preview.Text = "Selected security group: " + _groupDn +
                Environment.NewLine + "No membership changes have been made.";
            _status.Text = $"Found {_computers.Items.Count} computer accounts in the OU subtree. " +
                (data.GetProperty("writable").GetBoolean()
                    ? "Writable DC verified." : "Read-only DC: changes are disabled.");
        }
        catch (Exception ex)
        {
            _status.Text = "AD computer search failed; no changes made.";
            UiErrorDialog.Show(this, "Search AD computer accounts", ex);
        }
    }

    private async Task PreviewAsync()
    {
        if (_busy || _parentBusy()) return;
        var pc = Selected;
        if (pc is null)
        {
            MessageBox.Show(this, "Select a computer first.",
                "Computer group assignment", MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }
        InvalidatePlan();
        if (string.Equals(pc.Membership, "Primary", StringComparison.OrdinalIgnoreCase))
        {
            _status.Text = "Primary AD group: direct Add/Remove is not supported.";
            return;
        }
        string action = ActionName;
        try
        {
            _status.Text = "Validating group and computer membership (read-only)...";
            var plan = await RunAsync("Preview", new {
                domain = _domain, dc = _dc, ouDn = _ouDn,
                groupDn = _groupDn, computerDn = pc.Dn, action
            });
            if (!string.Equals(plan.GetProperty("computerGuid").GetString(),
                    pc.Guid, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Computer identity changed during preview.");
            _approvedPlan = plan;
            _approvedDn = pc.Dn;
            _approvedAction = action;
            _preview.Text = "READ-ONLY MEMBERSHIP PREVIEW" +
                Environment.NewLine + "Domain: " + _domain +
                Environment.NewLine + "DC: " + _dc +
                Environment.NewLine + "Computer: " +
                    plan.GetProperty("computerSam").GetString() +
                Environment.NewLine + "Computer DN: " + pc.Dn +
                Environment.NewLine + "Security group: " + _groupDn +
                Environment.NewLine + "Action: " + action +
                Environment.NewLine + "Direct member now: " +
                    plan.GetProperty("directMember").GetBoolean() +
                Environment.NewLine + "Nested/effective member now: " +
                    plan.GetProperty("effectiveMember").GetBoolean() +
                Environment.NewLine + "Preview expires after 5 minutes." +
                Environment.NewLine + "No GPO or computer account will be created or deleted.";
            _apply.Enabled = plan.GetProperty("writable").GetBoolean();
            _status.Text = _apply.Enabled
                ? "Preview ready. Type computer SAM to approve the change."
                : "Read-only DC: select a writable DC before applying.";
        }
        catch (Exception ex)
        {
            InvalidatePlan();
            _status.Text = "Membership preview failed; no changes made.";
            UiErrorDialog.Show(this, "Preview group membership", ex);
        }
    }

    private static bool Confirm(IWin32Window parent, string sam,
        string action, string group)
    {
        using var dialog = new Form {
            Text = "Confirm AD group membership change",
            StartPosition = FormStartPosition.CenterParent,
            AutoScaleMode = AutoScaleMode.Dpi,
            MinimumSize = new Size(620, 295), Size = new Size(770, 340)
        };
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(14)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 119));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.Controls.Add(new Label {
            Dock = DockStyle.Fill,
            Text = "AD security group: " + group +
                Environment.NewLine + "Operation: " + action +
                " direct membership for " + sam +
                Environment.NewLine +
                "Type the exact computer SAM account (including $) to confirm.",
            AutoEllipsis = true
        }, 0, 0);
        var answer = new TextBox { Dock = DockStyle.Fill };
        root.Controls.Add(answer, 0, 1);
        var actions = new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = true, AutoScroll = true
        };
        var accept = Button("Confirm change", 172,
            () => dialog.DialogResult = DialogResult.OK, true);
        accept.Enabled = false;
        answer.TextChanged += (_, _) =>
            accept.Enabled = string.Equals(answer.Text.Trim(), sam,
                StringComparison.OrdinalIgnoreCase);
        actions.Controls.Add(accept);
        actions.Controls.Add(Button("Cancel", 120,
            () => dialog.DialogResult = DialogResult.Cancel));
        root.Controls.Add(actions, 0, 2);
        dialog.Controls.Add(root);
        UiStyle.Apply(dialog);
        return dialog.ShowDialog(parent) == DialogResult.OK;
    }

    private async Task ApplyAsync()
    {
        if (_busy || _parentBusy() || _approvedPlan is not { } plan ||
            Selected is not { } pc ||
            !string.Equals(pc.Dn, _approvedDn, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ActionName, _approvedAction, StringComparison.Ordinal))
            return;
        string sam = plan.GetProperty("computerSam").GetString() ?? "";
        string action = _approvedAction!;
        if (!Confirm(this, sam, action, _groupDn)) return;
        InvalidatePlan();
        try
        {
            _status.Text = "Applying one approved AD group membership change...";
            var result = await RunAsync("Apply", new {
                domain = _domain, dc = _dc, ouDn = _ouDn, groupDn = _groupDn,
                computerDn = pc.Dn, action,
                previewUtc = plan.GetProperty("previewUtc").GetString(),
                planHash = plan.GetProperty("hash").GetString(),
                approveSam = sam, approveGroupDn = _groupDn
            });
            if (!result.GetProperty("verified").GetBoolean())
                throw new InvalidDataException("AD did not confirm the intended membership.");
            _preview.Text = "Membership change verified on " + _dc +
                Environment.NewLine + action + " direct membership for " + sam +
                Environment.NewLine + "Target group: " + _groupDn +
                Environment.NewLine +
                "Computers may need a new logon/reboot to refresh their computer security token." +
                Environment.NewLine + "GPO links and security filtering were not modified.";
            await SearchAsync();
            _status.Text = "Group membership verified on " + _dc +
                ". Computer security tokens may require restart/logon to refresh.";
        }
        catch (Exception ex)
        {
            _status.Text = "Membership apply failed or could not be verified. Refresh before retrying.";
            UiErrorDialog.Show(this, "Apply group membership", ex);
        }
    }
}
