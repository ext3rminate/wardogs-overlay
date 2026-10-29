namespace WardogsOverlay;

sealed record SlotOption(Attachment? Att, BuildStats Stats, bool Current, double Score);

sealed class SlotOptions
{
    public required string SlotLabel { get; init; }
    public required List<StatDef> Columns { get; init; }
    public required List<SlotOption> Rows { get; init; }
    public bool ShowCapacity { get; init; }
}

sealed class BestBuild
{
    public required Priority Priority { get; init; }
    public required List<Attachment> Atts { get; init; }
    public required BuildStats Stats { get; init; }
    public required int Cost { get; init; }
    public int? Budget { get; init; }
    public List<string> Kept { get; init; } = [];
}

static class Builds
{
    public static List<Attachment> ForSlot(GameData data, Weapon w, string slotKey) =>
        data.Attachments.Values.Where(a => a.InVendor && a.SlotKey == slotKey && a.FitsWeapon(w)).ToList();

    public static string SlotLabel(string slotKey) => Stats.SlotName(slotKey);

    /// <summary>
    /// Все обвесы на выбранный слот: итоговые статы сборки, если поставить каждый из них.
    /// Колонки — те статы, которые у вариантов реально различаются.
    /// </summary>
    public static SlotOptions? Options(GameData data, Weapon w, List<Attachment> current, string slotKey, int max)
    {
        var cands = ForSlot(data, w, slotKey);
        if (cands.Count == 0) return null;
        var cur = current.FirstOrDefault(a => a.SlotKey == slotKey);
        var others = current.Where(a => a.SlotKey != slotKey).ToList();

        var rows = cands.Select(c =>
        {
            var st = Stats.Compute(w, others.Append(c));
            return new SlotOption(c, st, cur != null && c.Slug == cur.Slug, Stats.Score(st));
        }).ToList();
        if (cur == null)
        {
            var st = Stats.Compute(w, others);
            rows.Add(new SlotOption(null, st, true, Stats.Score(st)));
        }

        var sorted = rows.OrderByDescending(r => r.Score).ToList();
        var shown = sorted.Take(max).ToList();
        var curRow = sorted.FirstOrDefault(r => r.Current);
        if (curRow != null && !shown.Contains(curRow)) { shown[^1] = curRow; }

        // Колонки — по показанным строкам, иначе бывают колонки, где у всех видимых «—» или «±0%».
        var columns = Stats.Defs
            .Select(d => (d, spread: shown.Max(r => Rel(r.Stats, d)) - shown.Min(r => Rel(r.Stats, d))))
            .Where(t => t.spread > 0.005 && (t.d.Better != Better.Neutral || t.d.Kind == StatKind.Zoom))
            .OrderByDescending(t => t.d.Kind == StatKind.Zoom ? 99 : t.spread * Math.Max(0.3, Stats.Weight(t.d, Priority.Balance)))
            .Take(4).Select(t => t.d)
            .OrderBy(d => Array.IndexOf(Stats.Defs, d)).ToList();

        return new SlotOptions
        {
            SlotLabel = SlotLabel(slotKey),
            Columns = columns,
            Rows = shown,
            ShowCapacity = slotKey == "Magazine",
        };
    }

    static double Rel(BuildStats s, StatDef d) => d.Kind switch
    {
        StatKind.Zoom => s.Value(d),
        StatKind.Seconds => s.Delta(d) / s.Base(d),
        _ => s.Delta(d),
    };

    /// <summary>
    /// Лучшая сборка под приоритет и бюджет. Прицел и магазин — дело вкуса (зум, патроны), их оставляем как есть.
    /// Эффекты обвесов почти независимы (перемножаются), поэтому слоты складываются как рюкзак по цене.
    /// </summary>
    public static BestBuild? Best(GameData data, Weapon w, List<Attachment> current, Priority p, int? budget)
    {
        var keep = current.Where(a => a.SlotKey is "Sight" or "Magazine" or "CantedSight").ToList();
        var slots = w.Slots.Select(s => s == "Pistolgrip" ? "Grip" : s).Distinct()
                     .Where(s => s is not ("Sight" or "Magazine" or "CantedSight")).ToList();
        if (slots.Count == 0) return null;

        double baseScore = Stats.Score(Stats.Compute(w, keep), p);
        var options = slots.Select(s => ForSlot(data, w, s)
                .Select(a => (att: (Attachment?)a, price: a.ShopPrice, gain: Stats.Score(Stats.Compute(w, keep.Append(a)), p) - baseScore))
                .Where(o => o.gain > 0.001)
                .Prepend((att: (Attachment?)null, price: 0, gain: 0.0))
                .ToList()).ToList();

        int keepCost = keep.Sum(a => a.ShopPrice);
        int limit = budget is int b ? Math.Max(0, b - keepCost) : int.MaxValue;
        const int Step = 10;
        int buckets = budget == null ? 1 : limit / Step + 1;

        // dp[i][c] — лучший прирост для первых i слотов при цене ≤ c·Step.
        var dp = new double[slots.Count + 1, buckets];
        var pick = new int[slots.Count + 1, buckets];
        for (int i = 1; i <= slots.Count; i++)
        {
            for (int c = 0; c < buckets; c++)
            {
                dp[i, c] = double.NegativeInfinity;
                for (int o = 0; o < options[i - 1].Count; o++)
                {
                    var opt = options[i - 1][o];
                    int cost = budget == null ? 0 : (opt.price + Step - 1) / Step;
                    if (cost > c) continue;
                    double v = dp[i - 1, c - cost] + opt.gain;
                    if (v > dp[i, c]) { dp[i, c] = v; pick[i, c] = o; }
                }
            }
        }

        var chosen = new List<Attachment>(keep);
        for (int i = slots.Count, c = buckets - 1; i >= 1; i--)
        {
            var opt = options[i - 1][pick[i, c]];
            if (opt.att != null) chosen.Add(opt.att);
            if (budget != null) c -= (opt.price + Step - 1) / Step;
        }

        return new BestBuild
        {
            Priority = p,
            Atts = chosen,
            Stats = Stats.Compute(w, chosen),
            Cost = chosen.Sum(a => a.ShopPrice),
            Budget = budget,
            Kept = keep.Select(a => a.Name).ToList(),
        };
    }

