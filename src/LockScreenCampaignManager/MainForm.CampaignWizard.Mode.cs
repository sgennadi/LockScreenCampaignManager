using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

public sealed partial class MainForm
{
    private sealed partial class CampaignWizardDialog
    {
        // CI-only verification: a user Screen Saver-only campaign goes from
        // Type straight to Save, with zero JPEGs and no AD/LDAP access.
        internal void ExerciseScreenSaverSkipForSmokeTest()
        {
            _step = 1;
            RenderPage();
            var selector = _host.Controls.OfType<TableLayoutPanel>()
                .SelectMany(panel => Walk(panel))
                .OfType<ComboBox>()
                .Single(x => x.Name == "WizardCampaignType");
            selector.SelectedItem = CampaignTargetMode.ScreenSaver;
            if (NeedsLockImages || _owner._project.ImagePaths.Count != 0)
                throw new InvalidOperationException(
                    "Screen Saver type must not require JPEG images.");
            NextStep();
            if (_step != 5 || !_next.Enabled ||
                !_host.Controls.OfType<TableLayoutPanel>()
                    .SelectMany(panel => Walk(panel))
                    .OfType<TextBox>()
                    .Any(x => x.Name == "WizardCampaignNameInput"))
                throw new InvalidOperationException(
                    "Screen Saver-only did not skip images/arrangement/schedule to Save.");
        }

        private static IEnumerable<Control> Walk(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (Control grandchild in Walk(child))
                    yield return grandchild;
            }
        }

        // New campaigns have no chosen mode. Historical projects with saved
        // images/target rows may infer their legacy mode without changing JSON.
        private bool HasChosenCampaignType =>
            _owner._project.CampaignModeExplicit ||
            (!_owner._project.RequiresCampaignTypeChoice &&
                (_owner._project.ImagePaths.Count > 0 ||
                 _owner._project.TargetAssignments.Count > 0));

        private bool NeedsLockImages =>
            _owner._project.EffectiveCampaignMode is
                CampaignTargetMode.LockScreen or CampaignTargetMode.Both ||
            (_owner._project.EffectiveCampaignMode == CampaignTargetMode.ScreenSaver &&
                _owner._project.ScreenSaver.UseJpegSlideshow);

        // Track only the name the wizard itself generated. A manually typed
        // name is never silently replaced when the user changes mode.
        private string? _suggestedCampaignName;

        private void SuggestCampaignName(CampaignTargetMode selected)
        {
            var currentName = _owner._name.Text.Trim();
            if (currentName.Length > 0 &&
                !string.Equals(currentName, _suggestedCampaignName,
                    StringComparison.Ordinal))
                return;
            string suggestion = selected switch
            {
                CampaignTargetMode.LockScreen => "Lock Screen Campaign",
                CampaignTargetMode.ScreenSaver => "Screen Saver Campaign",
                _ => "Lock Screen and Screen Saver Campaign"
            };
            _suggestedCampaignName = suggestion;
            _owner._name.Text = suggestion;
            _owner._project.Name = suggestion;
        }

