namespace LockScreenCampaignManager;

/// <summary>
/// Read-only all-DC GPO scanner captions. Values, GUIDs, DNs and policy
/// evidence stay invariant and are NEVER translated into serialized fields.
/// </summary>
internal static partial class UiLocale
{
    private static readonly Dictionary<string, string> Power108Russian = new(StringComparer.Ordinal)
    {
        ["Scan all DC GPOs"] = "Сканировать все GPO на DC",
        ["All DC GPO conflict audit"] = "Аудит всех GPO на DC",
        ["All domain GPOs on selected DC — read-only"] = "Все GPO на выбранном DC — только чтение",
        ["Static GPO source evidence; not winning RSoP, GPP/ACL/WMI applicability or endpoint behavior."] =
            "Данные источников GPO, а не итоговая RSoP, фильтры GPP/ACL/WMI или поведение ПК.",
        ["Search GPO, setting, registry key, value, link..."] =
            "Поиск GPO, параметра, ключа реестра, значения, связи...",
        ["All scopes"] = "Все области",
        ["All risks"] = "Все риски",
        ["All links"] = "Все связи",
        ["Computer"] = "Компьютер",
        ["User"] = "Пользователь",
        ["Link status"] = "Статус связи",
        ["Linked at"] = "Связана с",
        ["Linked candidate"] = "Возможна в выбранном OU",
        ["Outside selected OUs"] = "Вне выбранных OU",
        ["Inactive/filtered"] = "Отключена/отфильтрована",
        ["User OU not verified"] = "OU пользователей не проверен",
        ["Computer OU not verified"] = "OU компьютеров не проверен",
        ["Recognized settings | "] = "Распознанных параметров | ",
        [" recognized settings | "] = " распознанных параметров | ",
        [" GPOs | "] = " GPO | ",
        [" source issues | NOT RSoP"] = " ошибок источников | НЕ RSoP",
        ["Known-source read errors: none (unrecognized CSEs remain unverified)."] =
            "Ошибок известных источников нет (неизвестные CSE не проверены).",
        ["Unverified GPO source reads: "] = "Непроверенных чтений источников GPO: ",
        ["No complete GPO settings index, winning RSoP, security/WMI/GPP item-level filtering, client HKCU, MDM, site or loopback verification is claimed."] =
            "Полный индекс параметров, итоговая RSoP, фильтры ACL/WMI/GPP, HKCU, MDM, Site и Loopback не подтверждены.",
        ["This report contains private GPO names, domain paths and policy values. Save to a protected LOCAL folder only; do not upload raw CSV. Continue?"] =
            "Отчёт содержит частные имена GPO, пути домена и значения политик. Сохраняйте только локально в защищённой папке. Продолжить?",
        ["Private GPO report"] = "Частный отчёт GPO",
        ["Choose a Computer or User OU before scanning all DC GPOs."] =
            "Выберите OU компьютеров или пользователей перед проверкой всех GPO.",
        ["Domain GPO inventory finished (read-only). Review source and scope."] =
            "Инвентаризация GPO завершена (только чтение). Проверьте источник и применимость."
    };

    private static readonly Dictionary<string, string> Power108Hebrew = new(StringComparer.Ordinal)
    {
        ["Scan all DC GPOs"] = "סריקת כל מדיניות ה-GPO בבקר התחום",
        ["All DC GPO conflict audit"] = "בדיקת כל מדיניות ה-GPO בבקר התחום",
        ["All domain GPOs on selected DC — read-only"] = "כל מדיניות ה-GPO בבקר הנבחר — קריאה בלבד",
        ["Static GPO source evidence; not winning RSoP, GPP/ACL/WMI applicability or endpoint behavior."] =
            "מידע מקור של GPO בלבד, לא RSoP בפועל, מסנני GPP/ACL/WMI או התנהגות המחשב.",
        ["Search GPO, setting, registry key, value, link..."] =
            "חיפוש GPO, הגדרה, מפתח רישום, ערך או קישור...",
        ["All scopes"] = "כל ההיקפים",
        ["All risks"] = "כל רמות הסיכון",
        ["All links"] = "כל הקישורים",
        ["Computer"] = "מחשב",
        ["User"] = "משתמש",
        ["Link status"] = "מצב קישור",
        ["Linked at"] = "מקושר אל",
        ["Linked candidate"] = "מועמד מקושר",
        ["Outside selected OUs"] = "מחוץ ליחידות שנבחרו",
        ["Inactive/filtered"] = "מושבת/מסונן",
        ["User OU not verified"] = "יחידת המשתמשים לא אומתה",
        ["Computer OU not verified"] = "יחידת המחשבים לא אומתה",
        ["Recognized settings | "] = "הגדרות שזוהו | ",
        [" recognized settings | "] = " הגדרות שזוהו | ",
        [" GPOs | "] = " מדיניות GPO | ",
        [" source issues | NOT RSoP"] = " בעיות מקור | לא RSoP",
        ["Known-source read errors: none (unrecognized CSEs remain unverified)."] =
            "אין שגיאות קריאה במקורות מוכרים (CSE אחרים לא אומתו).",
        ["Unverified GPO source reads: "] = "קריאות מקור GPO שלא אומתו: ",
        ["No complete GPO settings index, winning RSoP, security/WMI/GPP item-level filtering, client HKCU, MDM, site or loopback verification is claimed."] =
            "לא אומתו כל ההגדרות, RSoP בפועל, מסנני ACL/WMI/GPP, ‏HKCU, ‏MDM, אתר או Loopback.",
        ["This report contains private GPO names, domain paths and policy values. Save to a protected LOCAL folder only; do not upload raw CSV. Continue?"] =
            "הדוח כולל שמות GPO, נתיבי תחום וערכי מדיניות פרטיים. שמור בתיקייה מקומית מוגנת בלבד. להמשיך?",
        ["Private GPO report"] = "דוח GPO פרטי",
        ["Choose a Computer or User OU before scanning all DC GPOs."] =
            "יש לבחור יחידת מחשבים או משתמשים לפני סריקת כל מדיניות ה-GPO.",
        ["Domain GPO inventory finished (read-only). Review source and scope."] =
            "סריקת GPO הושלמה (קריאה בלבד). יש לבדוק מקור והיקף."
    };

    private static readonly Dictionary<string, string> Power108EnglishByRussian =
        Power108Russian.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Power108EnglishByHebrew =
        Power108Hebrew.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
}
