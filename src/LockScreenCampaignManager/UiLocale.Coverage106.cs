namespace LockScreenCampaignManager;

/// <summary>Presentation-only Preview.106 Client Health and power-audit captions.
/// Never localize LDAP DNs, GPO IDs, SMB filenames or feedback JSON.</summary>
internal static partial class UiLocale
{
    private static readonly Dictionary<string, string> Coverage106Russian = new(StringComparer.Ordinal)
    {
        ["AD scope not loaded. Agent reports are untrusted diagnostics, not proof of online status."] =
            "Область AD не загружена. Отчёты агентов — непроверенные сведения, не доказательство работы компьютера в сети.",
        ["AD inventory not verified. Refresh AD before Repair or coverage comparisons."] =
            "Инвентаризация AD не подтверждена. Обновите данные AD перед Repair и сравнением охвата.",
        ["AD inventory verified: OU {0} computer objects; scoped group {1}. Read-only; not effective GPO targeting."] =
            "Проверено AD: в OU {0} компьютеров, в выбранной группе {1}. Только чтение; не подтверждение действия GPO.",
        ["AD lastLogonTimestamp is approximate; activity window never filters deployment or Repair."] =
            "lastLogonTimestamp в AD приблизителен. Интервал активности не ограничивает Deploy или Repair.",
        ["AD OU: {0} | OU + group: {1} | Enabled: {2} | Disabled: {3} | State unknown: {4} | AD last-seen <= {5} days: {6} | No timestamp: {7}"] =
            "AD OU: {0} | OU + группа: {1} | Включены: {2} | Отключены: {3} | Статус неизвестен: {4} | В AD за {5} дней: {6} | Без даты: {7}",
        ["Agent JSON received: {0}/{1} | Fresh: {2} | Stale reports: {3} | No report: {4}. Reports are untrusted hints, not online proof."] =
            "Отчёты агентов: {0}/{1} | Свежие: {2} | Старые: {3} | Нет отчёта: {4}. Это не подтверждение онлайн-статуса.",
        ["Agent comparison not verified (reload AD, campaign and SMB reports). No zero coverage claimed."] =
            "Сравнение с агентами не подтверждено (обновите AD, кампанию и SMB-отчёты). Нулевой охват не установлен.",
        ["Computer OU (Lock Screen target / optional Screen Saver power audit)"] =
            "OU компьютеров (Lock Screen / необязательная проверка питания Screen Saver)"
    };

    private static readonly Dictionary<string, string> Coverage106Hebrew = new(StringComparer.Ordinal)
    {
        ["AD scope not loaded. Agent reports are untrusted diagnostics, not proof of online status."] =
            "תחום ה-AD לא נטען. דוחות הסוכנים הם מידע לא מאומת, ואינם הוכחה לחיבור מקוון.",
        ["AD inventory not verified. Refresh AD before Repair or coverage comparisons."] =
            "נתוני AD לא אומתו. יש לרענן את AD לפני תיקון או השוואת כיסוי.",
        ["AD inventory verified: OU {0} computer objects; scoped group {1}. Read-only; not effective GPO targeting."] =
            "מלאי AD אומת: {0} מחשבים ב-OU, מתוכם {1} בקבוצה. לקריאה בלבד; לא הוכחה להחלת GPO.",
        ["AD lastLogonTimestamp is approximate; activity window never filters deployment or Repair."] =
            "הערך lastLogonTimestamp ב-AD משוער. טווח פעילות אינו מסנן פריסה או תיקון.",
        ["AD OU: {0} | OU + group: {1} | Enabled: {2} | Disabled: {3} | State unknown: {4} | AD last-seen <= {5} days: {6} | No timestamp: {7}"] =
            "AD OU: {0} | OU + קבוצה: {1} | פעילים: {2} | מושבתים: {3} | מצב לא ידוע: {4} | נצפו ב-AD ב-{5} ימים: {6} | ללא תאריך: {7}",
        ["Agent JSON received: {0}/{1} | Fresh: {2} | Stale reports: {3} | No report: {4}. Reports are untrusted hints, not online proof."] =
            "דוחות סוכן: {0}/{1} | עדכניים: {2} | ישנים: {3} | ללא דוח: {4}. הדוח אינו הוכחת מצב מקוון.",
        ["Agent comparison not verified (reload AD, campaign and SMB reports). No zero coverage claimed."] =
            "השוואת הסוכנים לא אומתה (יש לרענן AD, קמפיין ודוחות SMB). אין לפרש ככיסוי אפס.",
        ["Computer OU (Lock Screen target / optional Screen Saver power audit)"] =
            "OU מחשבים (יעד מסך נעילה / בדיקת חשמל אופציונלית לשומר מסך)"
    };

    private static readonly Dictionary<string, string> Coverage106EnglishByRussian =
        Coverage106Russian.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Coverage106EnglishByHebrew =
        Coverage106Hebrew.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
}
