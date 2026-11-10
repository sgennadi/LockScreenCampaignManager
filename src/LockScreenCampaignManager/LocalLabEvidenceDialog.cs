using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Offline inspection only. Local-only, untrusted JSON evidence is never
/// treated as a command or as proof of an authenticated endpoint identity.
/// </summary>
internal sealed class LocalLabEvidencePanel : UserControl
{
    private readonly TextBox _canaryDetails = new();
    private readonly TextBox _cleanupDetails = new();
    private readonly Label _canaryStatus = new();
    private readonly Label _cleanupStatus = new();

    internal LocalLabEvidencePanel()
    {
        Name = "LocalLabEvidencePanel";
        Dock = DockStyle.Fill;
        BackColor = UiStyle.Canvas;
        var layout = new TableLayoutPanel {
            Name = "LocalLabEvidenceLayout", Dock = DockStyle.Fill,
            ColumnCount = 1, RowCount = 4,
            Padding = new Padding(10)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 54));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        Controls.Add(layout);

        layout.Controls.Add(new Label {
            Text = "Local Windows lab evidence (offline, read-only)",
            Dock = DockStyle.Fill, Name = "LocalLabEvidenceHeader",
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = false,
            Font = new Font("Segoe UI Semibold", 13.2f),
            ForeColor = UiStyle.Navy
        }, 0, 0);
        layout.Controls.Add(Card("SMB LocalSystem canary", _canaryStatus,
            _canaryDetails, "Import SMB canary JSON...", "LocalLabCanaryImport",
            () => Import("SMB LocalSystem canary", _canaryStatus, _canaryDetails)), 0, 1);
        layout.Controls.Add(Card("Endpoint cleanup audit", _cleanupStatus,
            _cleanupDetails, "Import cleanup JSON...", "LocalLabCleanupImport",
            () => Import("Endpoint cleanup audit", _cleanupStatus, _cleanupDetails)), 0, 2);
        layout.Controls.Add(new Label {
            Text = "These reports have no signed endpoint identity. Check the computer and GPO separately. " +
                "Never interpret them as physical Lock Screen display or remote online proof.",
            Dock = DockStyle.Fill, AutoSize = false,
            Name = "LocalLabEvidenceCaution", ForeColor = UiStyle.Muted,
            TextAlign = ContentAlignment.MiddleLeft,
        }, 0, 3);
    }

    private static Control Card(string title, Label status, TextBox details,
        string buttonText, string buttonName, Action open)
    {
        var host = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Margin = new Padding(2, 3, 2, 8), Padding = new Padding(8),
            BackColor = UiStyle.Surface
        };
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        host.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
        host.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        var top = new FlowLayoutPanel {
            Dock = DockStyle.Fill, WrapContents = false,
            AutoScroll = true, Padding = new Padding(1)
        };
        top.Controls.Add(new Label {
            Text = title, AutoSize = true,
            Margin = new Padding(4, 11, 12, 2),
            Font = new Font("Segoe UI Semibold", 10.4f), ForeColor = UiStyle.Navy
        });
        var browse = new Button {
            Name = buttonName, Text = buttonText, AutoSize = true,
            MinimumSize = new Size(205, 37), Height = 39,
            Tag = "NoIcon", Margin = new Padding(5, 1, 5, 1)
        };
        browse.Click += (_, _) => open();
        top.Controls.Add(browse);
        host.Controls.Add(top, 0, 0);
        status.Text = UiLocale.Display("No report imported");
        status.Dock = DockStyle.Fill;
        status.Name = buttonName + "Status";
        status.TextAlign = ContentAlignment.MiddleLeft;
        status.AutoEllipsis = false;
        status.ForeColor = UiStyle.Muted;
        host.Controls.Add(status, 0, 1);
        details.Multiline = true;
        details.ReadOnly = true;
        details.Name = buttonName + "Summary";
        details.Dock = DockStyle.Fill;
        details.WordWrap = true;
        details.ScrollBars = ScrollBars.Vertical;
        details.RightToLeft = RightToLeft.No;
        details.BackColor = UiStyle.Surface;
        details.Font = new Font("Segoe UI", 9.2f);
        details.Text = UiLocale.Display("Select a local JSON produced by an approved test PC. " +
            "Do not upload it to GitHub or treat it as authenticated evidence.");
        host.Controls.Add(details, 0, 2);
        return host;
    }

    private void Import(string expectedKind, Label status, TextBox details)
    {
        using var open = new OpenFileDialog {
            Title = UiLocale.Display("Select LOCAL Windows lab report"),
            Filter = UiLocale.Display("JSON report (*.json)") + "|*.json",
            CheckFileExists = true, CheckPathExists = true,
            Multiselect = false
        };
        if (open.ShowDialog(FindForm()) != DialogResult.OK) return;
        try
        {
            var evidence = LocalLabEvidence.LoadLocalFile(
                open.FileName, DateTimeOffset.UtcNow);
            if (evidence.Kind != expectedKind)
                throw new InvalidDataException("The selected file is the other report type.");
            var text = new StringBuilder();
            text.AppendLine("REPORT TYPE: " + evidence.Kind);
            text.AppendLine("RESULT (untrusted local claim): " + evidence.Result);
            text.AppendLine("OBSERVED UTC: " + evidence.ObservedUtc.ToString("u"));
            text.AppendLine("WINDOWS BUILD (reported): " + evidence.OsBuild);
            text.AppendLine("AGE: " + (evidence.Stale ? "STALE (over 24 h)" : "Within 24 h"));
            text.AppendLine("SIGNED / REMOTELY AUTHENTICATED: NO");
            text.AppendLine("VISUAL LOCK SCREEN VERIFIED: NO");
            text.AppendLine();
            foreach (var check in evidence.Checks)
                text.AppendLine((check.Passed == true ? "[YES] " :
                    check.Passed == false ? "[NO] " : "[UNKNOWN] ") +
                    check.Label + " - " + check.Description);
            text.AppendLine();
            text.AppendLine("FINDINGS: " + (evidence.Findings.Count == 0
                ? "(none reported)" : string.Join(", ", evidence.Findings)));
            status.Text = UiLocale.Display(evidence.Stale ? "Stale report - review on the endpoint"
                : evidence.Result is "FAIL_CLOSED" or "INCOMPLETE" or
                  "POSTCLEANUP_REQUIRES_REVIEW"
                    ? "Incomplete / failed checks - review needed"
                    : "Imported locally - NOT authenticated endpoint proof");
            status.ForeColor = evidence.Stale ? UiStyle.Muted :
                evidence.Result is "FAIL_CLOSED" or "INCOMPLETE" or
                    "POSTCLEANUP_REQUIRES_REVIEW" ? Color.DarkRed : UiStyle.Navy;
            details.Text = text.ToString();
        }
        catch (Exception ex) when (ex is InvalidDataException or
            IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            status.Text = UiLocale.Display("Report rejected - invalid or incompatible JSON");
            status.ForeColor = Color.DarkRed;
            details.Text = UiLocale.Display("The JSON failed strict offline validation. " +
                "No data was sent and no endpoint or AD action was performed.");
            MessageBox.Show(FindForm(), ex.Message, UiLocale.Display("Local report rejected"),
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    internal TabPage AsTabPage()
    {
        var page = new TabPage("Lab evidence") {
            Name = "GpoLocalLabEvidenceTab", BackColor = UiStyle.Canvas
        };
        page.Controls.Add(this);
        return page;
    }
}

internal sealed class LocalLabEvidenceDialog : Form
{
    internal LocalLabEvidenceDialog()
    {
        Name = "LocalLabEvidenceDialog";
        Text = "LockScreen Campaign Manager - Local lab evidence";
        StartPosition = FormStartPosition.CenterParent;
        MinimumSize = new Size(780, 590);
        Size = new Size(1050, 750);
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        Controls.Add(new LocalLabEvidencePanel());
        UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "local-lab-evidence");
    }
}
