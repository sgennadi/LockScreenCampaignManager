# Развёртывание из GPO Startup (не GPO Run)

**Путь:** Computer Configuration → Policies → Windows Settings → Scripts → Startup → PowerShell Scripts.

1. Создай отдельную GPO и привяжи к тестовой OU с Windows 11 Education/Enterprise.
2. Открой LockScreen Campaign Manager и выбери изображения по порядку.
3. Укажи дату начала, время ежедневной смены (например, 14:00) и часовой пояс `Israel Standard Time`.
4. Экспортируй кампанию. Программа создаст папку с `campaign.json`, `images`, `LockScreen-Agent.ps1`.
5. Скопируй папку в `\\ad.example.com\SYSVOL\ad.example.com\scripts\LockScreenCampaigns\`.
6. В Startup → PowerShell Scripts → Add укажи полный UNC-путь к `LockScreen-Agent.ps1` в новой папке.
7. В `Script Parameters` впиши `-Mode Install`.
8. На тестовом компьютере сделай перезагрузку (один `gpupdate /force` недостаточен для Startup Script).
9. Проверяй локальное задание и состояние с помощью `Get-ScheduledTask -TaskName 'LockScreen-LS-<ID>'` и локального `LockScreen-Agent.ps1 -Mode Status`.
10. В конце кампании скрипт восстановит локальные значения реестра и удалит локальные изображения и задание. Удаление самой доменной GPO/SYSVOL выполняется отдельно администратором.

**Что важно:** GPO Startup выполняется от `LocalSystem`; у обычных пользователей не запрашиваются административные права. Для доступности SYSVOL можно включить Computer Configuration → Administrative Templates → System → Logon → Always wait for the network at computer startup and logon.

**Тест картинки до старта:** запусти локальный агент от администратора с `-Mode Test -TestImage 1 -TestSeconds 180`. Не закрывай консоль до восстановления параметров.

**Повторение через год:** создай **новый** campaign ID, новую папку в SYSVOL и новый GPO Startup script. Старую кампанию не редактируй в процессе исполнения.
