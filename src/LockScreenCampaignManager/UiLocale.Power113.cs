namespace LockScreenCampaignManager;

/// <summary>UI-only captions. DialogResult values stay invariant.</summary>
internal static partial class UiLocale
{
    private static readonly Dictionary<string,string> Power113Russian = new(StringComparer.Ordinal)
    {
        ["Notification"] = "Уведомление",
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
