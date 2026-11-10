using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using UniversalUpdater;

namespace LockScreenCampaignManager;

/// <summary>
/// Stable, build-generated application version without hard-coded diagnostics
/// version drift. The +commit part of .NET InformationalVersion is not SemVer
/// precedence and does not participate in the release comparison.
/// </summary>
internal static class AppVersion
{
    internal static string Current
    {
        get
        {
            var value = typeof(AppVersion).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion.Split('+', 2)[0] ?? "0.4.0-preview.31";
            return SemanticVersion.TryParse(value, out _) ? value : "0.4.0-preview.31";
        }
    }
}

/// <summary>
/// The canonical sgennadi/UniversalUpdater .NET 10 core is embedded in this EXE.
/// All installations are deliberately manual: replacing an EXE while the
/// unattended gMSA task runs could corrupt the administrator-owned worker.
/// No GitHub token, automatic installer, elevated helper, or background RPC.
/// </summary>
internal sealed class UniversalUpdaterWindow : Form
{
    internal const string Repository = "sgennadi/LockScreenCampaignManager";
    internal const string AssetPrefix = "LockScreenCampaignManager-Update-";
    private readonly ComboBox _channel = new();
    private readonly TextBox _sessionToken = new() { UseSystemPasswordChar = true };
    private readonly TextBox _output = new();
    private readonly Label _state = new();
    private readonly Button _check = new();
    private readonly Button _download = new();
    private readonly Button _install = new();
    private readonly Button _releases = new();
    private readonly Button _backupStatus = new();
    private readonly Button _close = new();
    private UpdateRelease? _available;
    private UpdateOptions? _options;
    private CancellationTokenSource? _operation;
    private string? _stagedPath;
    private StagedPackage? _staged;
    // Operator captions only; release IDs, archives, hashes and session token
    // remain untouched by localization.
    private static string T(string text) => UiLocale.Display(text);