    // ---------- сборка под бюджет ----------

    public enum SightBand { None, Red, Mid, Long }
    public enum LoadoutGoal { Balance, Ttk, Recoil, Handling }

    public sealed class LoadoutRequest
    {
        public int Budget { get; init; }
        public int ArmourLevel { get; init; }      // 0 — без брони, иначе бронежилет и шлем этого уровня
        public int EnemyArmour { get; init; } = 2; // для оценки времени убийства
        public SightBand Sight { get; init; }
        public string? WeaponClass { get; init; }  // null — любой
        public LoadoutGoal Goal { get; init; }
        /// <summary>Учитывать только открытое: уровень по каждой роли (null — всё доступно).</summary>
        public Dictionary<string, int>? Levels { get; init; }
    }

    public sealed class Loadout
    {
        public required Weapon Weapon { get; init; }
        public required List<Attachment> Atts { get; init; }
        public DbItem? Armour { get; init; }
        public DbItem? Helmet { get; init; }
        public required BuildStats Stats { get; init; }
        public int Cost { get; init; }
        public int? Shots { get; init; }
        public double? Ttk { get; init; }
        public double? Climb { get; init; }
        public double Ads { get; init; }
        public double Score { get; set; }
    }

    static bool Unlocked(Unlock? u, Dictionary<string, int>? levels) =>
        levels == null || u == null || u.Level <= 0 || u.Role == null || u.Role == "None"
        || (levels.TryGetValue(u.Role, out var lvl) && lvl >= u.Level);

    static bool InBand(Attachment s, SightBand band)
    {
        double z = s.Zoom ?? 1.0;
        return band switch
        {
            SightBand.Red => z <= 2.0,
            SightBand.Mid => z > 2.0 && z <= 4.5,
            SightBand.Long => z > 4.5,
            _ => false,
        };
    }

