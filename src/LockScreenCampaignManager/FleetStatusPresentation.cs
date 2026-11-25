using System.Globalization;
using System.Text.RegularExpressions;

namespace LockScreenCampaignManager;

/// <summary>Display-only fleet strings. No localized value is used for
/// authorization, filtering, report serialization or task status decisions.</summary>
internal static class FleetStatusPresentation
{
    private static readonly Dictionary<string, (string Russian, string Hebrew)> Strings =
        new(StringComparer.Ordinal)
    {
        ["Select a verified computer. AD activity is approximate; SMB reports are untrusted."] = ("Выберите проверенный компьютер. Активность AD приблизительна; SMB-отчёты не являются доказательством.", "בחר מחשב מאומת. פעילות AD משוערת; דוחות SMB אינם הוכחה מאומתת."),
        ["Computer: {0}"] = ("Компьютер: {0}", "מחשב: {0}"),
        ["AD last activity (replicated, approximate): {0} | Disabled: {1}"] = ("Последняя активность AD (репликация, приблизительно): {0} | Отключён: {1}", "פעילות AD אחרונה (משוכפלת, משוערת): {0} | מושבת: {1}"),
        ["RPC TCP/135: {0} | Probe: {1}"] = ("RPC TCP/135: {0} | Проверка: {1}", "RPC TCP/135: {0} | בדיקה: {1}"),
        ["Client report: {0} | Version: {1} | Last: {2}"] = ("Отчёт клиента: {0} | Версия: {1} | Последний: {2}", "דוח לקוח: {0} | גרסה: {1} | אחרון: {2}"),
        ["Managed GPO method: {0} | Client-reported type: {1} | Comparison: {2}"] = ("Метод управляемого GPO: {0} | Тип по отчёту клиента: {1} | Сравнение: {2}", "שיטת GPO מנוהל: {0} | סוג לפי דוח הלקוח: {1} | השוואה: {2}"),
        ["WARNING: Client-reported type differs from the verified managed GPO method. Verify installed endpoint binary before any repair."] = ("ВНИМАНИЕ: тип в отчёте клиента отличается от проверенного метода GPO. Перед исправлением проверьте установленный клиент.", "אזהרה: סוג הלקוח המדווח שונה משיטת ה-GPO שאומתה. לפני תיקון, בדוק את הקובץ המותקן במחשב."),
        ["Last remote task submission: {0} / {1} at {2}"] = ("Последняя отправка удалённой задачи: {0} / {1} в {2}", "שליחה אחרונה של משימה מרוחקת: {0} / {1} בשעה {2}"),
        ["Last remote task submission: no recorded command in this Manager session"] = ("Последняя отправка задачи: в текущем сеансе Manager нет записанной команды", "שליחת משימה אחרונה: אין פקודה מתועדת בהפעלה הנוכחית של Manager"),
        ["Task result: {0}"] = ("Результат отправки задачи: {0}", "תוצאת שליחת המשימה: {0}"),
        ["Never / unknown"] = ("Никогда / неизвестно", "מעולם לא / לא ידוע"),
        ["Unknown"] = ("Неизвестно", "לא ידוע"),
        ["Yes"] = ("Да", "כן"),
        ["No"] = ("Нет", "לא"),
        ["Not checked"] = ("Не проверено", "לא נבדק"),
        ["Not verified"] = ("Не подтверждено", "לא אומת"),
        ["Reachable"] = ("Доступен", "נגיש"),
        ["Not reachable"] = ("Недоступен по RPC", "לא נגיש ב-RPC"),
        ["No report"] = ("Нет отчёта", "אין דוח"),
        ["Invalid report"] = ("Неверный отчёт", "דוח לא תקין"),
        ["Reported completed"] = ("По отчёту: завершено", "לפי הדוח: הושלם"),
        ["Stale report"] = ("Устаревший отчёт", "דוח ישן"),
        ["Reported error"] = ("По отчёту: ошибка", "לפי הדוח: שגיאה"),
        ["Reported issue"] = ("По отчёту: проблема", "לפי הדוח: נדרשת בדיקה"),
        ["Reported pending"] = ("По отчёту: ожидается", "לפי הדוח: ממתין"),
        ["Reported healthy"] = ("По отчёту: исправен", "לפי הדוח: תקין"),
        ["Unverified report"] = ("Неподтверждённый отчёт", "דוח לא מאומת"),
        ["GPO method unknown"] = ("Метод GPO неизвестен", "שיטת GPO לא ידועה"),
        ["Not reported (legacy client)"] = ("Тип не указан (старый клиент)", "הסוג לא דווח (לקוח ישן)"),
        ["Reported match"] = ("По отчёту: совпадает", "לפי הדוח: תואם"),
        ["Reported mismatch"] = ("По отчёту: не совпадает", "לפי הדוח: לא תואם"),
        ["Submission accepted"] = ("Отправка принята", "השליחה התקבלה"),
        ["Submission pending"] = ("Отправка ожидается", "השליחה ממתינה"),
        ["Submission unavailable"] = ("Отправка недоступна", "השליחה אינה זמינה"),
        ["Submission failed"] = ("Ошибка отправки", "השליחה נכשלה"),
        ["Completed"] = ("Завершено", "הושלם"),
        ["Active"] = ("Активен", "פעיל"),
        ["Pending"] = ("Ожидается", "ממתין"),
        ["Healthy"] = ("Исправен", "תקין"),
        ["Error"] = ("Ошибка", "שגיאה"),
        ["Needs attention"] = ("Требует внимания", "נדרשת בדיקה"),
        ["Ready / pending"] = ("Готов / ожидает", "מוכן / ממתין"),
        ["RepairNeeded"] = ("Требуется исправление", "נדרש תיקון"),
        ["OK"] = ("ОК", "תקין"),
        ["Missing {0}, Corrupt {1}"] = ("Отсутствует: {0}, повреждено: {1}", "חסרים: {0}, פגומים: {1}"),
        ["No validated, campaign-matching SMB status received for this AD computer."] = ("Нет проверенного SMB-отчёта этого компьютера, соответствующего кампании.", "לא התקבל דוח SMB תקין התואם לקמפיין עבור מחשב AD זה."),
        ["SMB report identity, campaign or timestamp does not match the verified target."] = ("Идентификатор, кампания или время SMB-отчёта не соответствуют проверенной цели.", "זהות, קמפיין או חותמת זמן בדוח SMB אינם תואמים ליעד המאומת."),
        ["Client-reported: {0} / {1} | client method {2} | image {3} | registry {4} | task {5} | missing files {6} | corrupt files {7} | timestamp supplied by endpoint; not authenticated proof."] = ("По отчёту клиента: {0} / {1} | метод клиента: {2} | изображение: {3} | реестр: {4} | задача: {5} | отсутствует файлов: {6} | повреждено файлов: {7} | время указано клиентом; не является аутентифицированным доказательством.", "לפי דוח הלקוח: {0} / {1} | שיטת הלקוח: {2} | תמונה: {3} | רישום: {4} | משימה: {5} | קבצים חסרים: {6} | קבצים פגומים: {7} | חותמת הזמן נמסרה מהמחשב; אינה הוכחה מאומתת."),
        ["match"] = ("совпадает", "תואם"),
        ["not verified"] = ("не подтверждено", "לא אומת"),
        ["present"] = ("присутствует", "קיימת"),
        ["missing"] = ("отсутствует", "חסרה"),
    };

