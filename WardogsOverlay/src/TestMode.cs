using System.Diagnostics;

namespace WardogsOverlay;

/// <summary>
/// Проверка без игры: WardogsOverlay.exe --test снимок.png [ещё.png …]
/// Печатает ход распознавания и сохраняет снимок.result.png с нарисованной плашкой.
/// </summary>
static class TestMode
{
    public static int Run(string[] files)
    {
        if (files.Length == 0)
        {
            Console.WriteLine("Использование: WardogsOverlay.exe --test снимок.png [ещё.png …]");
            return 2;
        }
        if (files.Length == 3 && files[0] == "--dump-icon")
        {
            IconMatcher.Dump(files[1], files[2]);
            return 0;
        }
        if (files.Length >= 1 && files[0] == "--test-window") return TestWindow();
        if (files.Length >= 1 && files[0] == "--self-check") return SelfCheck();
        if (files.Length >= 2 && files[0] == "--test-db") return TestDatabase(files[1], files.Contains("--en"));
        if (files.Length >= 2 && files[0] == "--test-main")
        {
            // Главное окно без запуска: рисуем в картинку, не показывая (поиск не стартует).
            L.Current = files.Contains("--en") ? Lang.En : Lang.Ru;
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Ui.Theme.Load();
            var st = new Settings { Language = L.Current };
            using var ov = new OverlayForm(st);
            using var mf = new MainForm(st, ov) { Preview = true, StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000), ShowInTaskbar = false };
            mf.Show();
            if (files.Contains("--tall")) mf.Height = 4200; // всё окно целиком, без прокрутки
            var until = Environment.TickCount64 + 600;
            while (Environment.TickCount64 < until) { Application.DoEvents(); Thread.Sleep(10); }
            using var bmp = new Bitmap(mf.Width, mf.Height);
            mf.DrawToBitmap(bmp, new Rectangle(0, 0, mf.Width, mf.Height));
            bmp.Save(files[1]);
            Console.WriteLine($"→ {files[1]} {mf.Width}x{mf.Height}");
            return 0;
        }
        if (files.Length >= 1 && files[0] == "--list-names")
        {
            var d = GameData.Load();
            int same = 0;
            foreach (var a in d.Attachments.Values.Where(a => a.InVendor).OrderBy(a => a.Slot).ThenBy(a => a.Name))
            {
                var ru = L.AttachmentRu(a.Name);
                if (ru == a.Name) same++;
                Console.WriteLine($"{(ru == a.Name ? "!!" : "  ")} {a.Slot,-12} {a.Name}  →  {ru}");
            }
            foreach (var w in d.Weapons) Console.WriteLine($"   weapon       {w.Name}  →  {L.WeaponRu(w.Name)}   [{w.KeyRu}]");
            Console.WriteLine($"без перевода: {same}");
            return 0;
        }
        if (files.Length >= 2 && files[0] == "--ocr-all")
        {
            // Все слова кадра (приведённого к 1080) — посмотреть, как OCR читает интерфейс.
            OcrService.ForceEnglish = files.Contains("--no-ru-ocr");
            using var b = new Bitmap(files[1]);
            var fr = Frame.FromBitmap(b);
            double k = fr.H >= 700 ? 1080.0 / fr.H : 1;
            var nf = k == 1 ? fr : fr.Resize((int)(fr.W * k), (int)(fr.H * k));
            foreach (var line in new OcrService().Russian(nf))
                Console.WriteLine(string.Join("  ", line.Words.Select(w => $"«{w.Text}»{w.Box}")));
            return 0;
        }
        if (files.Length == 2 && files[0] == "--ocr-top")
        {
            using var b = new Bitmap(files[1]);
            var fr = Frame.FromBitmap(b);
            double k = fr.H >= 700 ? 1080.0 / fr.H : 1;
            var nf = k == 1 ? fr : fr.Resize((int)(fr.W * k), (int)(fr.H * k));
            var svc = new OcrService();
            foreach (var line in svc.Russian(nf).Where(l => l.Words.Count > 0 && l.Words[0].Box.Y < nf.H * 0.1))
                Console.WriteLine(string.Join("  ", line.Words.Select(w => $"«{w.Text}»{w.Box}")));
            var corner = RectI.FromLTRB((int)(nf.W * 0.68), 0, nf.W, (int)(nf.H * 0.09));
            var crop = nf.Crop(corner);
            using (var cb = crop.Resize(crop.W * 2, crop.H * 2).ToBitmap()) cb.Save(Path.ChangeExtension(files[1], ".corner.png"));
            var tight = nf.Crop(RectI.FromLTRB((int)(nf.W * 0.84), (int)(nf.H * 0.008), (int)(nf.W * 0.94), (int)(nf.H * 0.055)));
            foreach (var sc in new[] { 2, 3, 4 })
            {
                var t = tight.Resize(tight.W * sc, tight.H * sc);
                using (var tb = t.ToBitmap()) tb.Save(Path.ChangeExtension(files[1], $".tight{sc}.png"));
                Console.WriteLine($"тесно ×{sc} ({t.W}x{t.H}) en: " + string.Join(" | ", svc.English(t).Select(l => l.Text)) +
                                  "  /инв: " + string.Join(" | ", svc.English(t.Inverted()).Select(l => l.Text)) +
                                  "  /ru: " + string.Join(" | ", svc.Russian(t).Select(l => l.Text)));
            }
            foreach (var sc in new[] { 2, 3 })
            {
                var up = crop.Resize(crop.W * sc, crop.H * sc);
                Console.WriteLine($"×{sc} en: " + string.Join(" | ", svc.English(up).Select(l => l.Text)));
                Console.WriteLine($"×{sc} ru: " + string.Join(" | ", svc.Russian(up).Select(l => l.Text)));
                Console.WriteLine($"×{sc} en инверсия: " + string.Join(" | ", svc.English(up.Inverted()).Select(l => l.Text)));
                Console.WriteLine($"×{sc} ru инверсия: " + string.Join(" | ", svc.Russian(up.Inverted()).Select(l => l.Text)));
            }
            return 0;
        }
        L.Current = files.Contains("--en") ? Lang.En : Lang.Ru;
        Recognizer.DisableNameOcr = files.Contains("--icon-weapon");
        OcrService.ForceEnglish = files.Contains("--no-ru-ocr");
        // --hide=СЛОВО: считать слово непрочитанным (проверка запасных путей распознавания).
        foreach (var hw in files.Where(f => f.StartsWith("--hide="))) Recognizer.HiddenWords.Add(hw[7..]);
        files = files.Where(f => !f.StartsWith("--")).ToArray();
        if (!GameData.IsDownloaded || GameData.Load().NeedsUpdate) GameData.DownloadAsync(Console.WriteLine).GetAwaiter().GetResult();
        var recognizer = new Recognizer(new OcrService()) { Data = GameData.Load() };
        int failures = 0;

