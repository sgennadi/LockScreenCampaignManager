using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Partial, explicit operator-facing English/Hebrew UI dictionary.
/// Never translate user data, JSON, LDAP/UNC paths, log contents or mutable
/// ComboBox selection values. Missing translations deliberately stay English.
/// </summary>
internal static partial class UiLocale
{
    private sealed class ComboLanguage
    {
        internal string Language = "English";
        internal bool Attached;
    }

    private static readonly ConditionalWeakTable<ComboBox, ComboLanguage> ComboLanguages = new();
    // Header text is UI only. Preserve original column names and row values.
    private sealed class ColumnHeaderSource
    {
        internal string Caption;
        internal string LastApplied;
        internal ColumnHeaderSource(string caption)
        { Caption = caption; LastApplied = caption; }
    }
    private static readonly ConditionalWeakTable<DataGridViewColumn, ColumnHeaderSource> Headers = new();
    private static readonly ConditionalWeakTable<ColumnHeader, ColumnHeaderSource> ListHeaders = new();
    // Save each control's ORIGINAL caption, so in-place switching among three
    // languages does not confuse similar translated labels or command values.
    private sealed class CaptionSource
    {
        internal string English;
        internal string LastApplied;
        internal CaptionSource(string english, string lastApplied)
        { English = english; LastApplied = lastApplied; }
    }
    private static readonly ConditionalWeakTable<Control, CaptionSource> Captions = new();

