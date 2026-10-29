using System.Text.Json;
using System.Text.RegularExpressions;

namespace WardogsOverlay;

enum Lang { Ru, En }

/// <summary>
/// Язык программы. Строки интерфейса пишутся парами прямо в коде: L.T("по-русски", "in English").
/// Названия пушек и обвесов: английские — с MetaForge, русские — словарь assets/names-ru.json
/// в стиле игры («Коллиматорный прицел Kobra»), а чего в словаре нет — перевод по правилам.
/// </summary>
static class L
{
    public static Lang Current { get; set; } = Lang.Ru;
    public static bool Ru => Current == Lang.Ru;
    public static string T(string ru, string en) => Ru ? ru : en;

    static readonly Dictionary<string, string> WeaponsRu = [];
    static readonly Dictionary<string, string> AttachmentsRu = [];

    static L()
    {
        using var s = typeof(L).Assembly.GetManifestResourceStream("WardogsOverlay.names-ru.json");
        if (s == null) return;
        using var doc = JsonDocument.Parse(s);
        foreach (var p in doc.RootElement.GetProperty("weapons").EnumerateObject()) WeaponsRu[p.Name] = p.Value.GetString()!;
        foreach (var p in doc.RootElement.GetProperty("attachments").EnumerateObject()) AttachmentsRu[p.Name] = p.Value.GetString()!;
    }

    public static string Name(Weapon w) => Ru ? WeaponRu(w.Name) : w.Name;
    public static string Name(Attachment a) => Ru ? AttachmentRu(a.Name) : a.Name;

    public static string WeaponRu(string en) => WeaponsRu.GetValueOrDefault(en, en);

    // ---------- справочник ----------

    public static string Role(string? role) => role switch
    {
        "Career" => T("Карьера", "Career"),
        "Infantry" => T("Штурмовик", "Assault"),
        "Medic" => T("Медик", "Medic"),
        "Recon" => T("Разведчик", "Recon"),
        "Support" => T("Поддержка", "Support"),
        "Driver" => T("Водитель", "Driver"),
        "Pilot" => T("Пилот", "Pilot"),
        null or "" or "None" => "—",
        _ => role,
    };

    public static readonly string[] Roles = ["Career", "Infantry", "Medic", "Recon", "Support", "Driver", "Pilot"];

    public static string UnlockText(Unlock? u) =>
        u == null ? "—" : u.Level <= 0 ? T("сразу", "from start")
        : $"{Role(u.Role)} {u.Level}" + (u.Cash > 0 ? $" · ${u.Cash:N0}" : "");

    public static string Money(int v) => "$" + v.ToString("N0", Ru ? System.Globalization.CultureInfo.GetCultureInfo("ru-RU")
                                                                   : System.Globalization.CultureInfo.InvariantCulture);

    public static string TabName(string tab) => tab switch
    {
        "weapons" => T("Пушки", "Weapons"),
        "attachments" => T("Обвесы", "Attachments"),
        "ammunition" => T("Патроны", "Ammunition"),
        "armor" => T("Броня", "Armour"),
        "throwables" => T("Гранаты", "Throwables"),
        "deployables" => T("Установка", "Deployables"),
        "vehicles" => T("Техника", "Vehicles"),
        "mounted-weapons" => T("Станковое", "Mounted weapons"),
        "storage" => T("Хранение", "Storage"),
        "buildables" => T("Постройки", "Buildables"),
        "equipment" => T("Снаряжение", "Equipment"),
        _ => T("Прочее", "Other"),
    };

    public static string Name(DbItem i) => !Ru ? i.Name : i.Tab switch
    {
        "weapons" => WeaponRu(i.Name),
        "attachments" => AttachmentRu(i.Name),
        "armor" => ArmourRu(i.Name),
        "ammunition" => AmmoRu(i.Name),
        _ => i.Name,
    };

    static readonly Regex Level = new(@"^Level (\d) (Armor|Helmet)$", RegexOptions.CultureInvariant);

    public static string ArmourRu(string en)
    {
        var m = Level.Match(en);
        if (m.Success) return (m.Groups[2].Value == "Armor" ? "Бронежилет " : "Шлем ") + m.Groups[1].Value + "-го уровня";
        return en switch
        {
            "Ghillie Body Suit" => "Маскхалат «Гилли»",
            "Ghillie Body Suit (Dead)" => "Маскхалат «Гилли» (сухой)",
            "Ghillie Headwear" => "Капюшон «Гилли»",
            "Ghillie Headwear (Dead)" => "Капюшон «Гилли» (сухой)",
            "Pilot Helmet" => "Шлем пилота",
            "Tank Helmet" => "Шлем танкиста",
            _ => en,
        };
    }