        private void RenderCampaignType(
            FlowLayoutPanel actions, Panel body, Label status)
        {
            // All three choices are offered before image selection.
            // A Screen Saver-only project goes directly to Save / Skip.
            var heading = new Label
            {
                Text = "Choose the campaign content first",
                Font = new Font("Segoe UI Semibold", 12f),
                ForeColor = UiStyle.Navy, AutoSize = true,
                Margin = new Padding(5, 10, 5, 10)
            };
            Action(actions, "Lock Screen / Screen Saver / Both", () =>
            {
                status.Text = "Choose one mode below. Separate computer and user " +
                    "groups are assigned through the Deployment Wizard.";
            }, 305, true);

            var details = new TableLayoutPanel
            {
                Name = "WizardCampaignTypeDetails",
                Dock = DockStyle.Top, ColumnCount = 1, RowCount = 9,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Height = 650, Padding = new Padding(14, 12, 14, 14)
            };
            foreach (int h in new[] { 53, 55, 87, 58, 52, 59, 57, 54, 115 })
                details.RowStyles.Add(new RowStyle(SizeType.Absolute, h));
            details.Controls.Add(heading, 0, 0);

            var mode = new ComboBox
            {
                Name = "WizardCampaignType",
                Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = "Choose Lock Screen, Screen Saver or Both"
            };
            mode.Items.AddRange(Enum.GetValues<CampaignTargetMode>()
                .Cast<object>().ToArray());
            if (HasChosenCampaignType)
                mode.SelectedItem = _owner._project.EffectiveCampaignMode;
            else
                mode.SelectedIndex = -1; // Never preselect a new campaign.
            details.Controls.Add(mode, 0, 1);

            var message = new Label
            {
                Name = "WizardCampaignTypeDescription",
                Dock = DockStyle.Fill, AutoEllipsis = false,
                ForeColor = UiStyle.Navy, TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(7, 8, 7, 8)
            };
            details.Controls.Add(message, 0, 2);

            var saver = new ComboBox
            {
                Name = "WizardScreenSaverExecutable",
                Dock = DockStyle.Fill, DropDownStyle = ComboBoxStyle.DropDownList,
                AccessibleName = "Built-in Windows screen saver"
            };
            string customOption = T("Custom .scr / .exe (local pilot only)");
            saver.Items.AddRange(CampaignTargetAssignments.BuiltInScreenSavers
                .Cast<object>().Concat([customOption]).ToArray());
            saver.SelectedItem = _owner._project.ScreenSaver.CustomExecutablePath.Length > 0
                ? customOption : _owner._project.ScreenSaver.Executable;
            if (saver.SelectedIndex < 0) saver.SelectedIndex = 0;
            var saverRow = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1
            };
            saverRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
            saverRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            saverRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
            saverRow.Controls.Add(new Label {
                Text = "Screen Saver (.scr)", Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = false
            }, 0, 0);
            saverRow.Controls.Add(saver, 1, 0);
            var photo = new CheckBox {
                Name = "WizardScreenSaverJpegMode", Dock = DockStyle.Fill,
                Text = "JPG photo slideshow (use selected campaign images)",
                Checked = _owner._project.ScreenSaver.UseJpegSlideshow
            };
            var customBrowse = new Button {
                Name = "WizardScreenSaverBrowseCustom",
                Text = T("Browse .scr / .exe..."),
                Dock = DockStyle.Fill, AutoEllipsis = true,
                Margin = new Padding(6, 2, 2, 2)
            };
            customBrowse.Click += (_, _) =>
            {
                using var picker = new OpenFileDialog {
                    Title = T("Choose local Screen Saver executable (pilot only)"),
                    Filter = "Screen Saver or executable (*.scr;*.exe)|*.scr;*.exe|Screen Savers (*.scr)|*.scr|Executables (*.exe)|*.exe",
                    CheckFileExists = true, CheckPathExists = true,
                    Multiselect = false, RestoreDirectory = true
                };
                if (picker.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    var observation = CustomSaverPilotFile.Inspect(picker.FileName);
                    // Toggle controls first: their events clear any previous
                    // custom selection before committing the newly inspected PE.
                    photo.Checked = false;
                    saver.SelectedItem = customOption;
                    _owner._project.ScreenSaver.CustomExecutablePath = observation.Path;
                    _owner._project.ScreenSaver.CustomExecutableSha256 = observation.Sha256;
                    RefreshDescription(true);
                    MessageBox.Show(this,
                        T("Selected local custom Screen Saver for manual pilot testing only.") +
                        Environment.NewLine + observation.Path +
                        Environment.NewLine + "SHA-256: " + observation.Sha256 +
                        Environment.NewLine + T("Windows Authenticode: ") +
                        T(observation.TrustedSignature ? "TRUSTED" : "NOT VERIFIED") +
                        " (" + observation.SignatureStatus + ")" +
                        Environment.NewLine + Environment.NewLine +
                        T("A .scr is an executable. An .exe must explicitly implement /s, /p, /c to work as a Windows Screen Saver. This selection does not execute, convert or deploy the file. GPO delivery is blocked."),
                        T("Custom Screen Saver — pilot only"),
                        MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                catch (Exception ex) { UiErrorDialog.Show(this, "Select custom Screen Saver", ex); }
            };
            saverRow.Controls.Add(customBrowse, 2, 0);
            details.Controls.Add(saverRow, 0, 3);

            details.Controls.Add(photo, 0, 4);
            var slide = new NumericUpDown {
                Name = "WizardScreenSaverSlideSeconds",
                Minimum = 5, Maximum = 3600, Increment = 5,
                Value = Math.Clamp(_owner._project.ScreenSaver.SlideSeconds, 5, 3600),
                Dock = DockStyle.Fill
            };
            var slideRow = new TableLayoutPanel {
                Dock = DockStyle.Fill, RowCount = 1, ColumnCount = 2
            };
            slideRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
            slideRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            slideRow.Controls.Add(new Label {
                Text = "Change JPG every (s)", Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft
            }, 0, 0);
            slideRow.Controls.Add(slide, 1, 0);
            details.Controls.Add(slideRow, 0, 5);

            var idle = new NumericUpDown
            {
                Name = "WizardScreenSaverIdleSeconds",
                Dock = DockStyle.Fill, Minimum = 60, Maximum = 86400,
                Increment = 60,
                Value = Math.Clamp(_owner._project.ScreenSaver.IdleSeconds, 60, 86400)
            };
            var idleRow = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1
            };
            idleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
            idleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            idleRow.Controls.Add(new Label {
                Text = "Idle timeout (seconds)", Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = false
            }, 0, 0);
            idleRow.Controls.Add(idle, 1, 0);
            details.Controls.Add(idleRow, 0, 6);
            var password = new CheckBox
            {
                Name = "WizardScreenSaverRequirePassword",
                Text = "Require Windows sign-in on resume",
                Dock = DockStyle.Fill, Checked = _owner._project.ScreenSaver.PasswordOnResume
            };
            details.Controls.Add(password, 0, 7);
            var note = new Label
            {
                Text = "Built-in Windows .scr is GPO-ready. Choose Browse to select a " +
                    "custom .scr or compatible .exe for LOCAL PILOT ONLY; a regular EXE " +
                    "is not automatically a Windows Screen Saver. JPG slideshow can be " +
                    "exported as PhotoScreenSaver.scr from selected images. " +
                    "GPO delivery of custom/JPG executables is blocked.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = false,
                ForeColor = UiStyle.Muted, Padding = new Padding(8, 8, 8, 8)
            };
            details.Controls.Add(note, 0, 8);

            void RefreshDescription(bool persist)
            {
                if (mode.SelectedItem is not CampaignTargetMode selected)
                {
                    saver.Enabled = idle.Enabled = password.Enabled = false;
                    photo.Enabled = slide.Enabled = false;
                    customBrowse.Enabled = false;
                    message.Text = T("Choose a campaign type to continue. No images or schedule steps are skipped yet.");
                    status.Text = T("No campaign type selected. Choose Lock Screen, Screen Saver or Both.");
                    _next.Enabled = false;
                    return;
                }
                bool saverEnabled = selected != CampaignTargetMode.LockScreen;
                saver.Enabled = saverEnabled && !photo.Checked;
                photo.Enabled = saverEnabled;
                customBrowse.Enabled = saverEnabled && !photo.Checked;
                slide.Enabled = saverEnabled && photo.Checked;
                idle.Enabled = password.Enabled = saverEnabled;
                message.Text = T(selected switch
                {
                    CampaignTargetMode.LockScreen =>
                        "LOCK SCREEN: next select campaign images, arrange them, " +
                        "and choose days and times. Applies to computers.",
                    CampaignTargetMode.ScreenSaver when photo.Checked =>
                        "JPG SCREEN SAVER: select photos on the next page, arrange " +
                        "their order and set the slide interval. Local pilot package first.",
                    CampaignTargetMode.ScreenSaver when
                        saver.SelectedItem is string value && value == customOption =>
                        "CUSTOM SCREEN SAVER: selected local .scr/.exe is for manual pilot only. " +
                        "Automatic User GPO delivery is blocked; image stages are skipped.",
                    CampaignTargetMode.ScreenSaver =>
                        "SCREEN SAVER: built-in .scr; image/arrange/schedule " +
                        "pages are skipped. Applies to selected user group.",
                    _ => "BOTH: configure image schedule and user Screen Saver. " +
                        "Choose independent computer/user groups at Deploy."
                });
                status.Text = T("Selected type: ") + T(selected.ToString()) +
                    (saverEnabled ? T(" | idle ") + idle.Value + T(" s") : "") +
                    T(" | no AD changes made.");
                bool missingCustom = saverEnabled && !photo.Checked &&
                    saver.SelectedItem is string option && option == customOption &&
                    string.IsNullOrWhiteSpace(_owner._project.ScreenSaver.CustomExecutablePath);
                _next.Enabled = !missingCustom;
                if (missingCustom)
                    status.Text = T("Select a custom .scr/.exe using Browse before continuing.");
                if (!persist) return;
                // Repaint the rail as soon as the operator changes the type.
                // JPG Screen Saver needs images; only built-in Screen Saver
                // skips those wizard stages.
                for (int i = 2; i <= 4; i++)
                {
                    bool skip = selected == CampaignTargetMode.ScreenSaver && !photo.Checked;
                    _rail[i].Text = T(skip ? StepNames[i] + " (skip)" : StepNames[i]);
                    _rail[i].ForeColor = skip ? UiStyle.Muted : UiStyle.Navy;
                }
                _footnote.Text = T(selected == CampaignTargetMode.ScreenSaver && !photo.Checked
                    ? "Built-in Screen Saver: images not needed"
                    : "JPG/Lock Screen: image stages are available");
                SuggestCampaignName(selected);
                _owner._project.CampaignMode = selected;
                _owner._project.CampaignModeExplicit = true;
                bool customSelected = saver.SelectedItem is string chosen &&
                    chosen == customOption && !photo.Checked;
                if (!customSelected)
                {
                    _owner._project.ScreenSaver.CustomExecutablePath = "";
                    _owner._project.ScreenSaver.CustomExecutableSha256 = "";
                }
                _owner._project.ScreenSaver.Executable =
                    customSelected ? "scrnsave.scr" :
                    saver.SelectedItem as string ?? "scrnsave.scr";
                _owner._project.ScreenSaver.UseJpegSlideshow = photo.Checked;
                _owner._project.ScreenSaver.SlideSeconds = (int)slide.Value;
                _owner._project.ScreenSaver.IdleSeconds = (int)idle.Value;
                _owner._project.ScreenSaver.PasswordOnResume = password.Checked;
                _owner._projectDirty = true;
                _saved = false;
            }

            mode.SelectedIndexChanged += (_, _) => RefreshDescription(true);
            saver.SelectedIndexChanged += (_, _) => RefreshDescription(true);
            photo.CheckedChanged += (_, _) => RefreshDescription(true);
            slide.ValueChanged += (_, _) => RefreshDescription(true);
            idle.ValueChanged += (_, _) => RefreshDescription(true);
            password.CheckedChanged += (_, _) => RefreshDescription(true);
            body.Controls.Add(details);
            RefreshDescription(false);
        }
    }
}
