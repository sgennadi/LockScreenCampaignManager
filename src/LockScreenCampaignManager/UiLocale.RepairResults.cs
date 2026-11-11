namespace LockScreenCampaignManager;

// Display-only repair outcomes; never translate queue records or operation codes.
internal static partial class UiLocale
{
    private static readonly Dictionary<string, string> RepairResultsRussian = new(StringComparer.Ordinal)
    {
        ["Repair attempted: {0} task success, {1} pending, {2} failed. Offline queued: {3}; health confirmation: {4}; uncertain tasks: {5}; failed: {6}. Check Client Health."] = "Попытка исправления: успешных заданий — {0}, ожидают — {1}, ошибок — {2}. В очереди недоступных: {3}; ожидают проверки состояния: {4}; задания с неизвестным результатом: {5}; требуют внимания: {6}. Проверьте состояние клиентов.",
        ["WARNING: Remote Repair may have executed but queue could not be saved; inspect clients before retry."] = "ВНИМАНИЕ: удалённое исправление могло выполниться, но очередь не сохранена. Проверьте клиенты перед повтором.",
        ["Repair request sent to {0} computers.\nSuccessful tasks: {1}\nPending: {2}\nFailures: {3}\n"] = "Запрос исправления отправлен на компьютеры: {0}.\nУспешных заданий: {1}\nОжидают: {2}\nОшибок: {3}\n",
        ["Deferred offline: {0}\nAwaiting client verification: {1}\nRequire manual review: {2}\n\n"] = "Отложено для недоступных ПК: {0}\nОжидают проверки клиента: {1}\nТребуют ручной проверки: {2}\n\n",
        ["WARNING: result queue was not saved; inspect the PCs manually.\n\n"] = "ВНИМАНИЕ: очередь результатов не сохранена. Проверьте компьютеры вручную.\n\n",
        ["Check fresh Client Health reports and actual Lock Screen application."] = "Проверьте свежие отчёты о состоянии клиентов и фактическое применение экрана блокировки.",
        ["Repair result"] = "Результат исправления",
        ["Select a managed GPO in the first tab before previewing a remote task."] = "Выберите управляемый GPO на первой вкладке перед просмотром удалённого задания.",
        ["Choose a managed GPO in the list first. Use Refresh GPOs to load policies managed by LockScreen Campaign Manager."] = "Сначала выберите управляемый GPO в списке. Обновите список GPO, чтобы загрузить политики LockScreen Campaign Manager.",
    };
    private static readonly Dictionary<string, string> RepairResultsEnglishByRussian =
        RepairResultsRussian.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);

    private static readonly Dictionary<string, string> RepairResultsHebrew = new(StringComparer.Ordinal)
    {
        ["Repair attempted: {0} task success, {1} pending, {2} failed. Offline queued: {3}; health confirmation: {4}; uncertain tasks: {5}; failed: {6}. Check Client Health."] = "בוצע ניסיון תיקון: {0} משימות הצליחו, {1} ממתינות, {2} נכשלו. בתור למחשבים לא זמינים: {3}; ממתינים לאימות מצב: {4}; משימות עם תוצאה לא ודאית: {5}; דורשים טיפול: {6}. בדקו את בריאות הלקוחות.",
        ["WARNING: Remote Repair may have executed but queue could not be saved; inspect clients before retry."] = "אזהרה: ייתכן שהתיקון מרחוק בוצע, אך התור לא נשמר. בדקו את הלקוחות לפני ניסיון נוסף.",
        ["Repair request sent to {0} computers.\nSuccessful tasks: {1}\nPending: {2}\nFailures: {3}\n"] = "בקשת תיקון נשלחה ל-{0} מחשבים.\nמשימות שהצליחו: {1}\nממתינות: {2}\nכשלים: {3}\n",
        ["Deferred offline: {0}\nAwaiting client verification: {1}\nRequire manual review: {2}\n\n"] = "נדחו עקב חוסר זמינות: {0}\nממתינים לאימות הלקוח: {1}\nדורשים בדיקה ידנית: {2}\n\n",
        ["WARNING: result queue was not saved; inspect the PCs manually.\n\n"] = "אזהרה: תור התוצאות לא נשמר. בדקו את המחשבים ידנית.\n\n",
        ["Check fresh Client Health reports and actual Lock Screen application."] = "בדקו דוחות עדכניים של בריאות הלקוחות ואת החלת מסך הנעילה בפועל.",
        ["Repair result"] = "תוצאת התיקון",
        ["Select a managed GPO in the first tab before previewing a remote task."] = "בחרו GPO מנוהל בכרטיסייה הראשונה לפני תצוגה מקדימה של משימה מרחוק.",
        ["Choose a managed GPO in the list first. Use Refresh GPOs to load policies managed by LockScreen Campaign Manager."] = "בחרו תחילה GPO מנוהל ברשימה. רעננו את רשימת ה-GPO כדי לטעון מדיניות המנוהלת על ידי LockScreen Campaign Manager.",
    };
    private static readonly Dictionary<string, string> RepairResultsEnglishByHebrew =
        RepairResultsHebrew.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);

}
