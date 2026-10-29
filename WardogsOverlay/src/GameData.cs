using System.Text.Json;

namespace WardogsOverlay;

sealed record Mod(string Attr, string Op, double Value);

/// <summary>Когда предмет открывается: роль, её уровень и стоимость открытия.</summary>
sealed record Unlock(string? Role, int Level, int Cash);

sealed class Weapon
{
    public required string Slug { get; init; }
    public required string Name { get; init; }
    public string Id { get; init; } = "";
    public string Sub { get; init; } = "";
    public Dictionary<string, double> Attr { get; init; } = [];
    public string[] Slots { get; init; } = [];
    public int? Damage { get; set; }
    public int? Rpm { get; set; }
    public int? Range { get; set; }
    public int? Velocity { get; set; }
    public int? MagSize { get; set; }
    public double? Moa { get; set; }
    public string[] FireModes { get; set; } = [];
    /// <summary>Множители урона по зонам для класса пушки (голова, грудь…).</summary>
    public Dictionary<string, double> Hit { get; set; } = [];
    /// <summary>Подброс ствола за первые 10 выстрелов очереди, градусы (сумма паттерна отдачи).</summary>
    public double? Climb10 { get; set; }
    /// <summary>Увод вбок за первые 10 выстрелов, градусы.</summary>
    public double? Drift10 { get; set; }
    public int Price { get; init; }
    public Unlock? Unlock { get; init; }
    public bool InVendor { get; init; }
    public string Key { get; init; } = "";
    /// <summary>Как название выглядит в русской версии игры, для сравнения с OCR («СВД», «АК74»).</summary>
    public string KeyRu { get; init; } = "";
    public string Icon { get; init; } = "";
}

sealed class Attachment
{
    public required string Slug { get; init; }
    public required string Name { get; init; }
    public string Id { get; init; } = "";
    public string Slot { get; init; } = "";
    public string Icon { get; init; } = "";
    public int Price { get; init; }
    /// <summary>Цены, которые может показать ценник: сам обвес и магазин вместе с патронами.</summary>
    public HashSet<int> Prices { get; } = [];
    public int? Capacity { get; init; }
    public string? Feeds { get; init; }
    public HashSet<string>? Fits { get; init; }
    public List<Mod> Mods { get; } = [];
    public string? StateNote { get; set; }
    public Unlock? Unlock { get; init; }
    /// <summary>Продаётся у продавца (заводские детали и тестовые предметы — нет).</summary>
    public bool InVendor { get; init; }
    /// <summary>Цена на ценнике в игре: у магазинов — вместе с обычными патронами.</summary>
    public int ShopPrice { get => shopPrice ?? Price; set => shopPrice = value; }
    int? shopPrice;
    /// <summary>Слот без различия «Grip»/«Pistolgrip» — в данных встречаются оба написания.</summary>
    public string SlotKey => Slot == "Pistolgrip" ? "Grip" : Slot;
    /// <summary>Кратность прицела (для подбора прицела по дальности).</summary>
    public double? Zoom => Mods.FirstOrDefault(m => m.Attr == "AdsZoomMultiplier" && m.Op == "override")?.Value;

    public bool FitsWeapon(Weapon w) => Fits == null || Fits.Count == 0 || Fits.Contains(w.Slug);
}

/// <summary>Любой предмет базы — для справочника (патроны, броня, техника, снаряжение…).</summary>
sealed class DbItem
{
    public required string Id { get; init; }
    public required string Slug { get; init; }
    public required string Name { get; init; }
    public required string Tab { get; init; }
    public string Sub { get; init; } = "";
    public int? Price { get; init; }
    public Unlock? Unlock { get; init; }
    public double? Weight { get; init; }
    public bool InVendor { get; init; }
    public string Description { get; init; } = "";
    public string? Calibre { get; init; }
    public string? RoundType { get; init; }
    public int? Amount { get; init; }
}

