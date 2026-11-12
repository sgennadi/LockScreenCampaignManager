namespace LockScreenCampaignManager;

/// <summary>
/// Presentation-only OU/group coverage translations; never modifies LDAP
/// distinguished names, identity matching, counts or GPO applicability.
/// </summary>
internal static partial class UiLocale
{
    private static readonly Dictionary<string, string> Coverage104Russian = new(StringComparer.Ordinal)
    {
        ["OU/group coverage: not verified."] = "Покрытие OU/группы: не проверено.",
        ["OU subtree: {0} computer objects. Choose a group to check direct matches."] = "В OU и дочерних OU компьютеров: {0}. Выберите группу для подсчёта прямых совпадений.",
        ["OU subtree: {0} computers; selected group: {1} direct matches. Includes inactive accounts; not effective GPO coverage."] = "В OU с вложенными: {0} компьютеров; в выбранной группе напрямую: {1}. Неактивные учтены. Это не результат GPO.",
        ["Computer OU/group coverage: not verified."] = "Компьютеры OU/группы: не проверено.",
        ["User OU/group coverage: not verified."] = "Пользователи OU/группы: не проверено.",
        ["{0} objects in User/Computer OU subtree. Select a security group."] = "В OU и дочерних OU объектов: {0}. Выберите группу безопасности.",
        ["OU subtree: {0} {1} objects; selected group: {2} direct matches. Inactive accounts included. Not effective GPO application."] = "В OU с вложенными: {0} объектов ({1}); в группе напрямую: {2}. Неактивные учтены; это не применение GPO.",
        ["All groups shown. OU direct-member coverage is read-only and not effective GPO application."] = "Показаны все группы. Подсчёт прямого членства в OU — только чтение, не результат применения GPO.",
    };
    private static readonly Dictionary<string, string> Coverage104Hebrew = new(StringComparer.Ordinal)
    {
        ["OU/group coverage: not verified."] = "כיסוי יחידה/קבוצה: לא אומת.",
        ["OU subtree: {0} computer objects. Choose a group to check direct matches."] = "בעץ היחידה יש {0} אובייקטי מחשב. בחרו קבוצה לבדיקת חברות ישירה.",
        ["OU subtree: {0} computers; selected group: {1} direct matches. Includes inactive accounts; not effective GPO coverage."] = "בעץ היחידה {0} מחשבים; {1} חברים ישירים בקבוצה שנבחרה. כולל חשבונות לא פעילים; לא תוצאת GPO.",
        ["Computer OU/group coverage: not verified."] = "כיסוי מחשבי היחידה/הקבוצה: לא אומת.",
        ["User OU/group coverage: not verified."] = "כיסוי משתמשי היחידה/הקבוצה: לא אומת.",
        ["{0} objects in User/Computer OU subtree. Select a security group."] = "בעץ היחידה יש {0} אובייקטים. בחרו קבוצת אבטחה.",
        ["OU subtree: {0} {1} objects; selected group: {2} direct matches. Inactive accounts included. Not effective GPO application."] = "בעץ היחידה {0} אובייקטים מסוג {1}; בקבוצה {2} חברים ישירים. כולל לא פעילים; זו לא החלה של GPO.",
        ["All groups shown. OU direct-member coverage is read-only and not effective GPO application."] = "מוצגות כל הקבוצות. בדיקת החברות הישירה ביחידה היא לקריאה בלבד ואינה מעידה על החלת GPO.",
    };
    private static readonly Dictionary<string, string> Coverage104EnglishByRussian =
        Coverage104Russian.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Coverage104EnglishByHebrew =
        Coverage104Hebrew.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
}
