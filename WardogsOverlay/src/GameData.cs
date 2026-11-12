using System.Text.Json;

namespace WardogsOverlay;

sealed record Mod(string Attr, string Op, double Value);

/// <summary>
/// Когда предмет открывается: роль, её уровень и стоимость открытия. С Сезона 3 часть предметов
/// (броня, рюкзаки, разгрузки, парашюты, патроны, танки) открывается в нескольких ветках — остальные ветки в Also.
/// </summary>
sealed record Unlock(string? Role, int Level, int Cash)
{
    /// <summary>Другие ветки, в которых открывается тот же предмет.</summary>
    public Unlock[] Also { get; init; } = [];
    /// <summary>Пометка для показа: открытие поменялось, а точных уровней в данных ещё нет.</summary>
    public (string Ru, string En)? Note { get; init; }
    /// <summary>Все ветки открытия: основная и остальные.</summary>
    public IEnumerable<Unlock> Tracks => Also.Length == 0 ? [this] : Also.Prepend(this);
}

sealed class Weapon
{
    public required string Slug { get; init; }
    public required string Name { get; init; }
    public string Id { get; init; } = "";
    public string Sub { get; init; } = "";
    public Dictionary<string, double> Attr { get; init; } = [];
    public string[] Slots { get; init; } = [];
    public int? Damage { get; set; }
    /// <summary>Дробин в выстреле (у дроби 8, у остальных 1).</summary>
    public int Pellets { get; set; } = 1;
    /// <summary>Ключ калибра в баллистике MetaForge («545mm») — по нему урон, пробитие и патроны в магазине.</summary>
    public string CalibreKey { get; set; } = "";
    /// <summary>Падение урона с дистанцией: точки (метры, множитель).</summary>
    public List<(double Distance, double Mult)> Falloff { get; set; } = [];
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
    public int Price { get; set; }
    public Unlock? Unlock { get; set; }
    public bool InVendor { get; set; }
    public string Key { get; init; } = "";
    /// <summary>Как название выглядит в русской версии игры, для сравнения с OCR («СВД», «АК74»).</summary>
    public string KeyRu { get; init; } = "";
    /// <summary>Русское название «по начертанию» — как его прочитает английский OCR.</summary>
    public string KeyShape { get; init; } = "";
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
    public int? Price { get; set; }
    public Unlock? Unlock { get; set; }
    public double? Weight { get; set; }
    public bool InVendor { get; set; }
    public string Description { get; init; } = "";
    public string? Calibre { get; init; }
    /// <summary>Ключ калибра («545mm»), в отличие от подписи («5.45x39mm»).</summary>
    public string? CalibreKey { get; init; }
    public string? RoundType { get; init; }
    public int? Amount { get; init; }
    public string Icon { get; init; } = "";
}

/// <summary>Бронежилет или шлем: снижение урона, прочность, что закрывает.</summary>
sealed record ArmourInfo(string Slot, int Reduction, int Durability, string[] Covers);

/// <summary>Правило урона калибра: модификатор патрона по плоти или против брони/шлема.</summary>
sealed record DamageRule(string Kind, double Pct, string[] Rounds, string[] Targets);

/// <summary>Калибр: базовый урон, дробины, пробитие по типам патронов («во сколько раз броня гасит сильнее»), правила.</summary>
sealed record CalibreInfo(double BaseDamage, int Pellets, Dictionary<string, double> Pen, List<DamageRule> Rules);

sealed record PatchChange(string Source, string[] Path, string From, string To);

sealed class PatchItem
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Tab { get; init; }
    public bool Added { get; init; }
    public bool Removed { get; init; }
    public List<PatchChange> Changes { get; } = [];
}

