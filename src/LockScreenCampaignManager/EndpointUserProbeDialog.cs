using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// A standalone, explicit CURRENT-USER HKCU collector for an approved pilot
/// Windows 10/11 device. No AD credentials, network requests, execution of
/// helper programs, registry writes, automatic uploads or scheduled tasks.
/// </summary>
internal sealed class EndpointUserProbeDialog : Form
{
    private EndpointUserReadinessEvidence? _snapshot;
    private readonly Label _status = new() { Name = "EndpointUserProbeStatus" };
    private readonly TextBox _preview = new() { Name = "EndpointUserProbePreview" };
    private readonly Button _save;

    internal EndpointUserProbeDialog()
    {
        Name = "EndpointUserProbeDialog";
        Text = "LockScreen Campaign Manager " + AppVersion.Current +
            " - " + UiLocale.Display("Endpoint user Screen Saver probe");
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        MinimumSize = new Size(750, 550);
        Size = new Size(1040, 730);
        StartPosition = FormStartPosition.CenterParent;
        BackColor = UiStyle.Canvas;
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(16),
            RowCount = 4, ColumnCount = 1
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 66));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        Controls.Add(root);

        root.Controls.Add(new Label
        {
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 10f),
            ForeColor = UiStyle.Foreground,
            AutoEllipsis = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Text = UiLocale.Display(
                "Run on the pilot PC as the actual signed-in user. Reads only this account's HKCU Screen Saver values. Administrator run-as reads the administrator's HKCU, not the target user's. No data is sent.")
        }, 0, 0);

        _preview.Dock = DockStyle.Fill;
        _preview.ReadOnly = true;
        _preview.Multiline = true;
        _preview.ScrollBars = ScrollBars.Both;
        _preview.WordWrap = false;
        _preview.RightToLeft = RightToLeft.No;
        _preview.Font = new Font("Consolas", 9.5f);
        _preview.BackColor = UiStyle.Surface;
        _preview.Text = UiLocale.Display(
            "No HKCU snapshot yet. Click Capture this Windows user to read the current account.");
        root.Controls.Add(_preview, 0, 1);

        _status.Dock = DockStyle.Fill;
        _status.TextAlign = ContentAlignment.MiddleLeft;
        _status.ForeColor = UiStyle.Muted;
        _status.AutoEllipsis = false;
        _status.Text = UiLocale.Display("Local read only. No file has been created.");
        root.Controls.Add(_status, 0, 2);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft,
            AutoScroll = true, WrapContents = false
        };
        var close = MakeButton("Close", 100);
        close.Click += (_, _) => Close();
        buttons.Controls.Add(close);
        _save = MakeButton("Save local HKCU JSON...", 225);
        _save.Name = "EndpointUserProbeSave";
        _save.Enabled = false;
        _save.Click += (_, _) => Save();
        buttons.Controls.Add(_save);
        var capture = MakeButton("Capture this Windows user", 245);
        capture.Name = "EndpointUserProbeCapture";
        capture.Click += (_, _) => Capture();
        buttons.Controls.Add(capture);
        root.Controls.Add(buttons, 0, 3);

        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "endpoint-user-hkcu-probe");
    }

    private static Button MakeButton(string caption, int minWidth) => new()
    {
        Text = UiLocale.Display(caption), MinimumSize = new Size(minWidth, 38),
        AutoSize = true, AutoSizeMode = AutoSizeMode.GrowOnly,
        FlatStyle = FlatStyle.Flat, BackColor = UiStyle.Surface,
        Margin = new Padding(4)
    };

    private void Capture()
    {
        _snapshot = null;
        _save.Enabled = false;
        _preview.Clear();
        try
        {
            var evidence = EndpointUserReadinessEvidence.CaptureLocal();
            _snapshot = evidence;
            _preview.Text = evidence.RawJson;
            _status.Text = UiLocale.Display("Captured locally — unsigned, not an effective GPO attestation.") +
                "  SHA-256 " + evidence.Sha256;
            _save.Enabled = true;
        }
        catch (Exception ex)
        {
            _status.Text = UiLocale.Display("HKCU snapshot not available.");
            UiErrorDialog.Show(this, "Capture pilot user's HKCU", ex);
        }
    }

    private void Save()
    {
        if (_snapshot is null) return;
        if (MessageBox.Show(this, UiLocale.Display(
            "This JSON contains private domain/computer/user identities, Screen Saver settings and paths. Save only in a protected LOCAL folder, transfer securely, and do not upload it to a public issue. Continue?"),
            UiLocale.Display("Private endpoint evidence"),
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        using var picker = new SaveFileDialog
        {
            Title = UiLocale.Display("Export local pilot user HKCU evidence"),
            Filter = "JSON (*.json)|*.json",
            DefaultExt = "json", AddExtension = true, OverwritePrompt = false,
            FileName = "LSCM-HKCU-" + DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmmss") + ".json"
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            _snapshot.SaveLocalJson(picker.FileName);
            _status.Text = UiLocale.Display("Saved LOCAL private HKCU evidence — not uploaded.") +
                "  SHA-256 " + _snapshot.Sha256;
        }
        catch (Exception ex)
        {
            UiErrorDialog.Show(this, "Save local HKCU evidence", ex);
        }
    }
}
