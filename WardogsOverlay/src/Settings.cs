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

    /// <summary>Настройки меняются из окна и горячими клавишами, читаются фоновым потоком.</summary>
    public event Action? Changed;
    public void NotifyChanged() { Save(); Changed?.Invoke(); }

    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "settings.json");

    public static Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath), Json) ?? new Settings();
        }
        catch (Exception e) { Log.Info("settings.json не читается, беру значения по умолчанию: " + e.Message); }
        var s = new Settings();
        s.Save();
        return s;
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json)); }
        catch (Exception e) { Log.Info("Не удалось сохранить settings.json: " + e.Message); }
    }
}
