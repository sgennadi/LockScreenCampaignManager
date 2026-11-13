namespace LockScreenCampaignManager;

/// <summary>
/// Presentation-only Russian and Hebrew for dual-scope gpresult comparison.
/// XML schema, policy GUIDs, domains, principals and registry keys remain
/// invariant; translated captions never become protocol identifiers.
/// </summary>
internal static partial class UiLocale
{
    private static readonly Dictionary<string, string> Power110Russian =
        new(StringComparer.Ordinal)
    {
        ["Compare gpresult XML..."] = "Сравнить с gpresult XML...",
        ["Compare gpresult XML"] = "Сравнение с gpresult XML",
        ["GPO source vs imported Computer / User gpresult"] =
            "Настройки GPO и импортированные gpresult компьютера/пользователя",
        ["Read-only source comparison by exact GPO GUID and policy scope. Typed identities and imported XML are NOT AD-verified or signed."] =
            "Сравнение по GUID и области политики, только чтение. Введённые имена и XML не подтверждены AD и не подписаны.",
        ["Computer FQDN (typed):"] = "FQDN компьютера (введено):",
        ["User identity (exact):"] = "Имя пользователя (точное):",
        ["Import Computer XML..."] = "Импорт XML компьютера...",
        ["Import User XML..."] = "Импорт XML пользователя...",
        ["Clear Computer"] = "Очистить ПК",
        ["Clear User"] = "Очистить пользователя",
        ["Search GPO, setting, registry key or XML observation..."] =
            "Поиск GPO, параметра, реестра или состояния XML...",
        ["All RSoP states"] = "Все состояния RSoP",
        ["Reported RSoP"] = "Состояние RSoP из XML",
        ["Source risk"] = "Риск по источнику",
        ["Reported applied"] = "Отмечена применённой",
        ["Reported filtered"] = "Отмечена отфильтрованной",
        ["Insufficient flags"] = "Не хватает данных",
        ["Not listed (unknown)"] = "Нет в XML (неизвестно)",
        ["Not imported"] = "Не импортировано",
        ["XML loaded"] = "XML загружен",
        ["Select local gpresult /x XML"] = "Выберите локальный XML gpresult /x",
        ["The selected gpresult XML may contain private usernames, group memberships and internal policy paths. Only GPO GUIDs/flags and report metadata are retained in memory, not uploaded or saved. An unsigned file cannot prove the actual client identity. Continue?"] =
            "XML gpresult может содержать имена пользователей, группы и внутренние пути. GUID и флаги GPO хранятся только в памяти, без отправки и сохранения. Файл без подписи не подтверждает источник. Продолжить?",
        ["The imported snapshot is over 24 hours old: "] =
            "Импортированному отчёту больше 24 часов: ",
        ["Use this snapshot as historical evidence only?"] =
            "Использовать только как исторические сведения?",
        ["Enter the full Computer FQDN from the gpresult XML first."] =
            "Сначала введите полный FQDN компьютера из gpresult XML.",
        ["Computer identity required"] = "Нужно имя компьютера",
        ["Enter the exact UserResults identity from gpresult first."] =
            "Сначала введите точное имя UserResults из gpresult.",
        ["User identity required"] = "Нужно имя пользователя",
        [" source rows | Computer: "] = " строк источников | Компьютер: ",
        [" | User: "] = " | Пользователь: ",
        [" | NOT winning RSoP"] = " | НЕ итоговая RSoP"
    };

    private static readonly Dictionary<string, string> Power110Hebrew =
        new(StringComparer.Ordinal)
    {
        ["Compare gpresult XML..."] = "השוואה ל-gpresult XML...",
        ["Compare gpresult XML"] = "השוואת gpresult XML",
        ["GPO source vs imported Computer / User gpresult"] =
            "השוואת מקור GPO מול gpresult של מחשב ומשתמש",
        ["Read-only source comparison by exact GPO GUID and policy scope. Typed identities and imported XML are NOT AD-verified or signed."] =
            "השוואה לקריאה בלבד לפי GUID והיקף GPO. זהויות שהוקלדו וקובצי XML אינם מאומתים ב-AD או חתומים.",
        ["Computer FQDN (typed):"] = "שם FQDN של מחשב (הוקלד):",
        ["User identity (exact):"] = "זהות משתמש (מדויקת):",
        ["Import Computer XML..."] = "ייבוא XML של מחשב...",
        ["Import User XML..."] = "ייבוא XML של משתמש...",
        ["Clear Computer"] = "ניקוי מחשב",
        ["Clear User"] = "ניקוי משתמש",
        ["Search GPO, setting, registry key or XML observation..."] =
            "חיפוש GPO, הגדרה, מפתח רישום או מצב XML...",
        ["All RSoP states"] = "כל מצבי RSoP",
        ["Reported RSoP"] = "מצב RSoP בקובץ XML",
        ["Source risk"] = "סיכון לפי מקור",
        ["Reported applied"] = "דווח כמיושם",
        ["Reported filtered"] = "דווח כמסונן",
        ["Insufficient flags"] = "אין מספיק נתונים",
        ["Not listed (unknown)"] = "לא מופיע (לא ידוע)",
        ["Not imported"] = "לא יובא",
        ["XML loaded"] = "XML נטען",
        ["Select local gpresult /x XML"] = "בחירת קובץ gpresult /x XML מקומי",
        ["The selected gpresult XML may contain private usernames, group memberships and internal policy paths. Only GPO GUIDs/flags and report metadata are retained in memory, not uploaded or saved. An unsigned file cannot prove the actual client identity. Continue?"] =
            "קובץ gpresult XML עשוי להכיל שמות משתמשים, חברות בקבוצות ונתיבי מדיניות פנימיים. מזהי GUID ודגלי GPO נשמרים בזיכרון בלבד ללא שליחה או שמירה. קובץ לא חתום אינו מאמת את זהות המחשב. להמשיך?",
        ["The imported snapshot is over 24 hours old: "] =
            "קובץ הנתונים ישן מ-24 שעות: ",
        ["Use this snapshot as historical evidence only?"] =
            "להשתמש בו כמידע היסטורי בלבד?",
        ["Enter the full Computer FQDN from the gpresult XML first."] =
            "יש להזין תחילה שם FQDN מלא של המחשב מתוך gpresult XML.",
        ["Computer identity required"] = "נדרש שם מחשב",
        ["Enter the exact UserResults identity from gpresult first."] =
            "יש להזין תחילה את זהות UserResults המדויקת מתוך gpresult.",
        ["User identity required"] = "נדרשת זהות משתמש",
        [" source rows | Computer: "] = " שורות מקור | מחשב: ",
        [" | User: "] = " | משתמש: ",
        [" | NOT winning RSoP"] = " | לא RSoP מנצח"
    };

    private static readonly Dictionary<string, string> Power110EnglishByRussian =
        Power110Russian.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Power110EnglishByHebrew =
        Power110Hebrew.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
}
