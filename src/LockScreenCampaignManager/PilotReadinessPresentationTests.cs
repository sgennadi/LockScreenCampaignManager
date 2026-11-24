using System.Windows.Forms;

namespace LockScreenCampaignManager;

internal static class PilotReadinessPresentationTests
{
    private const string Gpo = "Synthetic GPO (AC) / Policy 01";
    private const string Id = "01234567-89ab-cdef-0123-456789abcdef";
    private const string Next = "Ranked AD/SYSVOL source only. Confirm the actual user, active power plan and endpoint policy; not applied RSoP.";

    private static DomainPilotFinding Fixture() =>
        new PilotTimerPresentation("Display AC", 120, Gpo, Id, "Registry.pol",
            600, false).ToFinding(Next);

    internal static void Run()
    {
        var finding = Fixture();
        string english = finding.Observation;
        foreach (string language in new[] { "English", "Russian", "Hebrew" })
        {
            string text = PilotFindingPresentation.Observation(finding, language);
            if (!text.Contains(UiLocale.Translate("Display off (AC)", language), StringComparison.Ordinal) ||
                !text.Contains(UiLocale.Translate("Earlier than Screen Saver", language), StringComparison.Ordinal) ||
                !text.Contains(UiLocale.Translate("Campaign Screen Saver timeout", language), StringComparison.Ordinal) ||
                !text.Contains(Gpo + " [" + Id + "] | Registry.pol", StringComparison.Ordinal) ||
                !text.Contains("120", StringComparison.Ordinal) ||
                !text.Contains("600", StringComparison.Ordinal) ||
                (language != "English" && text == english))
                throw new InvalidOperationException("Timer evidence lost identity or localization.");
            string details = PilotFindingPresentation.Details(finding, language);
            if (!details.Contains(text, StringComparison.Ordinal) ||
                !details.Contains(UiLocale.Translate("Next step", language), StringComparison.Ordinal) ||
                !details.Contains(UiLocale.Translate(Next, language), StringComparison.Ordinal))
                throw new InvalidOperationException("Selected-row details differ from the grid.");
            var unresolved = new PilotTimerPresentation("Sleep DC", null, "", "", "",
                null, false).ToFinding(Next);
            if (unresolved.State != "Not verified" ||
                !PilotFindingPresentation.Observation(unresolved, language).Contains(
                    UiLocale.Translate("Timer source precedence unresolved.", language),
                    StringComparison.Ordinal))
                throw new InvalidOperationException("Unknown timer state lost localization.");
        }
        if (finding.Observation != english || finding.State != "Source candidate" ||
            finding.TimerPresentation?.GpoId != Id)
            throw new InvalidOperationException("Display changed canonical evidence.");
        var never = new PilotTimerPresentation("Display DC", 0, Gpo, Id, "Registry.pol",
            600, true).ToFinding(Next);
        if (!never.Observation.Contains("0 = Never (this timer)", StringComparison.Ordinal) ||
            !never.Observation.Contains("AD Screen Saver timeout", StringComparison.Ordinal))
            throw new InvalidOperationException("Zero timer or AD reference label changed.");
    }

    // Existing offline WinForms smoke only; no shown dialog, network or saved settings.
    internal static void RunLayout(Form form)
    {
        var grid = form.Controls.Find("PilotReadinessFindings", true)
            .OfType<DataGridView>().Single();
        var details = form.Controls.Find("PilotReadinessDetails", true)
            .OfType<TextBox>().Single();
        var finding = Fixture();
        int index = grid.Rows.Add(PilotFindingPresentation.Cells(finding, "English"));
        grid.Rows[index].Tag = finding;
        grid.ClearSelection();
        grid.CurrentCell = grid.Rows[index].Cells[0];
        grid.Rows[index].Selected = true;
        foreach (int scale in new[] { 100, 150, 200 })
            foreach (string language in new[] { "English", "Russian", "Hebrew" })
            {
                UiStyle.Apply(form, new UiPreferences {
                    UiScalePercent = scale, Language = language
                });
                form.PerformLayout();
                if (grid.AutoSizeColumnsMode != DataGridViewAutoSizeColumnsMode.Fill ||
                    grid.AutoSizeRowsMode != DataGridViewAutoSizeRowsMode.DisplayedCellsExceptHeaders ||
                    grid.DefaultCellStyle.WrapMode != DataGridViewTriState.True ||
                    !details.WordWrap || details.ScrollBars != ScrollBars.Vertical ||
                    grid.RightToLeft != RightToLeft.No || details.RightToLeft != RightToLeft.No)
                    throw new InvalidOperationException("Pilot grid or detail layout is not readable/LTR.");
                if (!ReferenceEquals(grid.Rows[index].Tag, finding) ||
                    grid.Columns[0].HeaderText != UiLocale.Translate("Area", language) ||
                    grid.Rows[index].Cells[2].Value?.ToString() !=
                        PilotFindingPresentation.Observation(finding, language) ||
                    !details.Text.Contains(PilotFindingPresentation.Details(finding, language),
                        StringComparison.Ordinal))
                    throw new InvalidOperationException("Pilot review language switch lost row/details identity.");
            }
    }
}
