using System.Globalization;

namespace WardogsOverlay;

/// <summary>
/// Сезон 3 (обновление 0.3.0, 12.11.2026). Пока MetaForge не выложил данные нового патча, поверх базы
/// Сезона 2 применяем то, что разработчики назвали в заметках к патчу точными числами: цены, уровни и стоимость
/// открытия, веса, урон штурмовых винтовок и пулемётов по торсу, скорость стрел, снятый с продажи ИК-дальномер.
/// Чего в заметках нет числами (урон DMR, отдача TCX-SPR, уровни мульти-трековых открытий брони и патронов) —
/// не выдумываем, а помечаем.
///
/// Каждая поправка применяется, только если в данных ещё прежнее значение («было» из заметок): когда MetaForge
/// обновится, поправки перестанут совпадать и сами отключатся, ничего не удвоив.
/// </summary>
static class Season3
{
    public const string Title = "Season 3";
    public const string Date = "2026-11-12";
    /// <summary>Номер обновления из анонса «Season 03 is now live» — им отличаем «данные по заметкам» от сборки MetaForge.</summary>
    public const string Build = "0.3.0";
    const string Season2Build = "CL-516586";
    const string Season2Date = "2026-10-15";

    /// <summary>Поправка из заметок к патчу: поле предмета было From, стало To.</summary>
    sealed record Fix(string Slug, string Field, double From, double To);

    static readonly Fix[] Fixes =
    [
        // Цены.
        new("basic-parachute", "price", 500, 1250),
        new("sport-parachute", "price", 700, 2250),
        new("wolf-110-m2-browning", "price", 7800, 6800),
        new("kodiak-m249", "price", 6750, 6250),
        new("ah-6m-miniguns", "price", 12000, 10480),
        new("havoc", "price", 28000, 26000),
        // Открытие.
        new("molotov-cocktail", "unlock/cash", 25000, 12500),
        new("molotov-cocktail", "unlock/level", 13, 8),
        new("c4-charge", "unlock/level", 4, 5),
        new("large-hammer", "unlock/level", 8, 14),
        new("9k333-verba", "unlock/level", 14, 8),
        new("amr-50", "unlock/level", 35, 41),
        // Танки в ветке водителя: «уровни 20–35 → 35–50».
        new("t-72b3", "unlock/level", 20, 35),
        new("l2a6", "unlock/level", 35, 50),
        // Веса.
        new("amp-9-20-rnd-magazine", "mass", 1, 0.3),
        new("improvised-explosive-device", "mass", 1.5, 2.7),
        // Стрелы лука на 50% быстрее.
        new("compound-bow", "muzzleVelocity", 160, 240),
        // ИК-дальномер убран из продажи до переделки.
        new("infrared-range-finder", "inVendor", 1, 0),
    ];

    /// <summary>Танки вернули в ветку карьеры (WARDOG) — на 200-м уровне, вдобавок к ветке водителя.</summary>
    static readonly (string Slug, string Role, int Level)[] ExtraTracks = [("t-72b3", "Career", 200), ("l2a6", "Career", 200)];

    /// <summary>
    /// Штурмовые винтовки — урон по торсу как в Сезоне 1, пулемёты — как у штурмовых: верх +13%, середина и низ +3%
    /// (от 0.9735 / 0.97 / 0.9215 это ровно 1.1 / 1.0 / 0.95).
    /// </summary>
    static readonly (string Zone, double From, double To)[] TorsoFix =
        [("torso_upper", 0.9735, 1.1), ("torso_middle", 0.97, 1.0), ("torso_lower", 0.9215, 0.95)];
    static readonly string[] TorsoClasses = ["Assault Rifle", "Light Machine Gun"];

    static readonly (string Ru, string En) AnyRoleNote = ("S3: и в ветках ролей", "S3: role tracks too");
    static readonly (string Ru, string En) AmmoNote = ("S3: перенесено", "S3: moved");

    /// <summary>База снята до Сезона 3 (MetaForge ещё не обновился).</summary>
    public static bool IsBefore(GameData gd) =>
        gd.PatchBuild == Season2Build || gd.PatchBuild.Length == 0 && string.CompareOrdinal(gd.Version, "2026-11-11") < 0;

