namespace LockScreenCampaignManager;

/// <summary>
/// Presentation-only Preview.107 power observation RU/HE captions.
/// AD, WMI, HKLM, GUIDs and serialized values remain English/invariant.
/// </summary>
internal static partial class UiLocale
{
    private static readonly Dictionary<string, string> Power107Russian = new(StringComparer.Ordinal)
    {
        ["Source disagreements (WMI vs HKLM): "] = "Расхождения WMI и HKLM: ",
        ["Verify Screen Saver power (PC)"] = "Проверить питание Screen Saver (ПК)",
        ["Screen Saver power audit"] = "Проверка питания Screen Saver",
        ["Screen Saver — observed endpoint power and static GPO candidates"] =
            "Screen Saver — питание компьютера и возможные конфликты GPO",
        ["Select a Screen Saver or Both campaign before checking client power settings."] =
            "Выберите кампанию Screen Saver или Both перед проверкой питания.",
        ["Configure a valid Screen Saver idle timeout first."] =
            "Сначала укажите корректное время включения Screen Saver.",
        ["Power audit completed (read-only). Review endpoint readings and GPO candidates."] =
            "Проверка питания завершена (только чтение). Смотрите показания ПК и GPO.",
        ["Configured User Screen Saver: "] = "Настроенный Screen Saver пользователя: ",
        ["Observed UTC: "] = "Проверено UTC: ",
        ["Observed endpoint power"] = "Параметры компьютера",
        ["Linked GPO candidates"] = "Предполагаемые GPO",
        ["Source"] = "Источник",
        ["Setting"] = "Параметр",
        ["Supply"] = "Питание",
        ["State"] = "Состояние",
        ["Value (s)"] = "Значение (с)",
        ["Compared with Screen Saver"] = "Сравнение с Screen Saver",
        ["Value"] = "Значение",
        ["Risk"] = "Риск",
        ["Link"] = "Связь",
        ["Note"] = "Примечание",
        ["Configured User Screen Saver is not verified as applied HKCU policy."] =
            "Тайм-аут Screen Saver задан в кампании, но его применение в HKCU пользователя не проверено.",
        ["Active plan: "] = "Активная схема: ",
        ["Client HKLM power policies: "] = "Политики питания HKLM: ",
        ["Observed short timeouts: "] = "Тайм-ауты раньше заставки: ",
        ["Static linked-GPO candidates: "] = "Статические кандидаты GPO: ",
        ["No winning GPO, active display state or actual User Screen Saver is proven. Verify gpresult /scope user and computer, powercfg /query, MDM and real desktop behavior."] =
            "Применённая GPO, текущее состояние дисплея и работа заставки не подтверждены. Проверьте gpresult пользователя/компьютера, powercfg /query, MDM и реальный сеанс.",
        ["Observed shorter timeout — potential blocker"] =
            "Наблюдаемый меньший тайм-аут — возможная помеха",
        ["Machine lock before configured saver — review"] =
            "Блокировка компьютера раньше заставки — проверить",
        ["Never (this setting only)"] = "Никогда (только этот параметр)",
        ["Same threshold — order not verified"] = "Одинаковое время — порядок неизвестен",
        ["Later than configured saver (this setting only)"] =
            "Позже заставки (только этот параметр)",
        ["Not configured here (other sources remain unknown)"] =
            "Здесь не задано (другие источники неизвестны)",
        ["Not verified"] = "Не проверено"
    };

    private static readonly Dictionary<string, string> Power107Hebrew = new(StringComparer.Ordinal)
    {
        ["Source disagreements (WMI vs HKLM): "] = "פערים בין WMI ו-HKLM: ",
        ["Verify Screen Saver power (PC)"] = "בדיקת חשמל לשומר מסך (מחשב)",
        ["Screen Saver power audit"] = "בדיקת מדיניות חשמל לשומר מסך",
        ["Screen Saver — observed endpoint power and static GPO candidates"] =
            "שומר מסך — הגדרות חשמל במחשב ומדיניות GPO אפשרית",
        ["Select a Screen Saver or Both campaign before checking client power settings."] =
            "בחר קמפיין שומר מסך או שניהם לפני בדיקת החשמל.",
        ["Configure a valid Screen Saver idle timeout first."] =
            "יש להגדיר תחילה זמן המתנה תקין לשומר מסך.",
        ["Power audit completed (read-only). Review endpoint readings and GPO candidates."] =
            "בדיקת החשמל הסתיימה (קריאה בלבד). עיין בערכי המחשב ובמדיניות האפשרית.",
        ["Configured User Screen Saver: "] = "זמן שומר המסך שהוגדר: ",
        ["Observed UTC: "] = "זמן בדיקה UTC: ",
        ["Observed endpoint power"] = "נתוני חשמל מהמחשב",
        ["Linked GPO candidates"] = "מדיניות GPO אפשרית",
        ["Source"] = "מקור",
        ["Setting"] = "הגדרה",
        ["Supply"] = "מקור חשמל",
        ["State"] = "מצב",
        ["Value (s)"] = "ערך (שניות)",
        ["Compared with Screen Saver"] = "השוואה לשומר מסך",
        ["Value"] = "ערך",
        ["Risk"] = "סיכון",
        ["Link"] = "קישור",
        ["Note"] = "הערה",
        ["Configured User Screen Saver is not verified as applied HKCU policy."] =
            "זמן שומר המסך הוגדר בקמפיין, אך לא אומתה החלתו ב-HKCU של המשתמש.",
        ["Active plan: "] = "תוכנית חשמל פעילה: ",
        ["Client HKLM power policies: "] = "מדיניות חשמל ב-HKLM: ",
        ["Observed short timeouts: "] = "זמנים קצרים מהפעלת שומר מסך: ",
        ["Static linked-GPO candidates: "] = "מדיניות GPO אפשרית: ",
        ["No winning GPO, active display state or actual User Screen Saver is proven. Verify gpresult /scope user and computer, powercfg /query, MDM and real desktop behavior."] =
            "לא אומתה מדיניות GPO מנצחת, מצב התצוגה או הפעלת שומר המסך בפועל. יש לבדוק gpresult למשתמש ולמחשב, powercfg /query, MDM והתנהגות אמיתית.",
        ["Observed shorter timeout — potential blocker"] =
            "זמן קצר משומר המסך — חסימה אפשרית",
        ["Machine lock before configured saver — review"] =
            "נעילת מחשב לפני שומר המסך — לבדיקה",
        ["Never (this setting only)"] = "לעולם לא (רק הגדרה זו)",
        ["Same threshold — order not verified"] = "זמן זהה — סדר ההפעלה לא אומת",
        ["Later than configured saver (this setting only)"] =
            "מאוחר יותר משומר המסך (רק הגדרה זו)",
        ["Not configured here (other sources remain unknown)"] =
            "לא מוגדר כאן (מקורות אחרים אינם ידועים)",
        ["Not verified"] = "לא אומת"
    };

    private static readonly Dictionary<string, string> Power107EnglishByRussian =
        Power107Russian.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Power107EnglishByHebrew =
        Power107Hebrew.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
}
