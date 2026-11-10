using System.Windows.Forms;

namespace LockScreenCampaignManager;

/// <summary>
/// Partial, explicit operator-facing English/Hebrew UI dictionary.
/// Never translate user data, JSON, LDAP/UNC paths, log contents or mutable
/// ComboBox selection values. Missing translations deliberately stay English.
/// </summary>
internal static class UiLocale
{
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

    internal static string Translate(string value, string language)
    {
        const string menuSuffix = "  ▾";
        if (value.EndsWith(menuSuffix, StringComparison.Ordinal))
            return Translate(value[..^menuSuffix.Length], language) + menuSuffix;
        if (IsHebrew(language))
        {
            if (Hebrew.TryGetValue(value, out string? he)) return he;
        }
        else if (EnglishByHebrew.TryGetValue(value, out string? en))
            return en;
        return value;
    }

    internal static void Apply(Form form, string language)
    {
        bool hebrew = IsHebrew(language);
        form.RightToLeft = hebrew ? RightToLeft.Yes : RightToLeft.No;
        form.RightToLeftLayout = hebrew;
        foreach (Control child in All(form))
        {
            if (child is Button or Label or CheckBox or RadioButton or
                TabPage or GroupBox)
                child.Text = Translate(child.Text, language);

            // Log output, GUIDs, paths, LDAP DNs and hash evidence must remain LTR.
            // User-editable normal text inputs can still inherit Hebrew direction.
            if (child is TextBox text && (text.ReadOnly || text.Multiline ||
                text.Name.Contains("Path", StringComparison.OrdinalIgnoreCase) ||
                text.Name.Contains("Root", StringComparison.OrdinalIgnoreCase) ||
                text.Name.Contains("UNC", StringComparison.OrdinalIgnoreCase) ||
                text.Name.Contains("Report", StringComparison.OrdinalIgnoreCase)))
                text.RightToLeft = RightToLeft.No;
            if (child is DataGridView or ListBox)
                child.RightToLeft = RightToLeft.No;
        }
    }

    private static IEnumerable<Control> All(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
            foreach (var descendant in All(child))
                yield return descendant;
    }
}
