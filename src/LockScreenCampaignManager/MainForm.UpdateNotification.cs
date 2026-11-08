using System.Drawing;
using System.Windows.Forms;
using UniversalUpdater;

namespace LockScreenCampaignManager;

public sealed partial class MainForm
{
    private readonly Button _updateNotice = new()
    {
        Name = "UpdateNoticeButton",
        Text = "Check updates",
        Tag = "NoIcon",
        Visible = true,
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        Margin = new Padding(4, 3, 5, 3),
        Cursor = Cursors.Hand,
        AccessibleName = "Open updater for a new version"
    };
    private readonly ToolTip _updateNoticeHelp = new()
    {
        AutoPopDelay = 23000, InitialDelay = 250, ReshowDelay = 150
    };
    private readonly CancellationTokenSource _updateNoticeClosing = new();
    private UpdateRelease? _noticedRelease;
    private ColumnStyle? _noticeColumn;
    private TableLayoutPanel? _noticeStatusRow;
    private TableLayoutPanel? _noticeFooter;
    private TableLayoutPanel? _noticeRoot;
    private DateTimeOffset _noticeLastChecked = DateTimeOffset.MinValue;
    private bool _noticeChecking;
    private bool _noticeCheckFailed;
    private static readonly TimeSpan NoticeCheckInterval = TimeSpan.FromMinutes(10);
    private readonly System.Windows.Forms.Timer _noticePoll = new()
    {
        Interval = 10 * 60 * 1000
    };

    internal event Action? UpdateNoticeChanged;
    internal string? AvailableUpdateVersion => _noticedRelease?.Version.ToString();
    internal string AvailableUpdateSummary => _updateNoticeHelp.GetToolTip(_updateNotice);

    private void ConfigureUpdateNotice(TableLayoutPanel footer,
        TableLayoutPanel editorRoot)
    {
        _noticeFooter = footer;
        _noticeRoot = editorRoot;
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(0),
            BackColor = UiStyle.Canvas
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        _noticeColumn = new ColumnStyle(SizeType.Absolute, 0);
        row.ColumnStyles.Add(_noticeColumn);
        _noticeStatusRow = row;
        row.Controls.Add(_status, 0, 0);
        row.Controls.Add(_updateNotice, 1, 0);
        footer.Controls.Add(row, 0, 1);
        _updateNotice.Click += (_, _) => OpenUniversalUpdaterFromNotice();
        _updateNotice.FontChanged += (_, _) => FitUpdateNotice();
        row.ClientSizeChanged += (_, _) => FitUpdateNotice();
    }

    private void EnableUpdateNoticeMonitoring()
    {
        // No startup dialog, token, downloads, auto-install, or blocking UI.
        // A failed read-only check is NOT a claim that the app is current.
        _noticePoll.Tick += async (_, _) => await CheckUpdateNoticeAsync();
        Shown += async (_, _) =>
        {
            _noticePoll.Start();
            await CheckUpdateNoticeAsync();
        };
        Activated += async (_, _) => await CheckUpdateNoticeAsync();
        FormClosed += (_, _) =>
        {
            _noticePoll.Stop();
            _noticePoll.Dispose();
            _updateNoticeClosing.Cancel();
        };
    }

