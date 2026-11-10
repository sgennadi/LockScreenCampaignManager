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
        "The public GitHub Issue form will open with a prepared title and error report.\n\n" +
        "You must review and explicitly submit it in GitHub; nothing is uploaded automatically. " +
        "This repository is PUBLIC, so remove private names, domains, paths, " +
        "computer details and secrets before submitting.\n\n" +
        "A copy of the report will also be placed on the clipboard as a fallback.\n\n" +
        "YES: Prepare a GitHub Issue.  NO: Stay here.";
    internal static void OpenReviewedReport(IWin32Window owner, string reviewedReport)
    {
        if (string.IsNullOrWhiteSpace(reviewedReport))
        {
            MessageBox.Show(owner, UiLocale.Display("Select a saved error report first."),
                UiLocale.Display("Report an error"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        var choice = MessageBox.Show(owner, UiLocale.Display(Explanation),
            UiLocale.Display("Report an error — public GitHub repository"), MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (choice != DialogResult.Yes) return;
        try
        {
            Clipboard.SetText(reviewedReport);
            // GitHub's issue query parameters populate the draft, but never
            // submit it. The user must review and click Submit new issue.
            // Keep the URL bounded for browsers and proxy configurations.
            string title = "LockScreen Campaign Manager — error report";
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(reviewedReport);
                var ev = document.RootElement.GetProperty("event");
                string component = ev.GetProperty("component").GetString() ?? "Application";
                string errorType = ev.GetProperty("error_type").GetString() ?? "Error";
                title = "[Bug] " + UniversalDiagnosticsEmbedded.Clean(component, 80) +
                    " — " + UniversalDiagnosticsEmbedded.Clean(errorType, 80);
            }
            catch (System.Text.Json.JsonException) { /* Keep a generic title for older reports. */ }
            var body = "### Diagnostic report (review before publishing)\n\n" +
                "This is a public issue. Remove any private information.\n\n" +
                "```json\n" + reviewedReport.Replace("```", "`` `") + "\n```\n";
            string target = IssueUrl + "?title=" + Uri.EscapeDataString(title) +
                "&body=" + Uri.EscapeDataString(body);
            if (target.Length > 7000)
            {
                target = IssueUrl + "?title=" + Uri.EscapeDataString(title);
                MessageBox.Show(owner,
                    "The report is too long to prefill safely. It was copied to the clipboard. " +
                    "Paste it into the GitHub issue body after reviewing it.",
                    UiLocale.Display("Report copied"), MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show(owner,
                "Cannot copy the report or open GitHub. Nothing was delivered." +
                Environment.NewLine + UniversalDiagnosticsEmbedded.Clean(ex.Message),
                UiLocale.Display("Error report was not sent"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 110));
        panel.Controls.Add(new Label { Dock = DockStyle.Fill,
            Text = UiLocale.Display("Error: ") +
                UniversalDiagnosticsEmbedded.Clean(error.GetType().Name) + "\n" +
                UiLocale.Display("No reports are sent automatically. Review the sanitized data.") }, 0, 0);
        var detail = new TextBox { Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
            ScrollBars = ScrollBars.Both, WordWrap = false, Font = new Font("Consolas", 9f),
            Text = UniversalDiagnosticsEmbedded.Preview(payload) };
        panel.Controls.Add(detail, 0, 1);
        var footer = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true };
        Button B(string text, Action act, int width)
        {
            var b = new Button { Text = UiLocale.Display(text), Width = width, Height = 42,
                MinimumSize = new Size(width, 42), AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowOnly, Margin = new Padding(0, 4, 10, 0) };
            b.Click += (_, _) => act(); return b;
        }
        footer.Controls.Add(B("Save locally", () => {
            try {
                var receipt = UniversalDiagnosticsEmbedded.CaptureReviewed(detail.Text);
                MessageBox.Show(form, UiLocale.Display("Report saved with pending status:") +
                    "\n" + receipt.Path + "\n" +
                    UiLocale.Display("Nothing was sent to GitHub."));
            } catch (Exception e) { MessageBox.Show(form, UiLocale.Display("Save error: ") +
                UniversalDiagnosticsEmbedded.Clean(e.Message)); }
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
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 100));
        layout.Controls.Add(panel, 0, 0);
        var buttons = new FlowLayoutPanel {
            Dock = DockStyle.Fill, AutoScroll = true, WrapContents = true,
            Padding = new Padding(6, 10, 6, 5)
        };
        void Add(string title, Action act)
        {
            var b = new Button { Text = UiLocale.Display(title),
                Width = title.StartsWith("Report") ? 178 : 148,
                Height = 38, MinimumSize = new Size(title.StartsWith("Report") ? 178 : 148, 38),
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowOnly,
                Margin = new Padding(4, 4, 4, 4), Tag = "NoIcon" };
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