        foreach (var file in files)
        {
            Console.WriteLine($"=== {file}");
            using var bmp = new Bitmap(file);
            var frame = Frame.FromBitmap(bmp);
            var sw = Stopwatch.StartNew();
            var rec = recognizer.Run(frame);
            sw.Stop();
            foreach (var t in rec.Trace) Console.WriteLine("  " + t);
            Console.WriteLine($"  меню: {rec.Menu} {rec.Reason}; пушка: {rec.Weapon?.Name ?? "?"}; время: {sw.ElapsedMilliseconds} мс");
            if (rec.Weapon == null) { failures++; continue; }

            var opts = new Settings { ShowSlotOptions = true };
            var model = OverlayModel.Build(rec.Weapon, rec.Cells, null, recognizer.Data, rec.SelectedSlot, opts);
            float u = (float)(frame.H >= 700 ? frame.H / 1080.0 : 1.0);
            var size = OverlayRenderer.Measure(model, u);
            using var outBmp = new Bitmap(bmp.Width, bmp.Height);
            using (var g = Graphics.FromImage(outBmp))
            {
                g.DrawImage(bmp, 0, 0, bmp.Width, bmp.Height);
                using var pen = new Pen(Color.Magenta, 1);
                foreach (var c in rec.Cells) g.DrawRectangle(pen, c.Cell.X, c.Cell.Y, c.Cell.W, c.Cell.H);
                g.DrawRectangle(Pens.Cyan, rec.Roi.X, rec.Roi.Y, rec.Roi.W, rec.Roi.H);
                int x = Math.Clamp(rec.AnchorX, 0, Math.Max(0, bmp.Width - size.Width));
                int y = Math.Clamp(rec.AnchorY, 0, Math.Max(0, bmp.Height - size.Height));
                using var plaque = new Bitmap(size.Width, size.Height);
                using (var pg = Graphics.FromImage(plaque)) OverlayRenderer.Draw(pg, model, u);
                g.DrawImage(plaque, x, y);
                plaque.Save(Path.ChangeExtension(file, ".plaque.png"));
            }
            var outPath = Path.ChangeExtension(file, ".result.png");
            outBmp.Save(outPath);
            Console.WriteLine($"  → {outPath}");

            // Режим сравнения: как будто запомнили сборку без последнего обвеса, но с другим дулом.
            var known = rec.Cells.Where(c => c.Att != null).Select(c => c.Att!).ToList();
            if (known.Count > 0)
            {
                var snap = known.Take(known.Count - 1).ToList();
                var muzzle = recognizer.Data.Attachments.Values.FirstOrDefault(a => a.Slot == "Muzzle" && a.FitsWeapon(rec.Weapon) && a.Mods.Count > 2);
                if (muzzle != null) snap.Add(muzzle);
                var cmp = OverlayModel.Build(rec.Weapon, rec.Cells, snap);
                var csize = OverlayRenderer.Measure(cmp, u);
                using var cb = new Bitmap(csize.Width, csize.Height);
                using (var cg = Graphics.FromImage(cb)) OverlayRenderer.Draw(cg, cmp, u);
                var cmpPath = Path.ChangeExtension(file, ".compare.png");
                cb.Save(cmpPath);
                Console.WriteLine($"  → {cmpPath}");
            }

            // Подбор: минимум отдачи с бюджетом $1500 и без ограничений.
            foreach (var (p, budget, suffix) in new[] { (Priority.Recoil, 1500, ".best-recoil.png"), (Priority.Handling, 0, ".best-ads.png") })
            {
                var bs = new Settings { ShowSlotOptions = false, BestPriority = p, BestBudget = budget };
                var bm = OverlayModel.Build(rec.Weapon, rec.Cells, null, recognizer.Data, null, bs);
                var bsize = OverlayRenderer.Measure(bm, u);
                using var bb = new Bitmap(bsize.Width, bsize.Height);
                using (var bg = Graphics.FromImage(bb)) OverlayRenderer.Draw(bg, bm, u);
                bb.Save(Path.ChangeExtension(file, suffix));
            }

            // Вторая и следующие прогонки — тёплые, показывают реальную стоимость кадра.
            sw.Restart();
            recognizer.Run(frame);
            Console.WriteLine($"  повторно: {sw.ElapsedMilliseconds} мс");
        }
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Открывает окно «База», рисует каждую вкладку в PNG и печатает результаты сборки под бюджет.</summary>
    /// <summary>Проверки логики без игры и без OCR: разбор баланса, экономика, статы, подбор.</summary>
    static int SelfCheck()
    {
        int fails = 0;
        void Check(string name, bool ok, string got = "")
        {
            if (!ok) fails++;
            Console.WriteLine($"{(ok ? "ok  " : "FAIL")} {name}{(got.Length > 0 ? "  → " + got : "")}");
        }

        // Баланс: слова OCR в правом верхнем углу кадра 1920x1080.
        var frame = new Frame(1920, 1080);
        int? Balance(params string[] words)
        {
            int x = 1400;
            var ws = new List<OcrWord>();
            foreach (var w in words)
            {
                if (w == "|") { x += 120; continue; }
                ws.Add(new OcrWord(w, new RectI(x, 30, 12 * w.Length, 20)));
                x += 12 * w.Length + 8;
            }
            return Recognizer.ReadBalance([new OcrLineText(string.Join(" ", words), ws)], frame, new Recognition());
        }
        Check("баланс $150 | $324 935", Balance("$150", "|", "$324", "935") == 324935, Balance("$150", "|", "$324", "935")?.ToString() ?? "null");
        Check("баланс слитно $336126", Balance("$0", "|", "$336126") == 336126);
        Check("«$» отдельным словом", Balance("$0", "|", "$", "324", "935") == 324935, Balance("$0", "|", "$", "324", "935")?.ToString() ?? "null");
        Check("З вместо 3 в группе", Balance("$0", "|", "$324", "9З5") == 324935, Balance("$0", "|", "$324", "9З5")?.ToString() ?? "null");
        Check("испорченная группа → не знаю, а не $324", Balance("$0", "|", "$324", "9?5") == null, Balance("$0", "|", "$324", "9?5")?.ToString() ?? "null");
        Check("правая сумма не читается → не $0 корзины", Balance("$0", "|", "$abc") == null, Balance("$0", "|", "$abc")?.ToString() ?? "null");

        Check("баланс английской игры $0 | $12,500", Balance("$0", "|", "$12,500") == 12500, Balance("$0", "|", "$12,500")?.ToString() ?? "null");

        // Слова интерфейса: русская игра (русским и английским OCR) и английская игра.
        foreach (var (word, modules) in new[] { ("МОДУЛИ", true), ("модули", true), ("MOAYJIVI", true), ("M0AYJIV1", true), ("ATTC.", true),
                                                ("ATTACHMENTS", true), ("MODE", false), ("ATTACK", false), ("AMMO", false), ("GALIL", false) })
            Check($"«{word}» — {(modules ? "" : "не ")}кнопка модулей", TextUtil.IsModulesWord(word) == modules);
        foreach (var (word, isAmmo) in new[] { ("БОЕПРИПАСЫ", true), ("EOEnPff1ACbl", true), ("EOEnPVff1ACbl", true), ("AMMO", true), ("ATTC.", false), ("MOAYJIVI", false) })
            Check($"«{word}» — {(isAmmo ? "" : "не ")}кнопка патронов", TextUtil.IsAmmoWord(word) == isAmmo);
        Check("«ПП-19 «БИЗОН»» по начертанию = «nn-19 «6VI3OH»»", TextUtil.Shape("ПП-19 «БИЗОН»") == TextUtil.Shape("nn-19 «6VI3OH»"),
              $"{TextUtil.Shape("ПП-19 «БИЗОН»")} / {TextUtil.Shape("nn-19 «6VI3OH»")}");

        var clean = TextUtil.CleanSteamText("[h2]Patch[/h2][list][*]One[*]Two[/list][img]{STEAM_CLAN_IMAGE}/a.png[/img]" +
                                            "[dynamiclink href=\"https://x\"][/dynamiclink][url=https://y]link[/url] &amp; done");
        Check("текст анонса Steam очищен от разметки", !clean.Contains('[') && clean.Contains("• One") && clean.Contains("link & done"), clean.Replace("\n", " / "));

        // Экономика: стрельбище $10 000 → продавец $324 935 → $376 326 (реальная сессия 29.10).
        var s = new EconomySession();
        var t0 = DateTime.Now;
        s.Points.AddRange([new(t0, 10000), new(t0.AddMinutes(7), 324935), new(t0.AddMinutes(93), 376326)]);
        Check("скачок со стрельбища не заработок", s.Earned == 51391 && s.Net == 51391, $"{s.Earned} / {s.Net}");
        Check("скачок в разы — IsJump", Economy.IsJump(10000, 324935) && !Economy.IsJump(324935, 376326));

        var data = GameData.Load();
        var m500 = data.Weapons.FirstOrDefault(w => w.Pellets > 1);
        Check("у дробовика 8 дробин", m500 != null && m500.Pellets == 8, m500 == null ? "нет" : $"{m500.Name} ×{m500.Pellets}");
        if (m500 != null && Combat.TtkChest(data, m500, 0) is { } ttk) Check("дробовик без брони убивает за 1–2 выстрела", ttk.Shots <= 2, $"{ttk.Shots}");

        // Модель урона сверена с калькулятором MetaForge (верх груди / голова, 50 м).
        var ak = data.Weapons.First(w => w.Slug == "ak74");
        int S(Weapon w, string zone, RoundLoad load, int body, int helmet) => Combat.Shots(data, w, zone, load, body, helmet, 50)?.Shots ?? -1;
        var akTorso = new[] { 0, 1, 2, 3, 4 }.Select(l => S(ak, "torso_upper", RoundLoad.FMJ, l, 0)).ToArray();
        Check("AK74 в верх груди, броня 0–4: 4/6/7/8/10 выстрелов", akTorso.SequenceEqual([4, 6, 7, 8, 10]), string.Join("/", akTorso));
        Check("AK74 броня 2: HP 19, AP 7", S(ak, "torso_upper", RoundLoad.HollowPoint, 2, 0) == 19 && S(ak, "torso_upper", RoundLoad.ArmorPiercing, 2, 0) == 7,
              $"HP {S(ak, "torso_upper", RoundLoad.HollowPoint, 2, 0)}, AP {S(ak, "torso_upper", RoundLoad.ArmorPiercing, 2, 0)}");
        Check("AK74 в голову, шлем 2: FMJ 3, HP 12, AP 3",
              S(ak, "head", RoundLoad.FMJ, 2, 2) == 3 && S(ak, "head", RoundLoad.HollowPoint, 2, 2) == 12 && S(ak, "head", RoundLoad.ArmorPiercing, 2, 2) == 3,
              $"{S(ak, "head", RoundLoad.FMJ, 2, 2)}/{S(ak, "head", RoundLoad.HollowPoint, 2, 2)}/{S(ak, "head", RoundLoad.ArmorPiercing, 2, 2)}");
        Check("AK74 HP в шею (не закрыта шлемом 2): 2 выстрела", S(ak, "neck", RoundLoad.HollowPoint, 2, 2) == 2);
        var akTtk = Combat.Shots(data, ak, "torso_upper", RoundLoad.FMJ, 2, 0, 50)?.Seconds ?? 0;
        Check("AK74 броня 2, 50 м: 0,61 с с полётом пули", Math.Abs(akTtk - 0.61) < 0.01, akTtk.ToString("0.000"));
        if (m500 != null)
        {
            var mt = new[] { 0, 1, 2, 3, 4 }.Select(l => S(m500, "torso_upper", RoundLoad.FMJ, l, 0)).ToArray();
            Check("M500 в верх груди, броня 0–4: 1/2/2/2/4", mt.SequenceEqual([1, 2, 2, 2, 4]), string.Join("/", mt));
        }
        Check("магазин AK74: FMJ дешевле AP", Combat.MagazineCost(data, ak, RoundLoad.FMJ) < Combat.MagazineCost(data, ak, RoundLoad.ArmorPiercing),
              $"{Combat.MagazineCost(data, ak, RoundLoad.FMJ)} / {Combat.MagazineCost(data, ak, RoundLoad.ArmorPiercing)}");

        int ArmourPrice(string slug) => data.Items.FirstOrDefault(i => i.Slug == slug)?.Price ?? 0;
        int life = OverlayModel.LifeCostOf(data, ak, [], 2);
        Check("цена жизни: пушка + броня и шлем 2 ур.", life == ak.Price + ArmourPrice("level-2-armor") + ArmourPrice("level-2-helmet") && life > ak.Price, L.Money(life));
        var kl = OverlayModel.KillLine(data, ak, 2, 50);
        Check("строка плашки про убийство", kl?.What.Contains("7") == true, kl is { } x ? $"{x.Where}: {x.What}" : "null");

        var canted = data.Attachments.Values.FirstOrDefault(a => a.SlotKey == "CantedSight" && a.Zoom != null);
        var scope = data.Attachments.Values.Where(a => a.SlotKey == "Sight" && a.Zoom >= 4 && a.Fits?.Count > 0).FirstOrDefault();
        var w4 = scope == null ? null : data.Weapons.FirstOrDefault(w => scope.Fits!.Contains(w.Slug));
        if (canted != null && scope != null && w4 != null)
        {
            var st = Stats.Compute(w4, [scope, canted]);
            var zoom = st.Value(Stats.Def("AdsZoomMultiplier"));
            Check("боковой прицел не перебивает зум основного", Math.Abs(zoom - scope.Zoom!.Value) < 1e-6, $"{zoom} при {scope.Zoom}");
        }

        var many = data.Weapons.First(w => w.Slots.Length > 2);
        var sw = Stopwatch.StartNew();
        var best = Builds.Best(data, many, [], Priority.Balance, 9_999_999);
        Check("подбор с бюджетом 9 999 999 быстрый", sw.ElapsedMilliseconds < 500, $"{sw.ElapsedMilliseconds} мс, {best?.Atts.Count ?? 0} обвесов");

        Console.WriteLine(fails == 0 ? "всё ок" : $"провалов: {fails}");
        return fails == 0 ? 0 : 1;
    }

