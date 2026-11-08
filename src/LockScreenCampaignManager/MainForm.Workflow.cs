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
            Text = "The Wizard first offers AD sign-in (or Skip AD), then guides you " +
                "through images, ordering, schedule, saving, and optional GPO deployment. " +
                "Deployment checks GPO permissions for your selected OU and group. " +
                "Cancel leaves the editor available.",
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
            _workflowStep = AdReady ? 1 : 0;
            SetWorkflowMode(false);
            _status.Text = _workflowStep == 0
                ? "Wizard: connect to AD first, or choose Skip AD to prepare locally."
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
        if (_footerLayout is not null && _workflowBar is not null)
        {
            // Advanced mode doesn't show a blank, reserved Wizard navigation strip.
            // Give its full height back to the grouped manual menus.
            _workflowBar.Visible = !advanced;
            // Reclaim the wizard row in Advanced; keep it pinned above images in Wizard.
            if (_workflowBar.Parent is TableLayoutPanel owner)
                owner.RowStyles[2].Height = advanced ? 0 : 106;
        }
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
            "1 / 6  Connect to AD to check directory access, or work offline.",
            "2 / 6  Import image files or choose a folder.",
            "3 / 6  Review and arrange image order.",
            "4 / 6  Configure schedule and change time.",
            "5 / 6  Validate and save the campaign locally.",
            "6 / 6  Deploy GPO: select OU/group and check write permissions."
        ];
        _workflowHint.Text = _advancedMode
            ? "Advanced tools: choose Project, Images, Review or AD / GPO below."
            : "STEP " + (_workflowStep + 1) + " OF 6  •  " +
              hints[Math.Clamp(_workflowStep, 0, 5)].Split("  ", 2).Last() +
              $"  |  Images: {_project.ImagePaths.Count}";
        _workflowHint.AccessibleDescription = _workflowHint.Text;
        _workflowBack.Enabled = _workflowStep > 0;
        _workflowNext.Visible = true;
        _workflowNext.Text = _workflowStep == 0
            ? (AdReady ? "Next" : "Skip AD")
            : _workflowStep == 5 ? "Finish offline" : "Next";
        _workflowNext.Enabled = _workflowStep is 0 or 5 || _project.ImagePaths.Count > 0;
        _workflowAction.Text = _workflowStep switch
        {
            0 => AdReady ? "AD connected" : "Connect to AD...",
            1 => "Add images...",
            2 => "Arrange images...",
            3 => "Edit schedule...",
            4 => "Validate / save...",
            _ => AdReady ? "GPO deployment..." : "Connect to AD..."
        };
        _workflowAction.Enabled = _workflowStep == 0 ? !AdReady :
            _workflowStep is 1 || _project.ImagePaths.Count > 0;
        _workflowAction.BackColor = UiStyle.Accent;
        _workflowAction.ForeColor = Color.White;
        _workflowNext.BackColor = UiStyle.Surface;
        _workflowNext.ForeColor = UiStyle.Navy;
        _workflowBack.BackColor = UiStyle.Surface;
        _workflowBack.ForeColor = UiStyle.Navy;
        _imageScheduleButton.Enabled = _project.ImagePaths.Count > 0;
    }

    private void AdvanceWorkflow()
    {
        ReadControls();
        if (_workflowStep == 5)
        {
            _status.Text = "Campaign finished locally. No AD or GPO changes were made.";
            MessageBox.Show(this,
                "The campaign is saved locally. Nothing was deployed to AD. " +
                "You can reopen the project or use Advanced > AD / GPO when ready.",
                "Finished offline", MessageBoxButtons.OK, MessageBoxIcon.Information);
            SetWorkflowMode(true);
            return;
        }
        if (_workflowStep is > 0 and < 5 && _project.ImagePaths.Count == 0)
        {
            _status.Text = "Add at least one image before continuing.";
            return;
        }
        if (_workflowStep == 3 || _workflowStep == 4)
        {
            try { CampaignSchedule.Validate(_project, requireImages: true); }
            catch (CampaignValidationException ex) { ShowCampaignValidation(ex.Message); return; }
            catch (FileNotFoundException)
            {
                ShowCampaignValidation("An image file no longer exists."); return;
            }
            catch (Exception ex) { UiErrorDialog.Show(this, "Validate campaign", ex); return; }
        }
        _workflowStep = Math.Min(5, _workflowStep + 1);
        _status.Text = _workflowStep == 5
            ? "Final stage: connect to AD (if needed), then choose OU/group and check GPO rights."
            : "Step " + (_workflowStep + 1) + ": follow the highlighted action above.";
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
            case 0:
                if (!AdReady) ConnectAndReadAd();
                break;
            case 1:
                AddImages();
                break;
            case 2:
                _arrangeImagesButton?.PerformClick();
                break;
            case 3:
                OpenScheduleEditor();
                break;
            case 4:
                try
                {
                    ReadControls();
                    CampaignSchedule.Validate(_project, requireImages: true);
                    if (TrySaveProject())
                    {
                        _workflowStep = 5;
                        _status.Text = "Campaign saved. Final step: choose AD deployment, or finish offline.";
                    }
                }
                catch (CampaignValidationException ex) { ShowCampaignValidation(ex.Message); }
                catch (FileNotFoundException)
                {
                    ShowCampaignValidation("A selected image no longer exists.");
                }
                catch (Exception ex)
                {
                    UiErrorDialog.Show(this, "Validate before saving", ex);
                }
                break;
            case 5:
                if (!AdReady) ConnectAndReadAd();
                if (AdReady) OpenDomainDeployment();
                break;
        }
        UpdateWorkflow();
    }
}
