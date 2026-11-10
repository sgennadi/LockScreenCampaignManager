using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

public sealed partial class MainForm
{
    private CampaignWizardDialog? _activeCampaignWizard;
    // CI-only readback for an unsaved, local wizard time-zone change.
    internal string CampaignTimeZoneForSmokeTest => _project.TimeZoneId;
    internal CampaignContentSource CampaignSourceForSmokeTest => _project.ContentSource;
    internal string CampaignShareRootForSmokeTest => _project.ContentShareRoot;


    // Save is optional for the guided campaign flow. The GPO Deployment
    // wizard already exports the validated in-memory campaign independently.
    internal static bool ShouldSaveCampaignProject(bool saved, bool skipSaving) =>
        !saved && !skipSaving;

    // All native file pickers and existing AD/schedule/deployment dialogs
    // are owned by the visible wizard rather than the disabled editor below.
    private IWin32Window CampaignDialogOwner =>
        _activeCampaignWizard is { IsDisposed: false } wizard ? wizard : this;

    private void StartGuidedCampaignWizard()
    {
        if (IsDisposed || Disposing) return;
        if (_activeCampaignWizard is { IsDisposed: false } existing)
        {
            existing.Activate();
            return;
        }
        _workflowStep = 0;
        // Wizard mode shows a dedicated landing behind the modal dialog.
        // The Advanced editor and its preview remain hidden.
        SetWorkflowMode(false);
        using var wizard = new CampaignWizardDialog(this);
        _activeCampaignWizard = wizard;
        try
        {
            wizard.ShowDialog(this);
        }
        finally
        {
            _activeCampaignWizard = null;
            // Once the modal wizard closes (Close Wizard, title-bar X, ESC,
            // or Finish), reveal the editable Advanced workspace instead
            // of leaving the user on the otherwise empty Wizard landing.
            if (!IsDisposed && !Disposing)
            {
                SetWorkflowMode(true);
                _status.Text = "Campaign Wizard closed. Advanced editor is ready.";
            }
        }
    }

    // Allows offline UI smoke tests to validate all seven wizard pages
    // without ShowDialog, LDAP access or filesystem changes.
    internal Form CreateCampaignWizardForSmokeTest(int page = 0, string? imagePath = null)
    {
        if (!string.IsNullOrWhiteSpace(imagePath))
            _project.ImagePaths.Add(imagePath);
        var wizard = new CampaignWizardDialog(this);
        if (page == -1) wizard.ExerciseScreenSaverSkipForSmokeTest();
        else wizard.ShowPageForSmokeTest(page);
        return wizard;
    }

    private sealed partial class CampaignWizardDialog : Form
    {
        private static string T(string message) =>
            UiLocale.Translate(message, UiStyle.Preferences.Language);

        private static readonly string[] StepNames =
        [
            "1  AD connection", "2  Campaign type", "3  Images",
            "4  Arrange", "5  Schedule", "6  Save / Skip", "7  Deploy"
        ];
        private readonly MainForm _owner;
        private readonly TableLayoutPanel _root = new();
        private readonly TableLayoutPanel _footer = new();
        private readonly TableLayoutPanel _header = new();
        private readonly Label _heading = new();
        private readonly Label _subheading = new();
        private readonly Label[] _rail = new Label[7];
        private readonly Panel _host = new();
        private readonly Button _back = new();
        private readonly Button _next = new();
        private readonly Button _close = new();
        private readonly Button _updateAvailable = new();
        private readonly ToolTip _updateHelp = new() { AutoPopDelay = 20000 };
        private readonly Label _footnote = new();
        private ListBox? _images;
        private PictureBox? _wizardImagePreview;
        private Label? _wizardImageName;
        private Label? _wizardPreviewPlaceholder;
        private RadioButton? _connectChoice;
        private RadioButton? _offlineChoice;
        private RadioButton? _deployChoice;
        private int _step;
        private bool _saved;
        // A previously saved JSON project may become stale if deployment options change.
        private bool _storageModifiedAfterSave;
        private bool _skipProjectSave;
        private bool _editingPage;
        private bool _everConfiguredSchedule;

        internal CampaignWizardDialog(MainForm owner)
        {
            _owner = owner;
            Text = "LockScreen Campaign Manager " + AppVersion.Current + " - Campaign Wizard";
            Font = new Font("Segoe UI", 10f);
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.Dpi;
            Size = new Size(1120, 805);
            MinimumSize = new Size(950, 690);
            BackColor = UiStyle.Canvas;
            BuildLayout();
            _owner.UpdateNoticeChanged += UpdateAvailableNotice;
            Disposed += (_, _) => _owner.UpdateNoticeChanged -= UpdateAvailableNotice;
            UiStyle.Apply(this);
            UpdateAvailableNotice();
            FitHeader();
            DpiChanged += (_, _) => BeginInvoke(new Action(() =>
            {
                ConstrainToWorkingArea();
                FitHeader();
                FitFooter();
            }));
            UiWindowPlacement.Attach(this, "campaign-setup-wizard");
            Shown += (_, _) =>
            {
                // Runs after window-position restoration; do not allow a
                // remembered large window to hide the bottom action bar.
                ConstrainToWorkingArea();
                FitHeader();
                RenderPage();
                FitFooter();
            };
        }

        private void BuildLayout()
        {
            _root.Dock = DockStyle.Fill;
            _root.ColumnCount = 2;
            _root.RowCount = 2;
            _root.Padding = new Padding(14, 12, 14, 12);
            _root.BackColor = UiStyle.Canvas;
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 215));
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            Controls.Add(_root);