    static int TestDatabase(string outDir, bool en)
    {
        L.Current = en ? Lang.En : Lang.Ru;
        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        var data = GameData.Load();
        Directory.CreateDirectory(outDir);

        foreach (var (budget, armour, sight, goal) in new[] { (3000, 1, Builds.SightBand.Red, Builds.LoadoutGoal.Balance),
                                                             (8000, 2, Builds.SightBand.Mid, Builds.LoadoutGoal.Ttk),
                                                             (15000, 3, Builds.SightBand.Long, Builds.LoadoutGoal.Recoil) })
        {
            var res = Builds.BestLoadouts(data, new Builds.LoadoutRequest { Budget = budget, ArmourLevel = armour, Sight = sight, Goal = goal }, 3);
            Console.WriteLine($"бюджет {budget}, броня {armour}, прицел {sight}, цель {goal}:");
            foreach (var l in res)
                Console.WriteLine($"  {l.Weapon.Name} ${l.Cost}  ttk={l.Ttk:0.00}  climb={l.Climb:0.0}  ads={l.Ads:0.00}  [{string.Join(", ", l.Atts.Select(a => a.Name))}]");
        }

        // Экономика с выдуманными показаниями — чтобы увидеть график.
        var eco = new Economy();
        var s = eco.Current;
        var t = DateTime.Now.AddMinutes(-40);
        foreach (var (min, bal) in new[] { (0, 10000), (6, 13200), (9, 9800), (17, 15400), (25, 21000), (28, 16500), (40, 24800) })
            s.Points.Add(new MoneyPoint(t.AddMinutes(min), bal));

        Ui.Theme.Load();
        using var form = new DatabaseForm(data, eco) { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000), ShowInTaskbar = false };
        form.Show();
        for (int i = 0; i < DatabaseForm.PageKeys.Count; i++)
        {
            form.SelectPage(i);
            var until = Environment.TickCount64 + 700;
            while (Environment.TickCount64 < until) { Application.DoEvents(); Thread.Sleep(10); }
            using var bmp = new Bitmap(form.Width, form.Height);
            form.DrawToBitmap(bmp, new Rectangle(0, 0, form.Width, form.Height));
            var path = Path.Combine(outDir, $"db_{DatabaseForm.PageKeys[i]}{(en ? "_en" : "")}.png");
            bmp.Save(path);
            Console.WriteLine($"→ {path}");
        }
        form.Close();
        return 0;
    }

    /// <summary>
    /// Показывает настоящее окно плашки на секунду, снимает экран и проверяет, что её видно
    /// (фон плашки и оранжевая полоса слева) — при разной непрозрачности.
    /// </summary>
    static int TestWindow()
    {
        var data = GameData.Load();
        var w = data.Weapons.First(x => x.Slug == "galil");
        var cells = new List<CellHit> { new() { Att = data.Attachments["afg-angled-foregrip"] } };
        var model = OverlayModel.Build(w, cells, null);
        int failures = 0;
        using var cap = new ScreenCapture();
        foreach (var opacity in new[] { 1.0, 0.94, 0.6 })
        {
            var s = new Settings { Opacity = opacity, HideFromCapture = false };
            using var form = new OverlayForm(s);
            _ = form.Handle;
            var area = new RectI(0, 0, Screen.PrimaryScreen!.Bounds.Width, Screen.PrimaryScreen.Bounds.Height);
            form.Present(model, 40, 40, 1f, area);
            var until = Environment.TickCount64 + 700;
            while (Environment.TickCount64 < until) { Application.DoEvents(); Thread.Sleep(15); }

            var b = form.Bounds;
            var shot = cap.Grab(new RectI(b.X, b.Y, b.Width, b.Height));
            var accent = shot.Rgb(1, b.Height / 2);
            var bg = shot.Rgb(b.Width - 6, b.Height - 4);
            bool orange = accent.R > 140 && accent.R > accent.B + 60;
            bool dark = opacity < 0.99 || (Math.Abs(bg.R - 20) <= 3 && Math.Abs(bg.G - 21) <= 3 && Math.Abs(bg.B - 22) <= 3);
            bool ok = form.Visible && orange && dark;
            if (!ok) failures++;
            Console.WriteLine($"непрозрачность {opacity:0.00}: окно {(form.Visible ? "показано" : "скрыто")} {b}, " +
                              $"полоса {accent}, фон {bg} → {(ok ? "ВИДНО" : "НЕ ВИДНО")}");
            form.Conceal();
        }
        return failures == 0 ? 0 : 1;
    }
}
