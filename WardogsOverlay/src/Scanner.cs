using System.Text.Json;

namespace WardogsOverlay;

/// <summary>
/// Фоновый цикл. Нагрузка подстраивается под ситуацию:
///  • игра не найдена / свёрнута / не в фокусе — раз в 0.6–2 с проверка окна, без снимков;
///  • игра в фокусе, меню нет — снимок + дешёвая проверка цвета, OCR только если есть оранжевые элементы;
///    через IdleAfterMs без меню — реже (или полный сон до Ctrl+Shift+O);
///  • меню открыто — раз в TrackIntervalMs снимок только нужной области и сравнение «отпечатка»,
///    полное распознавание — лишь когда картинка изменилась.
/// </summary>
sealed class Scanner : IDisposable
{
    readonly Settings settings;
    readonly GameWindow game;
    readonly Recognizer recognizer;
    readonly OverlayForm overlay;
    readonly ScreenCapture capture = new();
    readonly ScreenCapture moneyCapture = new();
    readonly AutoResetEvent wake = new(false);
    readonly Dictionary<string, List<string>> snapshots;
    readonly object snapLock = new();
    Thread? thread;
    volatile bool stop;

    sealed record Track(RectI Client, RectI RoiScreen, float[] Sig, long NextFull);
    Track? track;
    Recognition? current;
    long lastMenu = Environment.TickCount64;
    bool asleep, debugRequested, lastForeground, shown;
    bool menuVisible;
    int visit;
    string lastKey = "";
    int retries;
    string? pendingKey;
    long lastAutoDebug = -60_000;

    public bool Enabled { get; private set; } = true;
    public string GameStatus { get; private set; } = L.T("ищу…", "looking…");
    public bool GameFound { get; private set; }
    /// <summary>Игра на переднем плане — не время перезапускаться ради обновления.</summary>
    public bool GameForeground => lastForeground;
    /// <summary>Снимки игры давно чёрные: скорее всего, эксклюзивный полноэкранный режим.</summary>
    public bool CaptureBlack { get; private set; }
    int blankFrames;
    public string PlaqueStatus { get; private set; } = "—";
    public event Action? StatusChanged;

    static string SnapPath => Path.Combine(GameData.Dir, "snapshots.json");

    public Economy Economy { get; }

    public Scanner(Settings settings, Recognizer recognizer, OverlayForm overlay, Economy economy)
    {
        Economy = economy;
        this.settings = settings;
        this.recognizer = recognizer;
        this.overlay = overlay;
        game = new GameWindow(settings);
        snapshots = LoadSnapshots();
        overlay.HotkeyPressed += OnHotkey;
        overlay.Moved += (dx, dy) =>
        {
            settings.OverlayOffsetX += dx;
            settings.OverlayOffsetY += dy;
            settings.NotifyChanged();
            Log.Info($"Плашка сдвинута: {settings.OverlayOffsetX}, {settings.OverlayOffsetY}");
        };
        settings.Changed += Refresh;
    }

    void OnHotkey(OverlayForm.Hotkey key)
    {
        switch (key)
        {
            case OverlayForm.Hotkey.Toggle: SetEnabled(!Enabled); break;
            case OverlayForm.Hotkey.Snapshot: ToggleSnapshot(); break;
            case OverlayForm.Hotkey.Debug: debugRequested = true; wake.Set(); break;
            case OverlayForm.Hotkey.Best:
                settings.BestPriority = settings.BestPriority switch
                {
                    null => Priority.Balance,
                    Priority.Balance => Priority.Recoil,
                    Priority.Recoil => Priority.Handling,
                    _ => null,
                };
                settings.NotifyChanged();
                break;
            case OverlayForm.Hotkey.Options:
                settings.ShowSlotOptions = !settings.ShowSlotOptions;
                settings.NotifyChanged();
                break;
        }
    }

    /// <summary>Перерисовать плашку по свежему кадру (после смены настроек).</summary>
    public void Refresh()
    {
        track = null;
        lastKey = "";
        wake.Set();
    }

    public void Start()
    {
        thread = new Thread(Loop) { IsBackground = true, Name = "scanner", Priority = ThreadPriority.BelowNormal };
        thread.Start();
    }

    public void SetEnabled(bool on)
    {
        Enabled = on;
        asleep = false;
        lastMenu = Environment.TickCount64;
        Log.Info(on ? "Поиск включён" : "Поиск выключен");
        wake.Set();
        Notify();
    }