    /// <summary>
    /// Лучшие комплекты под бюджет: для каждой подходящей пушки — прицел нужной кратности и обвесы
    /// на остаток (тем же рюкзаком, что и подбор), затем сравнение пушек между собой по времени
    /// убийства, подбросу ствола и скорости прицеливания (нормировано по кандидатам).
    /// </summary>
    public static List<Loadout> BestLoadouts(GameData data, LoadoutRequest r, int count = 5)
    {
        DbItem? armour = null, helmet = null;
        if (r.ArmourLevel > 0)
        {
            armour = data.Items.FirstOrDefault(i => i.Slug == $"level-{r.ArmourLevel}-armor");
            helmet = data.Items.FirstOrDefault(i => i.Slug == $"level-{r.ArmourLevel}-helmet");
        }
        int gear = (armour?.Price ?? 0) + (helmet?.Price ?? 0);
        int left0 = r.Budget - gear;
        if (left0 <= 0) return [];
        int enemyReduction = Combat.ArmourReduction(data, r.EnemyArmour);
        var priority = r.Goal switch { LoadoutGoal.Recoil => Priority.Recoil, LoadoutGoal.Handling => Priority.Handling, _ => Priority.Balance };

        var list = new List<Loadout>();
        foreach (var w in data.Weapons)
        {
            if (!w.InVendor || w.Price <= 0 || w.Price > left0 || w.Damage == null || w.Rpm == null) continue;
            if (r.WeaponClass != null && w.Sub != r.WeaponClass) continue;
            // Без явного выбора — только основное оружие: пистолет идёт вторым слотом, лук и гранатомёт — особые.
            if (r.WeaponClass == null && w.Sub is "Pistol" or "Bow" or "Launcher" or "Other") continue;
            if (!Unlocked(w.Unlock, r.Levels)) continue;
            int left = left0 - w.Price;

            var keep = new List<Attachment>();
            if (r.Sight != SightBand.None)
            {
                var sight = ForSlot(data, w, "Sight")
                    .Where(s => InBand(s, r.Sight) && s.ShopPrice <= left && Unlocked(s.Unlock, r.Levels))
                    .OrderByDescending(s => Stats.Score(Stats.Compute(w, [s]), priority)).FirstOrDefault();
                if (sight == null) continue; // нужен прицел, а подходящего нет или не хватает денег
                keep.Add(sight);
            }

            var best = BestWithFilter(data, w, keep, priority, left, a => Unlocked(a.Unlock, r.Levels));
            var atts = best?.Atts ?? keep;
            var stats = Stats.Compute(w, atts);
            var ttk = Combat.TtkChest(w, enemyReduction);
            list.Add(new Loadout
            {
                Weapon = w, Atts = atts, Armour = armour, Helmet = helmet, Stats = stats,
                Cost = w.Price + atts.Sum(a => a.ShopPrice) + gear,
                Shots = ttk?.Shots, Ttk = ttk?.Seconds,
                Climb = Combat.Climb(w, stats), Ads = stats.Value(Stats.Def("AimTime")),
            });
        }
        if (list.Count == 0) return list;

        var (wt, wc, wa) = r.Goal switch
        {
            LoadoutGoal.Ttk => (0.7, 0.15, 0.15),
            LoadoutGoal.Recoil => (0.2, 0.6, 0.2),
            LoadoutGoal.Handling => (0.2, 0.2, 0.6),
            _ => (0.4, 0.3, 0.3),
        };
        double Norm(double v, double min, double max) => max - min < 1e-9 ? 0 : (v - min) / (max - min);
        double tMin = list.Min(l => l.Ttk ?? 9), tMax = list.Max(l => l.Ttk ?? 9);
        double cMin = list.Min(l => l.Climb ?? 99), cMax = list.Max(l => l.Climb ?? 99);
        double aMin = list.Min(l => l.Ads), aMax = list.Max(l => l.Ads);
        foreach (var l in list)
            l.Score = wt * (1 - Norm(l.Ttk ?? tMax, tMin, tMax)) + wc * (1 - Norm(l.Climb ?? cMax, cMin, cMax))
                      + wa * (1 - Norm(l.Ads, aMin, aMax));
        return list.OrderByDescending(l => l.Score).Take(count).ToList();
    }

    /// <summary>Как Best, но только среди разрешённых обвесов (например, уже открытых).</summary>
    static BestBuild? BestWithFilter(GameData data, Weapon w, List<Attachment> keep, Priority p, int budget, Func<Attachment, bool> allow)
    {
        var slots = w.Slots.Select(s => s == "Pistolgrip" ? "Grip" : s).Distinct()
                     .Where(s => s is not ("Sight" or "Magazine" or "CantedSight")).ToList();
        if (slots.Count == 0) return null;
        double baseScore = Stats.Score(Stats.Compute(w, keep), p);
        var options = slots.Select(s => ForSlot(data, w, s).Where(allow)
                .Select(a => (att: (Attachment?)a, price: a.ShopPrice, gain: Stats.Score(Stats.Compute(w, keep.Append(a)), p) - baseScore))
                .Where(o => o.gain > 0.001)
                .Prepend((att: (Attachment?)null, price: 0, gain: 0.0))
                .ToList()).ToList();

        int limit = Math.Max(0, budget - keep.Sum(a => a.ShopPrice));
        const int Step = 10;
        int buckets = limit / Step + 1;
        var dp = new double[slots.Count + 1, buckets];
        var pick = new int[slots.Count + 1, buckets];
        for (int i = 1; i <= slots.Count; i++)
            for (int c = 0; c < buckets; c++)
            {
                dp[i, c] = double.NegativeInfinity;
                for (int o = 0; o < options[i - 1].Count; o++)
                {
                    int cost = (options[i - 1][o].price + Step - 1) / Step;
                    if (cost > c) continue;
                    double v = dp[i - 1, c - cost] + options[i - 1][o].gain;
                    if (v > dp[i, c]) { dp[i, c] = v; pick[i, c] = o; }
                }
            }
        var chosen = new List<Attachment>(keep);
        for (int i = slots.Count, c = buckets - 1; i >= 1; i--)
        {
            var opt = options[i - 1][pick[i, c]];
            if (opt.att != null) chosen.Add(opt.att);
            c -= (opt.price + Step - 1) / Step;
        }
        return new BestBuild { Priority = p, Atts = chosen, Stats = Stats.Compute(w, chosen), Cost = chosen.Sum(a => a.ShopPrice), Budget = budget };
    }

    public static string PriorityName(Priority p) => p switch
    {
        Priority.Recoil => L.T("МИНИМУМ ОТДАЧИ", "LOWEST RECOIL"),
        Priority.Handling => L.T("БЫСТРОЕ ПРИЦЕЛИВАНИЕ", "FASTEST ADS"),
        _ => L.T("БАЛАНС", "BALANCED"),
    };
}