/// <summary>Бронежилет или шлем: снижение урона, прочность, что закрывает.</summary>
sealed record ArmourInfo(string Slot, int Reduction, int Durability, string[] Covers);

sealed record PatchChange(string Source, string[] Path, string From, string To);

sealed class PatchItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Tab { get; init; }
    public bool Added { get; init; }
    public List<PatchChange> Changes { get; } = [];
}

/// <summary>
/// База пушек и обвесов с MetaForge (metaforge.app — данные из файлов игры).
/// Скачивается один раз в папку data, обновляется из меню.
/// </summary>
sealed class GameData
{
    const string Api = "https://metaforge.app/api/wardogs/";
    const string IconUrl = "https://static.metaforge.app/wardogs/icons/";
    static readonly string[] Files = ["database", "attachment-stats", "ballistics/v2"];
    /// <summary>Необязательные файлы: без них программа работает, просто без раздела «Патч».</summary>
    static readonly string[] OptionalFiles = ["changelog"];

    public static string Dir => Path.Combine(AppContext.BaseDirectory, "data");
    static string IconDir => Path.Combine(Dir, "icons");
    static string JsonPath(string name) => Path.Combine(Dir, name.Replace('/', '-') + ".json");

    public List<Weapon> Weapons { get; } = [];
    public Dictionary<string, Attachment> Attachments { get; } = [];
    public List<DbItem> Items { get; } = [];
    public Dictionary<string, ArmourInfo> Armour { get; } = [];
    public List<PatchItem> Patch { get; } = [];
    public string PatchTitle { get; private set; } = "";
    public string PatchDate { get; private set; } = "";
    public string Version { get; private set; } = "";

    public static bool IsDownloaded => Files.All(f => File.Exists(JsonPath(f)));

    /// <summary>База скачана старой версией программы — не хватает иконок пушек или журнала патчей.</summary>
    public bool NeedsUpdate =>
        Weapons.Any(w => !string.IsNullOrEmpty(w.Icon) && IconFile(w.Icon) == null) ||
        OptionalFiles.Any(f => !File.Exists(JsonPath(f)));

    public string? IconFile(string icon)
    {
        if (string.IsNullOrEmpty(icon)) return null;
        var p = Path.Combine(IconDir, icon + ".webp");
        return File.Exists(p) ? p : null;
    }

    public static async Task DownloadAsync(Action<string> report)
    {
        Directory.CreateDirectory(Dir);
        Directory.CreateDirectory(IconDir);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WardogsOverlay/1.0 (+personal, non-commercial)");

        foreach (var name in Files.Concat(OptionalFiles))
        {
            report(L.T($"Скачиваю {name}…", $"Downloading {name}…"));
            try
            {
                var text = await http.GetStringAsync(Api + name);
                using (JsonDocument.Parse(text)) { } // проверка, что пришёл JSON, а не страница ошибки
                var path = JsonPath(name);
                await File.WriteAllTextAsync(path + ".tmp", text);
                File.Move(path + ".tmp", path, true);
            }
            catch when (OptionalFiles.Contains(name)) { /* без журнала патчей можно жить */ }
        }

        var data = Load();
        var icons = data.Attachments.Values.Select(a => a.Icon).Concat(data.Weapons.Select(w => w.Icon))
            .Where(i => !string.IsNullOrEmpty(i) && !File.Exists(Path.Combine(IconDir, i + ".webp")))
            .Distinct().ToList();
        report(L.T($"Скачиваю иконки: {icons.Count} шт.…", $"Downloading icons: {icons.Count}…"));
        using var gate = new SemaphoreSlim(6);
        int failed = 0;
        await Task.WhenAll(icons.Select(async icon =>
        {
            await gate.WaitAsync();
            try
            {
                var bytes = await http.GetByteArrayAsync(IconUrl + icon + ".webp");
                await File.WriteAllBytesAsync(Path.Combine(IconDir, icon + ".webp"), bytes);
            }
            catch { Interlocked.Increment(ref failed); }
            finally { gate.Release(); }
        }));
        report(failed == 0 ? L.T("База обновлена.", "Data updated.")
                           : L.T($"База обновлена, не скачалось иконок: {failed}.", $"Data updated, icons failed: {failed}."));
    }