    public void Poke() => wake.Set();

    void Loop()
    {
        while (!stop)
        {
            int delay;
            try { delay = Step(); }
            catch (Exception e)
            {
                Log.Info("Ошибка в цикле: " + e);
                delay = 2000;
            }
            if (delay < 0) wake.WaitOne();
            else wake.WaitOne(delay);
        }
    }

    int Step()
    {
        if (!game.Locate())
        {
            SetForeground(false);
            Hide("—");
            GameFound = false;
            SetGame(L.T("не найдена (запусти Wardogs)", "not found (start Wardogs)"));
            return 2000;
        }

        bool fg = game.IsForeground && !game.IsMinimized;
        SetForeground(fg);
        if (!fg)
        {
            Hide(Enabled ? "—" : PlaqueStatus);
            GameFound = true;
            SetGame(L.T("найдена (не в фокусе)", "found (not focused)"));
            return 600;
        }
        GameFound = true;
        SetGame(L.T("найдена", "found"));

        if (!Enabled)
        {
            Hide(L.T("поиск выключен (Ctrl+Shift+O)", "search off (Ctrl+Shift+O)"));
            return 500;
        }

        var client = game.ClientRect();
        if (client.W < 320 || client.H < 240) return 1000;
        long now = Environment.TickCount64;
        // Деньги — в реальном времени: раз в 2 с маленький кусок правого верхнего угла (в бою и у продавца).
        if (now >= nextMoney)
        {
            nextMoney = now + 2000;
            try { WatchMoney(client); } catch (Exception e) { Log.Info("Чтение денег: " + e.Message); }
        }

        if (track != null && !debugRequested && client == track.Client && now < track.NextFull)
        {
            var sig = capture.Grab(track.RoiScreen).Signature();
            if (Frame.SignatureDiff(sig, track.Sig) < 4f) return settings.TrackIntervalMs;
        }

        var frame = capture.Grab(client);
        // Чёрный снимок 20 раз подряд (≈20 с) — не загрузка, а полноэкранный режим, который не даёт снимать окно.
        if (frame.IsBlank())
        {
            if (++blankFrames == 20 && !CaptureBlack)
            {
                CaptureBlack = true;
                Log.Info("Снимки игры чёрные — похоже на эксклюзивный полноэкранный режим");
                Notify();
            }
            Hide(L.T("снимок экрана чёрный", "screen capture is black"));
            return settings.ScanIntervalMs;
        }
        if (blankFrames > 0 || CaptureBlack)
        {
            blankFrames = 0;
            if (CaptureBlack) { CaptureBlack = false; Notify(); }
        }
        var rec = recognizer.Run(frame);
        if (debugRequested) SaveDebug(frame, rec);
        else if (settings.AutoDebugFrames && rec.Menu && now - lastAutoDebug > 60_000
                 && !rec.Occluded && (rec.Weapon == null || (rec.Cells.Any(c => c.Att == null) && retries >= 3)))
        {
            // Меню видно, а что-то не узнали — сохраняем снимок сами (не чаще раза в минуту, последние 10).
            lastAutoDebug = now;
            SaveDebug(frame, rec, auto: true);
        }

        bool samePlaque = rec.Menu && rec.Weapon != null && current?.Weapon?.Slug == rec.Weapon.Slug;
        // Навели мышь на обвес — подсказка игры закрыла ячейки. Пока она висит, оставляем то, что
        // уже показано, и перепроверяем (без подсказки сигнатура снова поменяется).
        if (samePlaque && rec.Occluded)
        {
            lastMenu = now;
            track = null;
            return settings.TrackIntervalMs;
        }
        // Состав поменялся — показываем, только если следующий кадр видит то же самое: один кадр
        // на анимации или с плохо прочитанным ценником не должен заставлять плашку мигать.
        if (samePlaque && rec.Key != current!.Key && rec.Key != pendingKey)
        {
            pendingKey = rec.Key;
            track = null;
            return 150;
        }
        pendingKey = null;

        // Кадр попал на анимацию смены обвеса — не мигаем «не узнал», а быстро перепроверяем.
        bool shaky = rec.Menu && rec.Weapon != null && rec.Cells.Any(c => c.Att == null);
        if (shaky && current?.Weapon?.Slug == rec.Weapon!.Slug && retries < 3)
        {
            retries++;
            track = null;
            return 150;
        }
        retries = 0;

        if (rec.Menu && !menuVisible) visit++;
        menuVisible = rec.Menu;
        // Баланс — с любого экрана продавца. На стрельбище все цены $0 и баланс ненастоящий ($10 000) —
        // в экономику не пишем: ячейки есть, но ни одной цены, или ровно $10 000 без цен на экране.
        bool priced = rec.Cells.Any(c => c.Price > 0);
        bool range = rec.Cells.Count > 0 && !priced || rec.Balance == 10000 && !priced;
        if ((rec.Menu || rec.VendorScreen) && !range)
        {
            // Стоимость закупа и состав сборки — чтобы узнать списание, когда игрок выйдет в бой.
            if (rec.CartValue is int cart and > 0) lastCart = cart;
            if (rec.Weapon != null)
                lastKit = string.Join(", ", new[] { L.Name(rec.Weapon) }.Concat(rec.Cells.Where(c => c.Att != null).Select(c => L.Name(c.Att!))));
            if (rec.Balance is int balance)
            {
                frameForPurchase = frame;
                OnBalance(balance, rec);
                frameForPurchase = null;
            }
        }

        if (rec.Menu && rec.Weapon != null)
        {
            lastMenu = now;
            asleep = false;
            var roiScreen = rec.Roi.Offset(client.X, client.Y);
            track = new Track(client, roiScreen, frame.Crop(rec.Roi).Signature(), now + settings.FullRecheckMs);
            current = rec;
            if (!settings.MyWeapons.Contains(rec.Weapon.Slug))
            {
                settings.MyWeapons.Add(rec.Weapon.Slug);
                settings.Save();
            }
            if (rec.Key != lastKey)
            {
                lastKey = rec.Key;
                Log.Info($"Плашка: {rec.Weapon.Name} — " +
                         string.Join(", ", rec.Cells.Select(c => c.Att?.Name ?? $"? (${c.Price})")));
                foreach (var t in rec.Trace) Log.Info("  " + t);
            }
            Present(rec, client);
            return settings.TrackIntervalMs;
        }

        track = null;
        current = null;
        if (rec.Menu && rec.Key != lastKey + "#nomatch")
        {
            lastKey = rec.Key + "#nomatch";
            Log.Info($"Меню видно, но пушка не распознана (прочитано: «{rec.WeaponText}»)");
            foreach (var t in rec.Trace) Log.Info("  " + t);
        }
        Hide(rec.Menu ? L.T("меню видно, пушку не узнал", "menu found, weapon not recognized")
                      : L.T("жду меню «Модули»", "waiting for the attachments menu"));

        if (now - lastMenu > settings.IdleAfterMs)
        {
            if (!asleep) { asleep = true; Log.Info("Меню не видно — режим экономии"); }
            if (settings.IdleScanIntervalMs <= 0)
            {
                Enabled = false;
                Hide(L.T("спит — Ctrl+Shift+O в игре", "asleep — Ctrl+Shift+O in game"));
                return 500;
            }
            return settings.IdleScanIntervalMs;
        }
        return settings.ScanIntervalMs;
    }

