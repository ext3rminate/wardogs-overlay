using System.Text.Encodings.Web;
using System.Text.Json;

namespace WardogsOverlay;

sealed class Settings
{
    /// <summary>Язык программы. По умолчанию — как у Windows (русская система → русский).</summary>
    public Lang Language { get; set; } =
        System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ru" ? Lang.Ru : Lang.En;

    /// <summary>Класс окна Unreal Engine. Пусто — не проверять.</summary>
    public string WindowClass { get; set; } = "UnrealWindow";
    public string[] WindowTitleContains { get; set; } = ["wardogs", "elytra"];

    /// <summary>Множитель размера плашки (1.0 — как в игре при 1080p).</summary>
    public double OverlayScale { get; set; } = 1.0;
    public int OverlayOffsetX { get; set; }
    public int OverlayOffsetY { get; set; }
    public double Opacity { get; set; } = 0.94;
    /// <summary>Скрывать плашку от программ записи экрана (и от собственных снимков).</summary>
    public bool HideFromCapture { get; set; } = true;

    /// <summary>Показывать варианты обвесов для выбранного (оранжевого) слота.</summary>
    public bool ShowSlotOptions { get; set; } = true;
    public int SlotOptionsMax { get; set; } = 7;
    /// <summary>Подбор лучшей сборки: null — выключен, иначе приоритет (Balance / Recoil / Handling).</summary>
    public Priority? BestPriority { get; set; }
    /// <summary>Бюджет на обвесы для подбора, $. 0 — без ограничения.</summary>
    public int BestBudget { get; set; }
    public bool StartMinimized { get; set; }
    public bool CompactMode { get; set; }
    /// <summary>Строка экономики (заработок за сессию) внизу плашки.</summary>
    public bool ShowMoney { get; set; } = true;
    /// <summary>Строка «сколько выстрелов до убийства» на плашке и против какой брони/дистанции её считать.</summary>
    public bool ShowKill { get; set; } = true;
    public int TargetArmour { get; set; } = 2;
    public int TargetDistance { get; set; } = 50;
    /// <summary>Цена жизни (что теряешь при смерти без реанимации) и запас жизней по балансу.</summary>
    public bool ShowLifeCost { get; set; } = true;
    public int MyArmour { get; set; } = 2;
    /// <summary>
    /// Уровни ролей игрока (Career, Infantry, Medic…): у каждой роли свой уровень, качаются они
    /// независимо. Задаются в «Прогрессе»; пусто — уровни не указаны, ничего не отсекаем.
    /// </summary>
    public Dictionary<string, int> RoleLevels { get; set; } = [];
    /// <summary>Сборка под бюджет и подбор на плашке — только из того, что уже открыто по этим уровням.</summary>
    public bool OnlyUnlocked { get; set; } = true;

    /// <summary>Уровни всех ролей так, как их показывает «Прогресс»: не заданная роль — 1-й уровень.</summary>
    public Dictionary<string, int> EffectiveLevels() => L.Roles.ToDictionary(r => r, r => RoleLevels.GetValueOrDefault(r, 1));

    /// <summary>
    /// Уровни для подбора на плашке или null. Плашка отсекает закрытое, только если уровни уже указаны
    /// (иначе у тех, кто их не заполнял, подбор внезапно опустеет); «Сборка под бюджет» берёт EffectiveLevels.
    /// </summary>
    public Dictionary<string, int>? UnlockLevels() => OnlyUnlocked && RoleLevels.Count > 0 ? EffectiveLevels() : null;

    /// <summary>Цель для накопления (предмет, который хочется открыть) — slug из базы.</summary>
    public string? GoalSlug { get; set; }
    /// <summary>Ставить новые версии с GitHub самому (когда игра не на переднем плане). Выключено — только предлагать.</summary>
    public bool AutoUpdate { get; set; } = true;
    /// <summary>Версия, которую попросили не предлагать («Пропустить»).</summary>
    public string? SkippedUpdate { get; set; }
    /// <summary>Сборка игры, чей патч пользователь уже видел (баннер «Вышел патч» больше не показываем).</summary>
    public string? SeenPatchBuild { get; set; }
    /// <summary>Пушки, которые оверлей узнавал в меню, — для фильтра «Мои пушки» в разделе «Патч».</summary>
    public List<string> MyWeapons { get; set; } = [];
    /// <summary>Сохранять снимок в debug, если меню видно, а пушку или обвес узнать не удалось.</summary>
    public bool AutoDebugFrames { get; set; } = true;

