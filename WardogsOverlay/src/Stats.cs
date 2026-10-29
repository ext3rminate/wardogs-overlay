using System.Globalization;

namespace WardogsOverlay;

enum Better { Lower, Higher, Neutral }
enum Priority { Balance, Recoil, Handling }
enum StatKind { Percent, Seconds, Zoom }

sealed record StatDef(string Attr, string LabelRu, string ShortRu, string LabelEn, string ShortEn,
                      Better Better, StatKind Kind = StatKind.Percent)
{
    public string Label => L.T(LabelRu, LabelEn);
    public string Short => L.T(ShortRu, ShortEn);
    public bool Character => Attr.StartsWith("Character.", StringComparison.Ordinal);
}

/// <summary>Итоговые значения статов сборки и их относительные изменения.</summary>
sealed class BuildStats
{
    public required Weapon Weapon { get; init; }
    public required Dictionary<string, double> Final { get; init; }
    public double Base(StatDef d) => Stats.Base(Weapon, d);
    public double Value(StatDef d) => Final[d.Attr];
    /// <summary>Изменение относительно голой пушки: 0.06 = +6%.</summary>
    public double Delta(StatDef d) => d.Kind == StatKind.Seconds ? Value(d) - Base(d) : Value(d) / Base(d) - 1;
    public bool Changed(StatDef d) => d.Kind == StatKind.Seconds ? Math.Abs(Delta(d)) >= 0.005 : Math.Abs(Delta(d)) >= 0.005;
}

static class Stats
{
    public static readonly StatDef[] Defs =
    [
        new("AdsZoomMultiplier", "Зум прицела", "Зум", "Scope zoom", "Zoom", Better.Neutral, StatKind.Zoom),
        new("VerticalRecoilMultiplier", "Отдача вверх", "Отд↑", "Vertical recoil", "V.rec", Better.Lower),
        new("HorizontalRecoilMultiplier", "Отдача вбок", "Отд↔", "Horizontal recoil", "H.rec", Better.Lower),
        new("SpreadMultiplier", "Разброс", "Разбр", "Spread", "Spread", Better.Lower),
        new("AimTime", "Прицеливание", "Прицел", "ADS time", "ADS", Better.Lower, StatKind.Seconds),
        new("EquipSpeedMultiplier", "Скорость доставания", "Дост", "Equip speed", "Equip", Better.Higher),
        new("Character.WeaponSway.StabilizationTimeMultiplier", "Время стабилизации", "Стаб", "Stabilisation time", "Stab", Better.Lower),
        new("Character.AimSwayScaleMultiplier", "Качание прицела", "Кач", "Aim sway", "Sway", Better.Lower),
        new("Character.AimSwaySpeedMultiplier", "Скорость качания", "Кач.ск", "Sway speed", "Sw.spd", Better.Lower),
        new("Character.AimSwayHorizontalMovementMultiplier", "Качание при стрейфе", "Стрейф", "Sway when strafing", "Strafe", Better.Lower),
        new("Character.AimSwayVerticalMovementMultiplier", "Качание в прыжке", "Прыжок", "Sway when jumping", "Jump", Better.Lower),
        new("Character.WeaponSway.TranslationStabilizationMultiplier", "Стабилиз. в движении", "Ст.дв", "Translation stab.", "T.stab", Better.Neutral),
        new("Character.WeaponSway.RotationStabilizationMultiplier", "Стабилиз. поворота", "Ст.пов", "Rotation stab.", "R.stab", Better.Neutral),
    ];

    public static StatDef Def(string attr) => Defs.First(d => d.Attr == attr);

    /// <summary>Вес стата для оценки сборки при выбранном приоритете.</summary>
    public static double Weight(StatDef d, Priority p) => (d.Attr, p) switch
    {
        (_, _) when d.Better == Better.Neutral => 0,
        ("VerticalRecoilMultiplier" or "HorizontalRecoilMultiplier", Priority.Recoil) => 2.0,
        ("SpreadMultiplier", Priority.Recoil) => 1.0,
        ("AimTime", Priority.Recoil) => 0.3,
        ("AimTime", Priority.Handling) => 2.0,
        ("EquipSpeedMultiplier", Priority.Handling) => 1.0,
        ("VerticalRecoilMultiplier" or "HorizontalRecoilMultiplier" or "SpreadMultiplier", Priority.Handling) => 0.4,
        ("VerticalRecoilMultiplier" or "HorizontalRecoilMultiplier" or "SpreadMultiplier" or "AimTime", _) => 1.0,
        _ => 0.25,
    };