    void Present(Recognition rec, RectI client)
    {
        List<Attachment>? snap = null;
        lock (snapLock)
        {
            if (snapshots.TryGetValue(rec.Weapon!.Slug, out var slugs))
                snap = slugs.Select(s => recognizer.Data.Attachments.GetValueOrDefault(s)).OfType<Attachment>().ToList();
        }
        var model = OverlayModel.Build(rec.Weapon!, rec.Cells, snap, recognizer.Data, rec.SelectedSlot, settings);
        if (settings.ShowMoney && Economy.Current.Points.Count > 0) model.Money = Economy.Summary();
        if (model.LifeCost is int life && life > 0)
        {
            // Запас жизней: сколько раз можно купить такую же сборку на текущий баланс.
            var pts = Economy.Current.Points;
            // На стрельбище баланс ненастоящий ($10 000) — там берём последний баланс у продавца.
            int? balance = rec.Cells.Any(c => c.Price > 0) ? rec.Balance : null;
            balance ??= pts.Count > 0 ? pts[^1].Balance : null;
            model.Life = "≈ " + L.Money(life);
            if (balance is int b)
            {
                int lives = b / life;
                model.Life += L.T($" · запас {lives}", $" · {lives} rebuys");
                model.LifeWarn = lives < 3;
            }
            // Окупаемость: сколько минут игры при твоём среднем темпе отбивают эту сборку.
            if (Economy.AverageRate is double rate and > 0)
                model.Life += L.T($" · окуп. {Math.Max(1, (int)Math.Ceiling(life / rate))} мин", $" · payback {Math.Max(1, (int)Math.Ceiling(life / rate))} min");
        }
        float u = (float)(client.H / 1080.0 * settings.OverlayScale);
        int x = client.X + rec.AnchorX + settings.OverlayOffsetX;
        int y = client.Y + rec.AnchorY + settings.OverlayOffsetY;
        shown = true;
        overlay.BeginInvoke(() => overlay.Present(model, x, y, u, client));
        int n = rec.Cells.Count(c => c.Att != null);
        SetPlaque($"{L.Name(rec.Weapon!)} · {n} {L.T("обв.", n == 1 ? "attachment" : "attachments")}" +
                  (snap != null ? L.T(" · сравнение", " · comparing") : ""));
    }