    private async Task CheckUpdateNoticeAsync()
    {
        if (_noticeChecking || IsDisposed || Disposing ||
            DateTimeOffset.UtcNow - _noticeLastChecked < NoticeCheckInterval)
            return;
        _noticeChecking = true;
        _noticeLastChecked = DateTimeOffset.UtcNow;
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
                _updateNoticeClosing.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            var options = UniversalUpdaterWindow.PinnedOptions(UpdateChannel.Prerelease);
            using var client = new GitHubReleaseClient();
            UpdateRelease? newer = await client.CheckAsync(options, timeout.Token);
            if (!IsDisposed && !Disposing && !_updateNoticeClosing.IsCancellationRequested)
                ShowUpdateNotice(newer);
        }
        catch (OperationCanceledException)
        {
            ShowUpdateCheckError("GitHub check timed out or was cancelled.");
        }
        catch (Exception ex)
        {
            // Keep any previously verified update; expose a clear retry
            // action rather than hiding the checker on network/API failures.
            AppLog.Write(ex, "Background GitHub release check");
            ShowUpdateCheckError("Unable to check GitHub Releases. Open Updater for details.");
        }
        finally { _noticeChecking = false; }
    }

    private void ShowUpdateNotice(UpdateRelease? release)
    {
        _noticedRelease = release;
        _noticeCheckFailed = false;
        _updateNotice.Visible = true;
        if (release is null)
        {
            _updateNotice.Text = "Check updates";
            _updateNoticeHelp.SetToolTip(_updateNotice,
                "No newer release was detected at the last successful check. " +
                "Click to check GitHub Releases manually.");
            StyleUpdateNotice();
            FitUpdateNotice();
            UpdateNoticeChanged?.Invoke();
            return;
        }
        _updateNotice.Text = "Update available: " + release.Version;
        string date = release.PublishedAt?.ToLocalTime().ToString("yyyy-MM-dd") ?? "not specified";
        string notes = string.Join(" ", release.ReleaseNotes
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim()).Where(x => x.Length > 0).Take(4));
        if (notes.Length > 280) notes = notes[..280] + "...";
        _updateNoticeHelp.SetToolTip(_updateNotice,
            "Installed: " + AppVersion.Current +
            "\nAvailable: " + release.Version +
            "\nPublished: " + date +
            (notes.Length > 0 ? "\nChanges: " + notes : "") +
            "\n\nClick to open Updater (Check -> Install). No automatic update.");
        StyleUpdateNotice();
        FitUpdateNotice();
        UpdateNoticeChanged?.Invoke();
    }

    private void ShowUpdateCheckError(string explanation)
    {
        if (IsDisposed || Disposing || _updateNoticeClosing.IsCancellationRequested)
            return;
        _noticeCheckFailed = true;
        if (_noticedRelease is null)
        {
            _updateNotice.Visible = true;
            _updateNotice.Text = "Updates: check failed";
            _updateNoticeHelp.SetToolTip(_updateNotice,
                explanation + Environment.NewLine +
                "Click to open Updater and retry. No update status was determined.");
        }
        StyleUpdateNotice();
        FitUpdateNotice();
        UpdateNoticeChanged?.Invoke();
    }

    private void StyleUpdateNotice()
    {
        if (!_updateNotice.Visible) return;
        bool available = _noticedRelease is not null;
        _updateNotice.FlatStyle = FlatStyle.Flat;
        _updateNotice.UseVisualStyleBackColor = false;
        _updateNotice.BackColor = available || _noticeCheckFailed
            ? Color.FromArgb(255, 230, 173) : UiStyle.Surface;
        _updateNotice.ForeColor = available || _noticeCheckFailed
            ? Color.FromArgb(89, 55, 9) : UiStyle.Navy;
        _updateNotice.FlatAppearance.BorderColor = available || _noticeCheckFailed
            ? Color.FromArgb(206, 145, 43) : UiStyle.Border;
        _updateNotice.FlatAppearance.MouseOverBackColor = available || _noticeCheckFailed
            ? Color.FromArgb(255, 211, 124) : UiStyle.SoftBlue;
        _updateNotice.Tag = available || _noticeCheckFailed
            ? "UpdateNotice" : "NoIcon";
    }

    private void FitUpdateNotice()
    {
        if (_noticeStatusRow is null || _noticeColumn is null ||
            _noticeFooter is null || _noticeRoot is null ||
            _noticeFooter.IsDisposed || _noticeRoot.IsDisposed) return;

        int height = Math.Max(47,
            TextRenderer.MeasureText("Agpqy", _updateNotice.Font).Height + 19);
        if (_noticeFooter.RowStyles[1].Height < height)
        {
            float increase = height - _noticeFooter.RowStyles[1].Height;
            _noticeFooter.RowStyles[1].Height = height;
            // The footer is now pinned in the shell's SECOND row, not
            // inside the editor viewport. Grow that fixed footer in place.
            if (_noticeRoot.RowStyles.Count > 1)
                _noticeRoot.RowStyles[1].Height += increase;
        }
        if (_updateNotice.Visible)
        {
            int desired = TextRenderer.MeasureText(_updateNotice.Text,
                _updateNotice.Font).Width + 45;
            int available = Math.Max(200, _noticeStatusRow.ClientSize.Width);
            _noticeColumn.Width = Math.Min(Math.Max(270, desired),
                Math.Max(200, (int)(available * .55)));
            StyleUpdateNotice();
        }
        else _noticeColumn.Width = 0;
    }

    private void OpenUniversalUpdaterFromNotice()
    {
        // Always CHECK again inside Updater: the metadata might have changed
        // after the notification. Installing requires separate user approval.
        using var form = new UniversalUpdaterWindow(checkOnOpen: true);
        form.ShowDialog(CampaignDialogOwner);
    }

    internal void ShowSimulatedUpdateNoticeForLayoutTest()
    {
        // UI-only: a fake release notice with no network/API/AD interaction.
        _noticedRelease = null;
        _noticeCheckFailed = true;
        _updateNotice.Visible = true;
        _updateNotice.Text = "Update available: 0.4.0-preview.53";
        _updateNotice.Tag = "UpdateNotice";
        StyleUpdateNotice();
        _updateNoticeHelp.SetToolTip(_updateNotice,
            "Example new release. Click to open Updater.");
        FitUpdateNotice();
    }
}
