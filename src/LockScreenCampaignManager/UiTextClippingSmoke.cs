using System.Drawing;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// UI-only regression checks for the actual clipping reported by operators:
/// Screen Saver Browse, assignment captions, AD version/header, warning
/// dialog caption/body/buttons. No window shown or external access made.
/// </summary>
internal static class UiTextClippingSmoke
{
    private static IEnumerable<Control> All(Control root)
    {
        yield return root;
        foreach (Control c in root.Controls)
            foreach (Control child in All(c))
                yield return child;
    }

    internal static void Check(Form form, string profile)
    {
        var all = All(form).ToArray();

        var browse = all.OfType<Button>()
            .FirstOrDefault(x => x.Name == "WizardScreenSaverBrowseCustom");
        if (browse is not null)
        {
            if (browse.Parent is not TableLayoutPanel row ||
                row.Name != "WizardScreenSaverPickerLayout" ||
                row.RowCount != 2 || row.ColumnCount != 2 ||
                row.GetRow(browse) != 1 || row.GetColumn(browse) != 1 ||
                row.RowStyles[1].SizeType != SizeType.AutoSize ||
                !browse.AutoSize || browse.AutoEllipsis)
                throw new InvalidOperationException(
                    "Screen Saver Browse must have its own auto-sized row: " + profile);

            int neededWidth = TextRenderer.MeasureText(
                browse.Text, browse.Font).Width +
                (browse.Image is null ? 24 : 48);
            if (browse.Width + 2 < neededWidth)
                throw new InvalidOperationException(
                    $"Screen Saver Browse caption clipped ({profile}): " +
                    $"{browse.Width} < {neededWidth}");

            var details = all.OfType<TableLayoutPanel>()
                .FirstOrDefault(x => x.Name == "WizardCampaignTypeDetails");
            var note = all.OfType<Label>()
                .FirstOrDefault(x => x.Name == "WizardScreenSaverPilotNotice");
            var custom = all.OfType<Label>()
                .FirstOrDefault(x => x.Name == "WizardCustomScreenSaverStatus");
            if (details is null || details.RowStyles.Count < 10 ||
                details.RowStyles[3].SizeType != SizeType.AutoSize ||
                details.RowStyles[8].SizeType != SizeType.AutoSize ||
                details.RowStyles[9].SizeType != SizeType.AutoSize ||
                note is null || !note.AutoSize || note.AutoEllipsis ||
                custom is null || !custom.AutoSize || custom.AutoEllipsis)
                throw new InvalidOperationException(
                    "Screen Saver pilot notice/controls cannot grow for translated text: " +
                    profile);
        }

        var deploymentVersion = all.OfType<Label>()
            .FirstOrDefault(x => x.Name == "DeploymentVersion");
        if (deploymentVersion is not null)
        {
            if (deploymentVersion.Parent is not TableLayoutPanel header ||
                header.ColumnStyles.Count < 2 ||
                header.ColumnStyles[1].SizeType != SizeType.Absolute ||
                deploymentVersion.AutoEllipsis)
                throw new InvalidOperationException(
                    "Deployment version is not in a measured header column.");
            int needed = TextRenderer.MeasureText(
                deploymentVersion.Text, deploymentVersion.Font).Width + 17;
            if (header.ColumnStyles[1].Width + 2 < needed ||
                deploymentVersion.Width + 12 < needed)
                throw new InvalidOperationException(
                    $"AD preview version clipped ({profile}): " +
                    $"{deploymentVersion.Width}px < {needed}px");
            var title = all.OfType<Label>().FirstOrDefault(x =>
                x.Name == "DeploymentHeaderTitle");
            if (title is null || title.AutoEllipsis)
                throw new InvalidOperationException(
                    "AD deployment title should wrap instead of ellipsis.");
        }

        if (form is DomainDeploymentForm)
        {
            var connection = all.OfType<Panel>().FirstOrDefault(x =>
                x.Name == "DeploymentConnectionPanel");
            var layout = all.OfType<TableLayoutPanel>().FirstOrDefault(x =>
                x.Name == "DeploymentConnectionContents");
            var accounts = all.OfType<TableLayoutPanel>().FirstOrDefault(x =>
                x.Name == "DeploymentAccountSection");
            var buttons = all.OfType<FlowLayoutPanel>().FirstOrDefault(x =>
                x.Name == "DeploymentAccountButtons");
            var current = all.OfType<RadioButton>().FirstOrDefault(x =>
                x.Name == "DeploymentCurrentAccount");
            var alternate = all.OfType<RadioButton>().FirstOrDefault(x =>
                x.Name == "DeploymentAlternateAccount");
            var userField = all.OfType<TableLayoutPanel>().FirstOrDefault(x =>
                x.Name == "DeploymentConnectionAdUserField");
            var passwordField = all.OfType<TableLayoutPanel>().FirstOrDefault(x =>
                x.Name == "DeploymentConnectionPasswordField");
            if (connection is null || !connection.AutoScroll || layout is null ||
                !layout.AutoSize || layout.RowStyles.Cast<RowStyle>().Any(x =>
                    x.SizeType != SizeType.AutoSize) ||
                accounts is null || !accounts.AutoSize ||
                accounts.RowStyles.Cast<RowStyle>().Any(x => x.SizeType != SizeType.AutoSize) ||
                buttons is null || !buttons.AutoSize || !buttons.WrapContents ||
                current is null || alternate is null ||
                !current.AutoSize || !alternate.AutoSize ||
                userField is null || passwordField is null ||
                userField.RowStyles.Cast<RowStyle>().Any(x => x.SizeType != SizeType.AutoSize) ||
                passwordField.RowStyles.Cast<RowStyle>().Any(x => x.SizeType != SizeType.AutoSize))
                throw new InvalidOperationException(
                    "Current account / AD user / password / buttons must retain " +
                    "scrollable, auto-sized layout: " + profile);
            foreach (var field in new[] { userField, passwordField })
            {
                var label = field.Controls.OfType<Label>().FirstOrDefault();
                if (label is null || !label.AutoSize || label.AutoEllipsis)
                    throw new InvalidOperationException(
                        "AD account field caption clipped: " + profile);
            }
        }

        var fields = all.OfType<TableLayoutPanel>()
            .FirstOrDefault(x => x.Name == "AssignmentScrollableFields");
        if (fields is not null)
        {
            if (!fields.AutoSize || fields.RowStyles.Count < 16 ||
                fields.RowStyles[0].SizeType != SizeType.AutoSize ||
                fields.RowStyles[2].SizeType != SizeType.AutoSize ||
                fields.RowStyles[6].SizeType != SizeType.AutoSize ||
                fields.RowStyles[14].SizeType != SizeType.AutoSize ||
                fields.RowStyles[15].SizeType != SizeType.AutoSize)
                throw new InvalidOperationException(
                    "Assignment editor must grow field rows and scroll: " + profile);
            var captions = all.OfType<Label>()
                .Where(x => x.Parent is TableLayoutPanel p &&
                    p.RowCount == 2 && p.Parent == fields &&
                    p.Controls.OfType<ComboBox>().Any())
                .ToArray();
            if (captions.Length < 4 ||
                captions.Any(x => !x.AutoSize || x.AutoEllipsis))
                throw new InvalidOperationException(
                    "OU/group choice captions must wrap without clipping: " + profile);
        }

        if (form is ModernMessageDialog)
        {
            var outer = all.OfType<TableLayoutPanel>()
                .FirstOrDefault(x => x.Controls.OfType<Panel>()
                    .Any(p => p.Name == "ModernDialogAccent"));
            var title = all.OfType<Label>()
                .FirstOrDefault(x => x.Name == "ModernDialogHeaderTitle");
            var hint = all.OfType<Label>()
                .FirstOrDefault(x => x.Name == "ModernDialogHeaderHint");
            var body = all.OfType<TextBox>()
                .FirstOrDefault(x => x.Name == "ModernDialogBody");
            var actions = all.OfType<FlowLayoutPanel>()
                .FirstOrDefault(x => x.Name == "ModernDialogActionButtons");
            if (outer is null || title is null || hint is null ||
                body is null || actions is null ||
                outer.RowStyles.Count != 4 ||
                title.AutoEllipsis || hint.AutoEllipsis ||
                body.TabStop || !body.Multiline || !body.ReadOnly ||
                !body.WordWrap || body.ScrollBars != ScrollBars.Vertical ||
                outer.RowStyles[1].Height < 112 ||
                outer.RowStyles[3].Height < 76)
                throw new InvalidOperationException(
                    "Modern warning header/body/footer must auto-fit, stay readable " +
                    "and preserve safe modal semantics: " + profile);
            foreach (var button in actions.Controls.OfType<Button>())
            {
                int width = TextRenderer.MeasureText(
                    button.Text, button.Font).Width + 20;
                if (button.Width + 2 < width)
                    throw new InvalidOperationException(
                        "Modern dialog action text clipped: " +
                        button.Text + " at " + profile);
            }
            if (body.Height < 60)
                throw new InvalidOperationException(
                    "Warning body hidden behind header/footer: " + profile);
        }
    }
}
