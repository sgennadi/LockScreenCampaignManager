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

    internal UniversalUpdaterWindow()
    {
        Text = "UniversalUpdater — LockScreen Campaign Manager";
        Font = new Font("Segoe UI", 9.5f);
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(700, 510);
        Size = new Size(900, 625);

        var layout = new TableLayoutPanel {
            Dock = DockStyle.Fill, Padding = new Padding(16),
            ColumnCount = 1, RowCount = 6, AutoScroll = true
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 68));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 105));

        layout.Controls.Add(new Label {
            Dock = DockStyle.Fill, AutoSize = false,
            Text = "UniversalUpdater 0.1.0-rc.1 — manual release check and verified staging." +
                Environment.NewLine +
                "Operator-approved portable EXE installation, rollback on failure; no automatic GPO/gMSA changes."
        }, 0, 0);
        var channelRow = new FlowLayoutPanel {
            Dock = DockStyle.Fill, WrapContents = false, AutoScroll = true
        };
        channelRow.Controls.Add(new Label {
            Text = "Release channel:", AutoSize = true, Margin = new Padding(4, 11, 12, 0)
        });
        _channel.DropDownStyle = ComboBoxStyle.DropDownList;
        _channel.Width = 180;
        _channel.Items.AddRange(new object[] { "Preview + stable", "Stable only" });
        _channel.SelectedIndex = 0;
        _channel.SelectedIndexChanged += (_, _) => {
            _options = null;
            _available = null;
            _stagedPath = null;
            _staged = null;
            _download.Enabled = false;
            _install.Enabled = false;
            _state.Text = "Channel changed. Select Check for updates.";
        };
        channelRow.Controls.Add(_channel);
        channelRow.Controls.Add(new Label {
            Text = "Version: " + AppVersion.Current + " | " +
                UpdateOptions.CurrentWindowsArchitecture(),
            AutoSize = true, Margin = new Padding(22, 11, 0, 0)
        });
        layout.Controls.Add(channelRow, 0, 1);

        var credentials = new FlowLayoutPanel {
            Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true
        };
        credentials.Controls.Add(new Label {
            Text = "GitHub read-only token (optional):", AutoSize = true,
            Margin = new Padding(2, 9, 10, 0)
        });
        _sessionToken.Width = 275;
        _sessionToken.MaxLength = 512;
        _sessionToken.Margin = new Padding(2, 5, 10, 0);
        _sessionToken.AccessibleName = "Optional GitHub read-only session token";
        _sessionToken.TextChanged += (_, _) => {
            if (_operation is not null) return;
            _available = null;
            _options = null;
            _download.Enabled = false;
            _state.Text = "Session credentials changed. Check for updates again.";
        };
        credentials.Controls.Add(_sessionToken);
        credentials.Controls.Add(new Label {
            Text = "Contents: Read-only. Kept in this window only; never saved.",
            AutoSize = true, ForeColor = UiStyle.Muted,
            Margin = new Padding(2, 9, 0, 0)
        });
        layout.Controls.Add(credentials, 0, 2);

        _output.Dock = DockStyle.Fill;
        _output.ReadOnly = true;
        _output.Multiline = true;
        _output.WordWrap = true;
        _output.ScrollBars = ScrollBars.Vertical;
        _output.Text = "Select Check for updates to query published GitHub Releases." +
            Environment.NewLine + Environment.NewLine +
            "Public GitHub Releases work without a token. For private repositories only, " +
            "an optional read-only token is scoped to api.github.com and never saved." +
            Environment.NewLine + Environment.NewLine +
            "Download & verify works with published assets whose name, SHA-256 digest, " +
            "ZIP structure and EXE architecture match the pinned release profile. " +
            "After verification, use Install & restart for a portable user-profile EXE. " +
            "Admin-owned/gMSA copies cannot be modified by the in-app updater.";
        layout.Controls.Add(_output, 0, 3);
        _state.Dock = DockStyle.Fill;
        _state.AutoEllipsis = true;
        _state.Text = "Idle — no network request until Check for updates.";
        layout.Controls.Add(_state, 0, 4);
        var buttons = new FlowLayoutPanel {
            Dock = DockStyle.Fill, WrapContents = true, AutoScroll = true
        };
        Configure(_check, "Check for updates", 180);
        Configure(_download, "Download & verify", 180);
        Configure(_install, "Install & restart", 170);
        Configure(_releases, "Open releases", 143);
        Configure(_backupStatus, "Backup status", 151);
        Configure(_close, "Close", 110);
        _download.Enabled = false;
        _install.Enabled = false;
        buttons.Controls.AddRange(new Control[] {
            _check, _download, _install, _backupStatus, _releases, _close
        });
        layout.Controls.Add(buttons, 0, 5);
        Controls.Add(layout);

        _check.Click += async (_, _) => await CheckAsync();
        _download.Click += async (_, _) => await DownloadAsync();
        _install.Click += (_, _) => InstallAndRestart();
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
        UiWindowPlacement.Attach(this, "updater");
    }

    private static void Configure(Button button, string title, int width)
    {
        button.Text = title;
        button.Width = width;
        button.Height = 35;
        button.AutoEllipsis = true;
        button.Margin = new Padding(2, 3, 8, 0);
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
                    ? "No newer stable release is published. A preview is not a Stable release." +
                      Environment.NewLine + "Choose Preview + stable to find development previews."
                    : "No newer compatible preview or stable release is available." +
                      Environment.NewLine + "The release must include an architecture-specific Update ZIP " +
                      "with a GitHub SHA-256 digest.";
                _state.Text = "No newer compatible release returned by the selected access mode.";
            }
            else
            {
                _output.Text = "New release: " + found.Version +
                    Environment.NewLine + "Current: " + options.InstalledVersion +
                    Environment.NewLine + "Asset: " + found.Rule.AssetName +
                    Environment.NewLine + "Archive size: " + found.SizeBytes.ToString("N0") + " bytes" +
                    Environment.NewLine + "SHA-256: " + found.Sha256 +
                    Environment.NewLine + Environment.NewLine +
                    "Release notes:" + Environment.NewLine +
                    (found.ReleaseNotes.Length > 6000 ? found.ReleaseNotes[..6000] : found.ReleaseNotes);
                _state.Text = "New release available. Download and verify before installing.";
            }
        }
        catch (OperationCanceledException)
        {
            _state.Text = "Release check canceled.";
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "UniversalUpdater release check");
            _state.Text = "Could not check GitHub Releases.";
            _output.Text = "Update check failed: " +
                UniversalDiagnosticsEmbedded.Clean(ex.Message, 500) +
                Environment.NewLine +
                "Check internet access and repository visibility in the browser.";
        }
        finally { FinishWorking(); }
    }

    private async Task DownloadAsync()
    {
        if (_operation is not null || _available is null || _options is null) return;
        if (MessageBox.Show(this,
                "Download and verify this release in a per-user staging folder?" +
                Environment.NewLine + "The installed manager and gMSA worker are NOT changed.",
                "UniversalUpdater — Confirm download", MessageBoxButtons.YesNo,
                MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;

        var selected = _available;
        var options = _options;
        SetWorking("Downloading and verifying...");
        try
        {
            using var downloader = new UpdateDownloader();
            var progress = new Progress<UpdateProgress>(p =>
                _state.Text = p.Phase + ": " +
                    p.ReceivedBytes.ToString("N0") + " / " +
                    (p.TotalBytes ?? 0).ToString("N0") + " bytes");
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
            _output.Text = "SHA-256 and archive safety verified; package staged." +
                Environment.NewLine + "Windows Authenticode: " +
                (signature.Trusted ? "TRUSTED" : "NOT VERIFIED") +
                " (" + signature.Code + ")" +
                Environment.NewLine + signature.Explanation +
                Environment.NewLine + "Archive: " + staged.ArchivePath +
                Environment.NewLine + "Archive SHA-256: " + staged.ArchiveSha256 +
                Environment.NewLine + "EXE SHA-256: " + staged.ExtractedExecutableSha256 +
                Environment.NewLine + Environment.NewLine +
                "Nothing has been installed yet. Release SHA-256 verifies integrity, not publisher identity. " +
                "For a user-profile portable EXE, click Install & restart and approve the unsigned publisher warning. " +
                "Administrator-owned and gMSA copies require a separate maintenance window.";
            _state.Text = signature.Trusted
                ? "Verified package ready: choose Install & restart."
                : "Unsigned/untrusted publisher: installation requires explicit operator approval.";
            MessageBox.Show(this, "Verified update package is ready. Choose Install & restart to apply it." +
                Environment.NewLine + "Authenticode: " +
                (signature.Trusted ? "trusted" : "not verified") +
                ". No update is applied without your approval.",
                "UniversalUpdater", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (OperationCanceledException)
        {
            _state.Text = "Download canceled.";
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "UniversalUpdater verified staging");
            _state.Text = "Download/verification failed. No installation occurred.";
            _output.Text = "Release download or verification failed: " +
                UniversalDiagnosticsEmbedded.Clean(ex.Message, 500) +
                Environment.NewLine +
                "For public releases no token is needed; private assets require a scoped read-only token.";
        }
        finally { FinishWorking(); }
    }

    private void InstallAndRestart()
    {
        if (_operation is not null || _staged is null || _options is null) return;
        string exe = Application.ExecutablePath;
        var answer = MessageBox.Show(this,
            "Install this verified update and restart LockScreen Campaign Manager?" +
            Environment.NewLine + Environment.NewLine +
            "The current portable EXE will be saved as .bak. The updater waits " +
            "until the manager exits, checks SHA-256, replaces the EXE, runs the " +
            "offline self-test and restores the backup on failure." +
            Environment.NewLine + Environment.NewLine +
            "GitHub release hashes do NOT prove publisher identity. These preview builds " +
            "are unsigned. Continue only if you trust the official project release." +
            Environment.NewLine + Environment.NewLine +
            "Existing gMSA tasks and administrator-owned installations are NOT updated. " +
            "Save your campaign first; any unsaved changes will be confirmed on closing.",
            "Install verified portable update?", MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;
        try
        {
            PortableUpdateCoordinator.Launch(_staged, _options);
            _state.Text = "Update helper launched. Closing manager for replacement...";
            BeginInvoke(new Action(() => {
                Close();
                Application.Exit();
            }));
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "Portable updater launch");
            _state.Text = "Installation not started: " +
                UniversalDiagnosticsEmbedded.Clean(ex.Message, 230);
            MessageBox.Show(this,
                "In-app updates require a writable portable EXE in your user profile. " +
                "Administrator-owned/gMSA installations must be updated separately." +
                Environment.NewLine + UniversalDiagnosticsEmbedded.Clean(ex.Message, 220),
                "Update was not installed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
                _output.Text = "No local .bak checkpoint exists beside this executable." +
                    Environment.NewLine + "Backup status is read-only and never runs a downgrade." +
                    Environment.NewLine + "The protected gMSA worker EXE has a separate installation path.";
                _state.Text = "No local rollback checkpoint found.";
                return;
            }
            string digest = await Task.Run(async () => {
                if ((File.GetAttributes(backup) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Rollback checkpoint cannot be a filesystem link.");
                PeSafety.ValidateArchitecture(backup, architecture);
                return await UpdateDownloader.HashFileAsync(backup, _operation!.Token);
            });
            _output.Text = "A local .bak checkpoint exists for the current EXE." +
                Environment.NewLine + "Architecture: " + architecture +
                Environment.NewLine + "SHA-256: " + digest +
                Environment.NewLine + "Checkpoint: " + backup +
                Environment.NewLine + Environment.NewLine +
                "This is NOT proof of a trusted publisher or a valid rollback. " +
                "Do not replace an EXE used by gMSA while Task Scheduler is running. " +
                "A separate trusted, offline administrator action and application self-test are required.";
            _state.Text = "Rollback candidate inspected (no changes made).";
        }
        catch (OperationCanceledException)
        {
            _state.Text = "Rollback inspection canceled.";
        }
        catch (Exception ex)
        {
            AppLog.Write(ex, "UniversalUpdater rollback checkpoint inspection");
            _state.Text = "Rollback checkpoint is not usable.";
            _output.Text = UniversalDiagnosticsEmbedded.Clean(ex.Message, 260);
        }
        finally { FinishWorking(); }
    }

    private void SetWorking(string state)
    {
        _operation = new CancellationTokenSource();
        _state.Text = state;
        _check.Enabled = false;
        _download.Enabled = false;
        _install.Enabled = false;
        _channel.Enabled = false;
        _sessionToken.Enabled = false;
        _backupStatus.Enabled = false;
        _close.Text = "Cancel";
    }

    private void FinishWorking()
    {
        _operation?.Dispose();
        _operation = null;
        _check.Enabled = true;
        _download.Enabled = _available is not null && _options is not null &&
            string.IsNullOrEmpty(_stagedPath);
        _install.Enabled = _staged is not null && _options is not null;
        _channel.Enabled = true;
        _sessionToken.Enabled = true;
        _backupStatus.Enabled = true;
        _close.Text = "Close";
    }

    private static void OpenReleases() =>
        Process.Start(new ProcessStartInfo {
            FileName = "https://github.com/" + Repository + "/releases",
            UseShellExecute = true
        });
}
