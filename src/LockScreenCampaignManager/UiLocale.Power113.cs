namespace LockScreenCampaignManager;

/// <summary>UI-only captions. DialogResult values stay invariant.</summary>
internal static partial class UiLocale
{
    private static readonly Dictionary<string,string> Power113Russian = new(StringComparer.Ordinal)
    {
        ["Notification"] = "Уведомление",
        ["Custom .scr / .exe (local pilot only)"] = "Свой .scr / .exe (локальный тест)",
        ["Browse .scr / .exe..."] = "Обзор .scr / .exe...",
        ["Choose local Screen Saver executable (pilot only)"] = "Выберите файл Screen Saver для локального теста",
        ["Custom Screen Saver — pilot only"] = "Своя заставка — только локальный тест",
        ["Selected local custom Screen Saver for manual pilot testing only."] =
            "Выбранная заставка предназначена только для ручного локального тестирования.",
        ["Select a custom .scr/.exe using Browse before continuing."] =
            "Выберите .scr/.exe кнопкой «Обзор» перед продолжением.",
        ["A .scr is an executable. An .exe must explicitly implement /s, /p, /c to work as a Windows Screen Saver. This selection does not execute, convert or deploy the file. GPO delivery is blocked."] =
            "Файл .scr является исполняемым. Для работы заставкой .exe должен поддерживать /s, /p, /c. Файл не запускается и не развёртывается. Применение через GPO заблокировано.",
        ["Please review this warning before continuing."] = "Проверьте предупреждение перед продолжением.",
        ["Action failed — review the details below."] = "Действие не выполнено — подробности ниже.",
        ["Choose an action to continue."] = "Выберите действие.",
        ["Information from LockScreen Campaign Manager."] = "Информация LockScreen Campaign Manager.",
        ["Dialog details"] = "Подробности сообщения",
        ["Esc closes without approval"] = "Esc — отмена без подтверждения",
        ["Yes"] = "Да",
        ["No"] = "Нет",
        ["OK"] = "ОК",
        ["Cancel"] = "Отмена",
        ["Retry"] = "Повторить",
        ["Abort"] = "Прервать",
        ["Ignore"] = "Игнорировать",
        ["Try again"] = "Повторить попытку",
        ["Continue"] = "Продолжить"
    };
    private static readonly Dictionary<string,string> Power113Hebrew = new(StringComparer.Ordinal)
    {
        ["Notification"] = "הודעה",
        ["Custom .scr / .exe (local pilot only)"] = "שומר מסך אישי .scr / .exe (בדיקה מקומית)",
        ["Browse .scr / .exe..."] = "בחירת .scr / .exe...",
        ["Choose local Screen Saver executable (pilot only)"] =
            "בחירת קובץ שומר מסך לבדיקה מקומית",
        ["Custom Screen Saver — pilot only"] = "שומר מסך אישי — לבדיקה מקומית בלבד",
        ["Selected local custom Screen Saver for manual pilot testing only."] =
            "נבחר שומר מסך אישי לבדיקה מקומית ידנית בלבד.",
        ["Select a custom .scr/.exe using Browse before continuing."] =
            "יש לבחור קובץ .scr/.exe לפני המשך הפעולה.",
        ["A .scr is an executable. An .exe must explicitly implement /s, /p, /c to work as a Windows Screen Saver. This selection does not execute, convert or deploy the file. GPO delivery is blocked."] =
            "קובץ .scr הוא קובץ הרצה. כדי לשמש שומר מסך, קובץ .exe חייב לתמוך ב-/s, /p, /c. הקובץ לא מורץ ולא נפרס. פריסה באמצעות GPO חסומה.",
        ["Please review this warning before continuing."] = "יש לעיין באזהרה לפני המשך הפעולה.",
        ["Action failed — review the details below."] = "הפעולה נכשלה — פרטים בהמשך.",
        ["Choose an action to continue."] = "יש לבחור פעולה להמשך.",
        ["Information from LockScreen Campaign Manager."] = "מידע מ-LockScreen Campaign Manager.",
        ["Dialog details"] = "פרטי ההודעה",
        ["Esc closes without approval"] = "Esc סוגר ללא אישור",
        ["Yes"] = "כן",
        ["No"] = "לא",
        ["OK"] = "אישור",
        ["Cancel"] = "ביטול",
        ["Retry"] = "נסה שוב",
        ["Abort"] = "הפסק",
        ["Ignore"] = "התעלם",
        ["Try again"] = "נסה שוב",
        ["Continue"] = "המשך"
    };
    private static readonly Dictionary<string,string> Power113EnglishByRussian =
        Power113Russian.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
    private static readonly Dictionary<string,string> Power113EnglishByHebrew =
        Power113Hebrew.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
}
