using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>Public GitHub Issue link with explicit privacy review; no automatic upload.</summary>
internal static class ManualGitHubIssueHelp
{
    internal const string Repository = "sgennadi/LockScreenCampaignManager";
    internal const string IssueUrl = "https://github.com/" + Repository + "/issues/new";
    internal const string Explanation =
        "No diagnostics are sent automatically. This button copies the displayed report " +
        "and opens a blank GitHub Issue form.\n\n" +
        "This repository is PUBLIC. Anything posted in an Issue can be seen by anyone. " +
        "Review the JSON and remove all private domains, names, paths, machine details " +
        "and secrets before pasting or submitting it.\n\n" +
        "YES: Copy the reviewed report and open GitHub Issues.\n" +
        "NO: Stay here; nothing is sent.\n\n" +
        "The local report stays pending: this application cannot confirm GitHub delivery.";
    internal static void OpenReviewedReport(IWin32Window owner, string reviewedReport)
    {
        if (string.IsNullOrWhiteSpace(reviewedReport))
        {
            MessageBox.Show(owner, "Select a saved error report first.",
                "Report an error", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var choice = MessageBox.Show(owner, Explanation,
            "Report an error — public GitHub repository", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (choice != DialogResult.Yes) return;
        try
        {
            Clipboard.SetText(reviewedReport);
            // The issue must be reviewed, pasted and submitted by the user.
            Process.Start(new ProcessStartInfo { FileName = IssueUrl, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner,
                "Cannot copy the report or open GitHub. Nothing was delivered." +
                Environment.NewLine + UniversalDiagnosticsEmbedded.Clean(ex.Message),
                "Error report was not sent", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

}

/// <summary>UniversalDiagnostics v1 review / opt-in capture. No background upload.</summary>
internal static class UiErrorDialog
{
    public static void Show(IWin32Window? owner, string operation, Exception error)
    {
        AppLog.Write(error, operation);
        using var form = new Form { Text = "Report an error — LockScreen Campaign Manager",
            StartPosition = FormStartPosition.CenterParent, AutoScaleMode = AutoScaleMode.Dpi,
            Size = new Size(830, 590), MinimumSize = new Size(600, 440),
            Font = new Font("Segoe UI", 9.5f) };
        string payload = UniversalDiagnosticsEmbedded.MakePayload(operation, error);
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(14),
            RowCount = 3, ColumnCount = 1 };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 75));
        panel.Controls.Add(new Label { Dock = DockStyle.Fill,
            Text = "Error: " + UniversalDiagnosticsEmbedded.Clean(error.GetType().Name) + "\n" +
                "No reports are sent automatically. Review the sanitized data." }, 0, 0);
        var detail = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9f),
            Text = UniversalDiagnosticsEmbedded.Preview(payload) };
        panel.Controls.Add(detail, 0, 1);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true };
        Button B(string text, Action act, int width)
        {
            var b = new Button { Text = text, Width = width, Height = 42, Margin = new Padding(0, 4, 10, 0) };
            b.Click += (_, _) => act(); return b;
        }
        footer.Controls.Add(B("Save locally", () => {
            try {
                var receipt = UniversalDiagnosticsEmbedded.CaptureReviewed(detail.Text);
                MessageBox.Show(form, "Report saved with pending status:\n" + receipt.Path +
                    "\nNothing was sent to GitHub.");
            } catch (Exception e) { MessageBox.Show(form, "Save error: " + UniversalDiagnosticsEmbedded.Clean(e.Message)); }
        }, 180));
        footer.Controls.Add(B("Copy", () => Clipboard.SetText(detail.Text), 115));
        footer.Controls.Add(B("Report an error...", () =>
            ManualGitHubIssueHelp.OpenReviewedReport(form, detail.Text), 190));
        footer.Controls.Add(B("Close", form.Close, 100));
        panel.Controls.Add(footer, 0, 2);
        form.Controls.Add(panel);
        UiStyle.Apply(form);
        if (owner is null) form.ShowDialog(); else form.ShowDialog(owner);
    }
}

internal sealed class DiagnosticsWindow : Form
{
    private readonly ListBox _files = new();
    private readonly TextBox _detail = new();
    public DiagnosticsWindow()
    {
        Text = "Diagnostics — saved error reports";
        AutoScaleMode = AutoScaleMode.Dpi;
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterParent;
        Size = new Size(900, 610);
        MinimumSize = new Size(650, 430);
        var panel = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        Shown += (_, _) => {
            if (panel.Width > 530)
            {
                panel.Panel1MinSize = 200;
                panel.Panel2MinSize = 250;
                panel.SplitterDistance = Math.Clamp(panel.Width / 3, panel.Panel1MinSize,
                    panel.Width - panel.Panel2MinSize - panel.SplitterWidth);
            }
        };
        _files.Dock = DockStyle.Fill; _files.IntegralHeight = false;
        _detail.Dock = DockStyle.Fill; _detail.Multiline = true; _detail.ScrollBars = ScrollBars.Both;
        _detail.WordWrap = false; _detail.ReadOnly = true; _detail.Font = new Font("Consolas", 9.5f);
        _files.SelectedIndexChanged += (_, _) => {
            if (_files.SelectedItem is string path && File.Exists(path)) _detail.Text = File.ReadAllText(path);
        };
        panel.Panel1.Controls.Add(_files); panel.Panel2.Controls.Add(_detail);
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1 };
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.Controls.Add(panel, 0, 0);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true };
        void Add(string title, Action act)
        {
            var b = new Button { Text = title, Width = 148, Height = 38 };
            b.Click += (_, _) => act(); buttons.Controls.Add(b);
        }
        Add("Refresh", Reload);
        Add("Error trends", () => {
            using var trends = new DiagnosticsTrendsWindow();
            trends.ShowDialog(this);
        });
        Add("Copy", () => { if (!string.IsNullOrEmpty(_detail.Text)) Clipboard.SetText(_detail.Text); });
        Add("Report an error...", () => ManualGitHubIssueHelp.OpenReviewedReport(this, _detail.Text));
        Add("Open folder", () => {
            string path = UniversalDiagnosticsEmbedded.QueueDirectory(); Directory.CreateDirectory(path);
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
        });
        Add("Close", Close);
        layout.Controls.Add(buttons, 0, 1);
        Controls.Add(layout);
        Reload(); UiStyle.Apply(this);
        UiWindowPlacement.Attach(this, "diagnostics");
    }
    private void Reload()
    {
        _files.Items.Clear();
        foreach (string path in UniversalDiagnosticsEmbedded.GetReports()) _files.Items.Add(path);
        if (_files.Items.Count > 0) _files.SelectedIndex = 0;
    }
}
