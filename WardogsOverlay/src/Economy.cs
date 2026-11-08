using System.Text.Json;

namespace WardogsOverlay;

sealed record MoneyPoint(DateTime Time, int Balance);

/// <summary>Покупка у продавца: падение баланса в одном заходе и что за это время появилось в сборке.</summary>
sealed record Purchase(DateTime Time, int Amount, string What, string Kind);

sealed class EconomySession
{
    public DateTime Start { get; set; } = DateTime.Now;
    public List<MoneyPoint> Points { get; set; } = [];
    public List<Purchase> Purchases { get; set; } = [];

    public int Earned => Deltas().Where(d => d > 0).Sum();
    public int Spent => -Deltas().Where(d => d < 0).Sum();
    public int Net => Earned - Spent;
    public TimeSpan Duration => Points.Count < 2 ? TimeSpan.Zero : Points[^1].Time - Points[0].Time;
    /// <summary>Заработок в минуту (без учёта покупок) — сколько приносит игра.</summary>
    public double EarnedPerMinute => CountedMinutes < 1 ? 0 : Earned / CountedMinutes;

    /// <summary>Сколько минут учтено в темпе: время до скачка (стрельбище, другой аккаунт) не входит.</summary>
    public double CountedMinutes
    {
        get
        {
            double minutes = 0;
            for (int i = 1; i < Points.Count; i++)
                if (!Economy.IsJump(Points[i - 1].Balance, Points[i].Balance))
                    minutes += (Points[i].Time - Points[i - 1].Time).TotalMinutes;
            return minutes;
        }
    }

    /// <summary>
    /// Изменения баланса между показаниями. Скачок в разы (стрельбище с $10 000, ошибка OCR,
    /// другой аккаунт) — не заработок и не покупка, а новая точка отсчёта.
    /// </summary>
    IEnumerable<int> Deltas()
    {
        for (int i = 1; i < Points.Count; i++)
            if (!Economy.IsJump(Points[i - 1].Balance, Points[i].Balance))
                yield return Points[i].Balance - Points[i - 1].Balance;
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
        // История, записанная прошлыми версиями, могла собрать ложные показания — чистим один раз при загрузке.
        if (history.Aggregate(false, (any, s) => Clean(s) | any)) SaveLocked();
    }

    public IReadOnlyList<EconomySession> History
    {
        get { lock (gate) return history.Where(s => s.Points.Count >= 2).Reverse().ToList(); }
    }

    /// <summary>Скачок баланса в разы — такое не заработать за один заход.</summary>
    public static bool IsJump(int prev, int cur) =>
        prev > 0 && (cur > prev * 8L + 50_000 || cur * 8L + 1000 < prev);

    /// <summary>
    /// Два показания — про один и тот же баланс: разница не больше $25 000 или 12%. Дальше — либо другое
    /// число в углу экрана (игра показывает там и заработок за сессию), либо ошибка OCR.
    /// </summary>
    public static bool Near(int a, int b) => Math.Abs((long)a - b) <= Math.Max(25_000, Math.Max(a, b) * 0.12);

    /// <summary>
    /// Убрать из сессии ложные показания — временные выбросы, после которых баланс возвращается к прежнему
    /// уровню: другое число в углу экрана (заработок за сессию) вперемешку с балансом или неверно прочитанная
    /// цифра. Настоящая трата или заработок остаётся: после неё баланс не возвращается. Траты, записанные
    /// в моменты выбросов, удаляются вместе с ними. lastFinal — сколько последних точек ещё не судим.
    /// </summary>
    public static bool Clean(EconomySession s, int lastFinal = 0)
    {
        var pts = s.Points;
        int judge = Math.Max(0, pts.Count - lastFinal);
        var keep = new List<MoneyPoint>();
        var removed = new List<DateTime>();
        for (int i = 0; i < pts.Count; i++)
        {
            if (i >= judge || keep.Count == 0) { keep.Add(pts[i]); continue; }
            var anchor = keep[^1];
            int v = pts[i].Balance, a = anchor.Balance;
            long diff = Math.Abs((long)v - a);
            // Мелкое изменение (заработок за тик, мелкая покупка) — настоящее, если только следом баланс не
            // вернулся ровно к прежнему за минуту (неверно прочитанная цифра).
            if (diff <= Math.Max(1000, a * 0.02))
            {
                bool exactBack = i + 1 < pts.Count && pts[i + 1].Balance == a && (pts[i + 1].Time - pts[i].Time).TotalSeconds < 60;
                if (exactBack) removed.Add(pts[i].Time); else keep.Add(pts[i]);
                continue;
            }
            // Сильно в стороне (другой счётчик) — выброс, если в ближайших 6 показаниях баланс снова около прежнего.
            // Заметно в стороне (неверная цифра) — если в ближайших 3 вернулся почти точно.
            bool far = !Near(v, a);
            int look = far ? 6 : 3;
            double back = far ? Math.Max(25_000, a * 0.05) : Math.Max(500, a * 0.005);
            bool returns = pts.Skip(i + 1).Take(look).Any(p => Math.Abs((long)p.Balance - a) <= back
                                                             && (p.Time - anchor.Time).TotalMinutes < (far ? 10 : 3));
            if (returns) removed.Add(pts[i].Time);
            else keep.Add(pts[i]);
        }
        if (removed.Count == 0) return false;
        s.Points = keep;
        // Трата остаётся, только если между соседними настоящими показаниями баланс правда упал на эту сумму.
        bool Real(Purchase p)
        {
            for (int i = 1; i < keep.Count; i++)
                if (Math.Abs((keep[i].Time - p.Time).TotalSeconds) < 5 && keep[i - 1].Balance - keep[i].Balance == p.Amount) return true;
            return false;
        }
        s.Purchases = s.Purchases.Where(Real).ToList();
        return true;
    }