    public static GameData Load()
    {
        var gd = new GameData();
        using var db = JsonDocument.Parse(File.ReadAllText(JsonPath("database")));
        using var stats = JsonDocument.Parse(File.ReadAllText(JsonPath("attachment-stats")));
        using var bal = JsonDocument.Parse(File.ReadAllText(JsonPath("ballistics/v2")));
        gd.Version = Str(db.RootElement, "generatedAt") ?? "";

        // Цены одного патрона по калибру (обычные, экспансивные, бронебойные…) — ценник магазина
        // в игре показывает «магазин + патроны того типа, которым он заряжен».
        var perRound = new Dictionary<string, HashSet<double>>();
        foreach (var it in db.RootElement.GetProperty("items").EnumerateArray())
        {
            if (Str(it, "tab") != "ammunition") continue;
            var cal = Str(it, "calibre");
            double price = Num(it, "price") ?? 0, amount = Num(it, "amount") ?? 0;
            if (cal == null || price <= 0 || amount <= 0) continue;
            if (!perRound.TryGetValue(cal, out var set)) perRound[cal] = set = [];
            set.Add(price / amount);
        }

        var weaponsBySlug = new Dictionary<string, Weapon>();
        foreach (var it in db.RootElement.GetProperty("items").EnumerateArray())
        {
            var tab = Str(it, "tab");
            var slug = Str(it, "slug");
            var name = Str(it, "name");
            if (slug == null || name == null || tab == null) continue;
            var id = Str(it, "id") ?? slug;
            var unlock = ReadUnlock(it);
            bool inVendor = it.TryGetProperty("inVendor", out var iv) && iv.ValueKind == JsonValueKind.True;
            bool wip = it.TryGetProperty("wip", out var wp) && wp.ValueKind == JsonValueKind.True;

            if (!wip)
            {
                gd.Items.Add(new DbItem
                {
                    Id = id, Slug = slug, Name = name, Tab = tab, Sub = Str(it, "subcategory") ?? "",
                    Price = Num(it, "price") is double pr ? (int)pr : null,
                    Unlock = unlock, Weight = Num(it, "weight"), InVendor = inVendor,
                    Description = Str(it, "description") ?? "",
                    Calibre = Str(it, "calibreLabel") ?? Str(it, "calibre"), RoundType = Str(it, "roundType"),
                    Amount = Num(it, "amount") is double am ? (int)am : null,
                });
            }

            if (tab == "weapons" && it.TryGetProperty("attributes", out var attrs) && attrs.ValueKind == JsonValueKind.Object)
            {
                if (name.Contains("test", StringComparison.OrdinalIgnoreCase)) continue;
                var w = new Weapon
                {
                    Slug = slug, Name = name, Id = id, Sub = Str(it, "subcategory") ?? "",
                    Attr = attrs.EnumerateObject().Where(p => p.Value.ValueKind == JsonValueKind.Number)
                                .ToDictionary(p => p.Name, p => p.Value.GetDouble()),
                    Slots = Arr(it, "slots"),
                    Key = TextUtil.Latinize(name),
                    KeyRu = TextUtil.NormRu(L.WeaponRu(name)),
                    Icon = Str(it, "icon") ?? "",
                    Price = (int)(Num(it, "price") ?? 0),
                    Unlock = unlock,
                    InVendor = inVendor,
                };
                // Дубли по имени (тестовые записи) — оставляем вариант с обвесами.
                var dup = gd.Weapons.FindIndex(x => x.Key == w.Key);
                if (dup >= 0) { if (gd.Weapons[dup].Slots.Length >= w.Slots.Length) continue; gd.Weapons.RemoveAt(dup); }
                gd.Weapons.Add(w);
                weaponsBySlug[slug] = w;
            }
            else if (tab == "attachments")
            {
                var fits = Arr(it, "fits");
                var a = new Attachment
                {
                    Slug = slug, Name = name, Id = id, Slot = Str(it, "slot") ?? "", Icon = Str(it, "icon") ?? "",
                    Price = (int)(Num(it, "price") ?? 0),
                    Capacity = Num(it, "capacity") is double c ? (int)c : null,
                    Feeds = Str(it, "feeds"),
                    Fits = fits.Length > 0 ? [.. fits] : null,
                    InVendor = inVendor,
                    Unlock = unlock,
                };
                a.Prices.Add(a.Price);
                if (a.Capacity is int cap && a.Feeds != null && perRound.TryGetValue(a.Feeds, out var rounds))
                {
                    foreach (var pr in rounds)
                    {
                        double full = a.Price + cap * pr;
                        a.Prices.Add((int)Math.Floor(full));
                        a.Prices.Add((int)Math.Round(full));
                        a.Prices.Add((int)Math.Ceiling(full));
                    }
                    // В «что поставить» показываем цену с обычными патронами.
                    a.ShopPrice = (int)Math.Floor(a.Price + cap * rounds.Min());
                }
                gd.Attachments[slug] = a;
            }
        }

        foreach (var p in stats.RootElement.GetProperty("attachments").EnumerateObject())
        {
            if (!gd.Attachments.TryGetValue(p.Name, out var a)) continue;
            ReadMods(p.Value, a.Mods);
            if (p.Value.TryGetProperty("states", out var states) && states.ValueKind == JsonValueKind.Array)
            {
                // Сошки и т.п.: берём состояние по умолчанию (обычно «сложена»).
                var def = Str(p.Value, "defaultState");
                foreach (var s in states.EnumerateArray())
                {
                    if (Str(s, "key") != def) continue;
                    ReadMods(s, a.Mods);
                    a.StateNote = Str(s, "label");
                }
            }
        }

        ReadBallistics(bal.RootElement, gd, weaponsBySlug);
        ReadChangelog(gd);
        return gd;
    }

