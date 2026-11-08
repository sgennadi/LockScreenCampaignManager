using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

public sealed partial class MainForm
{
    // Explicit launch choice. Cancel closes onboarding, never the editor.
    // The Wizard never connects to AD without a separate user action.
    private void ShowStartupWizardChoice()
    {
        if (IsDisposed || Disposing) return;
        using var dialog = new Form
        {
            Text = "Start a lock screen campaign",
            StartPosition = FormStartPosition.CenterParent,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            MaximizeBox = false,
            MinimizeBox = false,
            ShowInTaskbar = false,
            AutoScaleMode = AutoScaleMode.Dpi,
            Font = new Font("Segoe UI", 10f),
            ClientSize = new Size(555, 270),
            MinimumSize = new Size(550, 300),
            BackColor = UiStyle.Canvas
        };
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Padding = new Padding(22, 20, 22, 16)
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 55));
        panel.Controls.Add(new Label
        {
            Text = "How would you like to start?",
            Dock = DockStyle.Fill,
            ForeColor = UiStyle.Navy,
            Font = new Font("Segoe UI Semibold", 16f, FontStyle.Bold)
        }, 0, 0);
        panel.Controls.Add(new Label
        {
            Text = "Wizard guides you through images, arranging their order, schedule and saving. " +
                "Active Directory deployment is optional. Advanced opens all tools. " +
                "Cancel keeps the editor open.",
            Dock = DockStyle.Fill,
            ForeColor = UiStyle.Foreground
        }, 0, 1);
        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 27));
        var guided = new Button { Text = "Start Wizard", Dock = DockStyle.Fill,
            Margin = new Padding(0, 5, 8, 5), DialogResult = DialogResult.Yes,
            Tag = "NoIcon", BackColor = UiStyle.Accent, ForeColor = Color.White };
        var advanced = new Button { Text = "Advanced", Dock = DockStyle.Fill,
            Margin = new Padding(0, 5, 8, 5), DialogResult = DialogResult.No,
            Tag = "NoIcon" };
        var cancel = new Button { Text = "Cancel", Dock = DockStyle.Fill,
            Margin = new Padding(0, 5, 0, 5), DialogResult = DialogResult.Cancel,
            Tag = "NoIcon" };
        buttons.Controls.Add(guided, 0, 0);
        buttons.Controls.Add(advanced, 1, 0);
        buttons.Controls.Add(cancel, 2, 0);
        panel.Controls.Add(buttons, 0, 2);
        dialog.Controls.Add(panel);
        dialog.AcceptButton = guided;
        dialog.CancelButton = cancel;
        UiStyle.Apply(dialog);
        var result = dialog.ShowDialog(this);
        if (result == DialogResult.Yes)
        {
            _workflowStep = _project.ImagePaths.Count > 0 ? 1 : 0;
            SetWorkflowMode(false);
            _status.Text = _workflowStep == 0
                ? "Wizard: add images or import a folder."
                : "Wizard: arrange imported images, then set the schedule.";
        }
        else if (result == DialogResult.No)
        {
            SetWorkflowMode(true);
            _status.Text = "Advanced tools are available. No AD changes have been made.";
        }
        else
        {
            _status.Text = "Wizard cancelled. Local editor remains available.";
        }
        UpdateWorkflow();
    }

    private bool AdReady => _adScope.Matches(
        _adSnapshot, _company, DateTimeOffset.UtcNow);

    private void SetWorkflowMode(bool advanced)
    {
        _advancedMode = advanced;
        _advancedButtons.Visible = advanced;
        _quickButtons.Visible = !advanced;
        _workflowButtons.Visible = !advanced;
        _wizardMode.Enabled = advanced;
        _advancedModeButton.Enabled = !advanced;
        _wizardMode.BackColor = advanced ? UiStyle.Surface : UiStyle.Accent;
        _wizardMode.ForeColor = advanced ? UiStyle.Foreground : Color.White;
        _advancedModeButton.BackColor = advanced ? UiStyle.Accent : UiStyle.Surface;
        _advancedModeButton.ForeColor = advanced ? Color.White : UiStyle.Foreground;
        UpdateWorkflow();
    }

    private void UpdateWorkflow()
    {
        if (_workflowHint.IsDisposed) return;
        string[] hints = [
            "1 / 5  Add image files or import a folder.",
            "2 / 5  Arrange image order (optional if already correct).",
            "3 / 5  Configure dates, weekdays and change time.",
            "4 / 5  Review images and save the local project.",
            "5 / 5  Optional: connect AD and deploy GPO."
        ];
        _workflowHint.Text = _advancedMode
            ? "Advanced: all tools available. AD deployment checks permissions."
            : hints[Math.Clamp(_workflowStep, 0, 4)] +
              $"  Images: {_project.ImagePaths.Count}";
        _workflowBack.Enabled = _workflowStep > 0;
        _workflowNext.Text = _workflowStep == 3 ? "Optional AD" : "Next";
        _workflowNext.Visible = _workflowStep < 4;
        _workflowNext.Enabled = _project.ImagePaths.Count > 0;
        _workflowAction.Text = _workflowStep switch
        {
            0 => "Add images...",
            1 => "Arrange / edit...",
            2 => "Edit schedule...",
            3 => "Save project...",
            _ => AdReady ? "Deploy wizard..." : "Connect to AD..."
        };
        _workflowAction.Enabled = _workflowStep != 1 ||
            _project.ImagePaths.Count > 0;
        _imageScheduleButton.Enabled = _project.ImagePaths.Count > 0;
    }

    private void AdvanceWorkflow()
    {
        ReadControls();
        if (_workflowStep < 4 && _project.ImagePaths.Count == 0) return;
        if (_workflowStep == 2)
        {
            try
            {
                CampaignSchedule.Validate(_project, requireImages: true);
            }
            catch (CampaignValidationException ex)
            {
                ShowCampaignValidation(ex.Message);
                return;
            }
            catch (FileNotFoundException)
            {
                ShowCampaignValidation("One or more image files no longer exist.");
                return;
            }
            catch (Exception ex)
            {
                UiErrorDialog.Show(this, "Check campaign schedule", ex);
                return;
            }
        }
        _workflowStep = Math.Min(4, _workflowStep + 1);
        UpdateWorkflow();
    }

    private void SortImagesByFilename()
    {
        if (_project.ImagePaths.Count < 2) return;
        if (MessageBox.Show(this,
                "Sort images alphabetically by filename? This changes which image plays in each scheduled slot.",
                "Sort campaign images", MessageBoxButtons.YesNo, MessageBoxIcon.Question,
                MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
        _project.ImagePaths = _project.ImagePaths
            .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase).ToList();
        _projectDirty = true;
        UpdateRows(0);
        _status.Text = "Images sorted by filename. Verify the date/time assignment."; 
    }

    private void AutoMatchImageNames()
    {
        if (_project.ImagePaths.Count == 0) return;
        ReadControls();
        try
        {
            var proposal = ImageFilenameMatcher.Suggest(_project, _project.ImagePaths);
            var ordered = proposal.Slots.Select(x => x.SuggestedPath).ToArray();
            if (ordered.Any(x => x is null) ||
                ordered.Distinct(StringComparer.OrdinalIgnoreCase).Count() != ordered.Length ||
                proposal.ConfidentCount != ordered.Length)
            {
                MessageBox.Show(this,
                    $"Matched {proposal.ConfidentCount} of {ordered.Length} image slots confidently. " +
                    "Some filenames are ambiguous. No changes made. " +
                    "Rename the files using a number, weekday or date, or arrange them manually.",
                    "Auto match needs review", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            if (MessageBox.Show(this,
                    $"Matched all {ordered.Length} image(s) to their planned dates. " +
                    "Apply the suggested order?",
                    "Confirm automatic image order", MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            _project.ImagePaths = ordered.Select(x => x!).ToList();
            _projectDirty = true;
            UpdateRows(0);
            _status.Text = "Image order inferred from filenames. Review before deployment.";
        }
        catch (Exception ex) { UiErrorDialog.Show(this, "Auto match images", ex); }
    }

    private void RunWorkflowAction()
    {
        switch (_workflowStep)
        {
            case 0: AddImages(); break;
            case 1: _arrangeImagesButton?.PerformClick(); break;
            case 2: OpenScheduleEditor(); break;
            case 3: SaveProject(); break;
            case 4:
                if (AdReady) OpenDomainDeployment();
                else ConnectAndReadAd();
                break;
        }
        UpdateWorkflow();
    }
}
