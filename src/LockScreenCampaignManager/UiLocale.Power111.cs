namespace LockScreenCampaignManager;

/// <summary>
/// Pilot readiness presentation text (EN/RU/HE). Registry, XML, RSoP, LDAP,
/// GUID and observation schema values remain invariant; no protocol string
/// is localized. Dynamic technical evidence may still display in English.
/// </summary>
internal static partial class UiLocale
{
    private static readonly Dictionary<string, string> Power111Russian = new(StringComparer.Ordinal)
    {
        ["Pilot readiness..."] = "Проверка готовности пилота...",
        ["Pilot readiness review"] = "Проверка пилотного компьютера",
        ["Screen Saver pilot readiness — read-only"] =
            "Готовность Screen Saver на тестовом ПК — только чтение",
        ["Correlates independent DC GPO, unsigned User/Computer gpresult XML and optional endpoint power. No automatic verdict or changes."] =
            "Сопоставление политик DC, XML пользователя/компьютера и питания ПК. Без автоматического решения и изменений.",
        ["Pilot computer (FQDN)"] = "Компьютер пилота (FQDN)",
        ["Verified target Computer OU"] = "OU компьютера для проверки",
        ["Area"] = "Раздел",
        ["State"] = "Состояние",
        ["Observation"] = "Наблюдение",
        ["Next step"] = "Следующий шаг",
        ["Refresh review"] = "Обновить анализ",
        ["Verify power (PC)..."] = "Проверить питание ПК...",
        ["Read power settings from this approved domain computer using remote WMI and HKLM? The selected DC must reverify the exact Computer OU/FQDN. No changes, gpupdate, remote tasks or scripts are performed."] =
            "Прочитать настройки питания выбранного ПК через WMI и HKLM? DC повторно проверит точные OU/FQDN. Изменений, gpupdate, задач или скриптов не будет.",
        ["Verify endpoint power — read-only"] = "Чтение питания ПК — только чтение",
        ["Read-only endpoint query still running; review its outcome before closing."] =
            "Чтение данных ПК ещё выполняется. Проверьте результат перед закрытием.",
        ["Reading endpoint power (read-only)..."] = "Чтение питания ПК (только чтение)...",
        ["Enter a full domain computer FQDN and a configured Screen Saver idle time."] =
            "Введите полный FQDN ПК и настройте время ожидания Screen Saver.",
        ["No scanned Computer OU. Select a Computer OU in the campaign and rescan DC GPOs."] =
            "Нет проверенного OU компьютеров. Выберите OU в кампании и повторите сканирование GPO.",
        ["Not verified — invalid pilot input"] = "Не проверено — неверный ввод",
        ["Not verified — Computer OU missing"] = "Не проверено — нет OU компьютеров",
        ["Not verified — evidence mismatch"] = "Не проверено — данные противоречат",
        [" observed concerns | "] = " наблюдений с риском | ",
        [" source candidates | "] = " возможных конфликтов | ",
        [" not verified"] = " не проверено",
        ["Observed concern"] = "Обнаружена проблема",
        ["Source candidate"] = "Возможный конфликт",
        ["Not verified"] = "Не проверено",
        ["Information"] = "Информация",
        ["DC inventory"] = "Инвентаризация GPO",
        ["DC source integrity"] = "Проверка источников DC",
        ["Computer gpresult"] = "gpresult компьютера",
        ["User gpresult"] = "gpresult пользователя",
        ["Computer GPO source"] = "GPO компьютера",
        ["User GPO source"] = "GPO пользователя",
        ["Endpoint power"] = "Питание компьютера",
        ["Interactive User"] = "Сеанс пользователя",
        ["Visual acceptance"] = "Визуальная проверка",
        ["Display off"] = "Отключение экрана",
        ["Sleep"] = "Сон",
        ["Machine inactivity lock"] = "Блокировка по бездействию"
    };
    private static readonly Dictionary<string, string> Power111Hebrew = new(StringComparer.Ordinal)
    {
        ["Pilot readiness..."] = "בדיקת מוכנות לפיילוט...",
        ["Pilot readiness review"] = "בדיקת מוכנות מחשב פיילוט",
        ["Screen Saver pilot readiness — read-only"] =
            "מוכנות שומר המסך בפיילוט — קריאה בלבד",
        ["Correlates independent DC GPO, unsigned User/Computer gpresult XML and optional endpoint power. No automatic verdict or changes."] =
            "השוואת GPO בבקר התחום, XML של משתמש/מחשב ונתוני צריכת חשמל. ללא החלטה אוטומטית או שינוי.",
        ["Pilot computer (FQDN)"] = "מחשב פיילוט (FQDN)",
        ["Verified target Computer OU"] = "יחידת OU של המחשב שנבחר",
        ["Area"] = "תחום",
        ["State"] = "מצב",
        ["Observation"] = "ממצא",
        ["Next step"] = "השלב הבא",
        ["Refresh review"] = "רענון הבדיקה",
        ["Verify power (PC)..."] = "בדיקת צריכת חשמל במחשב...",
        ["Read power settings from this approved domain computer using remote WMI and HKLM? The selected DC must reverify the exact Computer OU/FQDN. No changes, gpupdate, remote tasks or scripts are performed."] =
            "לקרוא הגדרות חשמל באמצעות WMI ו-HKLM מהמחשב שנבחר? בקר התחום יאמת שוב OU/FQDN. לא יבוצעו שינויים, gpupdate, משימות או סקריפטים.",
        ["Verify endpoint power — read-only"] = "בדיקת חשמל במחשב — קריאה בלבד",
        ["Read-only endpoint query still running; review its outcome before closing."] =
            "שאילתת המחשב עדיין פעילה. יש לבדוק את התוצאה לפני סגירה.",
        ["Reading endpoint power (read-only)..."] = "קריאת נתוני חשמל במחשב...",
        ["Enter a full domain computer FQDN and a configured Screen Saver idle time."] =
            "יש להזין FQDN מלא ולהגדיר זמן המתנה לשומר המסך.",
        ["No scanned Computer OU. Select a Computer OU in the campaign and rescan DC GPOs."] =
            "לא נבחר OU של מחשבים. יש לבחור OU בקמפיין ולסרוק GPO מחדש.",
        ["Not verified — invalid pilot input"] = "לא אומת — נתוני פיילוט שגויים",
        ["Not verified — Computer OU missing"] = "לא אומת — חסר OU של מחשבים",
        ["Not verified — evidence mismatch"] = "לא אומת — נתונים סותרים",
        [" observed concerns | "] = " ממצאים מדאיגים | ",
        [" source candidates | "] = " חשדות למקור | ",
        [" not verified"] = " לא אומתו",
        ["Observed concern"] = "ממצא מדאיג",
        ["Source candidate"] = "חשד במקור",
        ["Not verified"] = "לא אומת",
        ["Information"] = "מידע",
        ["DC inventory"] = "מיפוי GPO בבקר התחום",
        ["DC source integrity"] = "תקינות מקורות GPO",
        ["Computer gpresult"] = "gpresult של מחשב",
        ["User gpresult"] = "gpresult של משתמש",
        ["Computer GPO source"] = "מקור GPO למחשב",
        ["User GPO source"] = "מקור GPO למשתמש",
        ["Endpoint power"] = "הגדרות חשמל במחשב",
        ["Interactive User"] = "הפעלת המשתמש",
        ["Visual acceptance"] = "בדיקה חזותית",
        ["Display off"] = "כיבוי מסך",
        ["Sleep"] = "שינה",
        ["Machine inactivity lock"] = "נעילה עקב חוסר פעילות"
    };
    private static readonly Dictionary<string, string> Power111EnglishByRussian =
        Power111Russian.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
    private static readonly Dictionary<string, string> Power111EnglishByHebrew =
        Power111Hebrew.ToDictionary(x => x.Value, x => x.Key, StringComparer.Ordinal);
}
