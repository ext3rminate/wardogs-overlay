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
        if (files.Length >= 2 && files[0] == "--guide-plaque") return GuidePlaque(files[1], files.Contains("--en"));
        if (files.Length >= 2 && files[0] == "--purchase")
        {
            // Что куплено за N долларов; с картинкой — как в игре: текст экрана и картинки над ценниками.
            if (!GameData.IsDownloaded || GameData.Load().NeedsUpdate) GameData.DownloadAsync(Console.WriteLine).GetAwaiter().GetResult();
            var d = GameData.Load();
            int spent = int.Parse(files[1]);
            var rcg = new Recognizer(new OcrService()) { Data = d };
            Recognition r = new();
            Frame? fr = null;
            if (files.Length >= 3 && files[2] != "x")
            {
                using var b = new Bitmap(files[2]);
                fr = Frame.FromBitmap(b);
                r = rcg.Run(fr);
                Console.WriteLine($"  ценники: {string.Join(", ", r.PriceTags.Select(t => "$" + t.Value))}");
            }
            var w = files.Length >= 4 ? d.Weapons.FirstOrDefault(x => x.Slug == files[3]) : null;
            DbItem? ByIcon(List<DbItem> cands)
            {
                if (fr == null) return null;
                foreach (var tag in r.PriceTags.Where(t => t.Value == spent))
                {
                    var (item, score, second) = rcg.ItemAt(fr, tag.Box, cands);
                    Console.WriteLine($"  картинка над ${tag.Value} {tag.Box}: {item?.Name} ({score:0.00}, следующая {second:0.00}) из {cands.Count}");
                    if (item != null && score >= 0.45 && (cands.Count == 1 || score - second >= 0.04)) return item;
                }
                return null;
            }
            var (what, kind) = PurchaseMatcher.Attribute(d, spent, w, [], null, [], r.ScreenLines, ByIcon);
            Console.WriteLine($"−${spent}: [{kind}] {what}");
            return 0;
        }
        if (files.Length >= 2 && files[0] == "--corner")
        {
            // Баланс в углу экрана — как его раз в 2 с читает оверлей (в бою и у продавца).
            var rcg = new Recognizer(new OcrService()) { Data = GameData.Load() };
            foreach (var file in files.Skip(1).Where(x => !x.StartsWith("--")))
            {
                using var b = new Bitmap(file);
                var fr = Frame.FromBitmap(b);
                var corner = RectI.FromLTRB((int)(fr.W * 0.70), 0, fr.W, Math.Max(24, (int)(fr.H * 0.07)));
                var sw = Stopwatch.StartNew();
                var (bal, cart) = rcg.ReadCorner(fr.Crop(corner), fr.W, fr.H);
                Console.WriteLine($"{Path.GetFileName(file)}: баланс {bal?.ToString() ?? "—"}, закуп {cart?.ToString() ?? "—"} ({sw.ElapsedMilliseconds} мс)");
                if (files.Contains("--words"))
                {
                    var c = fr.Crop(corner); double k = 1080.0 / fr.H * 2.5;
                    var big = c.Resize((int)(c.W * k), (int)(c.H * k));
                    foreach (var eng in new[] { "en", "ru" })
                        Console.WriteLine($"  {eng}: " + string.Join("  ", (eng == "en" ? new OcrService().English(big) : new OcrService().Russian(big)).SelectMany(l => l.Words).Select(w => $"«{w.Text}»{w.Box}")));
                }
            }
            return 0;
        }
        if (files.Length >= 2 && files[0] == "--clean-economy")
        {
            // Как прошлые сессии выглядят после очистки ложных показаний (файл не меняется).
            var sessions = System.Text.Json.JsonSerializer.Deserialize<List<EconomySession>>(File.ReadAllText(files[1])) ?? [];
            sessions = sessions.Where(x => !files.Contains("--details") || x == sessions[^1]).ToList();
            foreach (var se in sessions)
            {
                var before = $"{se.Points.Count} точек, +{se.Earned} / −{se.Spent}, трат {se.Purchases.Count}";
                Economy.Clean(se);
                Console.WriteLine($"{se.Start:dd.MM HH:mm}: {before}  →  {se.Points.Count} точек, +{se.Earned} / −{se.Spent}, итог {se.Net:+#;−#;0}, трат {se.Purchases.Count}");
                if (files.Contains("--details"))
                {
                    for (int i = 1; i < se.Points.Count; i++)
                        if (se.Points[i].Balance < se.Points[i - 1].Balance - 2000)
                            Console.WriteLine($"   {se.Points[i].Time:HH:mm:ss} {se.Points[i - 1].Balance} → {se.Points[i].Balance} (−{se.Points[i - 1].Balance - se.Points[i].Balance})");
                    foreach (var pu in se.Purchases) Console.WriteLine($"   трата {pu.Time:HH:mm:ss} −{pu.Amount} {pu.What}");
                }
            }
            return 0;
        }
        if (files.Length >= 1 && files[0] == "--where") { Console.WriteLine($"data: {AppPaths.Root} (portable: {AppPaths.Portable})"); return 0; }
        if (files.Length >= 2 && files[0] == "--test-welcome")
        {
            L.Current = files.Contains("--en") ? Lang.En : Lang.Ru;
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Ui.Theme.Load();
            using var wf = new WelcomeForm(new Settings { Language = L.Current }, () => { }) { StartPosition = FormStartPosition.Manual, Location = new Point(-4000, -4000) };
            wf.Show();
            var until2 = Environment.TickCount64 + 500;
            while (Environment.TickCount64 < until2) { Application.DoEvents(); Thread.Sleep(10); }
            using var bmp = new Bitmap(wf.Width, wf.Height);
            wf.DrawToBitmap(bmp, new Rectangle(0, 0, wf.Width, wf.Height));
            bmp.Save(files[1]);
            Console.WriteLine($"→ {files[1]} {wf.Width}x{wf.Height}");
            return 0;
        }
        if (files.Length >= 2 && files[0] == "--test-db") return TestDatabase(files[1], files.Contains("--en"));
        if (files.Length >= 2 && files[0] == "--test-main")
        {
            // Главное окно без запуска: рисуем в картинку, не показывая (поиск не стартует).
            L.Current = files.Contains("--en") ? Lang.En : Lang.Ru;
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Ui.Theme.Load();
            var st = new Settings { Language = L.Current };
            using var ov = new OverlayForm(st);
            using var mf = new MainForm(st, ov) { Preview = true, StartPosition = FormStartPosition.Manual, Location = files.Contains("--onscreen") ? new Point(Screen.PrimaryScreen!.Bounds.X + 40, Screen.PrimaryScreen.Bounds.Y + 40) : new Point(-4000, -4000), ShowInTaskbar = false };
            mf.Show();
            if (files.Contains("--tall")) mf.Height = 4200; // всё окно целиком, без прокрутки
            if (files.Contains("--bottom")) { Application.DoEvents(); mf.PreviewScrollToEnd(); }
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
    /// <summary>
    /// Плашка для скриншота в руководстве: Galil с обвесами, строки «выстрелы до убийства» и «цена жизни»,
    /// заработок — на демонстрационных цифрах.
    /// </summary>
    static int GuidePlaque(string outPath, bool en)
    {
        L.Current = en ? Lang.En : Lang.Ru;
        Ui.Theme.Load();
        var data = GameData.Load();
        var w = data.Weapons.First(x => x.Slug == "galil");
        string[] names = ["Compact T-2 Red Dot", "AFG Angled Foregrip", "Hexagon Brake", "Galil 35 RND Magazine"];
        var cells = names.Select(n => data.Attachments.Values.FirstOrDefault(a => a.Name == n && a.Fits?.Contains(w.Slug) == true))
                         .OfType<Attachment>().Select(a => new CellHit { Att = a }).ToList();
        var s = new Settings { ShowSlotOptions = false, ShowKill = true, ShowLifeCost = true, TargetArmour = 2, TargetDistance = 50, MyArmour = 2 };
        var model = OverlayModel.Build(w, cells, null, data, null, s);
        model.Money = L.T("сессия +$22 700 · $567/мин", "session +$22 700 · $567/min");
        int life = model.LifeCost ?? 0;
        model.Life = $"≈ {L.Money(life)}" + L.T($" · запас {24800 / Math.Max(1, life)} · окуп. {(int)Math.Ceiling(life / 567.0)} мин",
                                                $" · {24800 / Math.Max(1, life)} rebuys · payback {(int)Math.Ceiling(life / 567.0)} min");
        float u = 600f / OverlayRenderer.Measure(model, 1f).Width;
        var size = OverlayRenderer.Measure(model, u);
        using var bmp = new Bitmap(size.Width, size.Height);
        using (var g = Graphics.FromImage(bmp)) OverlayRenderer.Draw(g, model, u);
        bmp.Save(outPath);
        Console.WriteLine($"→ {outPath} {size.Width}x{size.Height}, атт: {cells.Count}");
        return 0;
    }

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

        Check("«$304 0Б0» — это $304 060 (Б как 6)", Balance("$0", "|", "$304", "0Б0") == 304060, Balance("$0", "|", "$304", "0Б0")?.ToString() ?? "null");
        var ecoTest = new EconomySession();
        var tj = DateTime.Now;
        Check("одиночная сумма не принимается на экране без меню", Recognizer.ReadBalance([new OcrLineText("$280 787", [new OcrWord("$280", new RectI(1700, 30, 48, 20)), new OcrWord("787", new RectI(1756, 30, 36, 20))])],
              new Frame(1920, 1080), new Recognition(), requirePair: true) == null);
        var am = Recognizer.AmountsIn("-$23 810  $280 787");
        Check("угол экрана: «−$23 810  $280 787» — баланс справа, минус слева не закуп",
              am.Count == 2 && am[0] == (23810, true) && am[1] == (280787, false), string.Join(" | ", am));
        Check("плашка денег в бою «$12,500»", Recognizer.AmountsIn("$12,500") is [(12500, false)]);
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

        // Надёжность: чёрный кадр, отчёт о проблеме.
        var black = new Frame(1920, 1080);
        Check("чёрный кадр распознаётся как чёрный", black.IsBlank());
        var lit = new Frame(1920, 1080);
        for (int i = 0; i < lit.Px.Length; i += 4) { lit.Px[i] = 40; lit.Px[i + 1] = 50; lit.Px[i + 2] = 60; lit.Px[i + 3] = 255; }
        Check("обычный кадр — не чёрный", !lit.IsBlank());
        var report = ProblemReport.Create("test");
        using (var z = System.IO.Compression.ZipFile.OpenRead(report))
            Check("отчёт о проблеме: архив с system.txt", z.Entries.Any(e => e.Name == "system.txt"), string.Join(", ", z.Entries.Select(e => e.FullName)));
        File.Delete(report);

        // Два числа в углу вперемешку (баланс и заработок за сессию) и выброс неверной цифры — убираются,
        // настоящая трата остаётся.
        var mixed = new EconomySession();
        var t0m = DateTime.Now.AddMinutes(-30);
        int[] seq = [429004, 55668, 429164, 55828, 429324, 431993, 334742, 432153, 409643, 409803, 410000, 410160];
        for (int i = 0; i < seq.Length; i++) mixed.Points.Add(new MoneyPoint(t0m.AddSeconds(20 * i), seq[i]));
        Economy.Clean(mixed);
        Check("второй счётчик и выброс убраны, трата −$22 510 осталась",
              !mixed.Points.Any(pp => pp.Balance is 55668 or 55828 or 334742) && mixed.Spent == 22510,
              $"+{mixed.Earned} / −{mixed.Spent}: {string.Join(", ", mixed.Points.Select(pp => pp.Balance))}");

        // Неверное первое показание ($304 вместо $304 060) не должно отравлять сессию.
        var ecoFix = new Economy();
        ecoFix.NewSession();
        ecoFix.Add(304, 1);
        ecoFix.Add(304060, 1);
        Check("$304 → $304 060: потерянная группа цифр заменяется", ecoFix.Current.Points.Count == 1 && ecoFix.Current.Points[^1].Balance == 304060,
              string.Join(", ", ecoFix.Current.Points.Select(pp => pp.Balance)));
        ecoFix.Add(304, 1);
        Check("и обратно $304 после $304 060 не принимается", ecoFix.Current.Points[^1].Balance == 304060);

        // Экономика: стрельбище $10 000 → продавец $324 935 → $376 326 (реальная сессия 29.10).
        var s = new EconomySession();
        var t0 = DateTime.Now;
        s.Points.AddRange([new(t0, 10000), new(t0.AddMinutes(7), 324935), new(t0.AddMinutes(93), 376326)]);
        Check("скачок со стрельбища не заработок", s.Earned == 51391 && s.Net == 51391, $"{s.Earned} / {s.Net}");
        Check("скачок в разы — IsJump", Economy.IsJump(10000, 324935) && !Economy.IsJump(324935, 376326));

        // Модель и цифры ниже сверены с MetaForge — проверяем на данных как есть, без поправок из заметок к патчу.
        var data = GameData.Load(corrections: false);
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

        // Сборка под бюджет учитывает уровни ролей: при уровнях 1 нет ничего, что открывается позже.
        var low = L.Roles.ToDictionary(r => r, _ => 1);
        var lowList = Builds.BestLoadouts(data, new Builds.LoadoutRequest { Budget = 15000, ArmourLevel = 0, Sight = Builds.SightBand.None, Levels = low }, 10);
        var allList = Builds.BestLoadouts(data, new Builds.LoadoutRequest { Budget = 15000, ArmourLevel = 0, Sight = Builds.SightBand.None }, 10);
        bool lowOk = lowList.All(l => Builds.Unlocked(l.Weapon.Unlock, low) && l.Atts.All(a => Builds.Unlocked(a.Unlock, low)));
        Check("сборка при уровнях 1 — только открытое", lowOk && lowList.Select(l => l.Weapon.Slug).ToHashSet().IsSubsetOf(allList.Select(l => l.Weapon.Slug).Concat(lowList.Select(l => l.Weapon.Slug))),
              $"{string.Join(", ", lowList.Select(l => l.Weapon.Name))} | без уровней: {string.Join(", ", allList.Take(5).Select(l => l.Weapon.Name))}");
        var m4 = data.Weapons.FirstOrDefault(w => w.Slug == "m4");
        if (m4?.Unlock is { Level: > 1 } mu)
            Check($"M4 ({L.UnlockText(mu)}) не предлагается, пока роль ниже", lowList.All(l => l.Weapon.Slug != "m4"));

        // Уровни не заданы — «Сборка под бюджет» считает все роли 1-м уровнем (как показывает «Прогресс»).
        var fresh = new Settings();
        var reqFresh = new Builds.LoadoutRequest { Budget = 6000, ArmourLevel = 1, Sight = Builds.SightBand.Red, Levels = fresh.EffectiveLevels() };
        var freshList = Builds.BestLoadouts(data, reqFresh, 6);
        Check("уровни не заданы: M4 не предлагается", freshList.All(l => l.Weapon.Slug != "m4"), string.Join(", ", freshList.Select(l => l.Weapon.Name)));
        var vet = new Settings { RoleLevels = new() { ["Infantry"] = 28, ["Career"] = 30 } };
        var vetList = Builds.BestLoadouts(data, new Builds.LoadoutRequest { Budget = 6000, ArmourLevel = 1, Sight = Builds.SightBand.Red, Levels = vet.EffectiveLevels() }, 10);
        Check("штурмовик 28: M4 доступна", vetList.Any(l => l.Weapon.Slug == "m4"), string.Join(", ", vetList.Select(l => l.Weapon.Name)));
        Check("плашка не режет подбор, пока уровни не заданы", fresh.UnlockLevels() == null && vet.UnlockLevels() != null);

        // Покупки: −$30 000 при магазине AK74 на 60 патронов на экране — это его открытие (реальный случай).
        var mag60 = data.Attachments.Values.FirstOrDefault(a => a.Name == "AK74 60 RND Magazine");
        if (mag60 != null)
        {
            var (what, kind) = PurchaseMatcher.Attribute(data, 30000, ak, [mag60], "galil", [], []);
            Check("покупка −$30 000 = открытие магазина AK74 на 60", kind == "unlock" && what.Contains(L.Name(mag60)), $"{kind}: {what}");
            var (w2, k2) = PurchaseMatcher.Attribute(data, mag60.ShopPrice, ak, [mag60], "ak74", [], []);
            Check("покупка по цене обвеса", k2 == "attachments" && w2 == L.Name(mag60), $"{k2}: {w2}");
            var (w3, k3) = PurchaseMatcher.Attribute(data, 77, ak, [mag60], "ak74", [mag60], []);
            Check("непонятная сумма — патроны, броня или снаряжение", k3 == "other", $"{k3}: {w3}");
            var (pAmmo, _) = PurchaseMatcher.Attribute(data, 30, ak, [], "ak74", [], []);
            Check("мелкая покупка — патроны к текущей пушке", pAmmo.Contains("5.45"), pAmmo);
            var smokeItem = data.Items.FirstOrDefault(i => i.Name == "Smoke" && i.Price > 0);
            if (smokeItem != null)
            {
                var (pIcon, _) = PurchaseMatcher.Attribute(data, smokeItem.Price!.Value, null, [], null, [], [], c => c.FirstOrDefault(i => i.Name == "Smoke"));
                Check("покупка по картинке над ценником", pIcon == L.Name(smokeItem), pIcon);
            }
        }

        var canted = data.Attachments.Values.FirstOrDefault(a => a.SlotKey == "CantedSight" && a.Zoom != null);
        var scope = data.Attachments.Values.Where(a => a.SlotKey == "Sight" && a.Zoom >= 4 && a.Fits?.Count > 0).FirstOrDefault();
        var w4 = scope == null ? null : data.Weapons.FirstOrDefault(w => scope.Fits!.Contains(w.Slug));
        if (canted != null && scope != null && w4 != null)
        {
            var st = Stats.Compute(w4, [scope, canted]);
            var zoom = st.Value(Stats.Def("AdsZoomMultiplier"));
            Check("боковой прицел не перебивает зум основного", Math.Abs(zoom - scope.Zoom!.Value) < 1e-6, $"{zoom} при {scope.Zoom}");
        }

        SelfCheckSeason3(data, (n, ok, d) => Check(n, ok, d ?? ""));

        var many = data.Weapons.First(w => w.Slots.Length > 2);
        var sw = Stopwatch.StartNew();
        var best = Builds.Best(data, many, [], Priority.Balance, 9_999_999);
        Check("подбор с бюджетом 9 999 999 быстрый", sw.ElapsedMilliseconds < 500, $"{sw.ElapsedMilliseconds} мс, {best?.Atts.Count ?? 0} обвесов");

        Console.WriteLine(fails == 0 ? "всё ок" : $"провалов: {fails}");
        return fails == 0 ? 0 : 1;
    }

    /// <summary>Сезон 3: поправки из официальных заметок, мульти-трековые открытия, режим Limited, скидки продавца.</summary>
    static void SelfCheckSeason3(GameData raw, Action<string, bool, string?> check)
    {
        void Check(string name, bool ok, string? detail = null) => check(name, ok, detail);

        // Открытие в нескольких ветках — в любом из видов, в котором его может выложить MetaForge.
        Unlock? U(string json) { using var doc = System.Text.Json.JsonDocument.Parse(json); return GameData.ReadUnlock(doc.RootElement); }
        var u1 = U("""{"unlock":{"role":"Career","level":30,"cash":50000,"tracks":[{"role":"Infantry","level":12}]}}""");
        Check("открытие: ветки внутри объекта", u1 is { Role: "Career", Level: 30, Cash: 50000, Also.Length: 1 } && u1.Also[0] is { Role: "Infantry", Level: 12 },
              u1 == null ? "null" : L.UnlockText(u1));
        var u2 = U("""{"unlock":[{"role":"Driver","level":35,"cash":250000},{"role":"Career","level":200}]}""");
        Check("открытие: список веток", u2 is { Role: "Driver", Level: 35, Also.Length: 1 }, u2 == null ? "null" : L.UnlockText(u2));
        var u3 = U("""{"unlock":{"level":0}}""");
        Check("открытие «сразу» читается как раньше", u3 is { Level: 0, Also.Length: 0 });
        var lv = L.Roles.ToDictionary(r => r, _ => 1);
        lv["Infantry"] = 12;
        Check("хватает любой ветки открытия", u1 != null && Builds.Unlocked(u1, lv) && !Builds.Unlocked(u1 with { Also = [] }, lv));
        Check("ближайшая ветка — та, где меньше уровней", u1 != null && Builds.NearestTrack(u1, lv).Role == "Infantry");

        if (!Season3.IsBefore(raw))
        {
            Console.WriteLine("  (база уже Сезона 3 — поправки из заметок не применяются)");
            Check("на новой базе поправки не применяются", !GameData.Load().FromPatchNotes);
        }
        else
        {
            var s3 = GameData.Load();
            DbItem Item(GameData d, string slug) => d.Items.First(i => i.Slug == slug);
            Check("база Сезона 2 → поправки Сезона 3", s3.FromPatchNotes && s3.PatchTitle == Season3.Title && s3.PatchBuild == Season3.Build && s3.Patch.Count > 10,
                  $"{s3.PatchTitle} {s3.PatchBuild}, изменений {s3.Patch.Count}");
            var molotov = Item(s3, "molotov-cocktail").Unlock;
            Check("коктейль Молотова: штурмовик 8, $12 500", molotov is { Level: 8, Cash: 12500 }, L.UnlockText(molotov));
            Check("парашюты: $1 250 и $2 250", Item(s3, "basic-parachute").Price == 1250 && Item(s3, "sport-parachute").Price == 2250);
            Check("Havoc $26 000, AH-6M $10 480", Item(s3, "havoc").Price == 26000 && Item(s3, "ah-6m-miniguns").Price == 10480);
            Check("Verba — поддержка 8, большой молот — 14",
                  s3.Weapons.First(w => w.Slug == "9k333-verba").Unlock?.Level == 8 && Item(s3, "9k333-verba").Unlock?.Level == 8 && Item(s3, "large-hammer").Unlock?.Level == 14);
            Check("ИК-дальномер снят с продажи", !Item(s3, "infrared-range-finder").InVendor && s3.Patch.Any(p => p.Removed && p.Name == "Infrared Range Finder"));
            Check("стрелы лука на 50% быстрее", s3.Weapons.First(w => w.Slug == "compound-bow").Velocity == 240);
            var t72 = Item(s3, "t-72b3").Unlock!;
            Check("T-72B3: водитель 35 или карьера 200", t72.Level == 35 &&
                  Builds.Unlocked(t72, new() { ["Driver"] = 35 }) && Builds.Unlocked(t72, new() { ["Career"] = 200, ["Driver"] = 1 }) &&
                  !Builds.Unlocked(t72, new() { ["Career"] = 150, ["Driver"] = 34 }), L.UnlockText(t72));
            Check("броня помечена: в S3 открывается и в ветках ролей", Item(s3, "level-2-armor").Unlock?.Note != null, L.UnlockText(Item(s3, "level-2-armor").Unlock));

            var ak3 = s3.Weapons.First(w => w.Slug == "ak74");
            var akRaw = raw.Weapons.First(w => w.Slug == "ak74");
            double Hit(GameData d, Weapon w, string zone) => Combat.PerHit(d, w, zone, RoundLoad.FMJ, 0, 0, 0, out _);
            double up = Hit(s3, ak3, "torso_upper") / Hit(raw, akRaw, "torso_upper"), mid = Hit(s3, ak3, "torso_middle") / Hit(raw, akRaw, "torso_middle");
            Check("штурмовые: верх торса +13%, середина +3%", Math.Abs(up - 1.13) < 0.005 && Math.Abs(mid - 1.03) < 0.005, $"{up:0.000} / {mid:0.000}");
            var lmg = s3.Weapons.FirstOrDefault(w => w.Sub == "Light Machine Gun");
            Check("пулемёты по торсу — как штурмовые", lmg == null || lmg.Hit.GetValueOrDefault("torso_upper") == ak3.Hit.GetValueOrDefault("torso_upper"));
            Check("исходные данные не тронуты", akRaw.Hit["torso_upper"] < 1.0 && raw.Items.First(i => i.Slug == "basic-parachute").Price == 500);
            var svd = s3.Weapons.FirstOrDefault(w => w.Slug == "svd");
            Check("DMR: предупреждение, что чисел Сезона 3 ещё нет", svd == null || Season3.PendingNote(s3, svd) != null);

            // Поправки не накладываются дважды: если MetaForge обновит цены раньше журнала патчей, «было» уже не совпадёт.
            var twice = GameData.Load();
            twice.PatchBuild = "CL-516586";
            int before = twice.Patch.Count;
            Season3.Apply(twice);
            Check("повторное применение ничего не меняет", twice.Weapons.First(w => w.Slug == "ak74").Hit["torso_upper"] == 1.1 &&
                  twice.Items.First(i => i.Slug == "basic-parachute").Price == 1250 && twice.Patch.Count == before);
        }

        // Режим Limited: нет HP и AP, брони и шлемов 3–4-го уровня, ударной техники.
        var ak = raw.Weapons.First(w => w.Slug == "ak74");
        var kl = OverlayModel.KillLine(raw, ak, 4, 50, limited: true);
        Check("Limited: плашка считает броню 2 и только FMJ", kl is { } k && k.Where.Contains('2') && k.Where.Contains("Limited") && !k.What.Contains("AP") && !k.What.Contains("HP"),
              kl is { } x ? $"{x.Where}: {x.What}" : "null");
        Check("Limited: броня 3 и AP не продаются, броня 2 и Kodiak — да",
              Season3.LimitedExcludes(raw.Items.First(i => i.Slug == "level-3-armor")) && !Season3.LimitedExcludes(raw.Items.First(i => i.Slug == "level-2-armor")) &&
              raw.Items.Where(i => i.Tab == "ammunition" && i.RoundType?.Contains("(AP)") == true).All(Season3.LimitedExcludes) &&
              Season3.LimitedExcludes(raw.Items.First(i => i.Slug == "kodiak-m249")) && !Season3.LimitedExcludes(raw.Items.First(i => i.Slug == "kodiak")));

        // Скидка продавца: бронежилет 2-го уровня за 80% цены, название на экране.
        var vest = raw.Items.First(i => i.Slug == "level-2-armor");
        int paid = (int)(vest.Price!.Value * 0.8);
        var (what, _) = PurchaseMatcher.Attribute(raw, paid, null, [], null, [], [vest.Name]);
        Check("покупка со скидкой узнаётся по названию на экране", what.Contains(L.Name(vest)) && what.Contains("20"), what);
        var (blind, _) = PurchaseMatcher.Attribute(raw, paid, null, [], null, [], []);
        Check("без подтверждения на экране скидку не угадываем", !blind.Contains("20%"), blind);
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
        s.Purchases.Add(new Purchase(t.AddMinutes(9), 3400, "AK-74", "weapon"));
        s.Purchases.Add(new Purchase(t.AddMinutes(28), 820, L.T("Вертикальная передняя рукоятка TDG", "TDG Vertical Foregrip"), "attachments"));
        s.Purchases.Add(new Purchase(t.AddMinutes(28), 1260, L.T("Глушитель PBS-4", "PBS-4 Suppressor"), "attachments"));
        s.Purchases.Add(new Purchase(t.AddMinutes(29), 600, L.T("патроны, броня или снаряжение", "ammo, armour or gear"), "other"));
        var testSettings = new Settings
        {
            GoalSlug = data.Items.FirstOrDefault(i => i.Unlock is { Cash: > 100_000 })?.Slug,
            RoleLevels = new() { ["Career"] = 12, ["Recon"] = 33, ["Infantry"] = 5 },
        };

        Ui.Theme.Load();
        using var form = new DatabaseForm(data, eco, testSettings) { StartPosition = FormStartPosition.Manual, Location = new Point(-5000, -5000), ShowInTaskbar = false };
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
        // Окно сравнения пушек (для руководства).
        var cmpW = new[] { "ak74", "m4", "galil" }.Select(s => data.Weapons.FirstOrDefault(w => w.Slug == s)).OfType<Weapon>().ToList();
        if (form.CompareWeapons(cmpW) is { } cf)
        {
            var until2 = Environment.TickCount64 + 700;
            while (Environment.TickCount64 < until2) { Application.DoEvents(); Thread.Sleep(10); }
            using var cbmp = new Bitmap(cf.Width, cf.Height);
            cf.DrawToBitmap(cbmp, new Rectangle(0, 0, cf.Width, cf.Height));
            var cpath = Path.Combine(outDir, $"db_compare{(en ? "_en" : "")}.png");
            cbmp.Save(cpath);
            Console.WriteLine($"→ {cpath}");
            cf.Close();
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
