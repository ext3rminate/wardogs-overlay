namespace WardogsOverlay;

/// <summary>Тип патрона: обычный, экспансивный (по плоти), бронебойный.</summary>
enum RoundLoad { FMJ, HollowPoint, ArmorPiercing }

/// <summary>Результат: урон за попадание, выстрелов до убийства, время (первый выстрел — в ноль, плюс полёт пули).</summary>
sealed record Kill(double PerHit, int Shots, double Seconds, bool Armoured);

/// <summary>
/// Боевые оценки по баллистике MetaForge — та же модель, что в их калькуляторе (сверено по выстрелам):
/// урон = база калибра × дробины × зона × падение с дистанцией × модификатор патрона по плоти
///        × (1 − снижение брони × пробитие патрона) × правила патрона против брони/шлема.
/// Износ брони не учитывается, у цели 100 здоровья.
/// </summary>
static class Combat
{
    public const int Health = 100;

    /// <summary>Зоны попадания в порядке «сверху вниз», как в игре.</summary>
    public static readonly string[] Zones =
        ["head", "neck", "torso_upper", "torso_middle", "torso_lower", "pelvis", "arm_upper", "arm_lower", "arm_hand", "leg_thigh", "leg_calf", "leg_foot"];

    static readonly string[] Tiers = ["", "Light", "Medium", "Heavy", "SuperHeavy"];

    public static string LoadKey(RoundLoad l) => l.ToString();

    /// <summary>Снижение урона бронежилетом уровня 0–4 (0 — без брони).</summary>
    public static int ArmourReduction(GameData data, int level) =>
        Piece(data, "armor", level)?.Reduction ?? 0;

    static ArmourInfo? Piece(GameData data, string slot, int level) =>
        level <= 0 ? null : data.Armour.TryGetValue($"level-{level}-{slot}", out var a) ? a : null;

    /// <summary>Множитель урона на дистанции (между точками — по прямой).</summary>
    public static double Falloff(Weapon w, double distance)
    {
        var pts = w.Falloff;
        if (pts.Count == 0) return 1;
        if (distance <= pts[0].Distance) return pts[0].Mult;
        for (int i = 1; i < pts.Count; i++)
            if (distance <= pts[i].Distance)
            {
                var (d0, m0) = pts[i - 1];
                var (d1, m1) = pts[i];
                return d1 <= d0 ? m1 : m0 + (m1 - m0) * (distance - d0) / (d1 - d0);
            }
        return pts[^1].Mult;
    }

    /// <summary>Есть ли у пушки такой тип патронов (у дроби и стрел — только обычные).</summary>
    public static bool HasLoad(GameData data, Weapon w, RoundLoad load) =>
        data.Calibres.TryGetValue(w.CalibreKey, out var c) ? load == RoundLoad.FMJ || c.Pen.ContainsKey(LoadKey(load)) : load == RoundLoad.FMJ;

    /// <summary>Урон одного выстрела в зону.</summary>
    public static double PerHit(GameData data, Weapon w, string zone, RoundLoad load, int body, int helmet, double distance, out bool armoured)
    {
        armoured = false;
        var key = LoadKey(load);
        data.Calibres.TryGetValue(w.CalibreKey, out var cal);
        double dmg = (cal?.BaseDamage ?? w.Damage ?? 0) * (cal?.Pellets ?? w.Pellets) * w.Hit.GetValueOrDefault(zone, 1.0) * Falloff(w, distance);
        if (cal == null) return dmg;

        bool ForLoad(DamageRule r) => r.Rounds.Length == 0 || r.Rounds.Contains(key);
        // Модификатор патрона по плоти (экспансивный ×2, бронебойный ×0.8) — правила без цели.
        foreach (var r in cal.Rules.Where(r => r.Kind == "AmmoModifier" && r.Targets.Length == 0 && ForLoad(r)))
            dmg *= 1 + r.Pct / 100;

        // Броня: жилет закрывает торс (4-й уровень ещё таз и плечи), шлем — голову (3–4-й и шею).
        var (slot, level) = Piece(data, "helmet", helmet)?.Covers.Contains(zone) == true ? ("Helmet", helmet)
                          : Piece(data, "armor", body)?.Covers.Contains(zone) == true ? ("Armor", body) : ("", 0);
        if (level == 0) return dmg;
        armoured = true;
        var piece = Piece(data, slot == "Helmet" ? "helmet" : "armor", level)!;
        double pen = cal.Pen.GetValueOrDefault(key, 1.0);
        dmg *= Math.Max(0, 1 - piece.Reduction / 100.0 * pen);
        var tier = $"{slot}.{Tiers[level]}";
        // Правило на «Armor» — для любого жилета, на «Armor.SuperHeavy» — только для 4-го уровня.
        foreach (var r in cal.Rules.Where(r => r.Targets.Length > 0 && ForLoad(r) && (r.Targets.Contains(slot) || r.Targets.Contains(tier))))
            dmg *= 1 + r.Pct / 100;
        return dmg;
    }