    /// <summary>Оценка «насколько сборка лучше голой пушки» — для сортировки вариантов и подбора.</summary>
    public static double Score(BuildStats s, Priority p = Priority.Balance)
    {
        double score = 0;
        foreach (var d in Defs)
        {
            double w = Weight(d, p);
            if (w == 0) continue;
            double rel = d.Kind == StatKind.Seconds ? s.Delta(d) / s.Base(d) : s.Delta(d);
            score += w * (d.Better == Better.Lower ? -rel : rel);
        }
        return score;
    }

    public static double Base(Weapon w, StatDef d) =>
        d.Character ? 1.0 : w.Attr.GetValueOrDefault(d.Attr, d.Kind == StatKind.Seconds ? 0.5 : 1.0);

    /// <summary>
    /// Множители перемножаются (0.49 × 1.1 × 0.85 = 0.46 с — как в игре), override заменяет базу
    /// (зум прицела), add прибавляется в конце. Override времени прицеливания бывает только у
    /// боковых «косых» мушек — это отдельный режим прицеливания, на основной не влияет.
    /// </summary>
    public static BuildStats Compute(Weapon w, IEnumerable<Attachment> atts)
    {
        var list = atts.ToList();
        var final = new Dictionary<string, double>();
        foreach (var d in Defs)
        {
            double v = Base(w, d), mul = 1, add = 0;
            double? ovr = null;
            foreach (var m in list.SelectMany(a => a.Mods).Where(m => m.Attr == d.Attr))
            {
                switch (m.Op)
                {
                    case "multiply": mul *= m.Value; break;
                    case "add": add += m.Value; break;
                    case "override" when d.Attr != "AimTime": ovr = m.Value; break;
                }
            }
            final[d.Attr] = (ovr ?? v) * mul + add;
        }
        return new BuildStats { Weapon = w, Final = final };
    }

    static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

    public static string Pct(double delta)
    {
        int p = (int)Math.Round(delta * 100, MidpointRounding.AwayFromZero);
        return p == 0 ? "±0%" : p > 0 ? $"+{p}%" : $"−{-p}%";
    }

    static CultureInfo Num => L.Ru ? Ru : CultureInfo.InvariantCulture;
    static string S => L.T(" с", " s");

    public static string Sec(double v) => v.ToString("0.00", Num) + S;
    public static string SecShort(double v) => v.ToString("0.00", Num);

    public static string SecDelta(double d)
    {
        var s = Math.Abs(d).ToString("0.00", Num);
        return Math.Abs(d) < 0.005 ? "±0" + S : d > 0 ? $"+{s}{S}" : $"−{s}{S}";
    }

    public static string Zoom(double v) => v.ToString("0.##", Num) + "×";

    /// <summary>+1 — стало лучше, −1 — хуже, 0 — без оценки.</summary>
    public static int Goodness(StatDef d, double delta)
    {
        if (d.Better == Better.Neutral || Math.Abs(delta) < 0.005) return 0;
        return (d.Better == Better.Lower) == (delta < 0) ? 1 : -1;
    }

    public static string ClassName(string sub) => !L.Ru ? sub.ToUpperInvariant() : sub switch
    {
        "Assault Rifle" => "ШТУРМОВАЯ",
        "Sniper Rifle" => "СНАЙПЕРСКАЯ",
        "Marksman Rifle" => "МАРКСМАН",
        "Submachine Gun" => "ПП",
        "Light Machine Gun" => "ПУЛЕМЁТ",
        "Shotgun" => "ДРОБОВИК",
        "Pistol" => "ПИСТОЛЕТ",
        "Launcher" => "ГРАНАТОМЁТ",
        "Bow" => "ЛУК",
        _ => sub.ToUpperInvariant(),
    };

    public static string SlotName(Attachment a) => SlotName(a.Slot, a.Name.Contains("Suppressor", StringComparison.OrdinalIgnoreCase));

    public static string SlotName(string slot, bool suppressor = false) => slot switch
    {
        "Sight" => L.T("ПРИЦЕЛ", "OPTIC"),
        "CantedSight" => L.T("ДОП. ПРИЦЕЛ", "CANTED"),
        "Magazine" => L.T("МАГАЗИН", "MAGAZINE"),
        "Muzzle" => suppressor ? L.T("ГЛУШИТЕЛЬ", "SUPPRESSOR") : L.T("ДУЛО", "MUZZLE"),
        "Underbarrel" => L.T("РУКОЯТЬ", "GRIP"),
        "Handguard" => L.T("ЦЕВЬЁ", "HANDGUARD"),
        "Stock" => L.T("ПРИКЛАД", "STOCK"),
        "Barrel" => L.T("СТВОЛ", "BARREL"),
        "Grip" or "Pistolgrip" => L.T("РУКОЯТКА", "PISTOL GRIP"),
        "DustCover" => L.T("КРЫШКА", "DUST COVER"),
        _ => L.T("ОБВЕС", "ATTACHMENT"),
    };
}
