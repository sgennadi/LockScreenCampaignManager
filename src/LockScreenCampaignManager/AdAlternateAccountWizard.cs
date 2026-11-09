using System.Drawing;
using System.Text.Json;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Shared, two-page Active Directory sign-in wizard. Account lookup is always
/// read-only, uses the currently logged-on Windows identity (never the entered
/// password), and is optional: offline/non-domain administrators may type a
/// login manually. No password is submitted until the owning Connect action.
/// </summary>
internal sealed class AdAlternateAccountWizard : Form
{
    private sealed record AccountRow(string Name, string Sam, string Upn, string Dn)
    {
        internal string Login => !string.IsNullOrWhiteSpace(Upn) ? Upn : Sam;
        public override string ToString() =>
            string.IsNullOrWhiteSpace(Name) || Name.Equals(Sam, StringComparison.OrdinalIgnoreCase)
                ? Sam
                : Name + "   (" + Sam + ")";
    }

    private readonly string _domain;
    private readonly string _dc;
    private readonly string _initialUser;
    private readonly bool _allowSaving;
    private readonly TextBox _search = new();
    private readonly ListBox _results = new();
    private readonly Label _searchStatus = new();
    private readonly Label _selectedAccount = new();
    private readonly TextBox _password = new();
    private readonly CheckBox _remember = new();
    private readonly CheckBox _savePassword = new();
    private readonly Panel _searchPage = new();
    private readonly Panel _passwordPage = new();
    private readonly Label _pageTitle = new();
    private readonly Label _pageSubtitle = new();
    private readonly Label _stepOne = new();
    private readonly Label _stepTwo = new();
    private readonly Button _next = new();
    private readonly Button _back = new();
    private readonly Button _manual = new();
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 420 };
    private readonly ToolTip _tips = new() { AutoPopDelay = 12000, InitialDelay = 350, ReshowDelay = 160 };
    private string _chosenUser = "";
    private int _revision;
    private int _page;
    private bool _settingText;

    internal string SelectedUsername { get; private set; } = "";
    internal string SelectedPassword { get; private set; } = "";
    internal bool RememberUsername => _allowSaving && _remember.Checked;
    internal bool SaveEncryptedPassword => _allowSaving && _savePassword.Checked;

    internal AdAlternateAccountWizard(string domain, string dc, string username,
        string password, bool rememberUsername, bool saveEncryptedPassword,
        bool allowSaving)
    {
        _domain = domain.Trim();
        _dc = dc.Trim();
        _initialUser = username.Trim();
        _allowSaving = allowSaving;

        Text = "LockScreen Campaign Manager - Select AD account";
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(810, 620);
        Size = new Size(960, 705);
        BackColor = UiStyle.Canvas;
        ShowInTaskbar = false;

        BuildLayout();
        _settingText = true;
        _search.Text = _initialUser;
        _password.Text = password;
        _remember.Checked = rememberUsername;
        _savePassword.Checked = allowSaving && saveEncryptedPassword;
        _settingText = false;

        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await SearchUsersAsync(_revision, _search.Text.Trim());
        };
        _search.TextChanged += (_, _) => ScheduleSearch();
        _search.KeyDown += (_, e) =>
        {
            if (e.KeyCode != Keys.Enter) return;
            if (_results.SelectedItem is AccountRow account)
                ChooseAccount(account.Login, account.ToString());
            else if (_results.Items.Count == 1 && _results.Items[0] is AccountRow only)
                ChooseAccount(only.Login, only.ToString());
            e.SuppressKeyPress = true;
        };
        _results.SelectedIndexChanged += (_, _) => UpdateButtons();
        _results.DoubleClick += (_, _) =>
        {
            if (_results.SelectedItem is AccountRow account)
                ChooseAccount(account.Login, account.ToString());
        };
        _results.MouseMove += (_, e) =>
        {
            int index = _results.IndexFromPoint(e.Location);
            string hint = index >= 0 && index < _results.Items.Count &&
                _results.Items[index] is AccountRow row
                    ? row.ToString() + Environment.NewLine + row.Dn : "";
            if (!string.Equals(_tips.GetToolTip(_results), hint, StringComparison.Ordinal))
                _tips.SetToolTip(_results, hint);
        };
        _password.TextChanged += (_, _) => UpdateButtons();
        _remember.CheckedChanged += (_, _) =>
        {
            if (!_remember.Checked) _savePassword.Checked = false;
        };
        _savePassword.CheckedChanged += (_, _) =>
        {
            if (_savePassword.Checked) _remember.Checked = true;
        };
        _manual.Click += (_, _) =>
        {
            string typed = _search.Text.Trim();
            if (typed.Length > 0)
                ChooseAccount(typed, typed + " (manually entered; verified at sign-in)");
        };
        _back.Click += (_, _) => ShowPage(0);
        _next.Click += (_, _) =>
        {
            if (_page == 0)
            {
                if (_results.SelectedItem is AccountRow row)
                    ChooseAccount(row.Login, row.ToString());
            }
            else
            {
                if (string.IsNullOrWhiteSpace(_chosenUser) || _password.Text.Length == 0)
                    return;
                SelectedUsername = _chosenUser;
                SelectedPassword = _password.Text;
                DialogResult = DialogResult.OK;
            }
        };

        UiStyle.Apply(this);
        ShowPage(0);
        Shown += (_, _) =>
        {
            _search.Focus();
            if (_search.Text.Trim().Length > 0) ScheduleSearch();
        };
    }

    private void BuildLayout()
    {
        var frame = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            Margin = Padding.Empty
        };
        frame.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 227));
        frame.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        frame.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        Controls.Add(frame);

        var rail = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5,
            BackColor = UiStyle.Navy, Padding = new Padding(17, 30, 13, 20)
        };
        rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 90));
        rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        rail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        rail.Controls.Add(new Label
        {
            Text = "ACTIVE DIRECTORY", Dock = DockStyle.Fill, AutoEllipsis = true,
            ForeColor = Color.FromArgb(178, 208, 251),
            Font = new Font("Segoe UI Semibold", 10f)
        }, 0, 0);
        rail.Controls.Add(new Label
        {
            Text = "Account sign-in", Dock = DockStyle.Fill, AutoEllipsis = true,
            ForeColor = Color.White,
            Font = new Font("Segoe UI Semibold", 15f)
        }, 0, 1);
        _stepOne.Text = "01   Find user";
        _stepTwo.Text = "02   Password";
        foreach (var step in new[] { _stepOne, _stepTwo })
        {
            step.Dock = DockStyle.Fill;
            step.AutoEllipsis = true;
            step.TextAlign = ContentAlignment.MiddleLeft;
            step.Padding = new Padding(11, 8, 4, 8);
            step.Font = new Font("Segoe UI Semibold", 11f);
        }
        rail.Controls.Add(_stepOne, 0, 2);
        rail.Controls.Add(_stepTwo, 0, 3);
        rail.Controls.Add(new Label
        {
            Text = "User lookup is read-only.\nNo directory changes.", Dock = DockStyle.Bottom,
            ForeColor = Color.FromArgb(193, 210, 239), AutoEllipsis = true,
            Height = 65
        }, 0, 4);
        frame.Controls.Add(rail, 0, 0);

        var workspace = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(25, 22, 26, 16), BackColor = UiStyle.Canvas
        };
        workspace.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 115));
        workspace.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        workspace.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        frame.Controls.Add(workspace, 1, 0);

        var heading = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        heading.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        heading.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _pageTitle.Dock = DockStyle.Fill;
        _pageTitle.AutoEllipsis = true;
        _pageTitle.Font = new Font("Segoe UI Semibold", 16f);
        _pageTitle.ForeColor = UiStyle.Navy;
        heading.Controls.Add(_pageTitle, 0, 0);
        _pageSubtitle.Dock = DockStyle.Fill;
        _pageSubtitle.AutoEllipsis = true;
        _pageSubtitle.ForeColor = UiStyle.Muted;
        heading.Controls.Add(_pageSubtitle, 0, 1);
        workspace.Controls.Add(heading, 0, 0);

        var host = new Panel { Dock = DockStyle.Fill, BackColor = Color.White, Padding = new Padding(16) };
        workspace.Controls.Add(host, 0, 1);
        BuildUserPage();
        BuildPasswordPage();
        host.Controls.Add(_searchPage);
        host.Controls.Add(_passwordPage);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, AutoScroll = true, Padding = new Padding(0, 11, 0, 0)
        };
        var cancel = new Button
        {
            Text = "Cancel", Width = 112, Height = 44, Tag = "NoIcon",
            Margin = new Padding(8, 0, 0, 0), DialogResult = DialogResult.Cancel
        };
        _next.Text = "Next >";
        _next.Width = 172;
        _next.Height = 44;
        _next.Tag = "PrimaryAction";
        _next.Margin = new Padding(8, 0, 0, 0);
        _back.Text = "< Back";
        _back.Width = 112;
        _back.Height = 44;
        _back.Tag = "NoIcon";
        _back.Margin = new Padding(8, 0, 0, 0);
        footer.Controls.Add(cancel);
        footer.Controls.Add(_next);
        footer.Controls.Add(_back);
        workspace.Controls.Add(footer, 0, 2);
        CancelButton = cancel;
        AcceptButton = _next;
    }

    private void BuildUserPage()
    {
        _searchPage.Dock = DockStyle.Fill;
        _searchPage.BackColor = Color.White;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 82));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
        var searchField = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1
        };
        searchField.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        searchField.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        searchField.Controls.Add(new Label
        {
            Text = "Search AD users by name, username or email",
            Dock = DockStyle.Fill, AutoEllipsis = true, ForeColor = UiStyle.Foreground,
            Font = new Font("Segoe UI Semibold", 10f)
        }, 0, 0);
        _search.Dock = DockStyle.Fill;
        _search.PlaceholderText = "Start typing a name or username...";
        _search.Margin = new Padding(0, 0, 4, 6);
        searchField.Controls.Add(_search, 0, 1);
        layout.Controls.Add(searchField, 0, 0);

        _results.Dock = DockStyle.Fill;
        _results.IntegralHeight = false;
        _results.HorizontalScrollbar = true;
        _results.BorderStyle = BorderStyle.FixedSingle;
        _results.Font = new Font("Segoe UI", 10f);
        layout.Controls.Add(_results, 0, 1);

        _searchStatus.Dock = DockStyle.Fill;
        _searchStatus.TextAlign = ContentAlignment.MiddleLeft;
        _searchStatus.AutoEllipsis = true;
        UiStyle.ApplyStatus(_searchStatus,
            "Start typing to find matching AD users. Select a result to continue.",
            UiStatusKind.Neutral);
        layout.Controls.Add(_searchStatus, 0, 2);
        _manual.Text = "Use typed login instead";
        _manual.Width = 242;
        _manual.Height = 43;
        _manual.Tag = "NoIcon";
        _manual.Margin = new Padding(0, 8, 0, 0);
        var manualRow = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
        manualRow.Controls.Add(_manual);
        layout.Controls.Add(manualRow, 0, 3);
        _searchPage.Controls.Add(layout);
    }

    private void BuildPasswordPage()
    {
        _passwordPage.Dock = DockStyle.Fill;
        _passwordPage.BackColor = Color.White;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 84));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 95));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, _allowSaving ? 77 : 0));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _selectedAccount.Dock = DockStyle.Fill;
        _selectedAccount.ForeColor = UiStyle.Navy;
        _selectedAccount.Font = new Font("Segoe UI Semibold", 12f);
        _selectedAccount.AutoEllipsis = true;
        _selectedAccount.Padding = new Padding(10, 15, 8, 8);
        _selectedAccount.BackColor = UiStyle.SoftBlue;
        layout.Controls.Add(_selectedAccount, 0, 0);
        var passwordField = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Padding = new Padding(0, 13, 0, 5)
        };
        passwordField.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        passwordField.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        passwordField.Controls.Add(new Label
        {
            Text = "AD account password", Dock = DockStyle.Fill,
            ForeColor = UiStyle.Foreground, AutoEllipsis = true,
            Font = new Font("Segoe UI Semibold", 10f)
        }, 0, 0);
        _password.UseSystemPasswordChar = true;
        _password.Dock = DockStyle.Fill;
        passwordField.Controls.Add(_password, 0, 1);
        layout.Controls.Add(passwordField, 0, 1);

        var saving = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1,
            BackColor = UiStyle.SoftBlue, Padding = new Padding(7, 10, 7, 10),
            Visible = _allowSaving
        };
        saving.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        saving.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _remember.Text = "Remember username";
        _remember.Dock = DockStyle.Fill;
        _savePassword.Text = "Save password securely";
        _savePassword.Dock = DockStyle.Fill;
        saving.Controls.Add(_remember, 0, 0);
        saving.Controls.Add(_savePassword, 1, 0);
        layout.Controls.Add(saving, 0, 2);
        layout.Controls.Add(new Label
        {
            Text = _allowSaving
                ? "The password is used for authentication after this wizard. Saving it is optional; Windows Credential Manager protects it for this Windows account."
                : "The password is held only for this session. It is not saved to a campaign, log, or the domain.",
            Dock = DockStyle.Fill, ForeColor = UiStyle.Muted,
            Padding = new Padding(8, 19, 8, 8), AutoEllipsis = true
        }, 0, 3);
        _passwordPage.Controls.Add(layout);
    }

    private void ScheduleSearch()
    {
        if (_settingText || IsDisposed || Disposing) return;
        _revision++;
        _debounce.Stop();
        _results.Items.Clear();
        _chosenUser = "";
        string query = _search.Text.Trim();
        _manual.Enabled = query.Length > 0;
        if (query.Length == 0)
        {
            UiStyle.ApplyStatus(_searchStatus, "Type a name to search Active Directory.", UiStatusKind.Neutral);
        }
        else if (query.Length > 48)
        {
            UiStyle.ApplyStatus(_searchStatus, "Search text must contain 1–48 characters.", UiStatusKind.Warning);
        }
        else
        {
            UiStyle.ApplyStatus(_searchStatus, "Searching matching AD accounts...", UiStatusKind.Busy);
            _debounce.Start();
        }
        UpdateButtons();
    }

    private async Task SearchUsersAsync(int revision, string query)
    {
        if (query.Length is < 1 or > 48 || IsDisposed || Disposing) return;
        try
        {
            string lookup = query;
            // For DOMAIN\user and user@upn searches, use the account portion.
            // The complete typed string is retained for manual sign-in.
            int slash = lookup.LastIndexOf('\\');
            if (slash >= 0) lookup = lookup[(slash + 1)..];
            int at = lookup.IndexOf('@');
            if (at > 0) lookup = lookup[..at];
            if (lookup.Length == 0) return;
            var response = await Task.Run(() =>
                DomainPowerShellRunner.Execute(DomainOperation.Discovery, "Users",
                    new { domain = _domain, dc = _dc, search = lookup },
                    new DomainCredentials(_domain, "", "")));
            if (IsDisposed || Disposing || revision != _revision || _page != 0) return;
            var entries = response.GetProperty("data").EnumerateArray().Select(item =>
                new AccountRow(
                    item.GetProperty("name").GetString() ?? "",
                    item.GetProperty("sam").GetString() ?? "",
                    item.TryGetProperty("upn", out JsonElement upn) ? upn.GetString() ?? "" : "",
                    item.GetProperty("dn").GetString() ?? ""
                )).Where(x => x.Sam.Length > 0).ToArray();
            _results.BeginUpdate();
            try
            {
                _results.Items.Clear();
                _results.Items.AddRange(entries.Cast<object>().ToArray());
            }
            finally { _results.EndUpdate(); }
            UiStyle.ApplyStatus(_searchStatus,
                entries.Length switch
                {
                    0 => "No matching accounts found. Refine the search or enter a login manually.",
                    75 => "Showing up to 75 matches. Type more characters to narrow the list.",
                    _ => $"{entries.Length} matching AD account(s). Select one to continue."
                }, entries.Length == 0 ? UiStatusKind.Warning : UiStatusKind.Success);
            UpdateButtons();
        }
        catch (Exception)
        {
            if (IsDisposed || Disposing || revision != _revision || _page != 0) return;
            _results.Items.Clear();
            UiStyle.ApplyStatus(_searchStatus,
                "AD search unavailable. Check domain/network or use a typed login. Sign-in still verifies credentials.",
                UiStatusKind.Warning);
            UpdateButtons();
        }
    }

    private void ChooseAccount(string login, string description)
    {
        if (string.IsNullOrWhiteSpace(login)) return;
        _chosenUser = login.Trim();
        _selectedAccount.Text = description + Environment.NewLine + "Login: " + _chosenUser;
        if (!string.Equals(_chosenUser, _initialUser, StringComparison.OrdinalIgnoreCase))
            _password.Clear();
        ShowPage(1);
        _password.Focus();
    }

    private void ShowPage(int page)
    {
        _page = page;
        _debounce.Stop();
        if (_page != 0) _revision++;
        _searchPage.Visible = _page == 0;
        _passwordPage.Visible = _page == 1;
        _pageTitle.Text = _page == 0 ? "Select Active Directory user" : "Enter account password";
        _pageSubtitle.Text = _page == 0
            ? "Domain: " + _domain + "  |  Matching results appear as you type"
            : "Review the selected account and enter its password.";
        _stepOne.BackColor = _page == 0 ? UiStyle.Accent : UiStyle.Navy;
        _stepTwo.BackColor = _page == 1 ? UiStyle.Accent : UiStyle.Navy;
        _stepOne.ForeColor = _page == 0 ? Color.White : Color.FromArgb(185, 207, 240);
        _stepTwo.ForeColor = _page == 1 ? Color.White : Color.FromArgb(185, 207, 240);
        _next.Text = _page == 0 ? "Next >" : "Use this account";
        _back.Visible = _page == 1;
        UpdateButtons();
        if (_page == 0 && _search.Text.Trim().Length > 0) ScheduleSearch();
    }

    private void UpdateButtons()
    {
        _next.Enabled = _page == 0
            ? _results.SelectedItem is AccountRow
            : _chosenUser.Length > 0 && _password.Text.Length > 0;
        _manual.Enabled = _search.Text.Trim().Length > 0;
    }

    // Windows UI smoke never queries AD or displays a modal sign-in window.
    internal void ShowPasswordPageForSmokeTest() =>
        ChooseAccount("testuser@example.com", "Example Test User");

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _revision++;
        _debounce.Stop();
        base.OnFormClosed(e);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _debounce.Dispose();
            _tips.Dispose();
        }
        base.Dispose(disposing);
    }
}