/// <summary>Официальная запись разработчиков в Steam (заметки к патчу, анонсы).</summary>
sealed record NewsPost(string Title, DateTime Date, string Url, string Text);

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
    static readonly string[] OptionalFiles = ["changelog", NewsFile];
    /// <summary>Официальные анонсы и заметки к патчам из Steam (открытый API новостей, без ключа).</summary>
    const string NewsFile = "steam-news";
    const string NewsUrl = "https://api.steampowered.com/ISteamNews/GetNewsForApp/v2/?appid=1867240&count=12&maxlength=0&feeds=steam_community_announcements&format=json";
    static string UrlOf(string name) => name == NewsFile ? NewsUrl : Api + name;

    public static string Dir => Path.Combine(AppPaths.Root, "data");
    static string IconDir => Path.Combine(Dir, "icons");
    static string JsonPath(string name) => Path.Combine(Dir, name.Replace('/', '-') + ".json");
    /// <summary>Свежескачанный файл, ещё не проверенный (лежит рядом с рабочим, пока база из него не откроется).</summary>
    static string StagedPath(string name) => JsonPath(name) + ".new";
    static string ReadPath(string name, bool staged) =>
        staged && File.Exists(StagedPath(name)) ? StagedPath(name) : JsonPath(name);
    /// <summary>Иконки, которых нет на сервере: не перекачиваем их при каждом запуске.</summary>
    static string MissingIconsPath => Path.Combine(IconDir, "_missing.txt");

    public List<Weapon> Weapons { get; } = [];
    public Dictionary<string, Attachment> Attachments { get; } = [];
    public List<DbItem> Items { get; } = [];
    public Dictionary<string, ArmourInfo> Armour { get; } = [];
    public List<PatchItem> Patch { get; } = [];
    public string PatchTitle { get; set; } = "";
    public string PatchDate { get; set; } = "";
    /// <summary>Сборка игры, с которой снята база (CL-516586) — по ней видно, что вышел новый патч.</summary>
    public string PatchBuild { get; set; } = "";
    public string PatchPreviousDate { get; set; } = "";
    /// <summary>
    /// База MetaForge ещё от прошлого патча, и поверх неё применены изменения из официальных заметок
    /// разработчиков (Season3.Apply). Пропадает сам, когда MetaForge выложит данные нового патча.
    /// </summary>
    public bool FromPatchNotes { get; set; }
    /// <summary>Официальные записи разработчиков в Steam, новые сверху.</summary>
    public List<NewsPost> News { get; } = [];
    /// <summary>Калибры из баллистики MetaForge по ключу («545mm»).</summary>
    public Dictionary<string, CalibreInfo> Calibres { get; } = [];
    /// <summary>Множители урона по зонам для каждого класса оружия («Assault Rifle» → голова, грудь…). Общие с Weapon.Hit.</summary>
    public Dictionary<string, Dictionary<string, double>> Hitboxes { get; } = [];
    public string Version { get; private set; } = "";

    public static bool IsDownloaded => Files.All(f => File.Exists(JsonPath(f)));

    /// <summary>База скачана старой версией программы — не хватает иконок пушек или журнала патчей.</summary>
    public bool NeedsUpdate
    {
        get
        {
            var missing = File.Exists(MissingIconsPath) ? File.ReadAllLines(MissingIconsPath).ToHashSet() : [];
            return Weapons.Any(w => !string.IsNullOrEmpty(w.Icon) && IconFile(w.Icon) == null && !missing.Contains(w.Icon)) ||
                   VendorItemIcons().Any(i => IconFile(i) == null && !missing.Contains(i)) ||
                   OptionalFiles.Any(f => !File.Exists(JsonPath(f)));
        }
    }

    /// <summary>Иконки всего, что продаётся за деньги (снаряжение, патроны, броня, техника…) — чтобы узнавать покупки.</summary>
    public IEnumerable<string> VendorItemIcons() =>
        Items.Where(i => i.Price > 0 && (i.InVendor || i.Unlock is { Cash: > 0 }) && i.Tab is not ("weapons" or "attachments") && i.Icon.Length > 0)
             .Select(i => i.Icon).Distinct();

    public string? IconFile(string icon)
    {
        if (string.IsNullOrEmpty(icon)) return null;
        var p = Path.Combine(IconDir, icon + ".webp");
        return File.Exists(p) && new FileInfo(p).Length > 0 ? p : null;
    }

    /// <summary>
    /// Скачать базу. Возвращает true, если что-то поменялось (новые данные или иконки) —
    /// тогда базу нужно перечитать; false — на сервере то же самое, что уже лежит у нас.
    /// </summary>
    public static async Task<bool> DownloadAsync(Action<string> report)
    {
        Directory.CreateDirectory(Dir);
        Directory.CreateDirectory(IconDir);
        using var http = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
            { Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("WardogsOverlay/1.0 (+personal, non-commercial)");

        // Сначала всё скачиваем рядом (.new) и пробуем открыть базу из новых файлов. Рабочие файлы
        // заменяем, только если база открылась: ответ сервера не того вида не должен сломать то, что работало.
        var all = Files.Concat(OptionalFiles).ToList();
        foreach (var name in all) File.Delete(StagedPath(name));
        bool changed = false;
        try
        {
            foreach (var name in all)
            {
                report(L.T($"Скачиваю {name}…", $"Downloading {name}…"));
                try
                {
                    var text = await http.GetStringAsync(UrlOf(name));
                    using (JsonDocument.Parse(text)) { } // проверка, что пришёл JSON, а не страница ошибки
                    await File.WriteAllTextAsync(StagedPath(name), text);
                }
                catch (Exception e) when (OptionalFiles.Contains(name)) { Log.Info($"Не скачался {name}: {e.Message}"); }
            }
            try { Load(staged: true); }
            catch (Exception e)
            {
                throw new InvalidDataException(L.T("metaforge.app прислал данные в неожиданном виде, оставляю прежнюю базу.",
                                                   "metaforge.app returned data in an unexpected format; keeping the previous data."), e);
            }
            foreach (var name in all.Where(n => File.Exists(StagedPath(n))))
            {
                if (File.Exists(JsonPath(name)) && File.ReadAllText(JsonPath(name)) == File.ReadAllText(StagedPath(name))) continue;
                File.Move(StagedPath(name), JsonPath(name), true);
                changed = true;
            }
            File.WriteAllText(LastCheckPath, DateTime.UtcNow.ToString("O"));
        }
        finally
        {
            foreach (var name in all) File.Delete(StagedPath(name));
        }

        var data = Load();
        var missingBefore = File.Exists(MissingIconsPath) ? File.ReadAllLines(MissingIconsPath).ToHashSet() : [];
        var icons = data.Attachments.Values.Select(a => a.Icon).Concat(data.Weapons.Select(w => w.Icon))
            .Concat(data.VendorItemIcons().Where(i => !missingBefore.Contains(i)))
            .Where(i => !string.IsNullOrEmpty(i) && data.IconFile(i) == null)
            .Distinct().ToList();
        report(L.T($"Скачиваю иконки: {icons.Count} шт.…", $"Downloading icons: {icons.Count}…"));
        using var gate = new SemaphoreSlim(6);
        int failed = 0;
        var notFound = new System.Collections.Concurrent.ConcurrentBag<string>();
        await Task.WhenAll(icons.Select(async icon =>
        {
            await gate.WaitAsync();
            try
            {
                var bytes = await http.GetByteArrayAsync(IconUrl + icon + ".webp");
                var path = Path.Combine(IconDir, icon + ".webp");
                await File.WriteAllBytesAsync(path + ".tmp", bytes);
                File.Move(path + ".tmp", path, true);
            }
            catch (HttpRequestException e) when (e.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                notFound.Add(icon);
                Interlocked.Increment(ref failed);
            }
            catch { Interlocked.Increment(ref failed); }
            finally { gate.Release(); }
        }));
        File.WriteAllLines(MissingIconsPath, notFound.Order());
        report(failed == 0 ? L.T("База обновлена.", "Data updated.")
                           : L.T($"База обновлена, не скачалось иконок: {failed}.", $"Data updated, icons failed: {failed}."));
        return changed || icons.Count > failed;
    }

    static string LastCheckPath => Path.Combine(Dir, ".last-check");

    /// <summary>Когда базу последний раз сверяли с сервером (чтобы не качать при каждом перезапуске).</summary>
    public static DateTime LastCheck =>
        File.Exists(LastCheckPath) && DateTime.TryParse(File.ReadAllText(LastCheckPath), null, System.Globalization.DateTimeStyles.RoundtripKind, out var t)
            ? t : DateTime.MinValue;

    public static GameData Load() => Load(staged: false);

    /// <summary>corrections: false — данные MetaForge как есть, без поправок из официальных заметок к патчу.</summary>
    public static GameData Load(bool corrections) => Load(staged: false, corrections);

    static GameData Load(bool staged, bool corrections = true)
    {
        var gd = new GameData();
        using var db = JsonDocument.Parse(File.ReadAllText(ReadPath("database", staged)));
        using var stats = JsonDocument.Parse(File.ReadAllText(ReadPath("attachment-stats", staged)));
        using var bal = JsonDocument.Parse(File.ReadAllText(ReadPath("ballistics/v2", staged)));
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
                    Calibre = Str(it, "calibreLabel") ?? Str(it, "calibre"), CalibreKey = Str(it, "calibre"), RoundType = Str(it, "roundType"),
                    Amount = Num(it, "amount") is double am ? (int)am : null,
                    Icon = Str(it, "icon") ?? "",
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
                    KeyShape = TextUtil.Shape(L.WeaponRu(name)),
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
        ReadChangelog(gd, staged);
        ReadNews(gd, staged);
        if (corrections) Season3.Apply(gd);
        if (gd.Weapons.Count == 0 || gd.Attachments.Count == 0)
            throw new InvalidDataException(L.T("в базе нет пушек или обвесов", "data has no weapons or attachments"));
        return gd;
    }

    static void ReadBallistics(JsonElement root, GameData gd, Dictionary<string, Weapon> weaponsBySlug)
    {
        if (root.TryGetProperty("calibres", out var cals) && cals.ValueKind == JsonValueKind.Object)
            foreach (var c in cals.EnumerateObject())
                if (Num(c.Value, "baseDamage") is double d)
                {
                    var rules = new List<DamageRule>();
                    if (c.Value.TryGetProperty("rules", out var rs) && rs.ValueKind == JsonValueKind.Array)
                        foreach (var r in rs.EnumerateArray())
                            if (Num(r, "pct") is double pct)
                                rules.Add(new DamageRule(Str(r, "kind") ?? "", pct, Arr(r, "rounds"), Arr(r, "tgt").Concat(Arr(r, "targets")).ToArray()));
                    var pen = new Dictionary<string, double>();
                    if (c.Value.TryGetProperty("pen", out var pe) && pe.ValueKind == JsonValueKind.Object)
                        foreach (var p in pe.EnumerateObject())
                            if (p.Value.ValueKind == JsonValueKind.Number) pen[p.Name] = p.Value.GetDouble();
                    gd.Calibres[c.Name] = new CalibreInfo(d, Math.Max(1, (int)(Num(c.Value, "pellets") ?? 1)), pen, rules);
                }

        var hitboxes = gd.Hitboxes;
        if (root.TryGetProperty("hitboxes", out var hb) && hb.ValueKind == JsonValueKind.Object)
            foreach (var cls in hb.EnumerateObject())
                hitboxes[cls.Name] = cls.Value.EnumerateObject().Where(z => z.Value.ValueKind == JsonValueKind.Number)
                                              .ToDictionary(z => z.Name, z => z.Value.GetDouble());

        if (root.TryGetProperty("weapons", out var bw) && bw.ValueKind == JsonValueKind.Array)
            foreach (var b in bw.EnumerateArray())
            {
                if (Str(b, "slug") is not string s || !weaponsBySlug.TryGetValue(s, out var w)) continue;
                w.Rpm = Num(b, "roundsPerMinute") is double rpm ? (int)rpm : null;
                w.Range = Num(b, "effectiveRange") is double rng ? (int)rng : null;
                w.Velocity = Num(b, "muzzleVelocity") is double v ? (int)v : null;
                w.MagSize = Num(b, "magSize") is double ms ? (int)ms : null;
                w.Moa = Num(b, "accuracyMoa");
                w.FireModes = Arr(b, "fireModes");
                if (Str(b, "calibre") is string cal && gd.Calibres.TryGetValue(cal, out var ci))
                {
                    w.CalibreKey = cal;
                    w.Damage = (int)Math.Round(ci.BaseDamage);
                    w.Pellets = ci.Pellets;
                }
                if (b.TryGetProperty("falloff", out var fo) && fo.ValueKind == JsonValueKind.Array)
                    foreach (var pt in fo.EnumerateArray())
                        if (pt.ValueKind == JsonValueKind.Array && pt.GetArrayLength() >= 2 &&
                            pt[0].ValueKind == JsonValueKind.Number && pt[1].ValueKind == JsonValueKind.Number)
                            w.Falloff.Add((pt[0].GetDouble(), pt[1].GetDouble()));
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

    static void ReadChangelog(GameData gd, bool staged)
    {
        var path = ReadPath("changelog", staged);
        if (!File.Exists(path)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("current", out var cur)) return;
            gd.PatchTitle = Str(cur, "title") ?? "";
            gd.PatchDate = Str(cur, "date") ?? "";
            gd.PatchBuild = Str(cur, "build") ?? "";
            gd.PatchPreviousDate = Str(cur, "previousDate") ?? "";
            foreach (var (prop, kind) in new[] { ("added", 1), ("changed", 0), ("removed", -1) })
            {
                if (!cur.TryGetProperty(prop, out var list) || list.ValueKind != JsonValueKind.Array) continue;
                foreach (var x in list.EnumerateArray())
                {
                    var item = new PatchItem
                    {
                        Id = Str(x, "id") ?? "", Name = Str(x, "name") ?? "?", Tab = Str(x, "tab") ?? "", Added = kind > 0, Removed = kind < 0,
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

    static void ReadNews(GameData gd, bool staged)
    {
        var path = ReadPath(NewsFile, staged);
        if (!File.Exists(path)) return;
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("appnews", out var app) || !app.TryGetProperty("newsitems", out var items) ||
                items.ValueKind != JsonValueKind.Array) return;
            foreach (var n in items.EnumerateArray())
            {
                var date = n.TryGetProperty("date", out var d) && d.TryGetInt64(out var unix) ? DateTimeOffset.FromUnixTimeSeconds(unix).LocalDateTime : DateTime.MinValue;
                gd.News.Add(new NewsPost(Str(n, "title") ?? "", date, Str(n, "url") ?? "", TextUtil.CleanSteamText(Str(n, "contents") ?? "")));
            }
            gd.News.Sort((a, b) => b.Date.CompareTo(a.Date));
        }
        catch (Exception e) { Log.Info("Новости Steam не читаются: " + e.Message); }
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

    /// <summary>
    /// Открытие предмета. С Сезона 3 предмет может открываться в нескольких ветках, а в каком виде MetaForge
    /// это выложит, заранее неизвестно — принимаем и объект, и список, и список веток внутри объекта
    /// («tracks», «alternatives», «also», «roles»), и отдельный список «unlocks».
    /// </summary>
    internal static Unlock? ReadUnlock(JsonElement it)
    {
        var list = new List<Unlock>();
        void One(JsonElement u)
        {
            if (u.ValueKind != JsonValueKind.Object) return;
            if (Num(u, "level") is not null || Str(u, "role") is not null || Num(u, "cash") is not null)
                list.Add(new Unlock(Str(u, "role") ?? Str(u, "track"), (int)(Num(u, "level") ?? 0), (int)(Num(u, "cash") ?? 0)));
            foreach (var key in new[] { "tracks", "alternatives", "also", "roles" })
                if (u.TryGetProperty(key, out var more) && more.ValueKind == JsonValueKind.Array)
                    foreach (var m in more.EnumerateArray()) One(m);
        }
        foreach (var key in new[] { "unlock", "unlocks" })
        {
            if (!it.TryGetProperty(key, out var u)) continue;
            if (u.ValueKind == JsonValueKind.Array) foreach (var x in u.EnumerateArray()) One(x);
            else One(u);
        }
        if (list.Count == 0) return null;
        // Основная — первая ветка с ролью (у неё цена открытия, которую знала программа до Сезона 3), одинаковые не дублируем.
        var tracks = list.DistinctBy(t => (t.Role, t.Level)).ToList();
        var main = tracks.FirstOrDefault(t => t.Role is not (null or "None") && t.Level > 0) ?? tracks[0];
        return main with { Also = tracks.Where(t => t != main && t.Role is not (null or "None") && t.Level > 0).ToArray() };
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
