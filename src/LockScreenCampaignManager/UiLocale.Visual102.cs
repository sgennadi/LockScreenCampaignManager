namespace LockScreenCampaignManager;

/// <summary>
/// Display-only strings for operator-driven physical DPI and language review.
/// Never translate evidence JSON IDs, machine paths, AD/GPO values or versions.
/// </summary>
internal static partial class UiLocale
{
    private static readonly Dictionary<string, string> Visual102Russian = new(StringComparer.Ordinal)
    {
        ["Visual DPI / language check..."] = "Проверка DPI и языков...",
        ["Visual DPI / language acceptance"] = "Проверка DPI и языков",
        ["200% physical display check"] = "Проверка экрана при масштабе 200%",
        ["Operator review only. No screenshots, network, AD or GPO changes."] = "Только проверка оператором. Без снимков экрана, сети и изменений AD/GPO.",
        ["Display DPI: not checked."] = "DPI дисплея: не проверен.",
        ["Offline UI smoke: not run."] = "Автономная проверка интерфейса: не выполнялась.",
        ["Keep this window open beside the Manager. Check each item only after inspecting the real 200% monitor."] = "Оставьте это окно рядом с менеджером. Отмечайте пункты только после проверки на реальном мониторе 200%.",
        ["EN: Wizard, AD Deployment, Browse and Next buttons are fully visible."] = "EN: Wizard, AD Deployment, Browse и Next полностью видны.",
        ["RU: Russian labels and Client Health rows are not clipped."] = "RU: русские надписи и строки Client Health не обрезаны.",
        ["HE: Hebrew RTL Wizard, Updater and buttons are not clipped."] = "HE: иврит (RTL), Wizard, Updater и кнопки не обрезаны.",
        ["Mixed technical text (UNC, GUID, SHA-256, versions) remains LTR."] = "Технические значения (UNC, GUID, SHA-256, версии) отображаются слева направо.",
        ["Switching English / Russian / Hebrew / English preserves layout."] = "Переключение English / Русский / עברית / English не нарушает интерфейс.",
        ["I inspected the Manager on the 200% monitor, not an RDP window."] = "Я проверил менеджер на мониторе 200%, не через RDP.",
        ["Refresh display"] = "Обновить DPI экрана",
        ["Run offline UI smoke"] = "Запустить автономный UI-тест",
        ["Save local report"] = "Сохранить локальный отчёт",
        ["No data sent."] = "Данные не отправлялись.",
        ["Real 200% DPI detected on this window in the local console."] = "В локальной консоли определён фактический масштаб окна 200%.",
        ["Not accepted: move this window onto a real 200% local-console monitor."] = "Условие не выполнено: перенесите окно на локальный монитор с масштабом 200%.",
        ["Offline UI smoke: running."] = "Автономная проверка интерфейса выполняется.",
        ["Offline UI smoke: PASS (simulated layout only)."] = "Автономная проверка пройдена (только модель интерфейса).",
        ["Offline UI smoke: FAILED or timed out."] = "Автономная проверка не пройдена или истекло время.",
        ["Saved locally. Unsigned operator evidence; no data sent."] = "Сохранено локально. Подтверждение оператора без подписи; данные не отправлены.",
        ["Report saved locally. This is not proof of actual GPO application."] = "Отчёт сохранён локально. Это не доказывает фактическое применение GPO.",
        ["Report not saved. Check local folder permissions."] = "Отчёт не сохранён. Проверьте права на локальную папку.",
    };
    private static readonly Dictionary<string, string> Visual102Hebrew = new(StringComparer.Ordinal)
    {
        ["Visual DPI / language check..."] = "בדיקת DPI ושפות...",
        ["Visual DPI / language acceptance"] = "אימות חזותי של DPI ושפות",
        ["200% physical display check"] = "בדיקת תצוגה אמיתית בקנה מידה 200%",
        ["Operator review only. No screenshots, network, AD or GPO changes."] = "בדיקה ידנית בלבד. ללא צילומי מסך, רשת או שינויים ב-AD/GPO.",
        ["Display DPI: not checked."] = "DPI של התצוגה: לא נבדק.",
        ["Offline UI smoke: not run."] = "בדיקת ממשק לא מקוונת: לא הופעלה.",
        ["Keep this window open beside the Manager. Check each item only after inspecting the real 200% monitor."] = "השאירו חלון זה פתוח לצד המנהל. סמנו כל סעיף רק לאחר בדיקה על צג אמיתי ב-200%.",
        ["EN: Wizard, AD Deployment, Browse and Next buttons are fully visible."] = "אנגלית: אשף, פריסת AD והלחצנים Browse ו-Next מוצגים במלואם.",
        ["RU: Russian labels and Client Health rows are not clipped."] = "רוסית: הכיתובים ושורות מצב הלקוחות אינם חתוכים.",
        ["HE: Hebrew RTL Wizard, Updater and buttons are not clipped."] = "עברית: האשף, העדכונים והלחצנים בכיווניות מימין לשמאל אינם חתוכים.",
        ["Mixed technical text (UNC, GUID, SHA-256, versions) remains LTR."] = "ערכים טכניים (UNC, GUID, SHA-256, גרסאות) נשארים משמאל לימין.",
        ["Switching English / Russian / Hebrew / English preserves layout."] = "מעבר בין אנגלית / רוסית / עברית / אנגלית אינו פוגע בפריסה.",
        ["I inspected the Manager on the 200% monitor, not an RDP window."] = "בדקתי את המנהל על צג ב-200%, ולא בחלון RDP.",
        ["Refresh display"] = "רענון נתוני תצוגה",
        ["Run offline UI smoke"] = "הפעלת בדיקת ממשק לא מקוונת",
        ["Save local report"] = "שמירת דוח מקומי",
        ["No data sent."] = "לא נשלחו נתונים.",
        ["Real 200% DPI detected on this window in the local console."] = "זוהה קנה מידה אמיתי של 200% בחלון במסוף מקומי.",
        ["Not accepted: move this window onto a real 200% local-console monitor."] = "התנאי לא התקיים: העבירו את החלון לצג מקומי אמיתי ב-200%.",
        ["Offline UI smoke: running."] = "בדיקת הממשק הלא מקוונת מתבצעת.",
        ["Offline UI smoke: PASS (simulated layout only)."] = "בדיקת ממשק עברה (פריסה מדומה בלבד).",
        ["Offline UI smoke: FAILED or timed out."] = "בדיקת הממשק נכשלה או חרגה מזמן ההמתנה.",
        ["Saved locally. Unsigned operator evidence; no data sent."] = "נשמר מקומית. תיעוד מפעיל ללא חתימה; לא נשלחו נתונים.",
        ["Report saved locally. This is not proof of actual GPO application."] = "הדוח נשמר מקומית. אין בכך הוכחה להחלה בפועל של GPO.",
        ["Report not saved. Check local folder permissions."] = "הדוח לא נשמר. בדקו הרשאות לתיקייה המקומית.",
    };
    private static readonly Dictionary<string, string> Visual102EnglishByRussian =
        Visual102Russian.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Visual102EnglishByHebrew =
        Visual102Hebrew.ToDictionary(kv => kv.Value, kv => kv.Key, StringComparer.Ordinal);
}