    static readonly (string En, string Ru)[] Rounds =
    [
        ("Standard (FMJ)", "обычные (FMJ)"),
        ("Armor Piercing (AP)", "бронебойные (AP)"),
        ("Flesh Damage (HP)", "экспансивные (HP)"),
        ("Rifled Slug", "пуля"),
        ("Slug", "пуля"),
        ("Buckshot", "картечь"),
        ("(T)", "трассирующие"),
        ("Standard", "обычные"),
    ];

    public static string AmmoRu(string en)
    {
        var s = en;
        foreach (var (a, b) in Rounds) s = s.Replace(a, b, StringComparison.OrdinalIgnoreCase);
        return s.Replace("mm", " мм");
    }

    static readonly Dictionary<string, (string Ru, string En)> Zones = new()
    {
        ["head"] = ("голова", "head"), ["neck"] = ("шея", "neck"), ["torso_upper"] = ("грудь (верх)", "upper torso"),
        ["torso_middle"] = ("грудь", "chest"), ["torso_lower"] = ("живот", "stomach"), ["pelvis"] = ("таз", "pelvis"),
        ["arm_upper"] = ("плечо", "upper arm"), ["arm_lower"] = ("предплечье", "lower arm"), ["arm_hand"] = ("кисть", "hand"),
        ["leg_thigh"] = ("бедро", "thigh"), ["leg_calf"] = ("голень", "calf"), ["leg_foot"] = ("стопа", "foot"),
    };

    public static string Zone(string key) => Zones.TryGetValue(key, out var z) ? T(z.Ru, z.En) : key;

    /// <summary>Что поменяли в патче — понятными словами («уровень открытия», «урон: грудь»).</summary>
    public static string ChangeLabel(PatchChange c)
    {
        var p = c.Path;
        string Last(int i) => p.Length > i ? p[^(i + 1)] : "";
        var joined = string.Join("/", p);
        if (joined == "unlock/level") return T("уровень открытия", "unlock level");
        if (joined == "unlock/cash") return T("стоимость открытия", "unlock cost");
        if (joined == "unlock/role") return T("роль для открытия", "unlock role");
        if (joined == "price") return T("цена", "price");
        if (joined == "fits") return T("к каким пушкам подходит", "compatible weapons");
        if (joined == "gridSize") return T("размер в инвентаре", "inventory size");
        if (joined is "description" or "flavor") return T("описание", "description");
        if (joined == "baseDamage" || joined == "damage") return T("урон", "damage");
        if (joined == "recoilPattern") return T("паттерн отдачи", "recoil pattern");
        if (joined == "viewKickMultiplier") return T("подброс камеры", "view kick");
        if (joined == "reduction") return T("снижение урона", "damage reduction");
        if (joined == "mass") return T("вес", "weight");
        if (joined == "hitPoints") return T("прочность", "hit points");
        if (Zones.ContainsKey(joined)) return T("множитель урона: ", "damage multiplier: ") + Zone(joined);
        if (p.Length >= 3 && p.Contains("modifiers"))
        {
            var attr = p[Array.IndexOf(p, "modifiers") + 1];
            var def = Stats.Defs.FirstOrDefault(d => d.Attr == attr);
            var label = def?.Label ?? attr;
            return p[0] == "states" && p.Length > 1 ? $"{p[1]}: {label}" : label;
        }
        if (p.Length > 0 && p[0] == "rules") return T("правила пробития/урона", "penetration/damage rules");
        if (p.Length > 0 && p[0] == "protection") return T("защита техники", "vehicle protection");
        if (p.Length > 0 && (p[0] == "hits" || p[0] == "resistances")) return T("прочность постройки", "structure durability");
        return joined.Length > 0 ? joined : Last(0);
    }

    static readonly Regex ShortRu = new(@"\s+на\s+\d+\s+патр\.|\b[Пп]ередняя рукоятка\b|(?<![-\w])[Рр]укоятка(?![-\w])|\b[Пп]рицел\b",
                                        RegexOptions.CultureInvariant);
    static readonly Regex ShortEn = new(@"\s+(Foregrip|Scope|Sight|Magazine)\b", RegexOptions.CultureInvariant);