    void ToggleSnapshot()
    {
        var rec = current;
        if (rec?.Weapon == null) return;
        lock (snapLock)
        {
            if (snapshots.Remove(rec.Weapon.Slug))
                Log.Info($"Сравнение для {rec.Weapon.Name} сброшено");
            else
            {
                snapshots[rec.Weapon.Slug] = rec.Cells.Where(c => c.Att != null).Select(c => c.Att!.Slug).ToList();
                Log.Info($"Сборка {rec.Weapon.Name} запомнена");
            }
            SaveSnapshots();
        }
        track = null; // перерисовать сразу
        wake.Set();
    }

    void SaveDebug(Frame frame, Recognition rec, bool auto = false)
    {
        if (!auto) debugRequested = false;
        try
        {
            var dir = Path.Combine(AppPaths.Root, "debug");
            Directory.CreateDirectory(dir);
            if (auto)
                foreach (var old in new DirectoryInfo(dir).GetFiles("auto_*").OrderByDescending(f => f.LastWriteTime).Skip(18))
                    old.Delete();
            var name = Path.Combine(dir, $"{(auto ? "auto" : "frame")}_{DateTime.Now:yyyyMMdd_HHmmss}");
            using (var bmp = frame.ToBitmap()) bmp.Save(name + ".png");
            File.WriteAllLines(name + ".txt", rec.Trace.Prepend($"menu={rec.Menu} reason={rec.Reason} weapon={rec.Weapon?.Name}"));
            Log.Info($"Снимок для отладки сохранён: {name}.png");
        }
        catch (Exception e) { Log.Info("Не удалось сохранить снимок: " + e.Message); }
    }

    void Hide(string plaque)
    {
        if (shown)
        {
            shown = false;
            overlay.BeginInvoke(overlay.Conceal);
        }
        SetPlaque(plaque);
    }

    // Последний принятый баланс (из боя или у продавца) и что было у продавца — чтобы понять, на что ушли деньги.
    int? vendorBalance;
    string? vendorWeapon;
    long nextMoney;
    int? moneyCandidate, suspect;
    long suspectSince, lastNearAnchor;
    int? lastCart;
    string? lastKit;

    /// <summary>
    /// Баланс в углу экрана в реальном времени. Счётчик в бою «докручивается» анимацией, поэтому сумму
    /// принимаем, когда она дважды подряд (через 2 с) одна и та же.
    /// </summary>
    void WatchMoney(RectI client)
    {
        var corner = RectI.FromLTRB(client.X + (int)(client.W * 0.70), client.Y, client.Right, client.Y + Math.Max(24, (int)(client.H * 0.07)));
        var (balance, cart) = recognizer.ReadCorner(moneyCapture.Grab(corner), client.W, client.H);
        if (balance is not int b) { moneyCandidate = null; return; }
        if (b != moneyCandidate) { moneyCandidate = b; return; }
        // Стрельбище: там всегда $10 000 — не настоящие деньги.
        if (b == 10000 && Economy.LastBalance is int lb && lb != 10000) return;
        // В углу бывает и другое число (заработок за сессию — баланс минус баланс при входе). Принимаем только
        // близкое к текущему балансу; новый уровень — если держится полторы минуты без прежних значений.
        long now = Environment.TickCount64;
        if (Economy.LastBalance is int anchor && !Economy.Near(b, anchor))
        {
            if (suspect is not int sv || !Economy.Near(b, sv)) { suspect = b; suspectSince = now; }
            if (now - suspectSince < 90_000 || now - lastNearAnchor < 90_000) return;
            Log.Info($"Баланс сменился на новый уровень: {anchor} → {b}");
        }
        lastNearAnchor = now;
        suspect = null;
        if (cart is int c and > 0) lastCart = c;
        OnBalance(b, null);
    }

