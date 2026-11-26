namespace LockScreenCampaignManager;

/// <summary>Presentation-only Preview.106 Client Health and power-audit captions.
/// Never localize LDAP DNs, GPO IDs, SMB filenames or feedback JSON.</summary>
internal static partial class UiLocale
{
    private static readonly Dictionary<string, string> Coverage106Russian = new(StringComparer.Ordinal)
    {
        ["AD OU: {0} | OU + security group: {1} | Visible after filters: {2} | Disabled: {3} | AD inactive / unknown >90d: {4}"] = "AD OU: {0} | OU + группа безопасности: {1} | Видно после фильтров: {2} | Отключены: {3} | Неактивны в AD >90 дней / неизвестно: {4}",
        ["Agent AD inventory not verified."] = "Инвентаризация AD для агентов не подтверждена.",
        ["Agent AD activity is approximate, not live online status."] = "Активность в AD приблизительная, не онлайн-статус компьютера.",
        ["SMB reports: {0} / {1} eligible | Recent at check: {2} | Client type mismatch: {3} | No report: {4} | Invalid files: {5} | Checked: {6}"] = "SMB-отчёты: {0} / {1} подходящих ПК | Свежие на момент проверки: {2} | Несовпадение типа клиента: {3} | Без отчёта: {4} | Неверные файлы: {5} | Проверено: {6}",
        ["SMB check not verified"] = "Проверка SMB не подтверждена",
        ["Agent reports are untrusted diagnostic hints, not online proof."] = "Отчёты агентов — диагностические сведения, не доказательство онлайн-статуса.",
        ["Agent reports not checked. Choose a managed GPO and refresh feedback."] = "Отчёты агентов не проверены. Выберите управляемый GPO и обновите отчёты.",
        ["Select a managed GPO and refresh the reports."] = "Выберите управляемый GPO и обновите отчёты агентов.",
        ["SMB source changed. Refresh Agent Reports for the selected GPO."] = "Источник SMB изменён. Обновите Agent Reports для выбранного GPO.",
        ["Choose a managed GPO to read that campaign's SMB reports."] = "Выберите управляемый GPO, чтобы прочитать SMB-отчёты его кампании.",
        ["Client feedback share is missing or invalid. Configure SMB Client Health."] = "SMB-папка отчётов отсутствует или неверна. Настройте Client Health.",
        ["Duplicate or invalid AD short computer names: cannot map SMB reports safely."] = "Короткие имена ПК в AD повторяются или недопустимы: сопоставление SMB-отчётов невозможно.",
        ["Reading validated, campaign-matching SMB reports..."] = "Чтение проверяемых SMB-отчётов выбранной кампании...",
        ["Validated client reports for selected campaign; client-supplied status is not authenticated proof."] = "Отчёты сопоставлены с кампанией; данные клиента не являются подтверждённым доказательством.",
        ["Cannot read SMB Client Health share; report count is UNKNOWN, not zero."] = "Не удалось прочитать SMB Client Health: количество отчётов неизвестно, а не ноль.",
        ["AD inventory unavailable; all computer and report counts are UNKNOWN."] = "Инвентаризация AD недоступна: количество ПК и отчётов неизвестно.",
        ["Feedback source changed. Refresh SMB reports before reviewing or exporting."] = "Источник отчётов изменён. Обновите SMB-отчёты перед просмотром или экспортом.",
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
        ["AD OU: {0} | OU + security group: {1} | Visible after filters: {2} | Disabled: {3} | AD inactive / unknown >90d: {4}"] = "AD OU: {0} | OU + קבוצת אבטחה: {1} | מוצגים לאחר סינון: {2} | מושבתים: {3} | ללא פעילות ב-AD מעל 90 יום / לא ידוע: {4}",
        ["Agent AD inventory not verified."] = "מלאי AD עבור הסוכנים לא אומת.",
        ["Agent AD activity is approximate, not live online status."] = "פעילות הסוכנים ב-AD משוערת, אינה מצב מקוון בזמן אמת.",
        ["SMB reports: {0} / {1} eligible | Recent at check: {2} | Client type mismatch: {3} | No report: {4} | Invalid files: {5} | Checked: {6}"] = "דוחות SMB: {0} / {1} מחשבים מתאימים | עדכניים בעת הבדיקה: {2} | סוג לקוח לא תואם: {3} | ללא דוח: {4} | קבצים לא תקינים: {5} | נבדק: {6}",
        ["SMB check not verified"] = "בדיקת SMB לא אומתה",
        ["Agent reports are untrusted diagnostic hints, not online proof."] = "דוחות הסוכנים הם מידע אבחוני לא מאומת, לא הוכחה לחיבור מקוון.",
        ["Agent reports not checked. Choose a managed GPO and refresh feedback."] = "דוחות הסוכנים טרם נבדקו. בחר GPO מנוהל ורענן דוחות.",
        ["Select a managed GPO and refresh the reports."] = "בחר GPO מנוהל ורענן את דוחות הסוכנים.",
        ["SMB source changed. Refresh Agent Reports for the selected GPO."] = "מקור SMB השתנה. רענן את דוחות הסוכנים עבור ה-GPO שנבחר.",
        ["Choose a managed GPO to read that campaign's SMB reports."] = "בחר GPO מנוהל לקריאת דוחות SMB של הקמפיין שלו.",
        ["Client feedback share is missing or invalid. Configure SMB Client Health."] = "שיתוף SMB של דוחות הלקוח חסר או לא תקין. הגדר Client Health.",
        ["Duplicate or invalid AD short computer names: cannot map SMB reports safely."] = "שמות מחשבים קצרים ב-AD כפולים או לא תקינים: לא ניתן לשייך דוחות SMB בבטחה.",
        ["Reading validated, campaign-matching SMB reports..."] = "קורא דוחות SMB מאומתים מול הקמפיין שנבחר...",
        ["Validated client reports for selected campaign; client-supplied status is not authenticated proof."] = "דוחות הלקוח תואמים לקמפיין שנבחר; נתוני הלקוח אינם הוכחה מאומתת.",
        ["Cannot read SMB Client Health share; report count is UNKNOWN, not zero."] = "לא ניתן לקרוא את שיתוף SMB של Client Health: מספר הדוחות לא ידוע, אינו אפס.",
        ["AD inventory unavailable; all computer and report counts are UNKNOWN."] = "מלאי AD אינו זמין: מספר המחשבים והדוחות אינו ידוע.",
        ["Feedback source changed. Refresh SMB reports before reviewing or exporting."] = "מקור הדוחות השתנה. יש לרענן את דוחות SMB לפני סקירה או ייצוא.",
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
