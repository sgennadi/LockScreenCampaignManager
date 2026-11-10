namespace LockScreenCampaignManager;

/// <summary>
/// Pure presentation strings for the campaign-creation wizard.
/// Never localize enum keys, UNC/LDAP values, filesystem names or saved JSON.
/// </summary>
internal static partial class UiLocale
{
    private static readonly Dictionary<string, string> WizardHebrew = new(StringComparer.Ordinal)
    {
        ["Campaign Wizard"] = "אשף הקמפיינים",
        ["Selected type: "] = "סוג נבחר: ",
        [" | idle "] = " | ללא פעילות ",
        [" s"] = " שניות",
        [" | no AD changes made."] = " | לא בוצעו שינויים ב-AD.",
        ["Connected to AD: "] = "מחובר ל-AD: ",
        [". You may continue."] = ". אפשר להמשיך.",
        ["Image not found: "] = "התמונה לא נמצאה: ",
        ["Unable to preview: "] = "אי אפשר להציג: ",
        ["Selected time zone: "] = "אזור זמן נבחר: ",
        ["Time zone: "] = "אזור זמן: ",
        [". Check image times and continue to Save."] = ". בדוק את זמני התמונות והמשך לשמירה.",
        [" | Schedule edited. Continue to Save."] = " | התזמון נערך. המשך לשמירה.",
        [" | Select a time zone and review scheduled changes."] = " | בחר אזור זמן ובדוק את ההחלפות המתוזמנות.",
        [" |  every "] = " | כל ",
        [" min"] = " דקות",
        ["Client Health: ENABLED"] = "מצב לקוחות: מופעל",
        ["Client Health: DISABLED"] = "מצב לקוחות: כבוי",
        ["1  AD connection"] = "1  חיבור ל-AD",
        ["2  Campaign type"] = "2  סוג קמפיין",
        ["3  Images"] = "3  תמונות",
        ["4  Arrange"] = "4  סידור",
        ["5  Schedule"] = "5  תזמון",
        ["6  Save / Skip"] = "6  שמירה / דילוג",
        ["7  Deploy"] = "7  פריסה",
        ["AD connection"] = "חיבור ל-AD",
        ["Campaign type"] = "סוג קמפיין",
        ["Arrange"] = "סידור",
        ["Schedule"] = "תזמון",
        ["Save / Skip"] = "שמירה / דילוג",
        ["Deploy"] = "פריסה",
        ["Choose how to connect to Active Directory"] = "בחר כיצד להתחבר ל-Active Directory",
        ["Your edits stay local until you explicitly deploy a GPO."] = "השינויים נשמרים מקומית עד לאישור מפורש לפריסת GPO.",
        ["Connect to Active Directory"] = "התחברות ל-Active Directory",
        ["Choose campaign type"] = "בחר סוג קמפיין",
        ["Choose your campaign images"] = "בחר תמונות לקמפיין",
        ["Arrange the image order"] = "סדר את התמונות",
        ["Choose days and times"] = "בחר ימים ושעות",
        ["Save or skip saving"] = "שמור או דלג על שמירה",
        ["Deploy or finish offline"] = "פרוס או סיים במצב לא מקוון",
        ["Choose a Windows AD connection or prepare the campaign offline. No AD or GPO changes are made during sign-in."] = "בחר חיבור ל-AD או הכן את הקמפיין ללא חיבור. תהליך הכניסה אינו משנה AD או GPO.",
        ["Choose Lock Screen, Screen Saver or Both before images. This determines which pages are needed."] = "בחר מסך נעילה, שומר מסך או שניהם לפני התמונות. הבחירה קובעת אילו שלבים נדרשים.",
        ["Select multiple image files or import a complete folder. You can add more images before continuing."] = "בחר כמה קובצי תמונה או תיקייה שלמה. ניתן להוסיף תמונות לפני ההמשך.",
        ["Place images in the desired order. The first image is assigned to the first scheduled change."] = "סדר את התמונות לפי הסדר הרצוי. התמונה הראשונה תשויך למועד ההחלפה הראשון.",
        ["Set the start date, weekdays, exact dates and change time. Review the schedule before saving."] = "הגדר תאריך התחלה, ימי שבוע, תאריכים מדויקים ושעת החלפה. בדוק לפני השמירה.",
        ["Save a project for reopening later, or skip saving and continue directly to deployment. No AD changes here."] = "שמור פרויקט לפתיחה חוזרת או דלג והמשך לפריסה. בשלב זה אין שינויי AD.",
        ["Deploy using the AD/GPO Wizard, or finish offline and deploy later. No automatic GPO changes."] = "פרוס באמצעות אשף AD/GPO, או סיים ללא חיבור ופרוס בהמשך. אין שינוי GPO אוטומטי.",
        ["AD Connection Wizard..."] = "אשף חיבור ל-AD...",
        ["Continue (no save)"] = "המשך (ללא שמירה)",
        ["Connect with current Windows account or another AD account"] = "התחבר עם חשבון Windows הנוכחי או חשבון AD אחר",
        ["Work offline - connect only if you decide to deploy"] = "עבוד ללא חיבור — התחבר רק בעת פריסה",
        ["No AD session yet. Choose the connection wizard or offline mode."] = "אין חיבור AD פעיל. בחר באשף ההתחברות או בעבודה ללא חיבור.",
        ["AD sign-in is optional"] = "התחברות ל-AD אינה חובה",
        ["Choose a campaign type first"] = "בחר תחילה סוג קמפיין",
        ["Screen Saver: images not needed"] = "שומר מסך: אין צורך בתמונות",
        ["Local project not saved"] = "הפרויקט המקומי לא נשמר",
        ["Add at least one image to continue."] = "הוסף לפחות תמונה אחת כדי להמשיך.",
        ["Choose a campaign type to continue. No images or schedule steps are skipped yet."] = "בחר סוג קמפיין כדי להמשיך. עדיין לא דולגו שלבי תמונות ותזמון.",
        ["No campaign type selected. Choose Lock Screen, Screen Saver or Both."] = "לא נבחר סוג קמפיין. בחר מסך נעילה, שומר מסך או שניהם.",
        ["Choose the campaign content first"] = "בחר תחילה את תוכן הקמפיין",
        ["Choose one mode below. Separate computer and user groups are assigned through the Deployment Wizard."] = "בחר מצב. קבוצות מחשבים ומשתמשים נבחרות בנפרד באשף הפריסה.",
        ["Screen Saver (.scr)"] = "שומר מסך (.scr)",
        ["JPG photo slideshow (use selected campaign images)"] = "מצגת תמונות JPG (מתמונות הקמפיין)",
        ["Change JPG every (s)"] = "החלף JPG כל (שניות)",
        ["Idle timeout (seconds)"] = "המתנה ללא פעילות (שניות)",
        ["Require Windows sign-in on resume"] = "דרוש כניסה ל-Windows בחזרה",
        ["Screen Saver can use a Windows built-in .scr OR a local JPG slideshow package exported for pilot testing. JPG mode uses selected campaign images and restores image pages. Automatic GPO delivery for JPG is blocked until client pilot verification."] = "ניתן לבחור שומר מסך מובנה של Windows או מצגת JPG לבדיקה מקומית. תמונות הקמפיין משמשות במצגת. פריסת JPG אוטומטית דרך GPO חסומה עד לאימות בתחנות בדיקה.",
        ["LOCK SCREEN: next select campaign images, arrange them, and choose days and times. Applies to computers."] = "מסך נעילה: בשלב הבא בחר תמונות, סדר אותן וקבע ימים ושעות. חל על מחשבים.",
        ["JPG SCREEN SAVER: select photos on the next page, arrange their order and set the slide interval. Local pilot package first."] = "שומר מסך JPG: בחר תמונות בשלב הבא, סדר אותן וקבע מרווח החלפה. תחילה יש לבדוק חבילה מקומית.",
        ["SCREEN SAVER: built-in .scr; image/arrange/schedule pages are skipped. Applies to selected user group."] = "שומר מסך: קובץ .scr מובנה; שלבי התמונות, הסידור והתזמון ידולגו. חל על קבוצת המשתמשים שנבחרה.",
        ["BOTH: configure image schedule and user Screen Saver. Choose independent computer/user groups at Deploy."] = "שניהם: הגדר תזמון תמונות ושומר מסך למשתמשים. בחר קבוצות מחשבים ומשתמשים בנפרד בפריסה.",
        ["Built-in Screen Saver: images not needed"] = "שומר מסך מובנה: אין צורך בתמונות",
        ["JPG/Lock Screen: image stages are available"] = "JPG/מסך נעילה: שלבי התמונות זמינים",
        ["LockScreen"] = "מסך נעילה",
        ["ScreenSaver"] = "שומר מסך",
        ["Selected image preview"] = "תצוגה מקדימה של התמונה",
        ["Select an image to preview"] = "בחר תמונה לתצוגה מקדימה",
        ["Select an image from the list."] = "בחר תמונה מהרשימה.",
        ["Image file not found"] = "קובץ התמונה לא נמצא",
        ["Image cannot be opened"] = "לא ניתן לפתוח את התמונה",
        ["Edit schedule..."] = "ערוך תזמון...",
        ["Client Health / Feedback..."] = "מצב לקוחות / משוב...",
        ["Windows time zone for the campaign"] = "אזור הזמן של Windows לקמפיין",
        ["Schedule not configured"] = "התזמון לא הוגדר",
        ["Client Health: DISABLED. No client reports until enabled."] = "מצב לקוחות: כבוי. לא יישלחו דוחות עד להפעלה.",
        ["Campaign name"] = "שם הקמפיין",
        ["Skip saving & continue"] = "דלג על שמירה והמשך",
        ["Project saved. Continue to Deploy or finish offline."] = "הפרויקט נשמר. המשך לפריסה או סיים ללא חיבור.",
        ["Local save skipped. Continue to Deploy without a project file."] = "השמירה המקומית דולגה. המשך לפריסה ללא קובץ פרויקט.",
        ["Choose Save project or Skip saving & continue. Validation is required either way."] = "בחר שמירת פרויקט או דילוג על שמירה והמשך. נדרשת בדיקה בשני המקרים.",
        ["SYSVOL (default)"] = "SYSVOL (ברירת מחדל)",
        ["SMB network folder (UNC)"] = "תיקיית רשת SMB (UNC)",
        ["The GPO remains in SYSVOL. Test PC accounts require READ on both the SMB share and NTFS."] = "ה-GPO נשאר ב-SYSVOL. חשבונות מחשבי הבדיקה חייבים הרשאת קריאה גם ב-SMB וגם ב-NTFS.",
        ["Enter an existing dedicated UNC folder: \\\\server\\share\\folder (not a mapped drive)."] = "הזן תיקיית UNC ייעודית קיימת: \\\\server\\share\\folder (לא כונן ממופה).",
        ["GPO and immutable campaign files use the existing SYSVOL location."] = "קובצי ה-GPO והקמפיין נשמרים במיקום SYSVOL הקיים.",
        ["Unsaved storage changes. Save project changes before finishing if you need the JSON file up to date."] = "מיקום הקבצים השתנה ולא נשמר. שמור את השינויים כדי לעדכן את קובץ JSON.",
        ["Storage changed. The new choice is stored in the editor; save the project to keep it on disk."] = "מיקום הקבצים השתנה בעורך. שמור את הפרויקט כדי לשמור את הבחירה בדיסק.",
        ["The saved project still has an older storage path. Choose Save project changes to update it."] = "בפרויקט השמור מופיע עדיין המיקום הקודם. בחר שמירת שינויים כדי לעדכן אותו.",
        ["No local project file was saved. Deploy or return to the editor."] = "לא נשמר קובץ פרויקט מקומי. פרוס או חזור לעורך.",
        ["AD connected: select storage and deployment or finish without AD changes."] = "AD מחובר: בחר מיקום קבצים ופריסה, או סיים בלי לשנות את AD.",
        ["Offline: select storage now; connect to AD in the deployment wizard if needed."] = "ללא חיבור: בחר כעת מיקום קבצים; תוכל להתחבר ל-AD באשף הפריסה.",
        ["JPG Screen Saver: export and test the .scr package locally. AD deployment is blocked until group/photo delivery is pilot-verified."] = "שומר מסך JPG: ייצא ובדוק חבילת .scr מקומית. פריסת AD חסומה עד לאישור בדיקות.",
        ["Export JPG .scr pilot..."] = "ייצא חבילת בדיקה JPG .scr...",
        ["Export GPO lab kit..."] = "ייצא ערכת בדיקת GPO...",
        ["Install local JPG pilot..."] = "התקן בדיקת JPG מקומית...",
        ["Remove local JPG pilot..."] = "הסר בדיקת JPG מקומית...",
        ["Open GPO Deployment Wizard..."] = "פתח אשף פריסת GPO...",
        ["JPG Screen Saver pilot package exported. Test before domain rollout."] = "חבילת בדיקת שומר מסך JPG יוצאה. בדוק לפני פריסה לדומיין.",
        ["Previous current-user Screen Saver settings restored."] = "הגדרות שומר המסך הקודמות של המשתמש שוחזרו.",
        ["Read-only Computer Startup GPO pilot kit exported. User GPO activation remains blocked."] = "ערכת בדיקת Computer Startup GPO יוצאה. הפעלת User GPO עדיין חסומה.",
        ["Choose Save project changes to update it."] = "בחר שמירת שינויים כדי לעדכן אותו.",
    };