    /// <summary>Новое показание баланса: в экономику и, если деньги убыли, — в «Куда ушли деньги».</summary>
    void OnBalance(int balance, Recognition? rec)
    {
        Economy.Add(balance, visit);
        var pts = Economy.Current.Points;
        if (pts.Count == 0 || pts[^1].Balance != balance) return; // показание не принято (скачок, ошибка OCR)
        if (vendorBalance is int prev && balance < prev && !Economy.IsJump(prev, balance))
        {
            int spent = prev - balance;
            string what, kind;
            if (lastCart is int c && c == spent)
            {
                // Ровно стоимость закупа у продавца — это он: выход в бой с этой сборкой.
                what = L.T("закуп комплекта", "loadout purchase") + (lastKit != null ? $": {lastKit}" : "");
                kind = "kit";
            }
            else if (rec != null) (what, kind) = VendorPurchase(rec, spent);
            else (what, kind) = (L.T("трата в бою (закуп, возрождение, техника)", "spent in match (loadout, respawn, vehicle)"), "other");
            Economy.AddPurchase(spent, what, kind);
            Log.Info($"Покупка: −{spent} — {what}");
        }
        vendorBalance = balance;
        if (rec?.Weapon != null) { vendorWeapon = rec.Weapon.Slug; vendorAtts = rec.Cells.Where(c => c.Att != null).Select(c => c.Att!).ToList(); }
    }
    Frame? frameForPurchase;
    List<Attachment> vendorAtts = [];

    /// <summary>
    /// Баланс упал — деньги ушли продавцу (иначе они в игре не убывают). На что: ищем, чья цена или цена
    /// открытия ровно равна потраченному — среди пушки и обвесов на экране; иначе — обвесы, которых не было
    /// в прошлый раз; иначе — патроны, броня или снаряжение.
    /// </summary>
    (string What, string Kind) VendorPurchase(Recognition rec, int spent)
    {
        var now = rec.Cells.Where(c => c.Att != null).Select(c => c.Att!).ToList();
        // Картинка над ценником на ровно потраченную сумму — по ней узнаём предмет, если названия на экране нет.
        DbItem? ByIcon(List<DbItem> cands)
        {
            if (frameForPurchase == null) return null;
            // Ценник ровно на потраченную сумму (при скидке — если игра показывает цену уже со скидкой).
            foreach (var tag in rec.PriceTags.Where(t => t.Value == spent))
            {
                var (item, score, second) = recognizer.ItemAt(frameForPurchase, tag.Box, cands);
                if (item != null && score >= 0.45 && (cands.Count == 1 || score - second >= 0.04)) return item;
            }
            return null;
        }
        return PurchaseMatcher.Attribute(recognizer.Data, spent, rec.Weapon, now, vendorWeapon, vendorAtts, rec.ScreenLines, ByIcon, settings.LimitedMode);
    }

    void SetForeground(bool fg)
    {
        if (fg == lastForeground) return;
        lastForeground = fg;
        menuVisible = false;
        // Вернулись в игру — даём обычный темп поиска, а не сразу режим экономии.
        if (fg) { lastMenu = Environment.TickCount64; asleep = false; }
        overlay.BeginInvoke(() => overlay.SetGameForeground(fg));
    }

    void SetGame(string s) { if (GameStatus != s) { GameStatus = s; Notify(); } }
    void SetPlaque(string s) { if (PlaqueStatus != s) { PlaqueStatus = s; Notify(); } }
    void Notify() => StatusChanged?.Invoke();

    static Dictionary<string, List<string>> LoadSnapshots()
    {
        try
        {
            if (File.Exists(SnapPath))
                return JsonSerializer.Deserialize<Dictionary<string, List<string>>>(File.ReadAllText(SnapPath)) ?? [];
        }
        catch { }
        return [];
    }

    void SaveSnapshots()
    {
        try { File.WriteAllText(SnapPath, JsonSerializer.Serialize(snapshots)); }
        catch (Exception e) { Log.Info("Не удалось сохранить сборки: " + e.Message); }
    }

    public void Dispose()
    {
        stop = true;
        wake.Set();
        thread?.Join(2000);
        capture.Dispose();
        moneyCapture.Dispose();
    }
}