    static void ReadBallistics(JsonElement root, GameData gd, Dictionary<string, Weapon> weaponsBySlug)
    {
        var calDamage = new Dictionary<string, int>();
        if (root.TryGetProperty("calibres", out var cals))
            foreach (var c in cals.EnumerateObject())
                if (Num(c.Value, "baseDamage") is double d) calDamage[c.Name] = (int)Math.Round(d);

        var hitboxes = new Dictionary<string, Dictionary<string, double>>();
        if (root.TryGetProperty("hitboxes", out var hb))
            foreach (var cls in hb.EnumerateObject())
                hitboxes[cls.Name] = cls.Value.EnumerateObject().Where(z => z.Value.ValueKind == JsonValueKind.Number)
                                              .ToDictionary(z => z.Name, z => z.Value.GetDouble());

        if (root.TryGetProperty("weapons", out var bw))
            foreach (var b in bw.EnumerateArray())
            {
                if (Str(b, "slug") is not string s || !weaponsBySlug.TryGetValue(s, out var w)) continue;
                w.Rpm = Num(b, "roundsPerMinute") is double rpm ? (int)rpm : null;
                w.Range = Num(b, "effectiveRange") is double rng ? (int)rng : null;
                w.Velocity = Num(b, "muzzleVelocity") is double v ? (int)v : null;
                w.MagSize = Num(b, "magSize") is double ms ? (int)ms : null;
                w.Moa = Num(b, "accuracyMoa");
                w.FireModes = Arr(b, "fireModes");
                if (Str(b, "calibre") is string cal && calDamage.TryGetValue(cal, out var dmg)) w.Damage = dmg;
                var cls = Str(b, "weaponClass") ?? w.Sub;
                if (hitboxes.TryGetValue(cls, out var hit) || hitboxes.TryGetValue(w.Sub, out hit)) w.Hit = hit;
                if (b.TryGetProperty("recoilPattern", out var rp) && rp.ValueKind == JsonValueKind.Array)
                {
                    var pts = rp.EnumerateArray().Take(10).ToList();
                    if (pts.Count > 0)
                    {
                        w.Climb10 = pts.Sum(p => Num(p, "y") ?? 0);
                        w.Drift10 = pts.Sum(p => Math.Abs(Num(p, "x") ?? 0));
                    }
                }
            }

        if (root.TryGetProperty("armour", out var arm))
            foreach (var a in arm.EnumerateArray())
            {
                if (Str(a, "slug") is not string slug) continue;
                gd.Armour[slug] = new ArmourInfo(Str(a, "slot") ?? "", (int)(Num(a, "reduction") ?? 0),
                                                 (int)(Num(a, "durability") ?? 0), Arr(a, "covers"));
            }
    }