    internal UniversalUpdaterWindow(bool checkOnOpen = false)
    {
        Text = "Updates — LockScreen Campaign Manager";
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(800, 590);
        Size = new Size(950, 705);
        BackColor = UiStyle.Canvas;

        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, Padding = new Padding(18, 16, 18, 16),
            ColumnCount = 1, RowCount = 5, BackColor = UiStyle.Canvas
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 116));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 154));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 106));

        var banner = new TableLayoutPanel {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            BackColor = UiStyle.Navy, Padding = new Padding(19, 12, 19, 10)
        };
        banner.Name = "UpdaterHeader";
        banner.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        banner.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
        var bannerTitle = new Label {
            Name = "UpdaterHeaderTitle",
            Text = "Software updates",
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(0, 5, 0, 6),
            Font = new Font("Segoe UI Semibold", 17f), ForeColor = Color.White,
            AutoEllipsis = false
        };
        var bannerSubtitle = new Label {
            Name = "UpdaterHeaderSubtitle",
            Text = "1  Check for updates     2  Install update",
            Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 10f),
            ForeColor = Color.FromArgb(224, 238, 255), AutoEllipsis = false
        };
        banner.Controls.Add(bannerTitle, 0, 0);
        banner.Controls.Add(bannerSubtitle, 0, 1);
        layout.Controls.Add(banner, 0, 0);

        // Resize the outer banner too; growing only its label rows clips
        // the "Check for updates / Install update" line at high DPI.
        void FitUpdatesHeader() =>
            UiStyle.FitWizardHeader(layout, banner, bannerTitle, bannerSubtitle, 116);
        bannerTitle.FontChanged += (_, _) => FitUpdatesHeader();
        bannerSubtitle.FontChanged += (_, _) => FitUpdatesHeader();

        var config = new TableLayoutPanel {
            Name = "UpdaterConfiguration",
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
            BackColor = UiStyle.Surface, Padding = new Padding(15, 12, 15, 9),
            Margin = new Padding(0, 8, 0, 6)
        };
        config.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        config.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        // Absolute measured rows, rather than 50/50: at 200% a 24px label
        // plus the native ComboBox cannot fit in half of the old 154px area.
        config.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));
        config.RowStyles.Add(new RowStyle(SizeType.Absolute, 70));

        _channel.Name = "UpdaterReleaseChannel";
        _channel.DropDownStyle = ComboBoxStyle.DropDownList;
        _channel.Items.AddRange(new object[] { "Preview + stable", "Stable only" });
        _channel.SelectedIndex = 0;
        _channel.SelectedIndexChanged += (_, _) => {
            _options = null;
            _available = null;
            _stagedPath = null;
            _staged = null;
            _download.Enabled = false;
            _install.Enabled = false;
            _state.Text = T("Channel changed. Select Check for updates.");
        };
        config.Controls.Add(Field("Release channel", _channel), 0, 0);
        config.Controls.Add(Field("Installed version / architecture",
            new TextBox {
                Text = AppVersion.Current + "    |    " +
                    UpdateOptions.CurrentWindowsArchitecture(),
                ReadOnly = true, TabStop = false
            }), 1, 0);

        _sessionToken.MaxLength = 512;
        _sessionToken.AccessibleName = "Optional GitHub read-only session token";
        _sessionToken.TextChanged += (_, _) => {
            if (_operation is not null) return;
            _available = null;
            _options = null;
            _staged = null;
            _stagedPath = null;
            _download.Enabled = false;
            _install.Enabled = false;
            _state.Text = T("Session credentials changed. Check for updates again.");
        };
        config.Controls.Add(Field("GitHub token (optional; never saved)", _sessionToken), 0, 1);
        config.Controls.Add(Field("Repository", new TextBox {
            Text = Repository + "  |  Public releases do not need a token",
            ReadOnly = true, TabStop = false
        }), 1, 1);
        layout.Controls.Add(config, 0, 1);
        void FitUpdateFields()
        {
            if (config.IsDisposed || layout.IsDisposed) return;
            int total = 0;
            config.SuspendLayout();
            try
            {
                for (int row = 0; row < 2; row++)
                {
                    int minimum = 0;
                    for (int col = 0; col < 2; col++)
                    {
                        if (config.GetControlFromPosition(col, row) is not TableLayoutPanel field)
                            continue;
                        if (field.GetControlFromPosition(0, 0) is not Label label ||
                            field.GetControlFromPosition(0, 1) is not Control editor)
                            continue;
                        int captionHeight = TextRenderer.MeasureText("Agpqy", label.Font,
                            new Size(4096, 4096),
                            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Height +
                            label.Padding.Vertical + label.Margin.Vertical + 9;
                        int nativeHeight = editor switch
                        {
                            ComboBox picker => picker.PreferredHeight,
                            TextBox input => input.PreferredSize.Height,
                            _ => editor.PreferredSize.Height
                        };
                        int editorHeight = nativeHeight + editor.Margin.Vertical + 10;
                        field.RowStyles[0].SizeType = SizeType.Absolute;
                        field.RowStyles[0].Height = captionHeight;
                        field.RowStyles[1].SizeType = SizeType.Absolute;
                        field.RowStyles[1].Height = editorHeight;
                        minimum = Math.Max(minimum, captionHeight + editorHeight +
                            field.Padding.Vertical + field.Margin.Vertical + 5);
                    }
                    minimum = Math.Max(70, minimum);
                    config.RowStyles[row].SizeType = SizeType.Absolute;
                    config.RowStyles[row].Height = minimum;
                    total += minimum;
                }
                layout.RowStyles[1].SizeType = SizeType.Absolute;
                layout.RowStyles[1].Height = total + config.Padding.Vertical +
                    config.Margin.Vertical + 8;
            }
            finally { config.ResumeLayout(true); }
        }

        var resultArea = new TableLayoutPanel {
            Dock = DockStyle.Fill, RowCount = 2, ColumnCount = 1,
            BackColor = UiStyle.Surface, Padding = new Padding(12, 8, 12, 12),
            Margin = new Padding(0, 3, 0, 6)
        };
        resultArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
        resultArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        resultArea.Controls.Add(new Label {
            Text = "Update results and verification details",
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI Semibold", 11f),
            ForeColor = UiStyle.Navy, TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        _output.Dock = DockStyle.Fill;
        _output.BackColor = Color.White;
        _output.ForeColor = UiStyle.Foreground;
        _output.BorderStyle = BorderStyle.FixedSingle;
        _output.ReadOnly = true;
        _output.Multiline = true;
        _output.WordWrap = true;
        _output.ScrollBars = ScrollBars.Vertical;
        _output.Font = new Font("Segoe UI", 10f);
        _output.Text = T(
            "Check for updates to compare your installed version with GitHub Releases.\n\n" +
            "1. Check for updates (Stable or Preview).\n" +
            "2. Install: automatically download and verify, then request approval to restart.\n\n" +
            "Updating the portable EXE never modifies GPO or gMSA installations.");
        resultArea.Controls.Add(_output, 0, 1);
        layout.Controls.Add(resultArea, 0, 2);

        var statusPanel = new Panel {
            Dock = DockStyle.Fill, BackColor = UiStyle.SoftBlue,
            Padding = new Padding(13, 5, 13, 5),
            Margin = new Padding(0, 0, 0, 5)
        };
        _state.Dock = DockStyle.Fill;
        _state.AutoEllipsis = true;
        _state.TextAlign = ContentAlignment.MiddleLeft;
        _state.ForeColor = UiStyle.Navy;
        _state.Text = T("Ready. No network request has been made.");
        statusPanel.Controls.Add(_state);
        layout.Controls.Add(statusPanel, 0, 3);

        var buttons = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Padding = new Padding(4, 5, 4, 0)
        };
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        buttons.RowStyles.Add(new RowStyle(SizeType.Percent, 50));

        var primary = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1
        };
        primary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        primary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        Configure(_check, "1  Check", 180);
        Configure(_download, "Download & verify (internal)", 190);
        Configure(_install, "2  Install", 190);
        _check.Tag = _download.Tag = _install.Tag = "NoIcon";
        _check.BackColor = _install.BackColor = UiStyle.Accent;
        _check.ForeColor = _install.ForeColor = Color.White;
        _download.Enabled = false;
        _install.Enabled = false;
        primary.Controls.Add(_check, 0, 0);
        primary.Controls.Add(_install, 1, 0);
        buttons.Controls.Add(primary, 0, 0);

        var secondary = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 4, RowCount = 1
        };
        secondary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        secondary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30));
        secondary.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        secondary.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 1));
        Configure(_releases, "Open releases", 155);
        Configure(_backupStatus, "Backup status", 155);
        Configure(_close, "Close", 125);
        _releases.Tag = _backupStatus.Tag = _close.Tag = "NoIcon";
        secondary.Controls.Add(_releases, 0, 0);
        secondary.Controls.Add(_backupStatus, 1, 0);
        secondary.Controls.Add(_close, 2, 0);
        buttons.Controls.Add(secondary, 0, 1);
        layout.Controls.Add(buttons, 0, 4);
        Controls.Add(layout);

        _check.Click += async (_, _) => await CheckAsync();
        _download.Click += async (_, _) => await DownloadAsync();
        _install.Click += async (_, _) => await InstallSelectedAsync();
        _releases.Click += (_, _) => OpenReleases();
        _backupStatus.Click += async (_, _) => await InspectBackupAsync();
        _close.Click += (_, _) => {
            if (_operation is not null) _operation.Cancel();
            else Close();
        };
        FormClosing += (_, e) => {
            if (_operation is null) return;
            _operation.Cancel();
            e.Cancel = true;
        };
        FormClosed += (_, _) => _sessionToken.Clear();
        UiStyle.Apply(this);
        FitUpdatesHeader();
        FitUpdateFields();
        _channel.FontChanged += (_, _) => FitUpdateFields();
        _sessionToken.FontChanged += (_, _) => FitUpdateFields();
        Shown += (_, _) => FitUpdateFields();
        DpiChanged += (_, _) =>
        {
            if (IsHandleCreated && !IsDisposed)
                BeginInvoke(new Action(() =>
                {
                    FitUpdatesHeader();
                    FitUpdateFields();
                }));
        };
        UiWindowPlacement.Attach(this, "updater");
        if (checkOnOpen)
        {
            // Opening from the update notice performs a new read-only Check.
            // Download, verification and Install still require user action.
            Shown += async (_, _) => await CheckAsync();
        }
    }

    private static TableLayoutPanel Field(string caption, Control control)
    {
        var holder = new TableLayoutPanel {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = new Padding(4, 1, 12, 3)
        };
        holder.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        holder.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        holder.Controls.Add(new Label {
            Text = caption, Dock = DockStyle.Fill, ForeColor = UiStyle.Navy,
            AutoEllipsis = false, TextAlign = ContentAlignment.MiddleLeft
        }, 0, 0);
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(0, 1, 0, 1);
        holder.Controls.Add(control, 0, 1);
        return holder;
    }

    private static void Configure(Button button, string title, int width)
    {
        button.Text = title;
        button.Dock = DockStyle.Fill;
        button.Height = 39;
        button.AutoEllipsis = true;
        button.Margin = new Padding(4, 3, 8, 3);
    }

    internal static UpdateOptions PinnedOptions(UpdateChannel channel)
    {
        string exe = "LockScreenCampaignManager.exe";
        return new UpdateOptions {
            ProductId = "LockScreenCampaignManager",
            Repository = Repository,
            InstalledVersion = AppVersion.Current,
            Architecture = UpdateOptions.CurrentWindowsArchitecture(),
            Channel = channel,
            MaxArchiveBytes = 350L * 1024 * 1024,
            MaxExpandedBytes = 600L * 1024 * 1024,
            Assets = new[] {
                new UpdateAssetRule("win-x64", AssetPrefix + "win-x64.zip",
                    PackageKind.SingleExeZip, exe),
                new UpdateAssetRule("win-x86", AssetPrefix + "win-x86.zip",
                    PackageKind.SingleExeZip, exe)
            }
        };
    }

    private async Task CheckAsync()
    {
        if (_operation is not null) return;
        var options = PinnedOptions(
            _channel.SelectedIndex == 1 ? UpdateChannel.Stable : UpdateChannel.Prerelease);
        options.Validate();
        _available = null;
        _stagedPath = null;
        _staged = null;
        _options = options;
        SetWorking("Checking GitHub Releases...");
        try
        {
            using var client = new GitHubReleaseClient();
            var found = await client.CheckAsync(options, _operation!.Token,
                _sessionToken.Text.Length == 0 ? null : _sessionToken.Text);
            _available = found;
            if (found is null)
            {
                _output.Text = options.Channel == UpdateChannel.Stable
                    ? T("No newer stable release is published. A preview is not a Stable release.") +
                      Environment.NewLine + T("Choose Preview + stable to find development previews.")
                    : T("No newer compatible preview or stable release is available.") +
                      Environment.NewLine + T("The release must include an architecture-specific Update ZIP " +
                      "with a GitHub SHA-256 digest.");
                _state.Text = T("No newer compatible release returned by the selected access mode.");
            }
            else
            {
                _output.Text = T("New release: ") + found.Version +
                    Environment.NewLine + T("Current: ") + options.InstalledVersion +
                    Environment.NewLine + T("Asset: ") + found.Rule.AssetName +
                    Environment.NewLine + T("Archive size: ") + found.SizeBytes.ToString("N0") + T(" bytes") +
                    Environment.NewLine + "SHA-256: " + found.Sha256 +
                    Environment.NewLine + Environment.NewLine +
                    T("Release notes:") + Environment.NewLine +
                    (found.ReleaseNotes.Length > 6000 ? found.ReleaseNotes[..6000] : found.ReleaseNotes);
                _state.Text = T("New release available. Choose Install to download, verify and apply.");
            }
        }
        catch (OperationCanceledException)
        {
            _state.Text = T("Release check canceled.");
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "UniversalUpdater release check");
            _state.Text = T("Could not check GitHub Releases.");
            _output.Text = T("Update check failed: ") +
                UniversalDiagnosticsEmbedded.Clean(ex.Message, 500) +
                Environment.NewLine +
                T("Check internet access and repository visibility in the browser.");
        }
        finally { FinishWorking(); }
    }

    private async Task InstallSelectedAsync()
    {
        if (_operation is not null || _available is null || _options is null) return;
        if (_staged is null) await DownloadAsync();
        if (_operation is null && _staged is not null) InstallAndRestart();
    }

    private async Task DownloadAsync()
    {
        if (_operation is not null || _available is null || _options is null) return;
        var selected = _available;
        var options = _options;
        SetWorking("Downloading and verifying...");
        try
        {
            using var downloader = new UpdateDownloader();
            var progress = new Progress<UpdateProgress>(p =>
                _state.Text = T(p.Phase.ToString()) + ": " +
                    p.ReceivedBytes.ToString("N0") + " / " +
                    (p.TotalBytes ?? 0).ToString("N0") + T(" bytes"));
            StagedPackage staged = await downloader.StageAsync(
                options, selected, progress, _operation!.Token,
                _sessionToken.Text.Length == 0 ? null : _sessionToken.Text);
            _stagedPath = staged.ArchivePath;
            _staged = staged;
            var signature = staged.ExtractedExecutablePath is not null
                ? await Task.Run(() => WindowsAuthenticodeAudit.Inspect(
                    staged.ExtractedExecutablePath))
                : new WindowsAuthenticodeAudit.Result(false, "NOT_AVAILABLE",
                    "No extracted EXE for native Authenticode verification.");
            _output.Text = T("SHA-256 and archive safety verified; package staged.") +
                Environment.NewLine + T("Windows Authenticode: ") +
                T(signature.Trusted ? "TRUSTED" : "NOT VERIFIED") +
                " (" + signature.Code + ")" +
                Environment.NewLine + signature.Explanation +
                Environment.NewLine + T("Archive: ") + staged.ArchivePath +
                Environment.NewLine + T("Archive SHA-256: ") + staged.ArchiveSha256 +
                Environment.NewLine + T("EXE SHA-256: ") + staged.ExtractedExecutableSha256 +
                Environment.NewLine + Environment.NewLine +
                T("Nothing has been installed yet. Release SHA-256 verifies integrity, not publisher identity. " +
                "For a user-profile portable EXE, approve the unsigned publisher warning before installation. " +
                "Administrator-owned and gMSA copies require a separate maintenance window.");
            _state.Text = T(signature.Trusted
                ? "Verified package ready for final installation approval."
                : "Unsigned/untrusted publisher: installation requires explicit operator approval.");
            // InstallSelectedAsync now continues to the explicit final approval.
        }
        catch (OperationCanceledException)
        {
            _state.Text = T("Download canceled.");
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "UniversalUpdater verified staging");
            _state.Text = T("Download/verification failed. No installation occurred.");
            _output.Text = T("Release download or verification failed: ") +
                UniversalDiagnosticsEmbedded.Clean(ex.Message, 500) +
                Environment.NewLine +
                T("For public releases no token is needed; private assets require a scoped read-only token.");
        }
        finally { FinishWorking(); }
    }

    private void InstallAndRestart()
    {
        if (_operation is not null || _staged is null || _options is null) return;
        string exe = Application.ExecutablePath;
        var answer = MessageBox.Show(this,
            T("Install this verified update and restart LockScreen Campaign Manager?") +
            Environment.NewLine + Environment.NewLine +
            T("The current portable EXE will be saved as .bak. The updater waits " +
            "until the manager exits, checks SHA-256, replaces the EXE, runs the " +
            "offline self-test and restores the backup on failure.") +
            Environment.NewLine + Environment.NewLine +
            T("GitHub release hashes do NOT prove publisher identity. These preview builds " +
            "are unsigned. Continue only if you trust the official project release.") +
            Environment.NewLine + Environment.NewLine +
            T("Existing gMSA tasks and administrator-owned installations are NOT updated. " +
            "Save your campaign first; any unsaved changes will be confirmed on closing."),
            T("Install verified portable update?"), MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;
        try
        {
            PortableUpdateCoordinator.Launch(_staged, _options);
            _state.Text = T("Update helper launched. Closing manager for replacement...");
            BeginInvoke(new Action(() => {
                Close();
                Application.Exit();
            }));
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Portable updater launch");
            _state.Text = T("Installation not started: ") +
                UniversalDiagnosticsEmbedded.Clean(ex.Message, 230);
            MessageBox.Show(this,
                T("In-app updates require a writable portable EXE in your user profile. " +
                "Administrator-owned/gMSA installations must be updated separately.") +
                Environment.NewLine + UniversalDiagnosticsEmbedded.Clean(ex.Message, 220),
                T("Update was not installed"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private async Task InspectBackupAsync()
    {
        if (_operation is not null) return;
        SetWorking("Inspecting existing local rollback checkpoint...");
        try
        {
            string current = Application.ExecutablePath;
            string backup = current + ".bak";
            string architecture = UpdateOptions.CurrentWindowsArchitecture();
            if (!File.Exists(backup))
            {
                _output.Text = T("No local .bak checkpoint exists beside this executable.") +
                    Environment.NewLine + T("Backup status is read-only and never runs a downgrade.") +
                    Environment.NewLine + T("The protected gMSA worker EXE has a separate installation path.");
                _state.Text = T("No local rollback checkpoint found.");
                return;
            }
            string digest = await Task.Run(async () => {
                if ((File.GetAttributes(backup) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Rollback checkpoint cannot be a filesystem link.");
                PeSafety.ValidateArchitecture(backup, architecture);
                return await UpdateDownloader.HashFileAsync(backup, _operation!.Token);
            });
            _output.Text = T("A local .bak checkpoint exists for the current EXE.") +
                Environment.NewLine + T("Architecture: ") + architecture +
                Environment.NewLine + "SHA-256: " + digest +
                Environment.NewLine + T("Checkpoint: ") + backup +
                Environment.NewLine + Environment.NewLine +
                T("This is NOT proof of a trusted publisher or a valid rollback. " +
                "Do not replace an EXE used by gMSA while Task Scheduler is running. " +
                "A separate trusted, offline administrator action and application self-test are required.");
            _state.Text = T("Rollback candidate inspected (no changes made).");
        }
        catch (OperationCanceledException)
        {
            _state.Text = T("Rollback inspection canceled.");
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "UniversalUpdater rollback checkpoint inspection");
            _state.Text = T("Rollback checkpoint is not usable.");
            _output.Text = UniversalDiagnosticsEmbedded.Clean(ex.Message, 260);
        }
        finally { FinishWorking(); }
    }

    private void SetWorking(string state)
    {
        _operation = new CancellationTokenSource();
        _state.Text = T(state);
        _check.Enabled = false;
        _download.Enabled = false;
        _install.Enabled = false;
        _channel.Enabled = false;
        _sessionToken.Enabled = false;
        _backupStatus.Enabled = false;
        _close.Text = T("Cancel");
    }

    private void FinishWorking()
    {
        _operation?.Dispose();
        _operation = null;
        _check.Enabled = true;
        _download.Enabled = _available is not null && _options is not null &&
            string.IsNullOrEmpty(_stagedPath);
        _install.Enabled = _available is not null && _options is not null;
        _channel.Enabled = true;
        _sessionToken.Enabled = true;
        _backupStatus.Enabled = true;
        _close.Text = T("Close");
    }

    private static void OpenReleases() =>
        Process.Start(new ProcessStartInfo {
            FileName = "https://github.com/" + Repository + "/releases",
            UseShellExecute = true
        });
}