    public static void Apply(GameData gd)
    {
        if (!IsBefore(gd)) return;
        var patch = new List<PatchItem>();
        PatchItem Entry(string id, string name, string tab, bool removed = false)
        {
            var e = patch.FirstOrDefault(p => p.Id == id);
            if (e == null) patch.Add(e = new PatchItem { Id = id, Name = name, Tab = tab, Removed = removed });
            return e;
        }
        static bool Same(double a, double b) => Math.Abs(a - b) < 1e-6;
        static string Fmt(double v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        foreach (var f in Fixes)
        {
            var items = gd.Items.Where(i => i.Slug == f.Slug).ToList();
            var weapon = gd.Weapons.FirstOrDefault(w => w.Slug == f.Slug);
            bool applied = false;
            foreach (var it in items)
            {
                switch (f.Field)
                {
                    case "price" when it.Price is int p && Same(p, f.From): it.Price = (int)f.To; applied = true; break;
                    case "unlock/level" when it.Unlock is { } u && Same(u.Level, f.From): it.Unlock = u with { Level = (int)f.To }; applied = true; break;
                    case "unlock/cash" when it.Unlock is { } u && Same(u.Cash, f.From): it.Unlock = u with { Cash = (int)f.To }; applied = true; break;
                    case "mass" when it.Weight is double m && Same(m, f.From): it.Weight = f.To; applied = true; break;
                    case "inVendor" when it.InVendor && f.To == 0: it.InVendor = false; applied = true; break;
                }
            }
            if (weapon != null)
            {
                switch (f.Field)
                {
                    case "price" when Same(weapon.Price, f.From): weapon.Price = (int)f.To; applied = true; break;
                    case "unlock/level" when weapon.Unlock is { } u && Same(u.Level, f.From): weapon.Unlock = u with { Level = (int)f.To }; applied = true; break;
                    case "unlock/cash" when weapon.Unlock is { } u && Same(u.Cash, f.From): weapon.Unlock = u with { Cash = (int)f.To }; applied = true; break;
                    case "muzzleVelocity" when weapon.Velocity is int v && Same(v, f.From): weapon.Velocity = (int)f.To; applied = true; break;
                    case "inVendor" when weapon.InVendor && f.To == 0: weapon.InVendor = false; applied = true; break;
                }
            }
            if (!applied) continue;
            var src = items.FirstOrDefault();
            var e = Entry(src?.Id ?? weapon!.Id, src?.Name ?? weapon!.Name, src?.Tab ?? "weapons", removed: f.Field == "inVendor");
            e.Changes.Add(new PatchChange("patch-notes", f.Field.Split('/'), f.Field == "inVendor" ? L.T("да", "yes") : Fmt(f.From), f.Field == "inVendor" ? L.T("нет", "no") : Fmt(f.To)));
        }

        foreach (var (slug, role, level) in ExtraTracks)
            foreach (var it in gd.Items.Where(i => i.Slug == slug && i.Unlock != null && i.Unlock.Tracks.All(t => t.Role != role)))
            {
                it.Unlock = it.Unlock! with { Also = [.. it.Unlock.Also, new Unlock(role, level, 0)] };
                Entry(it.Id, it.Name, it.Tab).Changes.Add(new PatchChange("patch-notes", ["unlock", "also"], "—", $"{L.Role(role)} {level}"));
            }

        // Мульти-трековые открытия: точных уровней по ролям в заметках нет — только помечаем.
        foreach (var it in gd.Items.Where(i => i.Unlock is { Level: > 0, Role: not (null or "None"), Note: null }))
        {
            bool anyRole = it.Tab == "armor" && it.Slug.StartsWith("level-", StringComparison.Ordinal)
                           || it.Tab == "storage" && (it.Name.Contains("Backpack", StringComparison.OrdinalIgnoreCase) || it.Name.Contains("Tac Vest", StringComparison.OrdinalIgnoreCase))
                           || it.Name.Contains("Parachute", StringComparison.OrdinalIgnoreCase);
            bool ammo = it.Tab == "ammunition" && it.RoundType is { } rt && (rt.Contains("(HP)") || rt.Contains("(AP)"));
            if (anyRole) it.Unlock = it.Unlock! with { Note = AnyRoleNote };
            else if (ammo) it.Unlock = it.Unlock! with { Note = AmmoNote };
        }

        foreach (var cls in TorsoClasses)
        {
            if (!gd.Hitboxes.TryGetValue(cls, out var hit)) continue;
            foreach (var (zone, from, to) in TorsoFix)
            {
                if (!hit.TryGetValue(zone, out var v) || !Same(v, from)) continue;
                hit[zone] = to;
                Entry("hitbox:" + cls, Stats.ClassName(cls), "weapons").Changes.Add(new PatchChange("patch-notes", [zone], Fmt(from), Fmt(to)));
            }
        }

        if (patch.Count == 0) return;
        gd.FromPatchNotes = true;
        gd.PatchTitle = Title;
        gd.PatchDate = Date;
        gd.PatchBuild = Build;
        gd.PatchPreviousDate = Season2Date;
        gd.Patch.Clear();
        gd.Patch.AddRange(patch);
    }

    /// <summary>Что в Сезоне 3 поменяли у этой пушки, но без чисел в заметках, — честно предупреждаем.</summary>
    public static string? PendingNote(GameData gd, Weapon w)
    {
        if (!gd.FromPatchNotes) return null;
        if (w.Sub == "Marksman Rifle")
            return L.T("В Сезоне 3 урон DMR подняли (тело без брони — 2 выстрела, голова без шлема — 1), точные числа ещё не опубликованы — расчёт по Сезону 2.",
                       "Season 3 raised DMR damage (2 body shots without armour, 1 headshot without a helmet); exact values aren't published yet — figures are from Season 2.");
        if (w.Slug == "tcx-spr")
            return L.T("В Сезоне 3 отдачу TCX-SPR уменьшили (меньше подброс и увод), точных чисел ещё нет.",
                       "Season 3 reduced TCX-SPR recoil (less climb and drift); exact values aren't published yet.");
        return null;
    }

    // ---------- режим Limited ----------
    // «В продаже только то, что обычно доступно в начале и середине сезона»: нет HP и AP, брони и шлемов 3–4-го
    // уровня, ударной техники, CIWS, Stingray, пулемётного гнезда, буровой установки и противотанковых мин.

    /// <summary>Самая тяжёлая броня, которую можно купить в режиме Limited.</summary>
    public const int LimitedMaxArmour = 2;

    public static bool LimitedAllows(RoundLoad load) => load == RoundLoad.FMJ;

    public static int LimitArmour(int level, bool limited) => limited ? Math.Min(level, LimitedMaxArmour) : level;

    /// <summary>Нельзя купить в режиме Limited.</summary>
    public static bool LimitedExcludes(DbItem i)
    {
        if (i.Tab == "ammunition" && i.RoundType is { } rt && (rt.Contains("(HP)") || rt.Contains("(AP)"))) return true;
        if (i.Tab == "armor" && (i.Slug.StartsWith("level-3-", StringComparison.Ordinal) || i.Slug.StartsWith("level-4-", StringComparison.Ordinal))) return true;
        if (i.Slug.StartsWith("at-mine", StringComparison.Ordinal) || i.Slug.Contains("ciws") || i.Slug.StartsWith("stingray", StringComparison.Ordinal) ||
            i.Slug.StartsWith("mg-nest", StringComparison.Ordinal) || i.Slug.StartsWith("drill-rig", StringComparison.Ordinal)) return true;
        // Ударная техника: с вооружением в названии («Kodiak [M249]», кроме «[Pickup]»), танки, вертолёт Havoc, ЗСУ, САУ.
        if (i.Tab == "vehicles")
            return i.Name.Contains('[') && !i.Name.Contains("[Pickup]") ||
                   i.Slug is "t-72b3" or "l2a6" or "havoc" or "flakpanzer-gepard" or "sph-2";
        return false;
    }
}