    /// <summary>Как часто искать меню, пока игра на переднем плане.</summary>
    public int ScanIntervalMs { get; set; } = 1000;
    /// <summary>Через сколько мс без меню переходить в «сонный» режим.</summary>
    public int IdleAfterMs { get; set; } = 10000;
    /// <summary>Интервал поиска в «сонном» режиме. 0 — полностью уснуть до Ctrl+Shift+O.</summary>
    public int IdleScanIntervalMs { get; set; } = 3000;
    /// <summary>Как часто проверять, не изменилась ли сборка, пока меню открыто.</summary>
    public int TrackIntervalMs { get; set; } = 400;
    /// <summary>Полная перепроверка меню даже без изменений на экране.</summary>
    public int FullRecheckMs { get; set; } = 8000;

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>Файла настроек не было — первый запуск (покажем окно «Первый запуск»).</summary>
    [System.Text.Json.Serialization.JsonIgnore] public bool IsNew { get; private set; }

    /// <summary>Настройки меняются из окна и горячими клавишами, читаются фоновым потоком.</summary>
    public event Action? Changed;
    public void NotifyChanged() { Save(); Changed?.Invoke(); }

    public static string FilePath => Path.Combine(AppPaths.Root, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return (JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new Settings()).Sanitized();
        }
        catch (Exception e)
        {
            // Битый файл не затираем молча: откладываем рядом, чтобы настройки можно было вернуть руками.
            Log.Info("settings.json не читается, беру значения по умолчанию: " + e.Message);
            SafeFile.Quarantine(FilePath);
        }
        return new Settings { IsNew = !File.Exists(FilePath) };
    }

    /// <summary>Значения из файла могли поправить руками: 0 мс в интервале — это 100% ядра, null — падение.</summary>
    Settings Sanitized()
    {
        WindowClass ??= "";
        MyWeapons ??= [];
        RoleLevels ??= [];
        foreach (var k in RoleLevels.Keys.ToList()) RoleLevels[k] = Math.Clamp(RoleLevels[k], 1, 200);
        if (WindowTitleContains == null || WindowTitleContains.Length == 0) WindowTitleContains = new Settings().WindowTitleContains;
        OverlayScale = double.IsFinite(OverlayScale) ? Math.Clamp(OverlayScale, 0.5, 3) : 1;
        Opacity = double.IsFinite(Opacity) ? Math.Clamp(Opacity, 0.2, 1) : 0.94;
        SlotOptionsMax = Math.Clamp(SlotOptionsMax, 1, 20);
        TargetArmour = Math.Clamp(TargetArmour, 0, 4);
        MyArmour = Math.Clamp(MyArmour, 0, 4);
        TargetDistance = Math.Clamp(TargetDistance, 5, 1000);
        BestBudget = Math.Clamp(BestBudget, 0, 9_999_999);
        ScanIntervalMs = Math.Clamp(ScanIntervalMs, 200, 10_000);
        IdleAfterMs = Math.Clamp(IdleAfterMs, 1000, 600_000);
        IdleScanIntervalMs = IdleScanIntervalMs <= 0 ? 0 : Math.Clamp(IdleScanIntervalMs, 500, 60_000);
        TrackIntervalMs = Math.Clamp(TrackIntervalMs, 100, 5000);
        FullRecheckMs = Math.Clamp(FullRecheckMs, 1000, 120_000);
        return this;
    }

    public void Save()
    {
        try { SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json)); }
        catch (Exception e) { Log.Info("Не удалось сохранить settings.json: " + e.Message); }
    }
}

/// <summary>Запись файлов так, чтобы сбой посреди записи не оставил полфайла.</summary>
static class SafeFile
{
    public static void WriteAllText(string path, string text)
    {
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, text);
        File.Move(tmp, path, true);
    }

    /// <summary>Нечитаемый файл — в сторону (.corrupt), чтобы следующее сохранение его не затёрло.</summary>
    public static void Quarantine(string path)
    {
        try { if (File.Exists(path)) File.Move(path, path + ".corrupt", true); }
        catch (Exception e) { Log.Info($"Не удалось отложить {Path.GetFileName(path)}: " + e.Message); }
    }
}