    static void ReadChangelog(GameData gd)
    {
        var path = JsonPath("changelog");
        if (!File.Exists(path)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("current", out var cur)) return;
            gd.PatchTitle = Str(cur, "title") ?? "";
            gd.PatchDate = Str(cur, "date") ?? "";
            foreach (var (prop, added) in new[] { ("added", true), ("changed", false) })
            {
                if (!cur.TryGetProperty(prop, out var list) || list.ValueKind != JsonValueKind.Array) continue;
                foreach (var x in list.EnumerateArray())
                {
                    var item = new PatchItem
                    {
                        Id = Str(x, "id") ?? "", Name = Str(x, "name") ?? "?", Tab = Str(x, "tab") ?? "", Added = added,
                    };
                    if (x.TryGetProperty("changes", out var ch) && ch.ValueKind == JsonValueKind.Array)
                        foreach (var c in ch.EnumerateArray())
                        {
                            var p = c.TryGetProperty("path", out var pa) && pa.ValueKind == JsonValueKind.Array
                                ? pa.EnumerateArray().Select(e => e.ToString()).ToArray() : [];
                            item.Changes.Add(new PatchChange(Str(c, "source") ?? "", p, Val(c, "from"), Val(c, "to")));
                        }
                    gd.Patch.Add(item);
                }
            }
        }
        catch (Exception e) { Log.Info("Журнал патчей не читается: " + e.Message); }
    }

    /// <summary>Значение из журнала патчей текстом: число, строка или «список из N».</summary>
    static string Val(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var v)) return "—";
        return v.ValueKind switch
        {
            JsonValueKind.Number => v.GetDouble().ToString("0.###", System.Globalization.CultureInfo.InvariantCulture),
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Array => "[" + v.GetArrayLength() + "]",
            JsonValueKind.Null => "—",
            JsonValueKind.True => "да",
            JsonValueKind.False => "нет",
            _ => "…",
        };
    }

    static Unlock? ReadUnlock(JsonElement it)
    {
        if (!it.TryGetProperty("unlock", out var u) || u.ValueKind != JsonValueKind.Object) return null;
        return new Unlock(Str(u, "role"), (int)(Num(u, "level") ?? 0), (int)(Num(u, "cash") ?? 0));
    }

    static void ReadMods(JsonElement el, List<Mod> into)
    {
        if (!el.TryGetProperty("modifiers", out var mods) || mods.ValueKind != JsonValueKind.Array) return;
        foreach (var m in mods.EnumerateArray())
        {
            if (Str(m, "attribute") is string attr && Str(m, "op") is string op && Num(m, "value") is double v)
                into.Add(new Mod(attr, op, v));
        }
    }

    static string? Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    static double? Num(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;

    static string[] Arr(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).ToArray()
            : [];
}
