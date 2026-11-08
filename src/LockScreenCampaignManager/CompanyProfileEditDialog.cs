using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Local company profile editor. No AD connection or credential storage.
/// A profile is only a starting point for the existing verified GPO wizard.
/// </summary>
internal sealed class CompanyProfileEditDialog : Form
{
    private readonly CompanyProfile _original;
    private readonly TextBox _name = new();
    private readonly TextBox _domain = new();
    private readonly TextBox _dc = new();
    private readonly TextBox _ou = new();
    private readonly TextBox _group = new();
    private readonly TextBox _share = new();
    private readonly ComboBox _zone = new();
    private readonly CheckBox _feedback = new();
    private readonly Label _notice = new();
    private Button _save = new();
    internal CompanyProfile? ResultProfile { get; private set; }

    internal CompanyProfileEditDialog(CompanyProfile? existing = null)
    {
        _original = existing ?? new CompanyProfile();
        Text = existing is null ? "New company profile" : "Edit company profile";
        Font = new Font("Segoe UI", 9.5f);
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(710, 690);
        Size = new Size(880, 790);
        BackColor = UiStyle.Canvas;
        Build();
        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "company-profile-editor");
    }

    private void Build()
    {
        var root = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
            Padding = new Padding(18), AutoScroll = true, BackColor = UiStyle.Canvas
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 86));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        Controls.Add(root);

        var intro = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
        intro.RowStyles.Add(new RowStyle(SizeType.Absolute, 37));
        intro.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        intro.Controls.Add(new Label {
            Dock = DockStyle.Fill, Text = Text,
            Font = new Font("Segoe UI Semibold", 16), ForeColor = UiStyle.Foreground
        }, 0, 0);
        intro.Controls.Add(new Label {
            Dock = DockStyle.Fill, ForeColor = UiStyle.Muted,
            Text = "Local profile defaults only. AD rights, OU and group membership " +
                "are verified again before a GPO can be created."
        }, 0, 1);
        root.Controls.Add(intro, 0, 0);

        var fields = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 9,
            AutoScroll = true, Padding = new Padding(8), BackColor = UiStyle.Surface
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 230));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (int i = 0; i < 9; i++)
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, i == 7 ? 54 : 50));
        root.Controls.Add(fields, 0, 1);

        AddField(fields, 0, "Company name", _name, "e.g. Main Office");
        AddField(fields, 1, "Active Directory domain", _domain, "e.g. ad.example.test");
        AddField(fields, 2, "Preferred DC (optional)", _dc, "e.g. dc01.ad.example.test");
        AddField(fields, 3, "Computer OU (optional)", _ou, "OU=Computers,DC=ad,DC=example,DC=test");
        AddField(fields, 4, "Security group (optional)", _group,
            "CN=LockScreenPCs,OU=Groups,DC=ad,DC=example,DC=test");
        AddField(fields, 5, "Client Health SMB share", _share,
            @"\\fileserver\LockScreenReports$");

        _zone.DropDownStyle = ComboBoxStyle.DropDown;
        _zone.Dock = DockStyle.Fill;
        _zone.DropDownWidth = 320;
        _zone.Items.AddRange(TimeZoneInfo.GetSystemTimeZones()
            .OrderBy(zone => zone.BaseUtcOffset)
            .ThenBy(zone => zone.Id, StringComparer.OrdinalIgnoreCase)
            .Select(zone => (object)zone.Id).ToArray());
        fields.Controls.Add(Caption("Windows time zone"), 0, 6);
        fields.Controls.Add(_zone, 1, 6);
        _feedback.Text = "Enable Client Health for new campaigns";
        _feedback.Dock = DockStyle.Fill;
        _feedback.AutoSize = true;
        _feedback.CheckedChanged += (_, _) => _share.BackColor =
            _feedback.Checked ? UiStyle.Surface : SystemColors.Window;
        fields.Controls.Add(Caption("Feedback defaults"), 0, 7);
        fields.Controls.Add(_feedback, 1, 7);
        fields.Controls.Add(new Label {
            Text = "Passwords, gMSA identities and credentials are never saved in company profiles.",
            ForeColor = UiStyle.Muted, Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft
        }, 1, 8);

        _name.Text = _original.Name;
        _domain.Text = _original.Domain;
        _dc.Text = _original.PreferredDc;
        _ou.Text = _original.DefaultOuDn;
        _group.Text = _original.DefaultGroupDn;
        _share.Text = _original.FeedbackShare;
        _zone.Text = _original.TimeZoneId;
        _feedback.Checked = _original.EnableFeedback;

        _notice.Dock = DockStyle.Fill;
        _notice.ForeColor = UiStyle.Muted;
        _notice.Text = "Saving this profile does NOT contact AD or deploy a campaign." +
            Environment.NewLine + "Changing a profile does not modify previously exported campaigns.";
        root.Controls.Add(_notice, 0, 2);

        var buttons = new FlowLayoutPanel {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            AutoScroll = true, WrapContents = true
        };
        var cancel = MakeButton("Cancel", 120);
        cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        _save = MakeButton("Save company", 165);
        _save.BackColor = UiStyle.Accent;
        _save.ForeColor = Color.White;
        _save.Click += (_, _) => Save();
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(_save);
        root.Controls.Add(buttons, 0, 3);
        AcceptButton = _save;
        CancelButton = cancel;
    }

    private static void AddField(TableLayoutPanel table, int row, string title,
        TextBox field, string placeholder)
    {
        field.Dock = DockStyle.Fill;
        field.PlaceholderText = placeholder;
        field.Margin = new Padding(3, 7, 8, 7);
        table.Controls.Add(Caption(title), 0, row);
        table.Controls.Add(field, 1, row);
    }

    private static Label Caption(string text) => new() {
        Text = text, Dock = DockStyle.Fill, ForeColor = UiStyle.Foreground,
        TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true,
        Padding = new Padding(6, 0, 0, 0)
    };

    private static Button MakeButton(string text, int width) => new() {
        Text = text, Width = width, Height = 39,
        Margin = new Padding(2, 4, 8, 4), AutoEllipsis = true
    };

    private void Save()
    {
        try
        {
            ResultProfile = CompanyProfilesStore.Validate(_original with {
                Name = _name.Text, Domain = _domain.Text,
                PreferredDc = _dc.Text, DefaultOuDn = _ou.Text,
                DefaultGroupDn = _group.Text, FeedbackShare = _share.Text,
                EnableFeedback = _feedback.Checked, TimeZoneId = _zone.Text
            });
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            ResultProfile = null;
            _notice.Text = "Please correct the profile: " +
                UniversalDiagnosticsEmbedded.Clean(ex.Message, 170);
            UiErrorDialog.Show(this, "Company profile validation", ex);
        }
    }
}