            _header.Dock = DockStyle.Fill;
            _header.RowCount = 2;
            _header.ColumnCount = 2;
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 205));
            _header.Padding = new Padding(22, 13, 18, 10);
            _header.BackColor = UiStyle.Navy;
            _header.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            _header.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));

            _heading.Name = "CampaignWizardHeaderTitle";
            _heading.Text = "Campaign Wizard";
            _heading.Dock = DockStyle.Fill;
            _heading.TextAlign = ContentAlignment.MiddleLeft;
            _heading.ForeColor = Color.White;
            _heading.Font = new Font("Segoe UI Semibold", 17f, FontStyle.Bold);
            _heading.AutoEllipsis = true;
            _subheading.Name = "CampaignWizardHeaderSubtitle";
            _subheading.Dock = DockStyle.Fill;
            _subheading.Text = "Step 1 of 7  -  Choose how to connect to Active Directory";
            _subheading.TextAlign = ContentAlignment.MiddleLeft;
            _subheading.ForeColor = Color.FromArgb(226, 238, 255);
            _subheading.Font = new Font("Segoe UI", 10f);
            _subheading.AutoEllipsis = true;
            _heading.FontChanged += (_, _) => FitHeader();
            _subheading.FontChanged += (_, _) => FitHeader();
            _header.Controls.Add(_heading, 0, 0);
            _header.Controls.Add(new Label {
                Name = "CampaignWizardVersion",
                Text = "Version " + AppVersion.Current,
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleRight,
                ForeColor = Color.FromArgb(223, 238, 255),
                Font = new Font("Segoe UI Semibold", 10f),
                AutoEllipsis = true
            }, 1, 0);
            _header.Controls.Add(_subheading, 0, 1);
            _header.SetColumnSpan(_subheading, 2);
            _root.Controls.Add(_header, 0, 0);
            _root.SetColumnSpan(_header, 2);

            var rail = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, AutoScroll = true,
                ColumnCount = 1, RowCount = 8,
                BackColor = UiStyle.SoftBlue,
                Padding = new Padding(9, 16, 9, 10),
                Margin = new Padding(0, 0, 7, 0)
            };
            for (int i = 0; i < 7; i++)
            {
                rail.RowStyles.Add(new RowStyle(SizeType.Absolute, 59));
                var label = new Label
                {
                    Text = StepNames[i], Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleLeft,
                    Font = new Font("Segoe UI Semibold", 10f),
                    AutoEllipsis = false,
                    Margin = new Padding(3, 3, 2, 4),
                    Padding = new Padding(11, 4, 3, 4)
                };
                _rail[i] = label;
                rail.Controls.Add(label, 0, i);
            }
            rail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            rail.Controls.Add(new Label
            {
                Text = "Your edits stay local until you explicitly deploy a GPO.",
                Dock = DockStyle.Fill, ForeColor = UiStyle.Muted,
                Font = new Font("Segoe UI", 9f),
                Padding = new Padding(9, 12, 4, 4),
                AutoEllipsis = false
            }, 0, 7);
            _root.Controls.Add(rail, 0, 1);

            _host.Dock = DockStyle.Fill;
            _host.AutoScroll = true;
            _host.Padding = new Padding(18, 16, 18, 10);
            _host.BackColor = UiStyle.Surface;
            _root.Controls.Add(_host, 1, 1);
            _host.ClientSizeChanged += (_, _) => ResizePageToViewport();

            // Footer is a direct, bottom-docked FORM child, not a row inside
            // the scrollable main layout. This keeps Back/Next/Close visible
            // even when a high-DPI page exceeds available vertical space.
            var footer = _footer;
            footer.Name = "CampaignWizardFooter";
            footer.Dock = DockStyle.Bottom;
            footer.Height = 82;
            footer.MinimumSize = new Size(0, 62);
            footer.ColumnCount = 5;
            footer.RowCount = 1;
            footer.Padding = new Padding(12, 9, 12, 8);
            footer.Margin = Padding.Empty;
            footer.BackColor = UiStyle.Canvas;
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 0));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 125));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 175));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
            _footnote.Dock = DockStyle.Fill;
            _footnote.ForeColor = UiStyle.Muted;
            _footnote.TextAlign = ContentAlignment.MiddleLeft;
            _footnote.AutoEllipsis = true;
            footer.Controls.Add(_footnote, 0, 0);

            _updateAvailable.Name = "WizardUpdateNoticeButton";
            _updateAvailable.Text = "Check updates";
            _updateAvailable.Dock = DockStyle.Fill;
            _updateAvailable.Tag = "NoIcon";
            _updateAvailable.Visible = true;
            _updateAvailable.AutoEllipsis = true;
            _updateAvailable.Margin = new Padding(4, 2, 4, 2);
            _updateAvailable.Click += (_, _) => _owner.OpenUniversalUpdaterFromNotice();
            footer.Controls.Add(_updateAvailable, 1, 0);

            _back.Text = "Back";
            _back.Tag = "NoIcon";
            _back.Dock = DockStyle.Fill;
            _back.Margin = new Padding(4, 2, 4, 2);
            _back.Click += (_, _) => { _step = _step == 5 && !NeedsLockImages ? 1 : Math.Max(0, _step - 1); RenderPage(); };
            footer.Controls.Add(_back, 2, 0);

            _next.Name = "CampaignWizardContinueAction";
            _next.Text = "Next";
            // The decorative icon consumed the available width and clipped
            // "Continue" at higher DPI. The primary action stays blue.
            _next.Tag = "NoIcon";
            _next.ForeColor = Color.White;
            _next.BackColor = UiStyle.Accent;
            _next.AutoEllipsis = false;
            _next.Dock = DockStyle.Fill;
            _next.Margin = new Padding(4, 2, 4, 2);
            _next.Click += (_, _) => NextStep();
            footer.Controls.Add(_next, 3, 0);

            _close.Text = "Close Wizard";
            _close.Tag = "NoIcon";
            _close.Dock = DockStyle.Fill;
            _close.Margin = new Padding(4, 2, 4, 2);
            _close.Click += (_, _) => Close();
            footer.Controls.Add(_close, 4, 0);
            Controls.Add(footer);
            footer.ClientSizeChanged += (_, _) => FitFooter();
            _back.FontChanged += (_, _) => FitFooter();
            _next.FontChanged += (_, _) => FitFooter();
            _close.FontChanged += (_, _) => FitFooter();
            _updateAvailable.FontChanged += (_, _) => FitFooter();
            AcceptButton = _next;
            CancelButton = _close;
            RenderPage();
        }

        private void UpdateAvailableNotice()
        {
            if (IsDisposed) return;
            string? version = _owner.AvailableUpdateVersion;
            // Keep Check updates visible even if no newer release is known.
            // An unavailable API is NOT the same as an up-to-date install.
            _updateAvailable.Visible = true;
            _updateAvailable.Text = version is not null
                ? "Update " + version : _owner.UpdateNoticeCaption;
            if (_owner.UpdateNoticeWarning)
            {
                _updateAvailable.Tag = "UpdateNotice";
                UiStyle.DecorateForUpdateNotice(_updateAvailable);
            }
            else
            {
                _updateAvailable.Tag = "NoIcon";
                UiStyle.DecorateForWizard(_updateAvailable, false);
            }
            _updateHelp.SetToolTip(_updateAvailable,
                _owner.AvailableUpdateSummary.Length > 0
                    ? _owner.AvailableUpdateSummary
                    : "Check GitHub Releases in Updater (Check -> Install).");
            FitFooter();
        }

        internal void ShowPageForSmokeTest(int page)
        {
            _step = Math.Clamp(page, 0, 6);
            RenderPage();
        }

        private void FitHeader()
        {
            if (IsDisposed || _root.IsDisposed || _header.IsDisposed) return;
            UiStyle.FitWizardHeader(_root, _header, _heading, _subheading, 120);
        }

        private void ConstrainToWorkingArea()
        {
            if (IsDisposed || WindowState == FormWindowState.Minimized) return;
            Rectangle area = Screen.FromControl(this).WorkingArea;
            if (area.Width < 300 || area.Height < 240) return;
            // Even on a laptop with 150-200% scaling, the window MUST fit
            // the usable desktop, including the fixed navigation footer.
            MinimumSize = new Size(Math.Min(950, area.Width),
                Math.Min(690, area.Height));
            if (WindowState != FormWindowState.Normal) return;
            Rectangle fitted = UiWindowPlacement.ClampToScreens(
                Bounds, MinimumSize, new[] { area });
            if (Bounds != fitted) Bounds = fitted;
        }

        private void ResizePageToViewport()
        {
            if (_host.IsDisposed) return;
            if (_host.Controls.OfType<TableLayoutPanel>().FirstOrDefault() is { } page
                && !page.IsDisposed)
                page.Height = Math.Max(_step is 2 or 3 ? 720 : 555,
                    _host.ClientSize.Height - _host.Padding.Vertical);
        }

        private void FitFooter()
        {
            if (IsDisposed || _footer.IsDisposed || _footer.ColumnStyles.Count != 5)
                return;
            int available = Math.Max(0, _footer.ClientSize.Width - _footer.Padding.Horizontal);
            if (available < 200) return; // initial WinForms layout is not sized yet

            int textHeight = new[] { _back, _next, _close, _updateAvailable }
                .Max(x => TextRenderer.MeasureText("Agpqy", x.Font).Height);
            int height = Math.Clamp(textHeight + _footer.Padding.Vertical + 19, 68, 118);
            if (_footer.Height != height) _footer.Height = height;

            // Constrain all fixed columns by the actual viewport width;
            // the status note yields space first, never the navigation.
            int back = Math.Clamp(RequiredWidth(_back, 28), 94, 160);
            int next = Math.Clamp(RequiredWidth(_next, 36), 125, 360);
            int close = Math.Clamp(RequiredWidth(_close, 28), 115, 185);
            int update = _updateAvailable.Visible
                ? Math.Clamp(RequiredWidth(_updateAvailable, 28), 130, 245) : 0;
            int total = back + next + close + update;
            if (total > available)
            {
                double scale = (double)available / total;
                back = Math.Max(62, (int)Math.Floor(back * scale));
                next = Math.Max(76, (int)Math.Floor(next * scale));
                close = Math.Max(75, (int)Math.Floor(close * scale));
                update = update == 0 ? 0 : Math.Max(75, (int)Math.Floor(update * scale));
            }
            _footer.ColumnStyles[1].Width = update;
            _footer.ColumnStyles[2].Width = back;
            _footer.ColumnStyles[3].Width = next;
            _footer.ColumnStyles[4].Width = close;
            _footnote.Visible = available - back - next - close - update >= 115;
        }

        private static int RequiredWidth(Button button, int padding) =>
            TextRenderer.MeasureText(button.Text, button.Font).Width +
            padding + button.Margin.Horizontal;

        private void RenderPage(int selected = 0)
        {
            if (_editingPage || IsDisposed) return;
            _editingPage = true;
            try
            {
                // Release the previous page's preview bitmap before re-rendering.
                if (_wizardImagePreview is { IsDisposed: false } oldPreview)
                {
                    var image = oldPreview.Image;
                    oldPreview.Image = null;
                    image?.Dispose();
                }
                _wizardImagePreview = null;
                _wizardImageName = null;
                _wizardPreviewPlaceholder = null;
                _images = null;
                _connectChoice = null;
                _offlineChoice = null;
                _deployChoice = null;
                _host.SuspendLayout();
                foreach (Control old in _host.Controls.Cast<Control>().ToArray())
                {
                    _host.Controls.Remove(old);
                    old.Dispose();
                }
                _subheading.Text = T("Step " + (_step + 1) + " of 7  -  " + StepNames[_step][3..]);
                for (int i = 0; i < _rail.Length; i++)
                {
                    bool skippedForSaver = HasChosenCampaignType && !NeedsLockImages && i is >= 2 and <= 4;
                    _rail[i].Text = T(skippedForSaver ? StepNames[i] + " (skip)" : StepNames[i]);
                    _rail[i].BackColor = i == _step ? UiStyle.Accent : UiStyle.SoftBlue;
                    _rail[i].ForeColor = i == _step ? Color.White :
                        skippedForSaver ? UiStyle.Muted :
                        i < _step ? UiStyle.Green : UiStyle.Navy;
                }

                bool imagePage = _step is 2 or 3;
                var page = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    Height = Math.Max(imagePage ? 720 : 555,
                        _host.ClientSize.Height - _host.Padding.Vertical),
                    ColumnCount = 1, RowCount = 5,
                    Padding = new Padding(10, 7, 10, 9),
                    BackColor = UiStyle.Surface
                };
                // Images/Arrange dedicate the majority of the viewport to
                // the actual image. The old fixed headers consumed almost
                // the entire page at 125-200% DPI and collapsed PictureBox.
                page.RowStyles.Add(new RowStyle(SizeType.Absolute, imagePage ? 60 : 85));
                page.RowStyles.Add(new RowStyle(SizeType.Absolute, imagePage ? 67 : 118));
                page.RowStyles.Add(new RowStyle(SizeType.Absolute, imagePage ? 66 : 83));
                page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                page.RowStyles.Add(new RowStyle(SizeType.Absolute, imagePage ? 46 : 72));
                _host.Controls.Add(page);

                string[] titles =
                [
                    "Connect to Active Directory",
                    "Choose campaign type",
                    "Choose your campaign images",
                    "Arrange the image order",
                    "Choose days and times",
                    "Save or skip saving",
                    "Deploy or finish offline"
                ];
                string[] descriptions =
                [
                    "Choose a Windows AD connection or prepare the campaign offline. No AD or GPO changes are made during sign-in.",
                    "Choose Lock Screen, Screen Saver or Both before images. This determines which pages are needed.",
                    "Select multiple image files or import a complete folder. You can add more images before continuing.",
                    "Place images in the desired order. The first image is assigned to the first scheduled change.",
                    "Set the start date, weekdays, exact dates and change time. Review the schedule before saving.",
                    "Save a project for reopening later, or skip saving and continue directly to deployment. No AD changes here.",
                    "Deploy using the AD/GPO Wizard, or finish offline and deploy later. No automatic GPO changes."
                ];
                page.Controls.Add(new Label
                {
                    Text = titles[_step], Dock = DockStyle.Fill,
                    ForeColor = UiStyle.Navy,
                    Font = new Font("Segoe UI Semibold", 15f, FontStyle.Bold),
                    TextAlign = ContentAlignment.MiddleLeft,
                    AutoEllipsis = true
                }, 0, 0);
                page.Controls.Add(new Label
                {
                    Text = descriptions[_step], Dock = DockStyle.Fill,
                    ForeColor = UiStyle.Foreground,
                    TextAlign = ContentAlignment.MiddleLeft,
                    AutoEllipsis = false
                }, 0, 1);
                var actions = new FlowLayoutPanel
                {
                    Dock = DockStyle.Fill, AutoScroll = true,
                    WrapContents = false, FlowDirection = FlowDirection.LeftToRight,
                    Padding = new Padding(0, 6, 0, 5)
                };
                page.Controls.Add(actions, 0, 2);
                var body = new Panel
                {
                    Dock = DockStyle.Fill, BackColor = UiStyle.SoftBlue,
                    Padding = new Padding(14), AutoScroll = true
                };
                page.Controls.Add(body, 0, 3);
                var status = new Label
                {
                    Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                    ForeColor = UiStyle.Navy,
                    Font = new Font("Segoe UI", 9.5f),
                    Padding = new Padding(5, 7, 5, 9),
                    AutoEllipsis = false,
                    Name = "CampaignWizardBottomStatus"
                };
                page.Controls.Add(status, 0, 4);

                switch (_step)
                {
                    case 0:
                        RenderConnection(actions, body, status);
                        break;
                    case 1:
                        RenderCampaignType(actions, body, status);
                        break;
                    case 2:
                        RenderImages(actions, body, status, false, selected);
                        break;
                    case 3:
                        RenderImages(actions, body, status, true, selected);
                        break;
                    case 4:
                        RenderSchedule(actions, body, status);
                        break;
                    case 5:
                        RenderSave(actions, body, status);
                        break;
                    case 6:
                        RenderDeploy(actions, body, status);
                        break;
                }
                _back.Enabled = _step > 0;
                _next.Text = T(_step == 6 && _deployChoice?.Checked == true
                    ? "Deploy GPO..." : _step == 6 ? "Finish" :
                    _step == 5 && _skipProjectSave ? "Continue (no save)" :
                    _step == 5 && !_saved ? "Save & Next" :
                    _step == 0 ? "Continue" : "Next");
                _next.Enabled = _step switch
                {
                    1 => HasChosenCampaignType,
                    0 or 6 => true,
                    _ => !NeedsLockImages || _owner._project.ImagePaths.Count > 0
                };
                _footnote.Text = T(_step == 0 ? "AD sign-in is optional" :
                    !HasChosenCampaignType ? "Choose a campaign type first" :
                    !NeedsLockImages ? "Screen Saver: images not needed" :
                    _step >= 5 && _skipProjectSave && !_saved
                        ? "Local project not saved"
                        : _owner._project.ImagePaths.Count + " image(s) selected");
                _host.ResumeLayout(true);
                UiStyle.Apply(this);
                UpdateAvailableNotice();
                FitHeader();
                FitFooter();
            }
            finally { _editingPage = false; }
        }

        private static Button Action(FlowLayoutPanel host, string caption,
            Action click, int width = 205, bool primary = false)
        {
            var button = new Button
            {
                Text = T(caption), Width = width, Height = 43,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowOnly,
                MinimumSize = new Size(width, 43),
                Margin = new Padding(0, 0, 10, 0),
                Tag = primary ? "PrimaryAction" : "NoIcon",
                Cursor = Cursors.Hand
            };
            button.Click += (_, _) => click();
            host.Controls.Add(button);
            UiStyle.DecorateForWizard(button, primary);
            // Keep the guided controls text-only. Some default glyphs collide
            // with captions at high DPI in fixed-width wizard action strips.
            button.Image = null;
            button.Tag = "NoIcon";
            return button;
        }

        private static Label Information(string text, float fontSize = 11f) => new()
        {
            Text = T(text), Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", fontSize),
            ForeColor = UiStyle.Navy,
            Padding = new Padding(12, 9, 12, 9)
        };

        private void RenderConnection(FlowLayoutPanel actions, Panel body, Label status)
        {
            Action(actions, "AD Connection Wizard...", () =>
            {
                _connectChoice!.Checked = true;
                _owner.ConnectAndReadAd();
                if (_owner.AdReady) { _step = 1; RenderPage(); }
                else RenderPage();
            }, 250, true);
            Action(actions, "Continue offline", () =>
            {
                _offlineChoice!.Checked = true;
                _step = 1;
                RenderPage();
            }, 190);
            var options = new TableLayoutPanel
            {
                Dock = DockStyle.Top, Height = 160,
                RowCount = 3, ColumnCount = 1,
                Padding = new Padding(10)
            };
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
            options.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _connectChoice = new RadioButton
            {
                Text = "Connect with current Windows account or another AD account",
                AutoSize = false, Dock = DockStyle.Fill, Checked = true,
                Font = new Font("Segoe UI Semibold", 10.5f)
            };
            _offlineChoice = new RadioButton
            {
                Text = "Work offline - connect only if you decide to deploy",
                AutoSize = false, Dock = DockStyle.Fill
            };
            options.Controls.Add(_connectChoice, 0, 0);
            options.Controls.Add(_offlineChoice, 0, 1);
            body.Controls.Add(options);
            status.Text = _owner.AdReady
                ? T("Connected to AD: ") + _owner._adSnapshot!.Domain + T(". You may continue.")
                : T("No AD session yet. Choose the connection wizard or offline mode.");
        }

        private void RenderImages(FlowLayoutPanel actions, Panel body, Label status,
            bool arrangement, int selected)
        {
            if (!arrangement)
            {
                Action(actions, "Add image files...", () =>
                {
                    _owner.AddImages(); _saved = false; RenderPage();
                }, 185, true);
                Action(actions, "Choose image folder...", () =>
                {
                    _owner.AddImagesFromFolder(); _saved = false; RenderPage();
                }, 220);
                Action(actions, "Remove selected", () => RemoveSelectedImage(), 170);
            }
            else
            {
                Action(actions, "Move up", () => MoveSelectedImage(-1), 130);
                Action(actions, "Move down", () => MoveSelectedImage(1), 140);
                Action(actions, "Sort by filename", SortImages, 190);
                Action(actions, "Auto match names", AutoMatchImages, 190);
            }
            // A Wizard page is self-contained: list, selection and live
            // image preview are on the SAME page, not in Advanced behind it.
            // Do not auto-scroll this docked body: it can collapse a nested
            // percent-sized preview row before the first actual layout pass.
            body.AutoScroll = false;
            body.Padding = new Padding(7);
            var imageArea = new TableLayoutPanel
            {
                Name = "CampaignWizardImageArea", Dock = DockStyle.Fill,
                MinimumSize = new Size(400, 385),
                ColumnCount = 2, RowCount = 1,
                BackColor = UiStyle.SoftBlue, Margin = Padding.Empty
            };
            imageArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 43));
            imageArea.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 57));
            imageArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var listPanel = new Panel
            {
                Dock = DockStyle.Fill, Padding = new Padding(6),
                BackColor = UiStyle.Surface, Margin = new Padding(0, 0, 7, 0)
            };
            _images = new ListBox
            {
                Name = "CampaignWizardImageList", Dock = DockStyle.Fill,
                BorderStyle = BorderStyle.None, IntegralHeight = false,
                Font = new Font("Segoe UI", 10f)
            };
            listPanel.Controls.Add(_images);
            imageArea.Controls.Add(listPanel, 0, 0);

            var previewPanel = new TableLayoutPanel
            {
                Name = "CampaignWizardPreviewPanel",
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
                BackColor = UiStyle.Surface, Padding = new Padding(7),
                Margin = new Padding(7, 0, 0, 0)
            };
            previewPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
            // Reserve real pixels for the bitmap. A percent-only middle row
            // collapsed to 0 at some DPI/window sizes even when an image
            // was loaded successfully. The host scrolls on small screens.
            previewPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 290));
            previewPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            previewPanel.Controls.Add(new Label
            {
                Text = "Selected image preview", Dock = DockStyle.Fill,
                ForeColor = UiStyle.Navy, TextAlign = ContentAlignment.MiddleLeft,
                Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold)
            }, 0, 0);
            var preview = new PictureBox
            {
                Name = "CampaignWizardImagePreview",
                Dock = DockStyle.Fill, BackColor = Color.FromArgb(27, 34, 45),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                AccessibleName = "Preview of selected campaign image"
            };
            preview.Disposed += (_, _) =>
            {
                var image = preview.Image;
                preview.Image = null;
                image?.Dispose();
            };
            _wizardImagePreview = preview;
            var previewContainer = new Panel
            {
                Name = "CampaignWizardPreviewFrame",
                Dock = DockStyle.Fill, BackColor = Color.FromArgb(27, 34, 45),
                MinimumSize = new Size(130, 235),
                Margin = Padding.Empty
            };
            var placeholder = new Label
            {
                Name = "CampaignWizardPreviewPlaceholder",
                Text = "Select an image to preview",
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter,
                BackColor = Color.FromArgb(27, 34, 45),
                ForeColor = Color.White,
                Font = new Font("Segoe UI", 10f)
            };
            _wizardPreviewPlaceholder = placeholder;
            previewContainer.Controls.Add(preview);
            previewContainer.Controls.Add(placeholder);
            placeholder.BringToFront();
            previewPanel.Controls.Add(previewContainer, 0, 1);
            _wizardImageName = new Label
            {
                Name = "CampaignWizardImagePath", Dock = DockStyle.Fill,
                ForeColor = UiStyle.Muted, AutoEllipsis = true,
                TextAlign = ContentAlignment.MiddleLeft
            };
            previewPanel.Controls.Add(_wizardImageName, 0, 2);
            imageArea.Controls.Add(previewPanel, 1, 0);
            body.Controls.Add(imageArea);

            _images.SelectedIndexChanged += (_, _) => UpdateWizardImagePreview();
            for (int i = 0; i < _owner._project.ImagePaths.Count; i++)
                _images.Items.Add($"{i + 1:00}   {Path.GetFileName(_owner._project.ImagePaths[i])}");
            if (_images.Items.Count > 0)
                _images.SelectedIndex = Math.Clamp(selected, 0, _images.Items.Count - 1);
            UpdateWizardImagePreview();
            status.Text = _owner._project.ImagePaths.Count == 0
                ? "No images yet. Choose files or a folder to continue."
                : $"{_owner._project.ImagePaths.Count} image(s) ready. " +
                  (arrangement ? "Reorder them, then continue to Schedule." :
                   "Next: arrange the sequence.");
        }

        private void UpdateWizardImagePreview()
        {
            var preview = _wizardImagePreview;
            var name = _wizardImageName;
            if (preview is null || preview.IsDisposed || name is null || name.IsDisposed)
                return;
            var placeholder = _wizardPreviewPlaceholder;
            var previous = preview.Image;
            preview.Image = null;
            previous?.Dispose();
            if (placeholder is { IsDisposed: false })
            {
                placeholder.Visible = true;
                placeholder.Text = "Select an image to preview";
                placeholder.BringToFront();
            }
            int index = _images?.SelectedIndex ?? -1;
            if (index < 0 || index >= _owner._project.ImagePaths.Count)
            {
                name.Text = "Select an image from the list.";
                return;
            }
            string path = _owner._project.ImagePaths[index];
            name.Text = Path.GetFileName(path) + Environment.NewLine + path;
            if (!File.Exists(path))
            {
                name.Text = "Image not found: " + Path.GetFileName(path);
                if (placeholder is { IsDisposed: false })
                    placeholder.Text = "Image file not found";
                return;
            }
            try
            {
                using var source = Image.FromFile(path);
                // A large original image need not occupy hundreds of MB of
                // bitmap RAM just to render a small wizard thumbnail.
                const int maxWidth = 1600, maxHeight = 900;
                double factor = Math.Min(1d, Math.Min(
                    (double)maxWidth / source.Width,
                    (double)maxHeight / source.Height));
                var size = new Size(Math.Max(1, (int)(source.Width * factor)),
                    Math.Max(1, (int)(source.Height * factor)));
                preview.Image = new Bitmap(source, size);
                name.Text = Path.GetFileName(path) + "  (" +
                    source.Width + " x " + source.Height + ")" +
                    Environment.NewLine + path;
                if (placeholder is { IsDisposed: false })
                    placeholder.Visible = false;
                // Paint selected bitmap above the placeholder after DPI/layout.
                preview.BringToFront();
                preview.Invalidate();
            }
            catch (Exception ex)
            {
                name.Text = "Unable to preview: " + Path.GetFileName(path);
                if (placeholder is { IsDisposed: false })
                    placeholder.Text = "Image cannot be opened";
                AppLog.Write(ex, "Campaign Wizard image preview");
            }
        }

        private int SelectedImage => _images?.SelectedIndex ?? -1;

        private void RemoveSelectedImage()
        {
            int i = SelectedImage;
            if (i < 0) return;
            if (MessageBox.Show(this, "Remove the selected image from this campaign?",
                    "Remove image", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                    MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            _owner._table.ClearSelection();
            _owner._table.Rows[i].Selected = true;
            _owner.RemoveImage();
            _saved = false;
            RenderPage(Math.Max(0, i - 1));
        }

        private void MoveSelectedImage(int offset)
        {
            int index = SelectedImage;
            int target = index + offset;
            if (index < 0 || target < 0 || target >= _owner._project.ImagePaths.Count)
                return;
            (_owner._project.ImagePaths[index], _owner._project.ImagePaths[target]) =
                (_owner._project.ImagePaths[target], _owner._project.ImagePaths[index]);
            _owner._projectDirty = true;
            _owner.UpdateRows(target);
            _saved = false;
            RenderPage(target);
        }

        private void SortImages()
        {
            if (_owner._project.ImagePaths.Count < 2) return;
            if (MessageBox.Show(this, "Sort the images alphabetically by filename?",
                    "Confirm image order", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) !=
                DialogResult.Yes) return;
            _owner._project.ImagePaths = _owner._project.ImagePaths
                .OrderBy(x => Path.GetFileName(x), StringComparer.OrdinalIgnoreCase).ToList();
            _owner._projectDirty = true;
            _owner.UpdateRows(0);
            _saved = false;
            RenderPage();
        }

        private void AutoMatchImages()
        {
            try
            {
                var proposal = ImageFilenameMatcher.Suggest(_owner._project,
                    _owner._project.ImagePaths);
                var sorted = proposal.Slots.Select(s => s.SuggestedPath).ToArray();
                if (proposal.ConfidentCount != sorted.Length ||
                    sorted.Any(x => x is null) ||
                    sorted.Distinct(StringComparer.OrdinalIgnoreCase).Count() != sorted.Length)
                {
                    MessageBox.Show(this,
                        "Some names cannot be matched confidently. Review and reorder those images manually.",
                        "Image filename matching", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
                if (MessageBox.Show(this, "Apply the suggested order based on filenames?",
                        "Confirm image order", MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                    return;
                _owner._project.ImagePaths = sorted.Select(x => x!).ToList();
                _owner._projectDirty = true;
                _owner.UpdateRows(0);
                _saved = false;
                RenderPage();
            }
            catch (Exception ex) { UiErrorDialog.Show(this, "Auto match images", ex); }
        }

        private void RenderSchedule(FlowLayoutPanel actions, Panel body, Label status)
        {
            Action(actions, "Edit schedule...", () =>
            {
                _owner.OpenScheduleEditor();
                _everConfiguredSchedule = _owner._wizardScheduleConfigured;
                _saved = false;
                RenderPage();
            }, 207, true);
            Action(actions, "Client Health / Feedback...", () =>
            {
                using var feedback = new FeedbackSettingsDialog(_owner._project);
                if (feedback.ShowDialog(this) != DialogResult.OK) return;
                _owner._projectDirty = true;
                _saved = false;
                RenderPage();
            }, 264);
            // Time zone is a first-class editable field in the guided
            // schedule step. The previous version only displayed it in a
            // status line, requiring a hidden trip to the Advanced editor.
            var schedule = new TableLayoutPanel
            {
                Name = "WizardScheduleContent",
                Dock = DockStyle.Top, Height = 380, RowCount = 5, ColumnCount = 1,
                Padding = new Padding(9, 7, 9, 12)
            };
            // A top-docked fixed-height content surface makes the nested
            // Panel scroll when a laptop/DPI viewport is too short.
            schedule.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            schedule.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
            schedule.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            schedule.RowStyles.Add(new RowStyle(SizeType.Absolute, 88));
            schedule.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            schedule.Controls.Add(new Label
            {
                Text = "Windows time zone for the campaign",
                Dock = DockStyle.Fill, ForeColor = UiStyle.Navy,
                Font = new Font("Segoe UI Semibold", 11f),
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            var zone = new ComboBox
            {
                Name = "WizardTimeZoneSelect", Dock = DockStyle.Fill,
                AccessibleName = "Campaign Windows time zone",
                DropDownWidth = 540
            };
            TimeZoneChoices.Populate(zone, _owner._project.TimeZoneId);
            var offsetInfo = new Label
            {
                Name = "WizardTimeZoneDifference",
                Text = TimeZoneChoices.OffsetComparison(
                    _owner._project.TimeZoneId, DateTimeOffset.UtcNow),
                Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = UiStyle.Green, AutoEllipsis = false,
                Padding = new Padding(8, 6, 6, 8),
                Font = new Font("Segoe UI", 9.5f)
            };
            zone.SelectedIndexChanged += (_, _) =>
            {
                string id = TimeZoneChoices.SelectedId(zone);
                if (id.Length == 0) return;
                try
                {
                    _owner.ChooseCampaignTimeZone(id);
                    offsetInfo.Text = TimeZoneChoices.OffsetComparison(
                        id, DateTimeOffset.UtcNow);
                    _everConfiguredSchedule = true;
                    _saved = false;
                    status.Text = "Selected time zone: " + id +
                        ". Check image times and continue to Save.";
                }
                catch (Exception ex)
                {
                    UiErrorDialog.Show(this, "Select campaign time zone", ex);
                }
            };
            schedule.Controls.Add(zone, 0, 1);
            schedule.Controls.Add(offsetInfo, 0, 2);
            var feedbackSummary = _owner._project.EnableFeedback
                ? "Client Health: ENABLED\n" + _owner._project.FeedbackShare +
                    "  |  every " + _owner._project.FeedbackIntervalMinutes + " min"
                : "Client Health: DISABLED. No client reports until enabled.";
            var feedbackInfo = new TextBox
            {
                Text = feedbackSummary,
                Name = "WizardFeedbackConfiguration",
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true,
                ScrollBars = ScrollBars.Both, WordWrap = false,
                BorderStyle = BorderStyle.None,
                ForeColor = _owner._project.EnableFeedback ? UiStyle.Foreground : UiStyle.Muted,
                BackColor = UiStyle.SoftBlue,
                Font = new Font("Segoe UI", 9.5f),
                Margin = new Padding(6, 6, 6, 5)
            };
            schedule.Controls.Add(feedbackInfo, 0, 3);
            var list = new ListBox
            {
                Dock = DockStyle.Fill, IntegralHeight = false,
                Font = new Font("Segoe UI", 10f), BorderStyle = BorderStyle.None
            };
            for (int i = 0; i < _owner._project.ImagePaths.Count; i++)
            {
                string date;
                try { date = CampaignSchedule.At(_owner._project, i).ToString("ddd dd.MM.yyyy  HH:mm"); }
                catch { date = "Schedule not configured"; }
                list.Items.Add($"{i + 1:00}  {date}  |  {Path.GetFileName(_owner._project.ImagePaths[i])}");
            }
            schedule.Controls.Add(list, 0, 4);
            body.AutoScroll = true;
            body.Controls.Add(schedule);
            status.Text = "Time zone: " + _owner._project.TimeZoneId +
                (_everConfiguredSchedule || _owner._wizardScheduleConfigured
                    ? " | Schedule edited. Continue to Save."
                    : " | Select a time zone and review scheduled changes.");
        }

        private void RenderSave(FlowLayoutPanel actions, Panel body, Label status)
        {
            if (_owner._project.ScreenSaver.UseJpegSlideshow &&
                _owner._project.EffectiveCampaignMode != CampaignTargetMode.LockScreen)
                Action(actions, "Export JPG .scr pilot...", () =>
                {
                    try
                    {
                        _owner.ReadControls();
                        using var select = new FolderBrowserDialog {
                            Description = "Choose an EXISTING local folder for the JPG screen saver package"
                        };
                        if (select.ShowDialog(this) != DialogResult.OK) return;
                        string exported = PhotoJpegSaver.Export(_owner._project, select.SelectedPath);
                        MessageBox.Show(this,
                            "JPG Screen Saver package exported locally:\r\n" + exported +
                            "\r\n\r\nTest PhotoScreenSaver.scr /s on an isolated client. " +
                            "This does NOT deploy GPO or install anything.",
                            "JPG Screen Saver pilot ready",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        status.Text = "JPG Screen Saver pilot package exported. Test before domain rollout.";
                    }
                    catch (Exception ex) { UiErrorDialog.Show(this, "Export JPG Screen Saver", ex); }
                }, 222, true);
            if (_owner._project.ScreenSaver.UseJpegSlideshow &&
                _owner._project.EffectiveCampaignMode != CampaignTargetMode.LockScreen)
                Action(actions, "Export GPO lab kit...", () =>
                {
                    using var selected = new FolderBrowserDialog
                    {
                        Description = "Choose this campaign's previously exported JPG-ScreenSaver package"
                    };
                    if (selected.ShowDialog(this) != DialogResult.OK) return;
                    if (MessageBox.Show(this,
                        "Prepare a MANUAL Computer GPO Startup LAB kit for testing?\r\n\r\n" +
                        "It will contain a pinned PS1 installer, standalone JPG .scr, " +
                        "hash manifest and the original pictures.\r\n" +
                        "The kit will NOT create, link, or modify AD/GPO/SYSVOL. " +
                        "It will NOT enable a User GPO or change local registry settings.\r\n\r\n" +
                        "Only manually deploy to a dedicated pilot computer group after " +
                        "testing the .scr on an authorized Windows desktop.\r\n\r\n" +
                        "Create test kit beside the selected JPG package?",
                        "Confirm read-only JPG GPO lab export",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    try
                    {
                        _owner.ReadControls();
                        string parent = Path.GetDirectoryName(
                            Path.GetFullPath(selected.SelectedPath)) ??
                            throw new InvalidDataException("Invalid JPG Screen Saver folder.");
                        string exported = PhotoSaverGpoPilotKit.Export(_owner._project,
                            selected.SelectedPath, parent);
                        MessageBox.Show(this,
                            "JPG GPO LAB kit exported locally:\r\n" + exported +
                            "\r\n\r\nOpen LAB-GPO-INSTRUCTIONS.txt. " +
                            "No Active Directory, GPO or client settings were changed.",
                            "JPG GPO lab kit ready",
                            MessageBoxButtons.OK, MessageBoxIcon.Information);
                        status.Text = "Read-only Computer Startup GPO pilot kit exported. " +
                            "User GPO activation remains blocked.";
                    }
                    catch (Exception ex)
                    {
                        UiErrorDialog.Show(this, "Export JPG GPO lab kit", ex);
                    }
                }, 210, true);
            if (_owner._project.ScreenSaver.UseJpegSlideshow &&
                _owner._project.EffectiveCampaignMode != CampaignTargetMode.LockScreen)
                Action(actions, "Export GPO lab kit...", () =>
                {
                    if (MessageBox.Show(this,
                        "Export a MANUAL, computer-only GPO LAB kit?\r\n\r\n" +
                        "It contains a pinned JPG .scr package and a LocalSystem " +
                        "PowerShell Startup installer for test computers. " +
                        "It does NOT create GPOs, install on clients, " +
                        "configure user Screen Saver policies, or copy anything " +
                        "to SYSVOL. Choose an exported JPG .scr folder first.\r\n\r\n" +
                        "Continue?",
                        "JPG Screen Saver — manual GPO lab kit",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    using var source = new FolderBrowserDialog {
                        Description = "Select the existing JPG-ScreenSaver campaign package"
                    };
                    if (source.ShowDialog(this) != DialogResult.OK) return;
                    using var destination = new FolderBrowserDialog {
                        Description = "Choose an EXISTING local folder for the NEW GPO LAB kit"
                    };
                    if (destination.ShowDialog(this) != DialogResult.OK) return;
                    try
                    {
                        _owner.ReadControls();
                        string exported = PhotoSaverGpoPilotKit.Export(
                            _owner._project, source.SelectedPath, destination.SelectedPath);
                        MessageBox.Show(this,
                            "Read-only GPO LAB kit exported locally:\r\n" + exported +
                            "\r\n\r\nRead LAB-GPO-INSTRUCTIONS.txt before using GPMC. " +
                            "No Group Policy, Active Directory or SYSVOL was changed. " +
                            "User Screen Saver GPO activation is NOT part of this pilot.",
                            "JPG GPO lab kit ready", MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        status.Text = "Manual GPO lab kit exported; no AD or client changes.";
                    }
                    catch (Exception ex)
                    {
                        UiErrorDialog.Show(this, "Export JPG GPO lab kit", ex);
                    }
                }, 215, true);
            if (_owner._project.ScreenSaver.UseJpegSlideshow &&
                _owner._project.EffectiveCampaignMode != CampaignTargetMode.LockScreen)
                Action(actions, "Install local JPG pilot...", () =>
                {
                    using var select = new FolderBrowserDialog
                    {
                        Description = "Choose an exported PhotoScreenSaver.scr pilot package folder"
                    };
                    if (select.ShowDialog(this) != DialogResult.OK) return;
                    var timeout = _owner._project.ScreenSaver.IdleSeconds;
                    var secured = _owner._project.ScreenSaver.PasswordOnResume;
                    if (MessageBox.Show(this,
                        "Install a JPG screen saver for THIS Windows user only?\r\n\r\n" +
                        "Folder: " + select.SelectedPath + "\r\n" +
                        "Windows idle timeout: " + timeout + " seconds\r\n" +
                        "Require sign-in on resume: " + (secured ? "Yes" : "No") +
                        "\r\n\r\nThe installer will back up the four current HKCU " +
                        "Desktop screen saver values and copy verified JPGs and .scr " +
                        "to a local pilot folder. No AD/GPO settings are changed. " +
                        "If a policy already manages these values, installation will be refused.\r\n\r\n" +
                        "Continue with this TEST USER?",
                        "Confirm local JPG pilot installation",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    try
                    {
                        string scr = PhotoSaverLocalPilot.Install(select.SelectedPath,
                            timeout, secured);
                        status.Text = "JPG Screen Saver installed for current user only. " +
                            "Test automatic idle activation on the desktop.";
                        MessageBox.Show(this,
                            "Current-user JPG Screen Saver pilot installed:\r\n" + scr +
                            "\r\n\r\nTest Windows automatic idle activation and sign-in on resume. " +
                            "The Remove local JPG pilot button restores your original settings.",
                            "Local JPG pilot installed", MessageBoxButtons.OK,
                            MessageBoxIcon.Information);
                        RenderPage(); // Reveal Remove immediately after a successful install.
                    }
                    catch (Exception ex)
                    {
                        UiErrorDialog.Show(this, "Install local JPG Screen Saver pilot", ex);
                    }
                }, 220, true);
            if (PhotoSaverLocalPilot.HasPilot)
                Action(actions, "Remove local JPG pilot...", () =>
                {
                    if (MessageBox.Show(this,
                        "Restore your previous current-user Screen Saver values " +
                        "and delete the local JPG pilot package?\r\n\r\n" +
                        "If Windows or Group Policy changed the settings, removal will " +
                        "refuse an unsafe automatic restore.",
                        "Confirm local JPG pilot removal",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                        MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    try
                    {
                        if (PhotoSaverLocalPilot.Remove())
                        {
                            status.Text = "Previous current-user Screen Saver settings restored.";
                            MessageBox.Show(this,
                                "Previous Windows Screen Saver settings restored.",
                                "Local JPG pilot removed", MessageBoxButtons.OK,
                                MessageBoxIcon.Information);
                            RenderPage();
                        }
                    }
                    catch (Exception ex)
                    {
                        UiErrorDialog.Show(this, "Remove local JPG Screen Saver pilot", ex);
                    }
                }, 230, true);
            Action(actions, "Validate campaign", () =>
                _owner.ValidateCurrent(), 180);
            Action(actions, "Save project...", () =>
            {
                try
                {
                    _owner.ReadControls();
                    CampaignSchedule.Validate(_owner._project, requireImages: true);
                    if (_owner.TrySaveProject())
                    {
                        _saved = true;
                        _skipProjectSave = false;
                        _storageModifiedAfterSave = false;
                    }
                    RenderPage();
                }
                catch (CampaignValidationException ex)
                {
                    MessageBox.Show(this, ex.Message, "Check campaign",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (Exception ex) { UiErrorDialog.Show(this, "Save campaign", ex); }
            }, 190, true);
            Action(actions, "Skip saving & continue", () =>
            {
                _skipProjectSave = true;
                NextStep(); // Same validation as Save & Next; no SaveFileDialog
            }, 230);
            var group = new TableLayoutPanel
            {
                Name = "WizardCampaignNameField",
                Dock = DockStyle.Top, ColumnCount = 1, RowCount = 3,
                Height = 204, Padding = new Padding(10)
            };
            group.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
            group.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            group.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var campaignName = Information("Campaign name", 10f);
            campaignName.Name = "WizardCampaignNameLabel";
            campaignName.Padding = new Padding(6, 5, 6, 5);
            group.Controls.Add(campaignName, 0, 0);
            var name = new TextBox
            {
                Name = "WizardCampaignNameInput",
                Dock = DockStyle.Top, Text = _owner._name.Text,
                Font = new Font("Segoe UI", 11f)
            };
            void FitCampaignName()
            {
                if (group.IsDisposed || name.IsDisposed || campaignName.IsDisposed) return;
                int labelHeight = TextRenderer.MeasureText("Agpqy", campaignName.Font,
                    new Size(4096, 4096),
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Height +
                    campaignName.Padding.Vertical + campaignName.Margin.Vertical + 12;
                int editorHeight = Math.Max(name.Height, name.PreferredSize.Height) +
                    name.Margin.Vertical + 12;
                group.RowStyles[0].Height = Math.Max(44, labelHeight);
                group.RowStyles[1].Height = Math.Max(52, editorHeight);
                int minimum = (int)Math.Ceiling(group.RowStyles[0].Height +
                    group.RowStyles[1].Height) + 95 + group.Padding.Vertical;
                if (group.Height < minimum) group.Height = minimum;
            }
            campaignName.FontChanged += (_, _) => FitCampaignName();
            name.FontChanged += (_, _) => FitCampaignName();
            name.TextChanged += (_, _) =>
            {
                if (_editingPage) return;
                _owner._name.Text = name.Text;
                _owner._projectDirty = true;
                _saved = false;
            };
            group.Controls.Add(name, 0, 1);
            group.Controls.Add(Information(
                $"{_owner._project.ImagePaths.Count} image(s). " +
                $"Time zone: {_owner._project.TimeZoneId}. " +
                "Saving the local .json project is optional. If skipped, " +
                "settings remain in the editor; deployment exports its " +
                "own campaign package."), 0, 2);
            body.Controls.Add(group);
            var storage = new TableLayoutPanel
            {
                Name = "CampaignWizardSaveStorageOptions",
                Dock = DockStyle.Top, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1, RowCount = 4,
                Padding = new Padding(12, 10, 12, 12)
            };
            for (int row = 0; row < 4; row++)
                storage.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            AddStorageSelector(storage, 0, status);
            body.Controls.Add(storage);
            FitCampaignName();
            status.Text = T(_saved
                ? "Project saved. Continue to Deploy or finish offline."
                : _skipProjectSave
                    ? "Local save skipped. Continue to Deploy without a project file."
                    : "Choose Save project or Skip saving & continue. Validation is required either way.");
        }


        // Shared between step 6 (Save) and step 7 (Deploy): the storage
        // location must be chosen BEFORE the user saves a reusable JSON file.
        private void AddStorageSelector(TableLayoutPanel host, int firstRow,
            Label status, Button? gpoAction = null, bool gpoActionAllowed = true)
        {
            host.Controls.Add(new Label {
                Text = "Campaign files — choose where the agent and images are stored",
                Dock = DockStyle.Top, AutoSize = true, AutoEllipsis = false,
                ForeColor = UiStyle.Navy,
                Font = new Font("Segoe UI Semibold", 10.5f),
                Margin = new Padding(4, 0, 4, 8)
            }, 0, firstRow + 0);

            var source = new ComboBox {
                Name = "CampaignWizardContentSource",
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
                Margin = new Padding(4, 3, 4, 12)
            };
            source.Items.AddRange(new object[] {
                "SYSVOL (default)",
                "SMB network folder (UNC)"
            });
            source.SelectedIndex = _owner._project.ContentSource ==
                CampaignContentSource.SmbShare ? 1 : 0;
            host.Controls.Add(source, 0, firstRow + 1);

            var networkRow = new TableLayoutPanel {
                Name = "CampaignWizardNetworkStorageRow",
                Dock = DockStyle.Top, AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowOnly,
                MinimumSize = new Size(490, 0),
                ColumnCount = 2, RowCount = 1,
                Margin = new Padding(4, 0, 4, 9)
            };
            networkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            networkRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 145));
            networkRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            var networkPath = new TextBox {
                Name = "CampaignWizardContentShareRoot",
                Dock = DockStyle.Fill,
                Text = _owner._project.ContentShareRoot,
                PlaceholderText = @"\\files.example.com\Campaigns",
                Margin = new Padding(0, 4, 8, 4)
            };
            var browse = new Button {
                Name = "CampaignWizardContentShareBrowse",
                Text = "Browse...",
                Dock = DockStyle.Fill, AutoSize = true, Tag = "NoIcon",
                AutoEllipsis = false,
                Margin = new Padding(3, 1, 0, 1)
            };
            networkRow.Controls.Add(networkPath, 0, 0);
            networkRow.Controls.Add(browse, 1, 0);
            host.Controls.Add(networkRow, 0, firstRow + 2);
            var detail = new Label {
                Name = "CampaignWizardContentSourceHint",
                Dock = DockStyle.Top, AutoSize = true,
                AutoEllipsis = false, ForeColor = UiStyle.Muted,
                Margin = new Padding(4, 2, 4, 5)
            };
            host.Controls.Add(detail, 0, firstRow + 3);

            bool ValidSource()
            {
                if (source.SelectedIndex != 1) return true;
                try {
                    _ = CampaignPayloadLocation.ValidateRoot(networkPath.Text);
                    return true;
                }
                catch (CampaignValidationException) { return false; }
            }
            void RefreshStorage()
            {
                bool smb = source.SelectedIndex == 1;
                networkPath.Enabled = smb;
                browse.Enabled = smb;
                networkRow.Visible = smb;
                if (gpoAction is not null)
                    gpoAction.Enabled = gpoActionAllowed && ValidSource();
                detail.Text = T(smb
                    ? ValidSource()
                        ? "The GPO remains in SYSVOL. Test PC accounts require READ on both the SMB share and NTFS."
                        : @"Enter an existing dedicated UNC folder: \\server\share\folder (not a mapped drive)."
                    : "GPO and immutable campaign files use the existing SYSVOL location.");
            }
            void MarkStorageChanged()
            {
                _owner._project.ContentSource = source.SelectedIndex == 1
                    ? CampaignContentSource.SmbShare : CampaignContentSource.Sysvol;
                _owner._project.ContentShareRoot = networkPath.Text.Trim();
                _owner._projectDirty = true;
                _storageModifiedAfterSave |= _saved;
                _saved = false;
                RefreshStorage();
                status.Text = T(_storageModifiedAfterSave
                    ? "Unsaved storage changes. Save project changes before finishing if you need the JSON file up to date."
                    : "Storage changed. The new choice is stored in the editor; save the project to keep it on disk.");
            }
            source.SelectedIndexChanged += (_, _) => MarkStorageChanged();
            networkPath.TextChanged += (_, _) => MarkStorageChanged();
            browse.Click += (_, _) => {
                using var pick = new FolderBrowserDialog {
                    Description = "Choose an existing dedicated SMB UNC folder (computer accounts require READ)",
                    ShowNewFolderButton = false
                };
                if (pick.ShowDialog(this) != DialogResult.OK) return;
                try {
                    networkPath.Text = CampaignPayloadLocation.ValidateRoot(pick.SelectedPath);
                } catch (CampaignValidationException ex) {
                    MessageBox.Show(this, ex.Message, "SMB content folder",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            };

            RefreshStorage();
        }

        private void RenderDeploy(FlowLayoutPanel actions, Panel body, Label status)
        {
            var photoPilotOnly = _owner._project.ScreenSaver.UseJpegSlideshow &&
                _owner._project.EffectiveCampaignMode != CampaignTargetMode.LockScreen;
            var gpoAction = Action(actions, "Open GPO Deployment Wizard...", () =>
            {
                _deployChoice!.Checked = true;
                Deploy();
            }, 290, true);
            Action(actions, "Save project changes...", () =>
            {
                try
                {
                    _owner.ReadControls();
                    CampaignSchedule.Validate(_owner._project, requireImages: true);
                    if (_owner.TrySaveProject())
                    {
                        _saved = true;
                        _skipProjectSave = false;
                        _storageModifiedAfterSave = false;
                        RenderPage();
                    }
                }
                catch (Exception ex) when (ex is CampaignValidationException
                    or FileNotFoundException)
                {
                    MessageBox.Show(this, ex.Message, "Save campaign",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (Exception ex) { UiErrorDialog.Show(this, "Save campaign", ex); }
            }, 215);
            Action(actions, "Finish offline", () => FinishOffline(), 175);

            // Source selection belongs to the main, seven-step Wizard.
            // The AD deployment sub-wizard will receive exactly this choice,
            // and may still modify it if the operator changes their mind.
            // Auto-size every row: two stacked choices plus SMB/UNC editing
            // must remain readable at 125-200% DPI without fixed 57px cells.
            var options = new TableLayoutPanel
            {
                Name = "CampaignWizardStorageOptions",
                Dock = DockStyle.Top,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                ColumnCount = 1, RowCount = 6,
                Padding = new Padding(12, 13, 12, 15)
            };
            for (int i = 0; i < 6; i++)
                options.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _deployChoice = new RadioButton
            {
                Text = "Deploy GPO to selected OU and computer/group assignments",
                Dock = DockStyle.Top, AutoSize = true,
                Margin = new Padding(4, 8, 4, 12),
                Checked = _owner.AdReady && !photoPilotOnly,
                Enabled = !photoPilotOnly
            };
            var offline = new RadioButton
            {
                Text = _saved
                    ? "Finish offline - local project is saved"
                    : "Finish offline - keep the campaign in the editor",
                Dock = DockStyle.Top, AutoSize = true,
                Margin = new Padding(4, 5, 4, 16),
                Checked = !_owner.AdReady || photoPilotOnly
            };
            _deployChoice.CheckedChanged += (_, _) =>
            {
                if (_step == 6)
                    _next.Text = T(_deployChoice.Checked ? "Deploy GPO..." : "Finish");
            };
            options.Controls.Add(_deployChoice, 0, 0);
            options.Controls.Add(offline, 0, 1);

            AddStorageSelector(options, 2, status, gpoAction, !photoPilotOnly);
            body.Controls.Add(options);
            status.Text = T(_storageModifiedAfterSave
                ? "The saved project still has an older storage path. Choose Save project changes to update it."
                : photoPilotOnly
                ? "JPG Screen Saver: export and test the .scr package locally. " +
                  "AD deployment is blocked until group/photo delivery is pilot-verified."
                : _skipProjectSave && !_saved
                ? "No local project file was saved. Deploy or return to the editor."
                : _owner.AdReady
                    ? "AD connected: select storage and deployment or finish without AD changes."
                    : "Offline: select storage now; connect to AD in the deployment wizard if needed.");
        }

        private bool ConfirmSavedProjectIsOutdated()
        {
            if (!_storageModifiedAfterSave) return true;
            return MessageBox.Show(this,
                "You saved this project before changing its storage location.\r\n\r\n" +
                "The saved .json file still contains the earlier SYSVOL/SMB choice.\r\n" +
                "Choose 'Save project changes...' to update your file.\r\n\r\n" +
                "Continue without updating the saved project?",
                "Unsaved campaign storage changes", MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2)
                == DialogResult.Yes;
        }

        private void FinishOffline()
        {
            if (!ConfirmSavedProjectIsOutdated()) return;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Deploy()
        {
            if (_owner._project.ContentSource == CampaignContentSource.SmbShare)
            {
                try { _ = CampaignPayloadLocation.ValidateRoot(_owner._project.ContentShareRoot); }
                catch (CampaignValidationException ex)
                {
                    MessageBox.Show(this, ex.Message, "Select SMB campaign folder",
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
            }
            if (_owner._project.ScreenSaver.UseJpegSlideshow &&
                _owner._project.EffectiveCampaignMode != CampaignTargetMode.LockScreen)
            {
                MessageBox.Show(this,
                    "JPG .scr package must be pilot-tested locally first. " +
                    "Automatic user GPO delivery is not enabled.",
                    "JPG Screen Saver pilot only", MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }
            if (!ConfirmSavedProjectIsOutdated()) return;
            if (!_owner.AdReady)
            {
                _owner.ConnectAndReadAd();
                if (!_owner.AdReady) { RenderPage(); return; }
            }
            var earlierSource = _owner._project.ContentSource;
            var earlierShare = _owner._project.ContentShareRoot;
            _owner.OpenDomainDeployment();
            if (earlierSource != _owner._project.ContentSource ||
                !string.Equals(earlierShare, _owner._project.ContentShareRoot,
                    StringComparison.Ordinal))
            {
                _storageModifiedAfterSave |= _saved;
                _saved = false;
            }
            RenderPage();
        }

        private void NextStep()
        {
            switch (_step)
            {
                case 0:
                    if (_connectChoice?.Checked == true && !_owner.AdReady)
                    {
                        _owner.ConnectAndReadAd();
                        if (!_owner.AdReady) { RenderPage(); return; }
                    }
                    break;
                case 1:
                    if (!HasChosenCampaignType ||
                        !Enum.IsDefined(_owner._project.EffectiveCampaignMode))
                    {
                        MessageBox.Show(this, "Choose Lock Screen, Screen Saver or Both.",
                            "Campaign Wizard", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    break;
                case 2:
                case 3:
                    if (NeedsLockImages && _owner._project.ImagePaths.Count == 0)
                    {
                        MessageBox.Show(this, "Add at least one image to continue.",
                            "Campaign Wizard", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    break;
                case 4:
                case 5:
                    try
                    {
                        _owner.ReadControls();
                        CampaignSchedule.Validate(_owner._project, requireImages: true);
                    }
                    catch (Exception ex) when (ex is CampaignValidationException or FileNotFoundException)
                    {
                        MessageBox.Show(this, ex.Message, "Check campaign",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    if (_step == 5 && ShouldSaveCampaignProject(_saved, _skipProjectSave))
                    {
                        if (!_owner.TrySaveProject()) return;
                        _saved = true;
                        _storageModifiedAfterSave = false;
                    }
                    break;
                case 6:
                    if (_deployChoice?.Checked == true) { Deploy(); return; }
                    FinishOffline();
                    return;
            }
            // Screen Saver-only has no image selection, ordering or JPEG
            // schedule. Skip these pages and restore Type with Back.
            _step = _step == 1 && !NeedsLockImages ? 5 : Math.Min(6, _step + 1);
            RenderPage();
        }
    }
}
