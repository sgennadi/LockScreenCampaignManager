using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

public sealed partial class MainForm
{
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
            "1 / 4  Choose images first. No AD permissions needed.",
            "2 / 4  Set image order, days and time.",
            "3 / 4  Review and save project locally (no admin needed).",
            "4 / 4  Optional deployment. Your local project is ready without AD."
        ];
        _workflowHint.Text = _advancedMode
            ? "Advanced mode  •  All tools available; GPO actions still require fresh AD verification."
            : hints[Math.Clamp(_workflowStep, 0, 3)] +
              $"  Images: {_project.ImagePaths.Count}";
        _workflowBack.Enabled = _workflowStep > 0;
        _workflowNext.Text = _workflowStep == 2 ? "Optional AD" : "Next";
        _workflowNext.Visible = _workflowStep < 3;
        _workflowNext.Enabled = _workflowStep switch
        {
            0 => _project.ImagePaths.Count > 0,
            1 => _project.ImagePaths.Count > 0,
            2 => _project.ImagePaths.Count > 0,
            _ => false
        };
        _workflowAction.Text = _workflowStep switch
        {
            0 => "Add images...",
            1 => "Edit schedule...",
            2 => "Save project...",
            _ => AdReady ? "Deploy wizard..." : "Connect to AD..."
        };
        _workflowAction.Enabled = true;
        _imageScheduleButton.Enabled = _project.ImagePaths.Count > 0;
    }

    private void AdvanceWorkflow()
    {
        ReadControls();
        if (_workflowStep is 0 or 1 or 2 && _project.ImagePaths.Count == 0) return;
        if (_workflowStep == 1)
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
        _workflowStep = Math.Min(3, _workflowStep + 1);
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
            case 1: OpenScheduleEditor(); break;
            case 2: SaveProject(); break;
            case 3:
                if (AdReady) OpenDomainDeployment();
                else ConnectAndReadAd();
                break;
        }
        UpdateWorkflow();
    }
}
