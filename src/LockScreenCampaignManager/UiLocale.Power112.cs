namespace LockScreenCampaignManager;

/// <summary>
/// Presentation-only strings for the named GPO candidate review in EN/RU/HE.
/// Registry keys, GUIDs, state enum values and policy evidence stay invariant.
/// </summary>
internal static partial class UiLocale
{
    private static readonly Dictionary<string,string> Power112Russian = new(StringComparer.Ordinal)
    {
        ["Review GPO blockers..."] = "Проверить мешающие GPO...",
        ["Review candidate GPO blockers"] = "Проверка конфликтующих GPO",
        ["Named GPO blocker candidates — read-only"] =
            "Потенциально мешающие GPO поимённо — только чтение",
        ["A reported-applied GPO is NOT proof that its setting won or blocks the actual screen."] =
            "GPO, отмеченная применённой, ещё не означает, что её параметр победил или блокирует экран.",
        ["Search GPO, GUID, key, setting or value..."] =
            "Поиск GPO, GUID, ключа, параметра или значения...",
        ["All priorities"] = "Все приоритеты",
        ["Review priority"] = "Приоритет проверки",
        ["GPO GUID"] = "GUID политики",
        ["Review first (GPO reported applied)"] = "Проверить сначала (GPO отмечена применённой)",
        ["Historical gpresult"] = "Устаревший gpresult",
        ["Historical GPO source"] = "Устаревшие исходные данные GPO",
        ["Unverified applicability"] = "Применимость не проверена",
        [" named GPO candidates | "] = " потенциальных GPO | ",
        [" named source candidates | NOT winning RSoP"] =
            " потенциальных настроек | НЕ итоговая RSoP",
        ["No GPO candidate selected. No matches is not proof of compatibility."] =
            "GPO не выбрана. Отсутствие совпадений не доказывает отсутствие конфликтов.",
        ["Check actual per-setting RSoP, interactive HKCU and screen behavior before modifying any GPO."] =
            "Проверьте итоговый RSoP параметра, HKCU пользователя и экран перед изменением GPO.",
        ["The file contains private domain GPO names, GUIDs, policy paths and values. Save to a protected LOCAL folder only. Continue?"] =
            "Файл содержит частные имена GPO, GUID, пути и значения. Сохранять только локально в защищённой папке. Продолжить?",
        ["Export filtered named GPO candidates"] =
            "Экспорт отфильтрованных потенциальных GPO"
    };

    private static readonly Dictionary<string,string> Power112Hebrew = new(StringComparer.Ordinal)
    {
        ["Review GPO blockers..."] = "סקירת מדיניות GPO שעלולה להפריע...",
        ["Review candidate GPO blockers"] = "סקירת התנגשויות GPO אפשריות",
        ["Named GPO blocker candidates — read-only"] =
            "מדיניות GPO שעלולה להפריע לפי שם — קריאה בלבד",
        ["A reported-applied GPO is NOT proof that its setting won or blocks the actual screen."] =
            "GPO שדווחה כמיושמת אינה הוכחה שההגדרה שלה גברה או חוסמת את המסך.",
        ["Search GPO, GUID, key, setting or value..."] =
            "חיפוש GPO, GUID, מפתח, הגדרה או ערך...",
        ["All priorities"] = "כל סדרי העדיפויות",
        ["Review priority"] = "עדיפות לבדיקה",
        ["GPO GUID"] = "מזהה GUID של המדיניות",
        ["Review first (GPO reported applied)"] =
            "לבדוק תחילה (GPO דווחה כמיושמת)",
        ["Historical gpresult"] = "gpresult היסטורי",
        ["Historical GPO source"] = "מקור GPO היסטורי",
        ["Unverified applicability"] = "תחולה לא אומתה",
        [" named GPO candidates | "] = " מדיניות GPO אפשריות | ",
        [" named source candidates | NOT winning RSoP"] =
            " הגדרות אפשריות | לא RSoP מנצח",
        ["No GPO candidate selected. No matches is not proof of compatibility."] =
            "לא נבחרה GPO. היעדר התאמות אינו הוכחה לתאימות.",
        ["Check actual per-setting RSoP, interactive HKCU and screen behavior before modifying any GPO."] =
            "לפני שינוי GPO יש לבדוק RSoP של ההגדרה, HKCU של המשתמש והתנהגות המסך בפועל.",
        ["The file contains private domain GPO names, GUIDs, policy paths and values. Save to a protected LOCAL folder only. Continue?"] =
            "הקובץ מכיל שמות GPO פרטיים, GUID, נתיבים וערכים. יש לשמור בתיקייה מקומית מוגנת בלבד. להמשיך?",
        ["Export filtered named GPO candidates"] =
            "ייצוא מדיניות GPO אפשרית מסוננת"
    };

    private static readonly Dictionary<string,string> Power112EnglishByRussian =
        Power112Russian.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
    private static readonly Dictionary<string,string> Power112EnglishByHebrew =
        Power112Hebrew.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
}
