using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

public sealed partial class MainForm
{
    private CampaignWizardDialog? _activeCampaignWizard;
    // CI-only readback for an unsaved, local wizard time-zone change.
    internal string CampaignTimeZoneForSmokeTest => _project.TimeZoneId;


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

    // Allows offline UI smoke tests to validate all six wizard pages
    // without ShowDialog, LDAP access or filesystem changes.
    internal Form CreateCampaignWizardForSmokeTest(int page = 0, string? imagePath = null)
    {
        if (!string.IsNullOrWhiteSpace(imagePath))
            _project.ImagePaths.Add(imagePath);
        var wizard = new CampaignWizardDialog(this);
        wizard.ShowPageForSmokeTest(page);
        return wizard;
    }

    private sealed class CampaignWizardDialog : Form
    {
        private static readonly string[] StepNames =
        [
            "1  AD connection", "2  Images", "3  Arrange",
            "4  Schedule", "5  Save / Skip", "6  Deploy"
        ];
        private readonly MainForm _owner;
        private readonly TableLayoutPanel _root = new();
        private readonly TableLayoutPanel _footer = new();
        private readonly TableLayoutPanel _header = new();
        private readonly Label _heading = new();
        private readonly Label _subheading = new();
        private readonly Label[] _rail = new Label[6];
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
            _subheading.Text = "Step 1 of 6  -  Choose how to connect to Active Directory";
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
                ColumnCount = 1, RowCount = 7,
                BackColor = UiStyle.SoftBlue,
                Padding = new Padding(9, 16, 9, 10),
                Margin = new Padding(0, 0, 7, 0)
            };
            for (int i = 0; i < 6; i++)
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
            }, 0, 6);
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
            _back.Click += (_, _) => { _step = Math.Max(0, _step - 1); RenderPage(); };
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
            _step = Math.Clamp(page, 0, 5);
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
                page.Height = Math.Max(_step is 1 or 2 ? 720 : 390,
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
                _subheading.Text = "Step " + (_step + 1) + " of 6  -  " + StepNames[_step][3..];
                for (int i = 0; i < _rail.Length; i++)
                {
                    _rail[i].BackColor = i == _step ? UiStyle.Accent : UiStyle.SoftBlue;
                    _rail[i].ForeColor = i == _step ? Color.White :
                        i < _step ? UiStyle.Green : UiStyle.Navy;
                }

                bool imagePage = _step is 1 or 2;
                var page = new TableLayoutPanel
                {
                    Dock = DockStyle.Top,
                    Height = Math.Max(imagePage ? 720 : 390,
                        _host.ClientSize.Height - _host.Padding.Vertical),
                    ColumnCount = 1, RowCount = 5,
                    Padding = new Padding(10, 7, 10, 9),
                    BackColor = UiStyle.Surface
                };
                // Images/Arrange dedicate the majority of the viewport to
                // the actual image. The old fixed headers consumed almost
                // the entire page at 125-200% DPI and collapsed PictureBox.
                page.RowStyles.Add(new RowStyle(SizeType.Absolute, imagePage ? 60 : 72));
                page.RowStyles.Add(new RowStyle(SizeType.Absolute, imagePage ? 67 : 112));
                page.RowStyles.Add(new RowStyle(SizeType.Absolute, imagePage ? 66 : 77));
                page.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                page.RowStyles.Add(new RowStyle(SizeType.Absolute, imagePage ? 37 : 48));
                _host.Controls.Add(page);

                string[] titles =
                [
                    "Connect to Active Directory",
                    "Choose your campaign images",
                    "Arrange the image order",
                    "Choose days and times",
                    "Save or skip saving",
                    "Deploy or finish offline"
                ];
                string[] descriptions =
                [
                    "Choose a Windows AD connection or prepare the campaign offline. No AD or GPO changes are made during sign-in.",
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
                    AutoEllipsis = false,
                    Font = new Font("Segoe UI", 9.5f)
                };
                page.Controls.Add(status, 0, 4);

                switch (_step)
                {
                    case 0:
                        RenderConnection(actions, body, status);
                        break;
                    case 1:
                        RenderImages(actions, body, status, false, selected);
                        break;
                    case 2:
                        RenderImages(actions, body, status, true, selected);
                        break;
                    case 3:
                        RenderSchedule(actions, body, status);
                        break;
                    case 4:
                        RenderSave(actions, body, status);
                        break;
                    case 5:
                        RenderDeploy(actions, body, status);
                        break;
                }
                _back.Enabled = _step > 0;
                _next.Text = _step == 5 && _deployChoice?.Checked == true
                    ? "Deploy GPO..." : _step == 5 ? "Finish" :
                    _step == 4 && _skipProjectSave ? "Continue (no save)" :
                    _step == 4 && !_saved ? "Save & Next" :
                    _step == 0 ? "Continue" : "Next";
                _next.Enabled = _step is 0 or 5 || _owner._project.ImagePaths.Count > 0;
                _footnote.Text = _step == 0 ? "AD sign-in is optional" :
                    _step >= 4 && _skipProjectSave && !_saved
                        ? "Local project not saved"
                        : _owner._project.ImagePaths.Count + " image(s) selected";
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
                Text = caption, Width = width, Height = 43,
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
            Text = text, Dock = DockStyle.Fill,
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
                ? "Connected to AD: " + _owner._adSnapshot!.Domain + ". You may continue."
                : "No AD session yet. Choose the connection wizard or offline mode.";
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
                Dock = DockStyle.Fill, RowCount = 4, ColumnCount = 1,
                Padding = new Padding(9, 7, 9, 9)
            };
            schedule.RowStyles.Add(new RowStyle(SizeType.Absolute, 35));
            schedule.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
            schedule.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
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
            zone.SelectedIndexChanged += (_, _) =>
            {
                string id = TimeZoneChoices.SelectedId(zone);
                if (id.Length == 0) return;
                try
                {
                    _owner.ChooseCampaignTimeZone(id);
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
            var feedbackSummary = _owner._project.EnableFeedback
                ? "Client Health: ENABLED\n" + _owner._project.FeedbackShare +
                    "  |  every " + _owner._project.FeedbackIntervalMinutes + " min"
                : "Client Health: DISABLED. No client reports until enabled.";
            var feedbackInfo = new Label
            {
                Text = feedbackSummary,
                Name = "WizardFeedbackConfiguration",
                Dock = DockStyle.Fill,
                ForeColor = _owner._project.EnableFeedback ? UiStyle.Foreground : UiStyle.Muted,
                BackColor = UiStyle.SoftBlue, AutoEllipsis = true,
                Padding = new Padding(9, 7, 9, 7),
                Font = new Font("Segoe UI", 9.5f)
            };
            schedule.Controls.Add(feedbackInfo, 0, 2);
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
            schedule.Controls.Add(list, 0, 3);
            body.Controls.Add(schedule);
            status.Text = "Time zone: " + _owner._project.TimeZoneId +
                (_everConfiguredSchedule || _owner._wizardScheduleConfigured
                    ? " | Schedule edited. Continue to Save."
                    : " | Select a time zone and review scheduled changes.");
        }

        private void RenderSave(FlowLayoutPanel actions, Panel body, Label status)
        {
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
            FitCampaignName();
            status.Text = _saved
                ? "Project saved. Continue to Deploy or finish offline."
                : _skipProjectSave
                    ? "Local save skipped. Continue to Deploy without a project file."
                    : "Choose Save project or Skip saving & continue. Validation is required either way.";
        }

        private void RenderDeploy(FlowLayoutPanel actions, Panel body, Label status)
        {
            Action(actions, "Open GPO Deployment Wizard...", () =>
            {
                _deployChoice!.Checked = true;
                Deploy();
            }, 290, true);
            Action(actions, "Finish offline", () =>
            {
                DialogResult = DialogResult.OK;
                Close();
            }, 175);
            var options = new TableLayoutPanel
            {
                Dock = DockStyle.Top, RowCount = 2, ColumnCount = 1,
                Height = 130, Padding = new Padding(10)
            };
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
            options.RowStyles.Add(new RowStyle(SizeType.Absolute, 57));
            _deployChoice = new RadioButton
            {
                Text = "Deploy GPO to selected OU and computer/group assignments",
                Dock = DockStyle.Fill, Checked = _owner.AdReady
            };
            var offline = new RadioButton
            {
                Text = _saved
                    ? "Finish offline - local project is saved"
                    : "Finish offline - keep the campaign in the editor",
                Dock = DockStyle.Fill, Checked = !_owner.AdReady
            };
            _deployChoice.CheckedChanged += (_, _) =>
            {
                if (_step == 5)
                    _next.Text = _deployChoice.Checked ? "Deploy GPO..." : "Finish";
            };
            options.Controls.Add(_deployChoice, 0, 0);
            options.Controls.Add(offline, 0, 1);
            body.Controls.Add(options);
            status.Text = _skipProjectSave && !_saved
                ? "No local project file was saved. Deploy or return to the editor."
                : _owner.AdReady
                    ? "AD connected: choose deployment or finish without making AD changes."
                    : "Offline: connect to AD in the deployment wizard only if needed.";
        }

        private void Deploy()
        {
            if (!_owner.AdReady)
            {
                _owner.ConnectAndReadAd();
                if (!_owner.AdReady) { RenderPage(); return; }
            }
            _owner.OpenDomainDeployment();
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
                        if (!_owner.AdReady)
                        {
                            RenderPage();
                            return;
                        }
                    }
                    break;
                case 1:
                case 2:
                    if (_owner._project.ImagePaths.Count == 0)
                    {
                        MessageBox.Show(this, "Add at least one image to continue.",
                            "Campaign Wizard", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        return;
                    }
                    break;
                case 3:
                    try
                    {
                        _owner.ReadControls();
                        CampaignSchedule.Validate(_owner._project, requireImages: true);
                    }
                    catch (Exception ex) when (ex is CampaignValidationException or FileNotFoundException)
                    {
                        MessageBox.Show(this, ex.Message, "Check schedule",
                            MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return;
                    }
                    break;
                case 4:
                    // Skip saving never bypasses image/schedule validation.
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
                    if (ShouldSaveCampaignProject(_saved, _skipProjectSave))
                    {
                        if (!_owner.TrySaveProject()) return;
                        _saved = true;
                    }
                    break;
                case 5:
                    if (_deployChoice?.Checked == true)
                    {
                        Deploy();
                        return;
                    }
                    DialogResult = DialogResult.OK;
                    Close();
                    return;
            }
            _step = Math.Min(5, _step + 1);
            RenderPage();
        }
    }
}
