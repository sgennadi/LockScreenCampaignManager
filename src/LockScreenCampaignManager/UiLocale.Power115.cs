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
        ["AD timer priority"] = "Приоритет таймеров по AD",
        ["AD-first: OU, nested groups, ACL, WMI candidates and loopback. gpresult XML and HKCU are optional; no policy changes."] = "Сначала AD: OU, вложенные группы, ACL, WMI и loopback. XML gpresult и HKCU необязательны; политики не изменяются.",
        ["Screen Saver enabled flag"] = "Признак включения заставки",
        ["Screen Saver idle timer"] = "Время ожидания заставки",
        ["Display off (AC)"] = "Отключение экрана (AC)",
        ["Display off (DC)"] = "Отключение экрана (DC)",
        ["Sleep timer (AC)"] = "Переход в сон (AC)",
        ["Sleep timer (DC)"] = "Переход в сон (DC)",
        ["timer seconds"] = "сек.",
        ["AD Screen Saver timeout"] = "Таймер заставки по AD",
        ["Campaign Screen Saver timeout"] = "Таймер заставки кампании",
        ["0 = Never (this timer)"] = "0 = никогда (этот таймер)",
        ["Earlier than Screen Saver"] = "Раньше запуска заставки",
        ["Equal to Screen Saver threshold"] = "Одновременно с порогом заставки",
        ["Later than Screen Saver"] = "Позже запуска заставки",
        ["AD-only scan"] = "Сканирование только AD",
        ["Source candidates only, not endpoint RSoP."] = "Только возможные источники, не RSoP клиента.",
        ["Timer source precedence unresolved."] = "Приоритет источников таймера не определён.",
        ["Ranked AD/SYSVOL source only. Confirm the actual user, active power plan and endpoint policy; not applied RSoP."] =
            "Только приоритет источника AD/SYSVOL. Проверьте пользователя, активный план питания и политику клиента; не итоговый RSoP.",
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
        ["Replace candidate"] = "Возможный Replace",
        ["Conditional: loopback Replace candidate"] = "Условно: возможен Loopback Replace",
        ["Conditional: loopback unknown"] = "Условно: режим Loopback не определён",
        ["Conditional: loopback not confirmed"] = "Условно: Loopback не подтверждён на клиенте",
        ["Historical AD scope"] = "Устаревший снимок области AD",
        ["Computer GPO Read"] = "GPO: чтение ПК",
        ["Excluded: computer GPO Read denied"] =
            "Исключено: компьютеру запрещено чтение GPO",
        ["Conditional: computer Read not verified"] =
            "Условно: право компьютера на чтение GPO не подтверждено",
        ["SYSVOL / LocalSystem"] = "SYSVOL: чтение ПК",
        ["Import SYSTEM SYSVOL JSON..."] = "Импорт отчёта SYSVOL...",
        ["Clear SYSVOL evidence"] = "Очистить сведения SYSVOL",
        ["Import private local SYSTEM SYSVOL report"] = "Импорт локального отчёта SYSTEM SYSVOL"
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
        ["AD timer priority"] = "עדיפות זמני המתנה לפי AD",
        ["AD-first: OU, nested groups, ACL, WMI candidates and loopback. gpresult XML and HKCU are optional; no policy changes."] = "תחילה AD: יחידות OU, קבוצות מקוננות, ACL, מסנני WMI ו-Loopback. ‏XML של gpresult ו-HKCU אינם חובה; המדיניות לא תשתנה.",
        ["Screen Saver enabled flag"] = "דגל הפעלת שומר מסך",
        ["Screen Saver idle timer"] = "זמן המתנה לשומר מסך",
        ["Display off (AC)"] = "כיבוי תצוגה (AC)",
        ["Display off (DC)"] = "כיבוי תצוגה (DC)",
        ["Sleep timer (AC)"] = "מעבר לשינה (AC)",
        ["Sleep timer (DC)"] = "מעבר לשינה (DC)",
        ["timer seconds"] = "שניות",
        ["AD Screen Saver timeout"] = "זמן המתנה לשומר מסך לפי AD",
        ["Campaign Screen Saver timeout"] = "זמן המתנה לשומר מסך בקמפיין",
        ["0 = Never (this timer)"] = "0 = לעולם לא (טיימר זה)",
        ["Earlier than Screen Saver"] = "לפני הפעלת שומר המסך",
        ["Equal to Screen Saver threshold"] = "סף זמן זהה לשומר המסך",
        ["Later than Screen Saver"] = "אחרי הפעלת שומר המסך",
        ["AD-only scan"] = "סריקת AD בלבד",
        ["Source candidates only, not endpoint RSoP."] = "מקורות אפשריים בלבד, לא RSoP במחשב.",
        ["Timer source precedence unresolved."] = "עדיפות מקורות זמני ההמתנה לא נקבעה.",
        ["Ranked AD/SYSVOL source only. Confirm the actual user, active power plan and endpoint policy; not applied RSoP."] =
            "עדיפות מקור AD/SYSVOL בלבד. יש לאמת משתמש, תוכנית צריכת חשמל פעילה ומדיניות מחשב; לא RSoP מיושם.",
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
        ["Replace candidate"] = "Replace אפשרי",
        ["Conditional: loopback Replace candidate"] = "מותנה: Loopback Replace אפשרי",
        ["Conditional: loopback unknown"] = "מותנה: מצב Loopback לא ידוע",
        ["Conditional: loopback not confirmed"] = "מותנה: Loopback לא אומת במחשב",
        ["Historical AD scope"] = "צילום תחולת AD ישן",
        ["Computer GPO Read"] = "הרשאת קריאת GPO על ידי המחשב",
        ["Excluded: computer GPO Read denied"] =
            "לא חל: למחשב נדחתה הרשאת קריאת GPO",
        ["Conditional: computer Read not verified"] =
            "מותנה: הרשאת קריאת GPO של המחשב לא אומתה",
        ["SYSVOL / LocalSystem"] = "קריאת SYSVOL במחשב",
        ["Import SYSTEM SYSVOL JSON..."] = "ייבוא JSON של SYSVOL...",
        ["Clear SYSVOL evidence"] = "ניקוי נתוני SYSVOL",
        ["Import private local SYSTEM SYSVOL report"] = "ייבוא דוח SYSVOL מקומי של SYSTEM"
    };
    private static readonly Dictionary<string, string> Power115EnglishByRussian =
        Power115Russian.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Power115EnglishByHebrew =
        Power115Hebrew.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
}
