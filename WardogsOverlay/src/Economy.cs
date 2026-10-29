using System.Text.Json;

namespace WardogsOverlay;

sealed record MoneyPoint(DateTime Time, int Balance);

sealed class EconomySession
{
    public DateTime Start { get; set; } = DateTime.Now;
    public List<MoneyPoint> Points { get; set; } = [];

    public int Earned => Deltas().Where(d => d > 0).Sum();
    public int Spent => -Deltas().Where(d => d < 0).Sum();
    public int Net => Points.Count < 2 ? 0 : Points[^1].Balance - Points[0].Balance;
    public TimeSpan Duration => Points.Count < 2 ? TimeSpan.Zero : Points[^1].Time - Points[0].Time;
    /// <summary>Заработок в минуту (без учёта покупок) — сколько приносит игра.</summary>
    public double EarnedPerMinute => Duration.TotalMinutes < 1 ? 0 : Earned / Duration.TotalMinutes;

    IEnumerable<int> Deltas()
    {
        for (int i = 1; i < Points.Count; i++) yield return Points[i].Balance - Points[i - 1].Balance;
    }
}

/// <summary>
/// Деньги игрока. Баланс читается с экрана продавца (справа сверху) каждый раз, когда открыто меню
/// модулей. Рост между заходами к продавцу — заработок, падение — покупки.
/// Сессия — с запуска программы (или с кнопки «Новая сессия»), история хранится в data/economy.json.
/// </summary>
sealed class Economy
{
    readonly object gate = new();
    readonly List<EconomySession> history;
    static string FilePath => Path.Combine(GameData.Dir, "economy.json");
    static readonly JsonSerializerOptions Json = new() { WriteIndented = false };
    long lastSave;

    public EconomySession Current { get; private set; } = new();
    public event Action? Changed;

    public Economy()
    {
        history = Load();
    }

    public IReadOnlyList<EconomySession> History
    {
        get { lock (gate) return history.Where(s => s.Points.Count >= 2).Reverse().ToList(); }
    }

    /// <summary>Новое показание баланса. Одинаковые подряд и явные ошибки OCR отбрасываются.</summary>
    public void Add(int balance)
    {
        lock (gate)
        {
            var pts = Current.Points;
            if (pts.Count > 0 && pts[^1].Balance == balance) return;
            // OCR мог потерять или добавить цифру: скачок в 10 раз без подтверждения — не верим.
            if (pts.Count > 0 && pts[^1].Balance > 0 && (balance > pts[^1].Balance * 8 + 50_000 || balance * 8 + 1000 < pts[^1].Balance))
            {
                if (pending != balance) { pending = balance; return; }
            }
            pending = null;
            pts.Add(new MoneyPoint(DateTime.Now, balance));
            if (!history.Contains(Current)) history.Add(Current);
            if (Environment.TickCount64 - lastSave > 30_000) SaveLocked();
        }
        Changed?.Invoke();
    }

    int? pending;

    public void NewSession()
    {
        lock (gate)
        {
            SaveLocked();
            Current = new EconomySession();
        }
        Changed?.Invoke();
    }

    public void Save()
    {
        lock (gate) SaveLocked();
    }

    void SaveLocked()
    {
        lastSave = Environment.TickCount64;
        try
        {
            var keep = history.Where(s => s.Points.Count >= 2).TakeLast(200).ToList();
            File.WriteAllText(FilePath, JsonSerializer.Serialize(keep, Json));
        }
        catch (Exception e) { Log.Info("Не удалось сохранить экономику: " + e.Message); }
    }

    static List<EconomySession> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<List<EconomySession>>(File.ReadAllText(FilePath), Json) ?? [];
        }
        catch { }
        return [];
    }

    /// <summary>Коротко для плашки и окна: «+$12 400 · $830/мин».</summary>
    public string Summary()
    {
        var s = Current;
        if (s.Points.Count < 2) return s.Points.Count == 1 ? L.T($"баланс {L.Money(s.Points[0].Balance)}", $"balance {L.Money(s.Points[0].Balance)}") : "—";
        var net = s.Net >= 0 ? "+" + L.Money(s.Net) : "−" + L.Money(-s.Net);
        var rate = s.EarnedPerMinute > 0 ? L.T($" · {L.Money((int)s.EarnedPerMinute)}/мин", $" · {L.Money((int)s.EarnedPerMinute)}/min") : "";
        return L.T($"сессия {net}", $"session {net}") + rate;
    }
}
