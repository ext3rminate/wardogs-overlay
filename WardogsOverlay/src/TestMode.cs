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