    /// <summary>Сколько выстрелов в зону и за сколько секунд при беспрерывной стрельбе (с полётом пули).</summary>
    public static Kill? Shots(GameData data, Weapon w, string zone, RoundLoad load, int body, int helmet, double distance)
    {
        // У гранатомётов урон — взрыв, «выстрелов в грудь» для них не бывает.
        if (w.Sub == "Launcher" || w.Rpm is not int rpm || rpm <= 0) return null;
        if (!HasLoad(data, w, load)) return null;
        double hit = PerHit(data, w, zone, load, body, helmet, distance, out bool armoured);
        if (hit <= 0.01) return null;
        int shots = (int)Math.Ceiling(Health / hit - 1e-9);
        double travel = w.Velocity is int v && v > 0 ? distance / v : 0;
        return new Kill(hit, shots, (shots - 1) * 60.0 / rpm + travel, armoured);
    }

    /// <summary>Привычная оценка: обычные патроны в грудь через бронежилет уровня level, вплотную.</summary>
    public static (int Shots, double Seconds)? TtkChest(GameData data, Weapon w, int level) =>
        Shots(data, w, "torso_middle", RoundLoad.FMJ, level, 0, 0) is { } k ? (k.Shots, k.Seconds) : null;

    /// <summary>Патроны этого типа в магазине продавца: цена за 10 и что нужно для открытия.</summary>
    public static DbItem? AmmoItem(GameData data, Weapon w, RoundLoad load)
    {
        string label = load switch { RoundLoad.HollowPoint => "(HP)", RoundLoad.ArmorPiercing => "(AP)", _ => "(FMJ)" };
        var list = data.Items.Where(i => i.Tab == "ammunition" && i.CalibreKey == w.CalibreKey && (i.RoundType ?? i.Name).Contains(label)).ToList();
        return list.FirstOrDefault(i => i.InVendor) ?? list.FirstOrDefault();
    }

    /// <summary>Сколько стоит полный магазин патронов этого типа.</summary>
    public static int? MagazineCost(GameData data, Weapon w, RoundLoad load) =>
        AmmoItem(data, w, load) is { Price: int p, Amount: int a } && a > 0 && w.MagSize is int mag ? (int)Math.Round(p * (double)mag / a) : null;

    /// <summary>Подброс ствола за 10 выстрелов с учётом обвесов, градусы.</summary>
    public static double? Climb(Weapon w, BuildStats s) =>
        w.Climb10 is double c ? c * s.Value(Stats.Def("VerticalRecoilMultiplier")) : null;

    public static double? Drift(Weapon w, BuildStats s) =>
        w.Drift10 is double d ? d * s.Value(Stats.Def("HorizontalRecoilMultiplier")) : null;

    public static string Seconds(double s) => s.ToString("0.00", L.Ru ? System.Globalization.CultureInfo.GetCultureInfo("ru-RU")
                                                                     : System.Globalization.CultureInfo.InvariantCulture) + L.T(" с", " s");
}