    private static readonly Dictionary<string, string> WizardEnglish =
        WizardHebrew.ToDictionary(item => item.Value, item => item.Key, StringComparer.Ordinal);

    // Wizard step captions contain the current step number, so they cannot be
    // looked up as a single fixed dictionary entry.
    private static string TranslateWizardDynamic(string value, string language)
    {
        const string englishMarker = " of 7  -  ";
        const string hebrewMarker = " מתוך 7 — ";
        if (IsHebrew(language) && value.StartsWith("Step ", StringComparison.Ordinal))
        {
            int position = value.IndexOf(englishMarker, StringComparison.Ordinal);
            if (position > 5)
            {
                string number = value[5..position];
                string step = value[(position + englishMarker.Length)..];
                if (int.TryParse(number, out int index) && index is >= 1 and <= 7)
                    return $"שלב {number} מתוך 7 — {Translate(step, language)}";
            }
        }
        if (!IsHebrew(language) && value.StartsWith("שלב ", StringComparison.Ordinal))
        {
            int position = value.IndexOf(hebrewMarker, StringComparison.Ordinal);
            if (position > 4)
            {
                string number = value[4..position];
                string step = value[(position + hebrewMarker.Length)..];
                if (int.TryParse(number, out int index) && index is >= 1 and <= 7)
                    return $"Step {number} of 7  -  {Translate(step, language)}";
            }
        }
        return value;
    }
}
