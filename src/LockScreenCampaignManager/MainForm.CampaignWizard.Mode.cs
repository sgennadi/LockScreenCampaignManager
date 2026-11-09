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
            _owner._project.ImagePaths.Count > 0 ||
            _owner._project.TargetAssignments.Count > 0;

        private bool NeedsLockImages =>
            _owner._project.EffectiveCampaignMode is
                CampaignTargetMode.LockScreen or CampaignTargetMode.Both;

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
                Dock = DockStyle.Top, ColumnCount = 1, RowCount = 7,
                AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Height = 470, Padding = new Padding(14, 12, 14, 14)
            };
            foreach (int h in new[] { 53, 55, 87, 58, 57, 54, 95 })
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
            saver.Items.AddRange(CampaignTargetAssignments.BuiltInScreenSavers
                .Cast<object>().ToArray());
            saver.SelectedItem = _owner._project.ScreenSaver.Executable;
            if (saver.SelectedIndex < 0) saver.SelectedIndex = 0;
            var saverRow = new TableLayoutPanel {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1
            };
            saverRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 185));
            saverRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            saverRow.Controls.Add(new Label {
                Text = "Screen Saver (.scr)", Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = false
            }, 0, 0);
            saverRow.Controls.Add(saver, 1, 0);
            details.Controls.Add(saverRow, 0, 3);

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
            details.Controls.Add(idleRow, 0, 4);
            var password = new CheckBox
            {
                Name = "WizardScreenSaverRequirePassword",
                Text = "Require Windows sign-in on resume",
                Dock = DockStyle.Fill, Checked = _owner._project.ScreenSaver.PasswordOnResume
            };
            details.Controls.Add(password, 0, 5);
            var note = new Label
            {
                Text = "Screen Saver: built-in Windows .scr, user OU/security group. " +
                    "Lock Screen: campaign images, computer OU/security group. " +
                    "Both can have different modes for each group. " +
                    "A built-in .scr does not display JPEG campaign slides.",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = false,
                ForeColor = UiStyle.Muted, Padding = new Padding(8, 8, 8, 8)
            };
            details.Controls.Add(note, 0, 6);

            void RefreshDescription(bool persist)
            {
                if (mode.SelectedItem is not CampaignTargetMode selected)
                {
                    saver.Enabled = idle.Enabled = password.Enabled = false;
                    message.Text = "Choose a campaign type to continue. No images or schedule steps are skipped yet.";
                    status.Text = "No campaign type selected. Choose Lock Screen, Screen Saver or Both.";
                    _next.Enabled = false;
                    return;
                }
                bool saverEnabled = selected != CampaignTargetMode.LockScreen;
                saver.Enabled = idle.Enabled = password.Enabled = saverEnabled;
                message.Text = selected switch
                {
                    CampaignTargetMode.LockScreen =>
                        "LOCK SCREEN: next select campaign images, arrange them, " +
                        "and choose days and times. Applies to computers.",
                    CampaignTargetMode.ScreenSaver =>
                        "SCREEN SAVER: images, arrangement and JPEG schedule " +
                        "are skipped. Applies to users; configure a user " +
                        "OU/security group during Deploy.",
                    _ => "BOTH: configure image schedule and user Screen Saver. " +
                        "Choose independent computer/user groups at Deploy."
                };
                status.Text = "Selected type: " + selected +
                    (saverEnabled ? " | idle " + idle.Value + " s" : "") +
                    " | no AD changes made.";
                _next.Enabled = true;
                if (!persist) return;
                // Repaint the rail as soon as the operator changes the type.
                // Only Screen Saver-only skips image pages. Both never skips.
                for (int i = 2; i <= 4; i++)
                {
                    bool skip = selected == CampaignTargetMode.ScreenSaver;
                    _rail[i].Text = skip ? StepNames[i] + " (skip)" : StepNames[i];
                    _rail[i].ForeColor = skip ? UiStyle.Muted : UiStyle.Navy;
                }
                _footnote.Text = selected == CampaignTargetMode.ScreenSaver
                    ? "Screen Saver: images not needed" : "All image stages are available";
                SuggestCampaignName(selected);
                _owner._project.CampaignMode = selected;
                _owner._project.CampaignModeExplicit = true;
                _owner._project.ScreenSaver.Executable =
                    saver.SelectedItem as string ?? "scrnsave.scr";
                _owner._project.ScreenSaver.IdleSeconds = (int)idle.Value;
                _owner._project.ScreenSaver.PasswordOnResume = password.Checked;
                _owner._projectDirty = true;
                _saved = false;
            }

            mode.SelectedIndexChanged += (_, _) => RefreshDescription(true);
            saver.SelectedIndexChanged += (_, _) => RefreshDescription(true);
            idle.ValueChanged += (_, _) => RefreshDescription(true);
            password.CheckedChanged += (_, _) => RefreshDescription(true);
            body.Controls.Add(details);
            RefreshDescription(false);
        }
    }
}
