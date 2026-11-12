namespace LockScreenCampaignManager;

/// <summary>
/// Display-only approximate computer activity captions for Preview.105.
/// Canonical LDAP, JSON and OU/group/agent identities are never translated.
/// </summary>
internal static partial class UiLocale
{
    private static readonly Dictionary<string, string> Activity105Russian = new(StringComparer.Ordinal)
    {
        ["Last 30 days"] = "За 30 дней",
        ["Last 90 days"] = "За 90 дней",
        ["Last 180 days"] = "За 180 дней",
        ["Last 365 days"] = "За 365 дней",
        ["Computer activity: not verified (DC time unavailable)."] = "Активность компьютеров: не проверена (время DC недоступно).",
        ["Approximate replicated lastLogonTimestamp. Not a live heartbeat, deployment filter or GPO application."] = "Приблизительный реплицируемый lastLogonTimestamp. Не онлайн-статус, не фильтр назначения и не результат GPO.",
        ["Replicated AD lastLogonTimestamp (approximate, not online): enabled {0}, disabled {1}, unknown state {2}, no valid timestamp {3}; seen in {4} days: {5}. Does not change GPO targeting."] =
            "AD lastLogonTimestamp (приблизительно, не онлайн): включены {0}, отключены {1}, статус неизвестен {2}, без корректной даты {3}; за {4} дней: {5}. Назначение GPO не меняется.",
        ["Replicated AD lastLogonTimestamp (approximate, not online): enabled {0}, disabled {1}, unknown state {2}, no valid timestamp {3}; seen in {4} days: {5}; selected group: {6}. Does not change GPO targeting."] =
            "AD lastLogonTimestamp (приблизительно, не онлайн): включены {0}, отключены {1}, статус неизвестен {2}, без корректной даты {3}; за {4} дней: {5}; в выбранной группе: {6}. Назначение GPO не меняется."
    };

    private static readonly Dictionary<string, string> Activity105Hebrew = new(StringComparer.Ordinal)
    {
        ["Last 30 days"] = "ב-30 הימים האחרונים",
        ["Last 90 days"] = "ב-90 הימים האחרונים",
        ["Last 180 days"] = "ב-180 הימים האחרונים",
        ["Last 365 days"] = "ב-365 הימים האחרונים",
        ["Computer activity: not verified (DC time unavailable)."] = "פעילות המחשבים: לא אומתה (שעון בקר התחום אינו זמין).",
        ["Approximate replicated lastLogonTimestamp. Not a live heartbeat, deployment filter or GPO application."] = "זמן lastLogonTimestamp משוכפל ומשוער. אינו מצב מקוון, אינו מסנן פריסה ואינו הוכחת החלת GPO.",
        ["Replicated AD lastLogonTimestamp (approximate, not online): enabled {0}, disabled {1}, unknown state {2}, no valid timestamp {3}; seen in {4} days: {5}. Does not change GPO targeting."] =
            "AD lastLogonTimestamp (משוער, לא מקוון): פעילים {0}, מושבתים {1}, מצב לא ידוע {2}, ללא זמן תקין {3}; נצפו ב-{4} ימים: {5}. יעד ה-GPO אינו משתנה.",
        ["Replicated AD lastLogonTimestamp (approximate, not online): enabled {0}, disabled {1}, unknown state {2}, no valid timestamp {3}; seen in {4} days: {5}; selected group: {6}. Does not change GPO targeting."] =
            "AD lastLogonTimestamp (משוער, לא מקוון): פעילים {0}, מושבתים {1}, מצב לא ידוע {2}, ללא זמן תקין {3}; נצפו ב-{4} ימים: {5}; בקבוצה שנבחרה: {6}. יעד ה-GPO אינו משתנה."
    };
    private static readonly Dictionary<string, string> Activity105EnglishByRussian =
        Activity105Russian.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Activity105EnglishByHebrew =
        Activity105Hebrew.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
}