    /// <summary>
    /// Новое показание баланса. Одинаковые подряд и явные ошибки OCR отбрасываются.
    /// visit — номер захода в меню: скачок принимается, только если повторился в другом заходе
    /// (в одном заходе OCR читает одну и ту же картинку и повторяет ту же ошибку).
    /// </summary>
    public void Add(int balance, int visit)
    {
        lock (gate)
        {
            var pts = Current.Points;
            if (pts.Count > 0 && pts[^1].Balance == balance) { pending = null; return; }
            // Скачок ровно в ~1000 раз — потеряна группа цифр («$304» вместо «$304 060»): верим длинному числу.
            if (pts.Count > 0 && IsJump(pts[^1].Balance, balance))
            {
                double ratio = balance / (double)Math.Max(1, pts[^1].Balance);
                if (ratio is >= 300 and <= 3000)
                {
                    pts.RemoveAt(pts.Count - 1);
                    pending = null;
                    goto accept;
                }
                if (ratio is >= 1 / 3000.0 and <= 1 / 300.0) return;
                if (pending != balance) { pending = balance; pendingVisit = visit; return; }
                if (pendingVisit == visit) return;
            }
            pending = null;
        accept:
            pts.Add(new MoneyPoint(DateTime.Now, balance));
            // Ложное показание видно через несколько точек: вернулся ли баланс к прежнему уровню.
            Clean(Current, lastFinal: 6);
            if (!history.Contains(Current)) history.Add(Current);
            // Второе показание — сессия впервые попадает в файл: сохраняем сразу, не дожидаясь 30 с.
            if (Current.Points.Count == 2 || Environment.TickCount64 - lastSave > 30_000) SaveLocked();
        }
        Changed?.Invoke();
    }

    int? pending;
    int pendingVisit;

    /// <summary>Записать покупку (падение баланса у продавца) — для «Куда ушли деньги».</summary>
    public void AddPurchase(int amount, string what, string kind)
    {
        lock (gate)
        {
            Current.Purchases.Add(new Purchase(DateTime.Now, amount, what, kind));
            SaveLocked();
        }
        Changed?.Invoke();
    }

    /// <summary>
    /// Средний заработок в минуту по всем сессиям (и текущей) — для окупаемости и срока до цели.
    /// Меньше 10 учтённых минут — не знаем.
    /// </summary>
    public double AverageRate
    {
        get
        {
            lock (gate)
            {
                var all = history.Contains(Current) ? history : history.Append(Current);
                double minutes = all.Sum(s => s.CountedMinutes), earned = all.Sum(s => s.Earned);
                return minutes < 10 ? 0 : earned / minutes;
            }
        }
    }

    /// <summary>Последний баланс у продавца (из любой сессии).</summary>
    public int? LastBalance
    {
        get
        {
            lock (gate)
                return Current.Points.Count > 0 ? Current.Points[^1].Balance
                     : history.LastOrDefault(s => s.Points.Count > 0)?.Points[^1].Balance;
        }
    }

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
            SafeFile.WriteAllText(FilePath, JsonSerializer.Serialize(keep, Json));
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
        catch (Exception e)
        {
            // Иначе следующее сохранение перезапишет всю историю одной текущей сессией.
            Log.Info("economy.json не читается, история отложена в economy.json.corrupt: " + e.Message);
            SafeFile.Quarantine(FilePath);
        }
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
