namespace LockScreenCampaignManager;

/// <summary>
/// Display-only captions for the explicit pilot-user HKCU snapshot. Raw
/// evidence, usernames, domains, JSON keys and policy settings are invariant.
/// </summary>
internal static partial class UiLocale
{
    private static readonly Dictionary<string, string> Power114Russian =
        new(StringComparer.Ordinal)
    {
        ["Endpoint user Screen Saver probe"] = "Проверка заставки текущего пользователя",
        ["Endpoint user Screen Saver probe..."] = "Проверка заставки этого ПК...",
        ["Run on the pilot PC as the signed-in user. Reads this user's HKCU and one local Screen Saver file without executing it. Another administrator account reads its own HKCU. Nothing is sent."] =
            "Запустите от имени вошедшего пользователя. Читается HKCU и один локальный файл заставки без запуска. Другой администратор прочитает свой HKCU. Ничего не отправляется.",
        ["Local Screen Saver file"] = "Локальный файл заставки",
        ["No HKCU snapshot yet. Click Capture this Windows user to read the current account."] =
            "Снимка HKCU ещё нет. Нажмите «Считать HKCU этого пользователя».",
        ["Local read only. No file has been created."] =
            "Только локальное чтение. Файл не создан.",
        ["Capture this Windows user"] = "Считать HKCU этого пользователя",
        ["Save local HKCU JSON..."] = "Сохранить локальный HKCU JSON...",
        ["Captured locally — unsigned, not an effective GPO attestation."] =
            "Локальные данные получены, не подписаны; не подтверждают итоговую GPO.",
        ["HKCU snapshot not available."] = "Не удалось получить данные HKCU.",
        ["This JSON contains private domain/computer/user identities, Screen Saver settings and paths. Save only in a protected LOCAL folder, transfer securely, and do not upload it to a public issue. Continue?"] =
            "JSON содержит частные данные домена, ПК, пользователя, заставки и путей. Сохраняйте только в защищённой локальной папке; не публикуйте в открытом Issue. Продолжить?",
        ["Private endpoint evidence"] = "Частные данные тестового компьютера",
        ["Export local pilot user HKCU evidence"] = "Экспорт локального HKCU пользователя",
        ["Saved LOCAL private HKCU evidence — not uploaded."] =
            "Частные данные HKCU сохранены локально — ничего не отправлено.",
        ["Capture this PC's user HKCU..."] = "Считать HKCU пользователя этого ПК...",
        ["Import user HKCU JSON..."] = "Импорт HKCU JSON пользователя...",
        ["Clear HKCU evidence"] = "Очистить данные HKCU",
        ["Import private local user HKCU evidence"] =
            "Импорт частного локального HKCU пользователя",
        ["User HKCU snapshot"] = "Снимок HKCU пользователя",
        ["User identity correlation"] = "Сопоставление пользователя",
        ["User Screen Saver active"] = "Включение заставки",
        ["User Screen Saver idle"] = "Ожидание заставки",
        ["User Screen Saver file"] = "Файл заставки",
        ["User Screen Saver file access"] = "Доступ к файлу заставки",
        ["User saver vs power"] = "Заставка и электропитание",
        ["User saver vs display"] = "Заставка и отключение экрана",
        ["User saver vs sleep"] = "Заставка и переход в сон",
        ["User saver vs machine lock"] = "Заставка и блокировка компьютера",
        ["User Screen Saver secure"] = "Защита заставки",
        ["Screen Saver UI restriction"] = "Ограничение окна заставки"
    };

    private static readonly Dictionary<string, string> Power114Hebrew =
        new(StringComparer.Ordinal)
    {
        ["Endpoint user Screen Saver probe"] = "בדיקת שומר המסך של המשתמש הנוכחי",
        ["Endpoint user Screen Saver probe..."] = "בדיקת שומר המסך במחשב זה...",
        ["Run on the pilot PC as the signed-in user. Reads this user's HKCU and one local Screen Saver file without executing it. Another administrator account reads its own HKCU. Nothing is sent."] =
            "יש להפעיל כמשתמש המחובר. הכלי קורא HKCU וקובץ שומר מסך מקומי ללא הפעלה. מנהל אחר יקרא את ה-HKCU של עצמו. שום מידע לא נשלח.",
        ["Local Screen Saver file"] = "קובץ שומר מסך מקומי",
        ["No HKCU snapshot yet. Click Capture this Windows user to read the current account."] =
            "טרם נאספו נתוני HKCU. יש ללחוץ על איסוף נתוני המשתמש הנוכחי.",
        ["Local read only. No file has been created."] =
            "קריאה מקומית בלבד. לא נוצר קובץ.",
        ["Capture this Windows user"] = "איסוף HKCU של המשתמש הנוכחי",
        ["Save local HKCU JSON..."] = "שמירת קובץ HKCU JSON מקומי...",
        ["Captured locally — unsigned, not an effective GPO attestation."] =
            "הנתונים נאספו מקומית, אינם חתומים ואינם מוכיחים מדיניות GPO אפקטיבית.",
        ["HKCU snapshot not available."] = "נתוני HKCU אינם זמינים.",
        ["This JSON contains private domain/computer/user identities, Screen Saver settings and paths. Save only in a protected LOCAL folder, transfer securely, and do not upload it to a public issue. Continue?"] =
            "קובץ JSON כולל פרטי תחום, מחשב, משתמש, שומר מסך ונתיבים. שמרו רק בתיקייה מקומית מוגנת ואל תעלו אותו לדיווח ציבורי. להמשיך?",
        ["Private endpoint evidence"] = "נתוני מחשב בדיקה פרטיים",
        ["Export local pilot user HKCU evidence"] = "ייצוא נתוני HKCU מקומיים של המשתמש",
        ["Saved LOCAL private HKCU evidence — not uploaded."] =
            "נתוני HKCU הפרטיים נשמרו מקומית — לא הועלו.",
        ["Capture this PC's user HKCU..."] = "איסוף HKCU של המשתמש במחשב זה...",
        ["Import user HKCU JSON..."] = "ייבוא JSON של HKCU המשתמש...",
        ["Clear HKCU evidence"] = "ניקוי נתוני HKCU",
        ["Import private local user HKCU evidence"] =
            "ייבוא נתוני HKCU מקומיים ופרטיים",
        ["User HKCU snapshot"] = "צילום מצב של HKCU המשתמש",
        ["User identity correlation"] = "התאמת זהות המשתמש",
        ["User Screen Saver active"] = "הפעלת שומר המסך",
        ["User Screen Saver idle"] = "השהיית שומר המסך",
        ["User Screen Saver file"] = "קובץ שומר המסך",
        ["User Screen Saver file access"] = "גישה לקובץ שומר המסך",
        ["User saver vs power"] = "שומר מסך מול הגדרות חשמל",
        ["User saver vs display"] = "שומר מסך מול כיבוי תצוגה",
        ["User saver vs sleep"] = "שומר מסך מול מצב שינה",
        ["User saver vs machine lock"] = "שומר מסך מול נעילת מחשב",
        ["User Screen Saver secure"] = "אבטחת שומר המסך",
        ["Screen Saver UI restriction"] = "הגבלת ממשק שומר המסך"
    };
    private static readonly Dictionary<string,string> Power114EnglishByRussian =
        Power114Russian.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
    private static readonly Dictionary<string,string> Power114EnglishByHebrew =
        Power114Hebrew.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
}
