namespace LockScreenCampaignManager;

/// <summary>Localized labels for the unified, AD-first GPO applicability UI.
/// Registry keys, LDAP identities, classifications and source values stay invariant.</summary>
internal static partial class UiLocale
{
    private static readonly Dictionary<string, string> Power115Russian =
        new(StringComparer.Ordinal)
    {
        ["Unified GPO readiness"] = "Единый анализатор применимости GPO",
        ["Analyze AD scope (no XML)..."] = "Проверить применение GPO в AD (без XML)...",
        ["Pilot AD user (SAM or UPN, optional)"] = "Пользователь AD (SAM / UPN, необязательно)",
        ["Reading selected DC OU/group/ACL and loopback (read-only)..."] =
            "Чтение OU, групп, ACL и loopback с выбранного DC (только чтение)...",
        ["AD-only scope not scanned."] = "Проверка применимости AD ещё не выполнялась.",
        ["AD applicability"] = "Применимость GPO по AD",
        ["AD scope freshness"] = "Актуальность сведений AD",
        ["AD loopback"] = "Обратная обработка GPO (loopback)",
        ["User scope"] = "Область политики пользователя",
        ["AD source details"] = "Подробности источников GPO",
        ["No exact computer/user ACL+OU+loopback scan was requested."] =
            "Анализ связей OU, ACL и loopback для конкретного объекта ещё не запрошен.",
        ["AD-only expected applicability. Not endpoint RSoP or winning setting."] =
            "Ожидаемая применимость по AD. Не подтверждает RSoP клиента или итоговое значение.",
        ["AD candidate"] = "Возможна по данным AD",
        ["AD conditional/unverified"] = "Условно / не подтверждено AD",
        ["Excluded: link disabled"] = "Исключено: связь GPO отключена",
        ["Excluded: block inheritance"] = "Исключено: блокировка наследования",
        ["Excluded: policy half disabled"] = "Исключено: раздел GPO отключён",
        ["Excluded: matching security deny"] = "Исключено: запрет в ACL",
        ["Conditional: WMI not evaluated"] = "Условно: WMI не проверен",
        ["Unknown: security filtering"] = "Неизвестно: фильтрация доступа",
        ["Not configured"] = "Не настроено",
        ["Merge candidate"] = "Возможный Merge",
        ["Replace candidate"] = "Возможный Replace"
    };
    private static readonly Dictionary<string, string> Power115Hebrew =
        new(StringComparer.Ordinal)
    {
        ["Unified GPO readiness"] = "מנתח יחיד לתחולת מדיניות GPO",
        ["Analyze AD scope (no XML)..."] = "בדיקת תחולת GPO ב-AD (ללא XML)...",
        ["Pilot AD user (SAM or UPN, optional)"] = "משתמש AD (שם SAM או UPN, לא חובה)",
        ["Reading selected DC OU/group/ACL and loopback (read-only)..."] =
            "קריאת OU, קבוצות, ACL ו-loopback מבקר התחום הנבחר (קריאה בלבד)...",
        ["AD-only scope not scanned."] = "טרם בוצעה בדיקת תחולה לפי AD.",
        ["AD applicability"] = "תחולת המדיניות לפי AD",
        ["AD scope freshness"] = "עדכניות נתוני AD",
        ["AD loopback"] = "עיבוד מדיניות Loopback",
        ["User scope"] = "תחולת מדיניות המשתמש",
        ["AD source details"] = "פרטי מקורות GPO",
        ["No exact computer/user ACL+OU+loopback scan was requested."] =
            "טרם התבקשה בדיקת OU, ACL ו-loopback למחשב ולמשתמש הנבחרים.",
        ["AD-only expected applicability. Not endpoint RSoP or winning setting."] =
            "תחולה צפויה לפי AD בלבד; אינה מוכיחה RSoP במחשב או ערך מדיניות מכריע.",
        ["AD candidate"] = "מועמד לפי AD",
        ["AD conditional/unverified"] = "מותנה או לא מאומת",
        ["Excluded: link disabled"] = "לא חל: קישור GPO מושבת",
        ["Excluded: block inheritance"] = "לא חל: חסימת ירושה",
        ["Excluded: policy half disabled"] = "לא חל: חלק המדיניות מושבת",
        ["Excluded: matching security deny"] = "לא חל: חסימת ACL",
        ["Conditional: WMI not evaluated"] = "מותנה: מסנן WMI לא נבדק",
        ["Unknown: security filtering"] = "לא ידוע: סינון הרשאות",
        ["Not configured"] = "לא מוגדר",
        ["Merge candidate"] = "Merge אפשרי",
        ["Replace candidate"] = "Replace אפשרי"
    };
    private static readonly Dictionary<string, string> Power115EnglishByRussian =
        Power115Russian.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Power115EnglishByHebrew =
        Power115Hebrew.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
}
