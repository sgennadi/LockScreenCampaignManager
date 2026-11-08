using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Select/create/edit/delete local company profiles. Selection is only
/// returned to the caller; changing the active company is an explicit action
/// in MainForm after checking unsaved campaign edits.
/// </summary>
internal sealed class CompanyProfilesDialog : Form
{
    private readonly ListView _companies = new();
    private readonly TextBox _details = new();
    private readonly TextBox _search = new();
    private readonly Label _summary = new();
    private readonly Button _select = new();
    private readonly Button _edit = new();
    private readonly Button _duplicate = new();
    private readonly Button _delete = new();
    private readonly string _currentId;
    private IReadOnlyList<CompanyProfile> _profiles = Array.Empty<CompanyProfile>();

    internal CompanyProfile? ChosenCompany { get; private set; }
    internal bool UnassignedChosen { get; private set; }

    internal CompanyProfilesDialog(string? currentCompanyId = null)
    {
        _currentId = currentCompanyId ?? "";
        Text = "Company Profiles — LockScreen Campaign Manager";
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(840, 600);
        Size = new Size(1090, 755);
        BackColor = UiStyle.Canvas;
        Build();
        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "company-profiles");
        Reload(_currentId);
    }

    private void Build()
    {
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, Padding = new Padding(16),
            ColumnCount = 1, RowCount = 4, BackColor = UiStyle.Canvas
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));
        Controls.Add(root);

        var intro = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        intro.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
        intro.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        intro.Controls.Add(new Label {
            Dock = DockStyle.Fill, Text = "Choose a company",
            ForeColor = UiStyle.Foreground,
            Font = new Font("Segoe UI Semibold", 17f)
        }, 0, 0);
        intro.Controls.Add(new Label {
            Dock = DockStyle.Fill,
            Text = "Separate local company workspaces for campaign defaults and Active Directory targets.",
            ForeColor = UiStyle.Muted
        }, 0, 1);
        root.Controls.Add(intro, 0, 0);

        var toolbar = new FlowLayoutPanel {
            Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true
        };
        toolbar.Controls.Add(new Label {
            Text = "Search:", AutoSize = true,
            Margin = new Padding(0, 14, 8, 0)
        });
        _search.Width = 270;
        _search.PlaceholderText = "Company or AD domain...";
        _search.Margin = new Padding(0, 7, 10, 0);
        _search.TextChanged += (_, _) => DisplayList();
        toolbar.Controls.Add(_search);
        toolbar.Controls.Add(Button("New company", 157, Create));
        root.Controls.Add(toolbar, 0, 1);

        var split = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = UiStyle.Canvas
        };
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 48));
        split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 52));
        root.Controls.Add(split, 0, 2);

        _companies.Dock = DockStyle.Fill;
        _companies.View = View.Details;
        _companies.FullRowSelect = true;
        _companies.HideSelection = false;
        _companies.MultiSelect = false;
        _companies.GridLines = true;
        _companies.Columns.Add("Company", 190);
        _companies.Columns.Add("AD domain", 200);
        _companies.Columns.Add("Active", 85);
        _companies.SelectedIndexChanged += (_, _) => ShowSelection();
        _companies.DoubleClick += (_, _) => SelectCompany();
        split.Controls.Add(_companies, 0, 0);

        _details.Dock = DockStyle.Fill;
        _details.Multiline = true;
        _details.ScrollBars = ScrollBars.Vertical;
        _details.ReadOnly = true;
        _details.BackColor = UiStyle.Surface;
        _details.Font = new Font("Segoe UI", 10f);
        _details.Text = "Select a company to view its settings.";
        _details.Margin = new Padding(10, 0, 0, 0);
        split.Controls.Add(_details, 1, 0);

        var footer = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2
        };
        footer.RowStyles.Add(new RowStyle(SizeType.Absolute, 47));
        footer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var commands = new FlowLayoutPanel {
            Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true
        };
        _select.Text = "Select company";
        _select.Width = 179; _select.Height = 38;
        _select.BackColor = UiStyle.Accent; _select.ForeColor = Color.White;
        _select.Click += (_, _) => SelectCompany();
        commands.Controls.Add(_select);
        commands.Controls.Add(Button("Unassigned", 143, SelectUnassigned));
        _edit.Text = "Edit"; _edit.Width = 108; _edit.Height = 38;
        _edit.Click += (_, _) => Edit();
        commands.Controls.Add(_edit);
        _duplicate.Text = "Duplicate"; _duplicate.Width = 136; _duplicate.Height = 38;
        _duplicate.Click += (_, _) => Duplicate();
        commands.Controls.Add(_duplicate);
        _delete.Text = "Delete"; _delete.Width = 108; _delete.Height = 38;
        _delete.Click += (_, _) => Delete();
        commands.Controls.Add(_delete);
        commands.Controls.Add(Button("Close", 108, Close));
        footer.Controls.Add(commands, 0, 0);

        _summary.Dock = DockStyle.Fill;
        _summary.ForeColor = UiStyle.Muted;
        _summary.Text = "Company profiles do not contain passwords or grant deployment permissions.";
        footer.Controls.Add(_summary, 0, 1);
        root.Controls.Add(footer, 0, 3);
        AcceptButton = _select;
    }

    private static Button Button(string text, int width, Action action)
    {
        var button = new Button {
            Text = text, Width = width, Height = 38, AutoEllipsis = true,
            Margin = new Padding(0, 4, 8, 4)
        };
        button.Click += (_, _) => action();
        return button;
    }

    private CompanyProfile? Selected => _companies.SelectedItems.Count == 1
        ? _companies.SelectedItems[0].Tag as CompanyProfile : null;

    private void Reload(string? preferredId = null)
    {
        try
        {
            _profiles = CompanyProfilesStore.All();
            DisplayList(preferredId);
            _summary.Text = $"{_profiles.Count} saved company profiles. " +
                "Selection never connects to Active Directory or changes a GPO.";
        }
        catch (Exception ex)
        {
            _profiles = Array.Empty<CompanyProfile>();
            _companies.Items.Clear();
            _details.Text = "The local company profile file could not be verified.";
            _summary.Text = "Company profiles unavailable. No company will be selected.";
            UiErrorDialog.Show(this, "Load company profiles", ex);
        }
    }

    private void DisplayList(string? preferredId = null)
    {
        string? selectedId = preferredId ?? Selected?.Id;
        string search = _search.Text.Trim();
        _companies.BeginUpdate();
        try
        {
            _companies.Items.Clear();
            foreach (var company in _profiles.Where(x =>
                search.Length == 0 ||
                x.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                x.Domain.Contains(search, StringComparison.OrdinalIgnoreCase)))
            {
                var row = new ListViewItem(new[] {
                    company.Name, company.Domain,
                    string.Equals(company.Id, _currentId, StringComparison.OrdinalIgnoreCase)
                        ? "Current" : ""
                }) { Tag = company };
                _companies.Items.Add(row);
                if (string.Equals(company.Id, selectedId, StringComparison.OrdinalIgnoreCase))
                    row.Selected = true;
            }
            if (_companies.SelectedItems.Count == 0 && _companies.Items.Count > 0)
                _companies.Items[0].Selected = true;
        }
        finally { _companies.EndUpdate(); }
        ShowSelection();
    }

    private void ShowSelection()
    {
        var p = Selected;
        _select.Enabled = _edit.Enabled = _duplicate.Enabled = p is not null;
        _delete.Enabled = p is not null && !string.Equals(
            p.Id, _currentId, StringComparison.OrdinalIgnoreCase);
        if (p is null)
        {
            _details.Text = "No company selected." + Environment.NewLine +
                "Choose New company to create a local profile.";
            return;
        }
        _details.Text =
            "COMPANY PROFILE" + Environment.NewLine + Environment.NewLine +
            "Company: " + p.Name + Environment.NewLine +
            "AD domain: " + p.Domain + Environment.NewLine +
            "Preferred DC: " + (p.PreferredDc.Length > 0 ? p.PreferredDc : "(auto-detect)") +
            Environment.NewLine + Environment.NewLine +
            "Computer OU:" + Environment.NewLine +
            (p.DefaultOuDn.Length > 0 ? p.DefaultOuDn : "(select during AD discovery)") +
            Environment.NewLine + Environment.NewLine +
            "Security group:" + Environment.NewLine +
            (p.DefaultGroupDn.Length > 0 ? p.DefaultGroupDn : "(select during AD discovery)") +
            Environment.NewLine + Environment.NewLine +
            "Client Health: " + (p.EnableFeedback ? "Enabled" : "Disabled") +
            Environment.NewLine + "SMB: " +
            (p.FeedbackShare.Length > 0 ? p.FeedbackShare : "(not configured)") +
            Environment.NewLine + "Time zone: " + p.TimeZoneId +
            Environment.NewLine + Environment.NewLine +
            "SELECT COMPANY uses these defaults for NEW campaigns." +
            Environment.NewLine + "No stored passwords. GPO creation requires a fresh AD check.";
    }

    private void Create()
    {
        using var dialog = new CompanyProfileEditDialog();
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.ResultProfile is null) return;
        try
        {
            CompanyProfilesStore.Upsert(dialog.ResultProfile);
            Reload(dialog.ResultProfile.Id);
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Add company", ex); }
    }

    private void Edit()
    {
        var selected = Selected;
        if (selected is null) return;
        using var dialog = new CompanyProfileEditDialog(selected);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.ResultProfile is null) return;
        try
        {
            CompanyProfilesStore.Upsert(dialog.ResultProfile);
            Reload(dialog.ResultProfile.Id);
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Edit company", ex); }
    }

    private void Duplicate()
    {
        var selected = Selected;
        if (selected is null) return;
        var copy = selected with {
            Id = Guid.NewGuid().ToString("D"),
            Name = selected.Name + " - Copy"
        };
        using var dialog = new CompanyProfileEditDialog(copy);
        if (dialog.ShowDialog(this) != DialogResult.OK || dialog.ResultProfile is null) return;
        try
        {
            CompanyProfilesStore.Upsert(dialog.ResultProfile);
            Reload(dialog.ResultProfile.Id);
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Duplicate company", ex); }
    }

    private void Delete()
    {
        var selected = Selected;
        if (selected is null || selected.Id.Equals(
                _currentId, StringComparison.OrdinalIgnoreCase))
            return;
        if (MessageBox.Show(this,
            $"Delete the local profile '{selected.Name}'?" +
            Environment.NewLine +
            "This does not remove GPOs, exports, client files or AD accounts." +
            Environment.NewLine +
            "WARNING: saved projects bound to this profile cannot be reopened until " +
            "its original profile ID is restored from a backup.",
            "Delete company profile", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        try
        {
            CompanyProfilesStore.Remove(selected.Id);
            Reload();
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Delete company", ex); }
    }

    private void SelectUnassigned()
    {
        ChosenCompany = null;
        UnassignedChosen = true;
        DialogResult = DialogResult.OK;
        Close();
    }

    private void SelectCompany()
    {
        var selected = Selected;
        if (selected is null) return;
        ChosenCompany = selected;
        DialogResult = DialogResult.OK;
        Close();
    }
}
