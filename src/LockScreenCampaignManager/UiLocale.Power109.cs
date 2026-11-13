namespace LockScreenCampaignManager;

/// <summary>
/// Presentation-only labels for the read-only same-setting GPO comparisons.
/// Raw technical keys, policy values and CSV evidence never get translated.
/// </summary>
internal static partial class UiLocale
{
    private static readonly Dictionary<string, string> Power109Russian =
        new(StringComparer.Ordinal)
    {
        ["Compare GPO settings..."] = "Сравнить настройки GPO...",
        ["Compare GPO settings"] = "Сравнение настроек GPO",
        ["Same configured GPO settings — source comparison"] =
            "Совпадающие параметры GPO — сравнение источников",
        ["Linked candidates may target different devices and users. No winning GPO or effective conflict is proven."] =
            "Связанные GPO могут действовать на разные ПК и пользователей. Итоговая политика и конфликт не подтверждены.",
        ["Search source GPOs, setting, key or values..."] =
            "Поиск GPO, параметра, ключа реестра или значений...",
        ["All overlaps"] = "Все совпадения",
        ["Different configured values"] = "Различающиеся значения",
        ["Ambiguous source values"] = "Неоднозначные значения источников",
        ["Same configured value"] = "Одинаковое значение",
        ["Comparison"] = "Сравнение",
        ["Linked candidates"] = "Связанные кандидаты",
        ["Registry key"] = "Ключ реестра",
        ["GPOs"] = "Политики GPO",
        ["Values"] = "Значения",
        ["No overlap row selected. Zero overlaps does not prove GPO compatibility."] =
            "Не выбрано сравнение. Отсутствие совпадений не доказывает совместимость GPO.",
        ["Review user and computer gpresult before changing a GPO. No automatic winner or repair."] =
            "Перед изменением GPO проверьте gpresult пользователя и ПК. Победившая политика и исправление не определяются автоматически.",
        [" same-setting comparisons | NOT effective RSoP"] =
            " сравнений параметров | НЕ итоговая RSoP"
    };
    private static readonly Dictionary<string, string> Power109Hebrew =
        new(StringComparer.Ordinal)
    {
        ["Compare GPO settings..."] = "השוואת הגדרות GPO...",
        ["Compare GPO settings"] = "השוואת הגדרות GPO",
        ["Same configured GPO settings — source comparison"] =
            "הגדרות GPO זהות — השוואה בין מקורות",
        ["Linked candidates may target different devices and users. No winning GPO or effective conflict is proven."] =
            "מדיניות GPO מקושרת עשויה לחול על מחשבים ומשתמשים שונים. לא אומתו מדיניות מנצחת או התנגשות בפועל.",
        ["Search source GPOs, setting, key or values..."] =
            "חיפוש GPO, הגדרה, מפתח רישום או ערכים...",
        ["All overlaps"] = "כל החפיפות",
        ["Different configured values"] = "ערכים מוגדרים שונים",
        ["Ambiguous source values"] = "ערכי מקור לא חד-משמעיים",
        ["Same configured value"] = "ערך מוגדר זהה",
        ["Comparison"] = "השוואה",
        ["Linked candidates"] = "מדיניות מועמדת מקושרת",
        ["Registry key"] = "מפתח רישום",
        ["GPOs"] = "מדיניות GPO",
        ["Values"] = "ערכים",
        ["No overlap row selected. Zero overlaps does not prove GPO compatibility."] =
            "לא נבחרה שורת השוואה. היעדר חפיפות אינו מוכיח שמדיניות ה-GPO תואמת.",
        ["Review user and computer gpresult before changing a GPO. No automatic winner or repair."] =
            "יש לבדוק gpresult של המשתמש והמחשב לפני שינוי GPO. אין קביעה אוטומטית של מדיניות מנצחת או תיקון.",
        [" same-setting comparisons | NOT effective RSoP"] =
            " השוואות הגדרה | לא RSoP בפועל"
    };
    private static readonly Dictionary<string, string> Power109EnglishByRussian =
        Power109Russian.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Power109EnglishByHebrew =
        Power109Hebrew.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
}