    /// <summary>
    /// Короткое название для таблицы вариантов: тип слота уже написан в её заголовке, поэтому
    /// «Угловая передняя рукоятка AFG» → «Угловая AFG», «Магазин АК-74 на 30 патр.» → «АК-74»
    /// (ёмкость там отдельной колонкой). Иначе марка на конце обрезается и варианты не различить.
    /// </summary>
    public static string ShortName(Attachment a)
    {
        var full = Name(a);
        var s = Ru ? ShortRu.Replace(full, " ") : ShortEn.Replace(full, " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        if (Ru && s.StartsWith("Магазин ", StringComparison.Ordinal)) s = s["Магазин ".Length..];
        if (s.Length == 0) return full;
        return char.ToUpper(s[0]) + s[1..];
    }

    static readonly Dictionary<string, string> Cache = [];

    public static string AttachmentRu(string en)
    {
        if (AttachmentsRu.TryGetValue(en, out var ru)) return ru;
        lock (Cache)
        {
            if (!Cache.TryGetValue(en, out ru)) Cache[en] = ru = Translate(en);
            return ru;
        }
    }

    // Тип обвеса (по английскому окончанию) → как его называет русская версия игры.
    // Порядок важен: сначала длинные сочетания.
    static readonly (string En, string Ru)[] Types =
    [
        ("Angled Foregrip", "Угловая передняя рукоятка"),
        ("Vertical Foregrip", "Вертикальная передняя рукоятка"),
        ("Tactical Foregrip", "Тактическая передняя рукоятка"),
        ("Foregrip", "Передняя рукоятка"),
        ("Grip Pod", "Рукоятка-сошки"),
        ("Bipod", "Сошки"),
        ("Muzzle Brake", "Дульный тормоз"),
        ("Brake", "Дульный тормоз"),
        ("Flash Hider", "Пламегаситель"),
        ("Compensator", "Компенсатор"),
        ("Suppressor", "Глушитель"),
        ("Choke", "Чок"),
        ("Red Dot", "Коллиматорный прицел"),
        ("Reflex Sight", "Коллиматорный прицел"),
        ("Reflex", "Коллиматорный прицел"),
        ("Holographic Sight", "Голографический прицел"),
        ("Prism Scope", "Призматический прицел"),
        ("Scope", "Оптический прицел"),
        ("Optic", "Прицел"),
        ("Sight", "Прицел"),
        ("Pistol Grip", "Пистолетная рукоятка"),
        ("Grip", "Рукоятка"),
        ("Handguard", "Цевьё"),
        ("Stock", "Приклад"),
        ("Barrel", "Ствол"),
        ("Dust Cover", "Крышка ствольной коробки"),
        ("Trigger", "Спусковой крючок"),
    ];

    static readonly Regex Mag = new(@"^(?<w>.+?)\s+(?<n>\d+)\s*RND\s+(?<kind>Drum Magazine|Fabric Magazine|Magazine|Box)$",
                                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex ExtMag = new(@"^(?<w>.+?)\s+Extended\s+Mag(azine)?(\s+(?<n>\d+)\s*RD)?$",
                                       RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>«Galil 35 RND Magazine» → «Магазин Galil на 35 патр.», «Shift Foregrip» → «Передняя рукоятка Shift».</summary>
    public static string Translate(string en)
    {
        var m = Mag.Match(en);
        if (m.Success)
        {
            var kind = m.Groups["kind"].Value.ToLowerInvariant() switch
            {
                "drum magazine" => "Барабанный магазин",
                "fabric magazine" => "Мягкий короб",
                "box" => "Короб",
                _ => "Магазин",
            };
            return $"{kind} {WeaponRu(m.Groups["w"].Value)} на {m.Groups["n"].Value} патр.";
        }
        m = ExtMag.Match(en);
        if (m.Success)
            return $"Увеличенный магазин {WeaponRu(m.Groups["w"].Value)}" + (m.Groups["n"].Success ? $" на {m.Groups["n"].Value} патр." : "");

        foreach (var (typeEn, typeRu) in Types)
        {
            int i = en.IndexOf(typeEn, StringComparison.OrdinalIgnoreCase);
            if (i < 0) continue;
            var rest = (en[..i] + " " + en[(i + typeEn.Length)..]).Trim();
            rest = Regex.Replace(rest, @"\s+", " ");
            return rest.Length == 0 ? typeRu : $"{typeRu} {rest}";
        }
        return en;
    }
}