    private static readonly Regex FileCounts = new(
        @"\AMissing ([0-9]+), Corrupt ([0-9]+)\z",
        RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    internal static string T(string key, string language)
    {
        if (!Strings.TryGetValue(key, out var translated)) return key;
        return language == "Russian" ? translated.Russian :
            language == "Hebrew" ? translated.Hebrew : key;
    }

    internal static string Format(string key, string language, params object[] args) =>
        string.Format(CultureInfo.InvariantCulture, T(key, language), args);

    internal static string ClientType(string value, string language) =>
        value == "Unknown" ? T(value, language) : value;

    internal static string SubmissionKey(string status) =>
        status.ToLowerInvariant() switch {
            "success" => "Submission accepted",
            "pending" => "Submission pending",
            "offline" => "Submission unavailable",
            "failed" => "Submission failed",
            _ => status
        };

    internal static string HealthCell(int column, string raw, string language)
    {
        // Never translate computer names, timestamps, JPG filenames or versions,
        // even if the literal happens to match a translatable word such as Active.
        if (column == 9) return ClientType(raw, language);
        if (column is 1 or 2 or 6 or 7) return T(raw, language);
        if (column != 5) return raw;
        var match = FileCounts.Match(raw);
        return match.Success
            ? Format("Missing {0}, Corrupt {1}", language,
                match.Groups[1].Value, match.Groups[2].Value)
            : T(raw, language);
    }

    internal static string ReportDetails(AgentReportEvidence evidence, string language)
    {
        // Preserve the existing canonical raw English description.
        if (language == "English") return evidence.Details;
        if (evidence.Facts is not { } facts) return T(evidence.Details, language);
        return Format(
            "Client-reported: {0} / {1} | client method {2} | image {3} | registry {4} | task {5} | missing files {6} | corrupt files {7} | timestamp supplied by endpoint; not authenticated proof.",
            language, T(evidence.Stage, language), T(evidence.Health, language),
            ClientType(evidence.ClientType, language),
            T(facts.ImageMatches ? "match" : "not verified", language),
            T(facts.RegistryMatches ? "match" : "not verified", language),
            T(facts.TaskPresent ? "present" : "missing", language),
            facts.MissingFiles, facts.CorruptFiles);
    }
}