    private static readonly Dictionary<string, string> Hebrew = new(StringComparer.Ordinal)
    {
        ["Interface settings"] = "הגדרות ממשק",
        ["Language"] = "שפת ממשק",
        ["UI density"] = "צפיפות ממשק",
        ["UI scale"] = "קנה מידה של הממשק",
        ["Font scale"] = "גודל הגופן",
        ["Apply"] = "החל",
        ["Auto"] = "אוטומטי",
        ["Compact"] = "קומפקטי",
        ["Comfortable"] = "מרווח",
        ["English"] = "אנגלית",
        ["Settings..."] = "הגדרות...",
        ["Diagnostics..."] = "אבחון...",
        ["Check updates..."] = "בדיקת עדכונים...",
        ["Review local lab evidence..."] = "הצגת דוחות בדיקה מקומיים...",
        ["Wizard"] = "אשף",
        ["Advanced"] = "מתקדם",
        ["Back"] = "חזרה",
        ["Next"] = "הבא",
        ["Next >"] = "הבא >",
        ["< Back"] = "< חזרה",
        ["Cancel"] = "ביטול",
        ["Close"] = "סגור",
        ["Close Wizard"] = "סגור אשף",
        ["Finish"] = "סיום",
        ["Finish offline"] = "סיים ללא חיבור",
        ["Continue"] = "המשך",
        ["Continue offline"] = "המשך ללא חיבור",
        ["Save"] = "שמור",
        ["Save project..."] = "שמור פרויקט...",
        ["Save project changes..."] = "שמור שינויים בפרויקט...",
        ["Open project..."] = "פתח פרויקט...",
        ["New campaign..."] = "קמפיין חדש...",
        ["Companies..."] = "ארגונים...",
        ["History..."] = "היסטוריה...",
        ["Project"] = "פרויקט",
        ["Help"] = "עזרה",
        ["1  Add files..."] = "1  הוסף קבצים...",
        ["2  Arrange / edit"] = "2  סידור ועריכה",
        ["3  Schedule..."] = "3  תזמון...",
        ["Review / export"] = "סקירה וייצוא",
        ["Connected"] = "מחובר",
        ["Refresh"] = "רענן",
        ["Refresh reports"] = "רענן דוחות",
        ["Choose image folder..."] = "בחר תיקיית תמונות...",
        ["Tools"] = "כלים",
        ["Help / tools"] = "עזרה וכלים",
        ["AD / GPO"] = "ניהול AD / GPO",
        ["Images"] = "תמונות",
        ["Add image files..."] = "הוסף קובצי תמונה...",
        ["Add folder..."] = "הוסף תיקייה...",
        ["Remove selected"] = "הסר בחירה",
        ["Move up"] = "הזז למעלה",
        ["Move down"] = "הזז למטה",
        ["Sort by filename"] = "מיין לפי שם קובץ",
        ["Auto match names"] = "התאמה אוטומטית של שמות",
        ["Edit schedule..."] = "ערוך לוח זמנים...",
        ["Validate campaign"] = "בדוק קמפיין",
        ["Validate campaign..."] = "בדוק קמפיין...",
        ["Deploy GPO..."] = "פרוס מדיניות GPO...",
        ["Connect to AD..."] = "התחבר ל-AD...",
        ["Connect AD account..."] = "התחבר לחשבון AD...",
        ["Agent Management / Live..."] = "ניהול סוכנים / בזמן אמת...",
        ["Client feedback..."] = "דוחות לקוח...",
        ["Open Client Health"] = "פתח בריאות לקוחות",
        ["Open GPO Deployment Wizard..."] = "פתח אשף פריסת GPO...",
        ["Deploy GPO + files"] = "פרוס GPO וקבצים",
        ["Check GPO rights"] = "בדוק הרשאות GPO",
        ["Preview / check conflicts"] = "תצוגה מקדימה / בדיקת התנגשויות",
        ["Preview"] = "תצוגה מקדימה",
        ["Check"] = "בדוק",
        ["Install"] = "התקן",
        ["1  Check"] = "1  בדוק",
        ["2  Install"] = "2  התקן",
        ["Software updates"] = "עדכוני תוכנה",
        ["Release channel"] = "ערוץ הפצה",
        ["Installed version"] = "גרסה מותקנת",
        ["Portable updater"] = "מעדכן נייד",
        ["Backup status"] = "מצב גיבוי",
        ["Agent Management / Live"] = "ניהול סוכנים / בזמן אמת",
        ["GPO: Disable / Reuse / Delete"] = "GPO: השבתה / שימוש חוזר / מחיקה",
        ["Lock Screen conflicts"] = "התנגשויות מסך נעילה",
        ["Computers from OU + group"] = "מחשבים מ-OU וקבוצה",
        ["Check RPC reachability"] = "בדוק זמינות RPC",
        ["Refresh Agent Reports"] = "רענן דוחות סוכנים",
        ["Select first 25"] = "בחר 25 ראשונים",
        ["Clear selection"] = "נקה בחירה",
        ["Select reachable (max 25)"] = "בחר זמינים (עד 25)",
        ["Run on selected"] = "הפעל על הנבחרים",
        ["Refresh GPO list"] = "רענן רשימת GPO",
        ["Disable"] = "השבת",
        ["Enable"] = "הפעל",
        ["Unlink"] = "בטל קישור",
        ["Relink"] = "קשר מחדש",
        ["Reuse"] = "השתמש שוב",
        ["Delete"] = "מחק",
        ["Clear"] = "נקה",
        ["Back to Wizard"] = "חזרה לאשף",
        ["Browse..."] = "עיון...",
        ["Browse SMB..."] = "עיון ב-SMB...",
        ["Copy share path"] = "העתק נתיב שיתוף",
        ["Copy path"] = "העתק נתיב",
        ["Export CSV"] = "ייצא CSV",
        ["Export filtered CSV..."] = "ייצא CSV מסונן...",
        ["Local Windows lab evidence (offline, read-only)"] = "דוחות בדיקה מקומיים של Windows (לא מקוון, לקריאה בלבד)",
        ["SMB LocalSystem canary"] = "בדיקת SMB תחת LocalSystem",
        ["Endpoint cleanup audit"] = "בדיקת ניקוי תחנת קצה",
        ["Lab evidence"] = "דוחות מעבדה",
        ["Import SMB canary JSON..."] = "ייבא דוח SMB מסוג JSON...",
        ["Import cleanup JSON..."] = "ייבא דוח ניקוי מסוג JSON...",
        ["No report imported"] = "לא יובא דוח",
        ["No report"] = "אין דוח",
        ["Report rejected - invalid or incompatible JSON"] = "הדוח נדחה — JSON שגוי או לא תואם",
        ["Stale report - review on the endpoint"] = "דוח ישן — נדרשת בדיקה בתחנה",
        ["Incomplete / failed checks - review needed"] = "בדיקות נכשלו או חסרות — נדרשת בדיקה",
        ["Imported locally - NOT authenticated endpoint proof"] = "יובא מקומית — לא אימות זהות תחנה",
        ["Campaign files — choose where the agent and images are stored"] =
            "קובצי הקמפיין — היכן לשמור את הסוכן והתמונות",
        ["Deploy GPO to selected OU and computer/group assignments"] =
            "פרוס GPO ל-OU ולמחשבים/קבוצות שנבחרו",
        ["Finish offline - local project is saved"] = "סיים ללא חיבור — הפרויקט נשמר",
        ["Finish offline - keep the campaign in the editor"] =
            "סיים ללא חיבור — השאר את הקמפיין בעורך",
        ["Skip saving & continue"] = "דלג על שמירה והמשך",
        ["Save & Next"] = "שמור והבא",
        ["Campaign files"] = "קובצי קמפיין",
        ["Client type"] = "סוג סוכן",
        ["Domain"] = "דומיין",
        ["Security groups"] = "קבוצות אבטחה",
        ["Select OU"] = "בחר OU",
        ["Time zone"] = "אזור זמן",
        ["Lock Screen / Screen Saver / Both"] = "מסך נעילה / שומר מסך / שניהם",
        ["Screen Saver"] = "שומר מסך",
        ["Lock Screen"] = "מסך נעילה",
        ["Both"] = "שניהם",
        ["1 AD / Skip   2 Type   3 Images   4 Arrange   5 Schedule   6 Save   7 Deploy"] =
            "1 AD / דילוג   2 סוג   3 תמונות   4 סידור   5 תזמון   6 שמירה   7 פריסה"
    };
    private static readonly Dictionary<string, string> EnglishByHebrew =
        Hebrew.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);

    internal static bool IsHebrew(string language) => language == "Hebrew";
    internal static bool IsRussian(string language) => language == "Russian";

    /// <summary>
    /// Translate only known display captions. First normalize a label from
    /// English, Hebrew OR Russian back to the canonical English UI key.
    /// This makes Hebrew -> Russian -> English reversible on a live form and
    /// never changes ComboBox item values, AD objects, JSON or file paths.
    /// </summary>
    internal static string Translate(string value, string language)
    {
        if (string.IsNullOrEmpty(value)) return value;
        const string menuSuffix = "  ▾";
        if (value.EndsWith(menuSuffix, StringComparison.Ordinal))
            return Translate(value[..^menuSuffix.Length], language) + menuSuffix;

        foreach (string suffix in new[] { " (skip)", " (דילוג)", " (пропуск)" })
        {
            if (!value.EndsWith(suffix, StringComparison.Ordinal)) continue;
            string translated = Translate(value[..^suffix.Length], language);
            string targetSuffix = IsHebrew(language) ? " (דילוג)" :
                IsRussian(language) ? " (пропуск)" : " (skip)";
            return translated + targetSuffix;
        }

        if (TryTranslateWizardStep(value, language, out string dynamicStep))
            return dynamicStep;

        string key = value;
        if (EnglishByHebrew.TryGetValue(key, out string? english) ||
            WizardEnglish.TryGetValue(key, out english) ||
            AdHealthEnglish.TryGetValue(key, out english) ||
            OpsDiagnosticsEnglish.TryGetValue(key, out english) ||
            ScheduleAdvancedEnglish.TryGetValue(key, out english) ||
            Advanced95EnglishByHebrew.TryGetValue(key, out english) ||
            Advanced95EnglishByRussian.TryGetValue(key, out english) ||
            Preview96EnglishByHebrew.TryGetValue(key, out english) ||
            Preview96EnglishByRussian.TryGetValue(key, out english) ||
            EnglishByRussian.TryGetValue(key, out english))
            key = english;

        if (IsRussian(language))
        {
            if (Russian.TryGetValue(key, out string? ru)) return ru;
            if (Advanced95Russian.TryGetValue(key, out ru)) return ru;
            if (Preview96Russian.TryGetValue(key, out ru)) return ru;
        }
        if (IsHebrew(language))
        {
            if (Hebrew.TryGetValue(key, out string? he)) return he;
            if (WizardHebrew.TryGetValue(key, out he)) return he;
            if (AdHealthHebrew.TryGetValue(key, out he)) return he;
            if (OpsDiagnosticsHebrew.TryGetValue(key, out he)) return he;
            if (ScheduleAdvancedHebrew.TryGetValue(key, out he)) return he;
            if (Advanced95Hebrew.TryGetValue(key, out he)) return he;
            if (Preview96Hebrew.TryGetValue(key, out he)) return he;
        }
        return key;
    }

    internal static void Apply(Form form, string language)
    {
        bool hebrew = IsHebrew(language);
        form.RightToLeft = hebrew ? RightToLeft.Yes : RightToLeft.No;
        form.RightToLeftLayout = hebrew;
        // The version/company portion of a window title is technical data.
        // Only the fixed caption segment after the app version is localized.
        foreach (string englishCaption in new[] {
            "AD Connection Wizard", "Select AD account",
            "Active Directory Deployment", "Campaign Wizard",
            "Full GPO conflict report (read-only)",
            "Effective GPO candidates", "Winning Policy Analysis",
            "Campaign closure audit", "Repair history" })
        {
            string destination = Translate(englishCaption, language);
            foreach (string sourceLanguage in new[] { "English", "Hebrew", "Russian" })
            {
                string suffix = " - " + Translate(englishCaption, sourceLanguage);
                int position = form.Text.IndexOf(suffix, StringComparison.Ordinal);
                if (position >= 0)
                {
                    form.Text = form.Text[..position] + " - " + destination +
                        form.Text[(position + suffix.Length)..];
                    break;
                }
            }
        }

        foreach (string englishTitle in new[] {
            "Client feedback — SMB",
            "Diagnostics — saved error reports",
            "Report an error — LockScreen Campaign Manager",
            "Error trends — LockScreen Campaign Manager",
            "Campaign Schedule - Images and Dates",
            "Company Profiles — LockScreen Campaign Manager",
            "New company profile", "Edit company profile",
            "Client Health — Alert settings",
            "gMSA 24/7 — Setup", "gMSA Production Readiness — read-only",
            "Updates — LockScreen Campaign Manager",
            "LockScreen Campaign Manager - Local lab evidence" })
        {
            if (new[] { "English", "Hebrew", "Russian" }
                .Any(source => form.Text == Translate(englishTitle, source)))
            {
                form.Text = Translate(englishTitle, language);
                break;
            }
        }
        foreach (string sourceLanguage in new[] { "English", "Hebrew", "Russian" })
        {
            string oldMarker = " - " + Translate("GPO Operations", sourceLanguage) + " ";
            if (form.Text.Contains(oldMarker, StringComparison.Ordinal))
            {
                form.Text = form.Text.Replace(oldMarker,
                    " - " + Translate("GPO Operations", language) + " ",
                    StringComparison.Ordinal);
                break;
            }
        }
        foreach (Control child in All(form))
        {
            if (child is Button or Label or CheckBox or RadioButton or
                TabPage or GroupBox)
            {
                var caption = Captions.GetValue(child, source =>
                    new CaptionSource(Translate(source.Text, "English"), source.Text));
                if (!string.Equals(child.Text, caption.LastApplied, StringComparison.Ordinal))
                    caption.English = Translate(child.Text, "English"); // dynamic status changed
                child.Text = Translate(caption.English, language);
                caption.LastApplied = child.Text;
            }

            // Log output, GUIDs, paths, LDAP DNs and hash evidence must remain LTR.
            // User-editable normal text inputs can still inherit Hebrew direction.
            if (child is TextBox text && (text.ReadOnly || text.Multiline ||
                text.Name.Contains("Path", StringComparison.OrdinalIgnoreCase) ||
                text.Name.Contains("Root", StringComparison.OrdinalIgnoreCase) ||
                text.Name.Contains("UNC", StringComparison.OrdinalIgnoreCase) ||
                text.Name.Contains("Report", StringComparison.OrdinalIgnoreCase)))
                text.RightToLeft = RightToLeft.No;
            if (child is TextBox placeholder &&
                !string.IsNullOrEmpty(placeholder.PlaceholderText))
                placeholder.PlaceholderText = Translate(placeholder.PlaceholderText, language);
            // This is a fixed instructional paragraph, not a saved report or
            // user-entered UNC string. Unlike raw diagnostics, it may use RTL.
            if (child is TextBox instructions &&
                instructions.Name == "FeedbackSmbInstructions")
            {
                instructions.Text = Translate(instructions.Text, language);
                instructions.RightToLeft = hebrew ? RightToLeft.Yes : RightToLeft.No;
            }
            if (child is DataGridView or ListBox or ListView)
                child.RightToLeft = RightToLeft.No;
            if (child is ListView view)
                foreach (ColumnHeader column in view.Columns)
                {
                    var header = ListHeaders.GetValue(column,
                        key => new ColumnHeaderSource(key.Text));
                    if (!string.Equals(column.Text, header.LastApplied,
                        StringComparison.Ordinal))
                        header.Caption = Translate(column.Text, "English");
                    column.Text = Translate(header.Caption, language);
                    header.LastApplied = column.Text;
                }
            if (child is DataGridView healthGrid &&
                healthGrid.Name is ("ClientHealthGrid" or
                    "GpoDeferredRepairGrid" or "GpoGmsaQueueGrid" or
                    "DiagnosticsTrendsGrid" or "GroupConflictGrid" or
                    "CampaignScheduleGrid" or "EffectiveGpoGrid" or
                    "WinningPolicyGrid" or "CampaignHistoryGrid" or
                    "RepairHistoryGrid" or "CampaignClosureGrid" or
                    "ImageFolderMappingGrid" or "GmsaReadinessGrid" or
                    "DeviceRepairGrid"))
            {
                foreach (DataGridViewColumn column in healthGrid.Columns)
                {
                    var header = Headers.GetValue(column,
                        key => new ColumnHeaderSource(key.HeaderText));
                    if (!string.Equals(column.HeaderText, header.LastApplied,
                        StringComparison.Ordinal))
                        header.Caption = Translate(column.HeaderText, "English");
                    column.HeaderText = Translate(header.Caption, language);
                    header.LastApplied = column.HeaderText;
                }
            }
            if (child is Label technicalLabel && technicalLabel.Name is
                ("CampaignWizardVersion" or "WizardTimeZoneDifference" or
                 "CampaignWizardImagePath" or "ScheduleTimeZoneDifference"))
                technicalLabel.RightToLeft = RightToLeft.No;
            // UI-only formatted choices: SelectedItem stays the original enum or
            // English storage key. Never translate values saved to campaign JSON.
            if (child is ComboBox combo && combo.Name is
                ("WizardCampaignType" or "CampaignWizardContentSource" or
                 "DeploymentContentSource" or "DeploymentClientType" or
                 "GpoLifecycleAction" or "GpoPushPurpose" or
                 "GpoGmsaFilter" or "AgentActivityFilter" or "GroupConflictRisk" or
                 "UiSettingsDensity" or "AssignmentTargetMode" or
                 "CampaignScheduleMode" or
                 "EffectiveGpoFilter" or
                 "WinningPolicyFilter" or "RepairHistorySource" or
                 "CampaignClosureFilter" or "DeviceRepairFilter" or
                 "UpdaterReleaseChannel"))
                ApplyComboLocalization(combo, language);
        }
    }

    private static void ApplyComboLocalization(ComboBox combo, string language)
    {
        ComboLanguage state = ComboLanguages.GetValue(combo, _ => new ComboLanguage());
        if (!state.Attached)
        {
            combo.FormattingEnabled = true;
            combo.Format += (_, e) =>
            {
                if (e.ListItem is not null)
                    e.Value = Translate(e.ListItem.ToString() ?? string.Empty, state.Language);
            };
            state.Attached = true;
        }
        state.Language = language;
        // Redraw labels without replacing SelectedItem, changing SelectedIndex,
        // firing a deployment configuration handler or persisting new data.
        combo.Invalidate();
    }

    internal static string Display(string message) =>
        Translate(message, UiStyle.Preferences.Language);

    private static IEnumerable<Control> All(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
            foreach (var descendant in All(child))
                yield return descendant;
    }
}
