namespace WardogsOverlay;

sealed class CellHit
{
    public RectI Cell;            // в координатах исходного кадра
    public int? Price;
    public Attachment? Att;
    public double Score;
    public string How = "";
    public bool Occluded;         // ячейку закрывает всплывающая подсказка игры
}

sealed class Recognition
{
    public bool Menu;
    public string Reason = "";
    public Weapon? Weapon;
    public string WeaponText = "";
    public List<CellHit> Cells = [];
    public string? SelectedSlot;  // слот выбранной (оранжевой) ячейки
    public int? Balance;          // деньги игрока справа сверху
    public bool SelectedEmpty;
    public RectI Roi;             // что отслеживать на экране, координаты исходного кадра
    public int AnchorX, AnchorY;  // где ставить плашку, координаты исходного кадра
    public double UiHeight;       // высота кадра, к которой привязан размер плашки
    public List<string> Trace = [];
    /// <summary>Часть ячеек закрыта подсказкой (навели мышь на обвес) — кадр не годится, чтобы что-то менять.</summary>
    public bool Occluded => Cells.Any(c => c.Occluded);

    public string Key => Weapon == null ? "" :
        Weapon.Slug + ":" + string.Join(",", Cells.Select(c => c.Att?.Slug ?? "?" + c.Price)) + "@" + SelectedSlot;
}

/// <summary>
/// Разбор кадра: меню «Модули» → активная пушка → ячейки с обвесами.
/// Кадр приводится к высоте 1080, чтобы пороги не зависели от разрешения.
/// </summary>
sealed class Recognizer(OcrService ocr)
{
    const int NormHeight = 1080;
    GameData data = null!;
    IconMatcher icons = null!;

    public GameData Data
    {
        get => data;
        set { data = value; icons = new IconMatcher(value); }
    }

    static bool IsOrange(int r, int g, int b) => r > 200 && g > 105 && g < 195 && b < 90 && r - g > 45;
    static bool IsGreen(int r, int g, int b) => g > 80 && g > r + 35 && g > b + 30;

    /// <summary>Дешёвая проверка до OCR: есть ли на экране оранжевая кнопка/полоса интерфейса.</summary>
    public static bool OrangeGate(Frame f)
    {
        // Порог низкий: когда выбрана пустая ячейка обвеса, оранжевыми остаются только её рамка и полоса.
        int step = Math.Max(2, f.H / 270), hits = 0, need = Math.Max(20, f.W / step * (f.H / step) / 4000);
        for (int y = 0; y < f.H; y += step)
            for (int x = 0; x < f.W; x += step)
            {
                var (r, g, b) = f.Rgb(x, y);
                if (IsOrange(r, g, b) && ++hits >= need) return true;
            }
        return false;
    }

    public Recognition Run(Frame raw)
    {
        var rec = new Recognition { UiHeight = raw.H };
        if (!OrangeGate(raw)) { rec.Reason = "нет оранжевых элементов"; return rec; }

        double k = raw.H >= 700 ? (double)NormHeight / raw.H : 1.0;
        if (Math.Abs(k - 1) < 0.02) k = 1;
        var f = k == 1 ? raw : raw.Resize((int)Math.Round(raw.W * k), (int)Math.Round(raw.H * k));
        RectI ToRaw(RectI r) => k == 1 ? r : r.Scale(1 / k).Intersect(raw.Bounds);

        var lines = ocr.Russian(f);
        var words = lines.SelectMany(l => l.Words).ToList();
        var mods = words.Where(w => TextUtil.IsModulesWord(w.Text)).ToList();
        rec.Trace.Add($"кадр {raw.W}x{raw.H}, k={k:0.###}, слов {words.Count}, «МОДУЛИ»: {string.Join(" ", mods.Select(m => m.Box))}");
        if (mods.Count < 2) { rec.Reason = "нет меню «Модули»"; return rec; }

        // Заголовок раздела — самое нижнее «МОДУЛИ»; кнопки ячеек — выше него.
        var header = mods.MaxBy(w => w.Box.Y)!;
        int h = Math.Max(8, header.Box.H);
        var buttons = mods.Where(w => w != header && w.Box.Bottom <= header.Box.Y + 2).ToList();
        var active = buttons.Select(b => (b, frac: OrangeFraction(f, b.Box.Inflate(b.Box.H / 2, b.Box.H / 3))))
                            .Where(t => t.frac > 0.2).OrderByDescending(t => t.frac).Select(t => t.b).FirstOrDefault();
        // Пока выбран обвес, кнопка пушки бывает серой — тогда активная та, у которой текст яркий
        // (у пустых ячеек экипировки он тусклый).
        active ??= buttons.Select(b => (b, light: TextBrightness(f, b.Box))).Where(t => t.light >= 110)
                          .OrderByDescending(t => t.light).Select(t => t.b).FirstOrDefault();
        if (active == null) { rec.Reason = "не вижу выбранную ячейку экипировки (кнопку «МОДУЛИ» пушки)"; return rec; }
        rec.Menu = true;
        rec.Trace.Add($"заголовок {header.Box}, активная кнопка {active.Box}");

        // --- Пушка: подпись в выбранной ячейке, над кнопками.
        int bh = active.Box.H;
        var nameZone = RectI.FromLTRB(active.Box.X - (int)(3.2 * active.Box.W), active.Box.Y - (int)(3.8 * bh),
                                      active.Box.Right + bh / 2, active.Box.Y - bh / 5).Intersect(f.Bounds);
        rec.Weapon = DisableNameOcr ? null : FindWeapon(f, lines, nameZone, rec);
        rec.Weapon ??= WeaponByIcon(f, active.Box, rec);

        // --- Ячейки обвесов: полоса под заголовком «МОДУЛИ».
        var strip = RectI.FromLTRB(header.Box.X - 2 * h, header.Box.Bottom + h / 5,
                                   header.Box.X + 62 * h, header.Box.Bottom + 9 * h).Intersect(f.Bounds);
        var normCells = new List<(RectI Content, CellHit Hit)>();
        var tags = GreenTags(f, strip, h);
        int fullTag = tags.Count > 0 ? tags.Max(t => t.H) : 0;
        foreach (var tag in tags)
        {
            var cell = CellFromTag(f, tag);
            if (cell == null) continue;
            var (content, barTop) = cell.Value;
            var hit = new CellHit { Cell = ToRaw(RectI.FromLTRB(content.X, content.Y, content.Right, barTop)) };
            normCells.Add((content, hit));
            if (tag.H < 0.8 * fullTag)
            {
                // Ценник ниже, чем у соседей: его срезала всплывающая подсказка, и картинка под ней
                // тоже закрыта. По такой ячейке ничего не решаем.
                hit.Occluded = true;
                hit.How = "закрыта подсказкой";
                rec.Cells.Add(hit);
                rec.Trace.Add($"ячейка {content}: закрыта (ценник {tag.H} при обычном {fullTag})");
                continue;
            }
            hit.Price = ReadPrice(f, words, tag, p => p == 0 || data.Attachments.Values.Any(a => a.Prices.Contains(p) && Sellable(a, rec.Weapon)));
            Identify(f, content, tag, hit, rec);
            rec.Cells.Add(hit);
        }
        rec.Cells.Sort((a, b) => a.Cell.X.CompareTo(b.Cell.X));
        if (rec.Weapon != null) FindSelectedSlot(f, strip, h, normCells, rec);

        // --- Куда ставить плашку и что отслеживать.
        // Подпись панели ячеек — ближайшая над кнопками (в английской игре «EQUIPMENT» есть ещё и во вкладке сверху).
        var yach = words.Where(w => TextUtil.IsSlotsWord(w.Text) && w.Box.Y < active.Box.Y)
                        .MaxBy(w => w.Box.Y);
        int rightmost = buttons.Count > 0 ? buttons.Max(b => b.Box.Right) : header.Box.Right;
        int ax = Math.Max(rightmost + 4 * h, header.Box.X + 58 * h);
        int ay = yach != null ? yach.Box.Y + 5 * h : active.Box.Y - 12 * h;
        ax = (int)(ax / k); ay = (int)(Math.Max(0, ay) / k);
        rec.AnchorX = ax; rec.AnchorY = ay;

        rec.Balance = ReadBalance(lines, f, rec);
        if (rec.Balance == null)
        {
            // Мелкий белый текст на тёмных плашках общий OCR пропускает. Читаем тесный кусок у правого
            // края крупно (×3): с большой пустой областью вокруг OCR не видит его вовсе.
            // Только две плашки с суммами, без соседней с иконкой — иначе OCR сбивается.
            var tight = RectI.FromLTRB(f.W - 307, 8, f.W - 115, 59).Intersect(f.Bounds);
            if (!tight.IsEmpty)
            {
                var crop = f.Crop(tight);
                string text = "";
                foreach (var scale in new[] { 3, 4 })
                {
                    // Строки OCR не обязательно идут слева направо: сортируем по X, баланс — самая правая сумма.
                    var ocrLines = ocr.Russian(crop.Resize(crop.W * scale, crop.H * scale))
                        .Where(l => l.Words.Count > 0).OrderBy(l => l.Words.Min(w => w.Box.X)).ToList();
                    text = string.Join(" ", ocrLines.Select(l => l.Text));
                    var amounts = ocrLines.SelectMany(l => System.Text.RegularExpressions.Regex.Matches(
                            TextUtil.DigitLike(l.Text.Replace('S', '$').Replace('§', '$')),
                            @"\$\s*(\d{1,3}(?:[\s\u00A0\u202F]\d{3})+|\d{1,9})(?!\w)"))
                        .Select(m => new string(m.Groups[1].Value.Where(char.IsAsciiDigit).ToArray()))
                        .Where(d => d.Length is > 0 and <= 9).Select(int.Parse).ToList();
                    if (amounts.Count == 0) continue;
                    rec.Balance = amounts[^1];
                    break;
                }
                rec.Trace.Add($"деньги (крупно): «{text}» → {rec.Balance?.ToString() ?? "?"}");
            }
        }

        var slotZone = RectI.FromLTRB(nameZone.X, nameZone.Y, active.Box.Right + bh, active.Box.Bottom + bh);
        rec.Roi = ToRaw(slotZone.Union(strip).Intersect(f.Bounds));
        return rec;
    }

    /// <summary>
    /// Выбранная ячейка (оранжевая полоса снизу) → какой слот сейчас меняют.
    /// Занятая — слот стоящего обвеса. Пустая — слот пушки, который ещё ничем не занят;
    /// если таких несколько — сравниваем силуэт в ячейке с иконками обвесов каждого слота.
    /// </summary>
    void FindSelectedSlot(Frame f, RectI strip, int h, List<(RectI Content, CellHit Hit)> cells, Recognition rec)
    {
        var bar = FindOrangeBar(f, strip, h);
        if (bar == null) { rec.Trace.Add("выбранная ячейка: оранжевой полосы нет"); return; }
        var b = bar.Value;

        var filled = cells.FirstOrDefault(c => c.Content.X < b.Right && b.X < c.Content.Right);
        if (filled.Hit != null)
        {
            rec.SelectedSlot = filled.Hit.Att?.SlotKey;
            rec.Trace.Add($"выбранная ячейка {b}: занята → {rec.SelectedSlot ?? "?"}");
            return;
        }

        var used = rec.Cells.Where(c => c.Att != null).Select(c => c.Att!.SlotKey).ToHashSet();
        var free = rec.Weapon!.Slots.Select(s => s == "Pistolgrip" ? "Grip" : s).Distinct()
                      .Where(s => !used.Contains(s) && Builds.ForSlot(data, rec.Weapon, s).Count > 0).ToList();
        rec.SelectedEmpty = true;
        if (free.Count == 1)
        {
            rec.SelectedSlot = free[0];
            rec.Trace.Add($"выбранная ячейка {b}: пустая, свободный слот один → {free[0]}");
            return;
        }
        if (free.Count == 0) return;

        int cw = b.W, chh = (int)(cw * 0.86);
        var content = RectI.FromLTRB(b.X, b.Y - chh, b.Right, b.Y).Intersect(f.Bounds);
        bool Allowed(int x, int y) => x >= 3 && y >= 3 && x < content.W - 3 && y < content.H - 2;
        var scores = free.Select(s => (s, score: icons.MatchEdges(f, content, Allowed, Builds.ForSlot(data, rec.Weapon, s))))
                         .OrderByDescending(t => t.score).ToList();
        rec.Trace.Add($"выбранная ячейка {b}: пустая, силуэт → " + string.Join(", ", scores.Select(t => $"{t.s} {t.score:0.00}")));
        if (scores[0].score - scores[1].score >= 0.03) rec.SelectedSlot = scores[0].s;
    }

    static RectI? FindOrangeBar(Frame f, RectI strip, int h)
    {
        for (int y = strip.Y; y < strip.Bottom - 3; y++)
        {
            int run = 0;
            for (int x = strip.X; x <= strip.Right; x++)
            {
                bool o = x < strip.Right && IsOrange(f.Rgb(x, y).R, f.Rgb(x, y).G, f.Rgb(x, y).B);
                if (o) { run++; continue; }
                if (run >= 3.5 * h && run <= 9 * h)
                {
                    int x0 = x - run, mid = x - run / 2;
                    var p1 = f.Rgb(mid, y + 2); var p2 = f.Rgb(x0 + 2, y + 2);
                    if (IsOrange(p1.R, p1.G, p1.B) && IsOrange(p2.R, p2.G, p2.B))
                        return new RectI(x0, y, run, 3);
                }
                run = 0;
            }
        }
        return null;
    }

    /// <summary>
    /// Деньги игрока — справа сверху на экране продавца: «$0 | $10 000». Берём самую правую сумму.
    /// OCR разбивает «$10 000» на слова «$10» и «000», поэтому склеиваем соседние группы из трёх цифр.
    /// </summary>
    internal static int? ReadBalance(List<OcrLineText> lines, Frame f, Recognition rec)
    {
        var zone = RectI.FromLTRB((int)(f.W * 0.68), 0, f.W, (int)(f.H * 0.09));
        var amounts = new List<(int X, int? Value, string Text)>();
        foreach (var line in lines)
        {
            var ws = line.Words.Where(w => zone.Contains(w.Box.CenterX, w.Box.CenterY)).OrderBy(w => w.Box.X).ToList();
            for (int i = 0; i < ws.Count; i++)
            {
                var t = TextUtil.DigitLike(ws[i].Text.Replace('S', '$').Replace('§', '$'));
                if (!t.StartsWith('$')) continue;
                int x = ws[i].Box.X, right = ws[i].Box.Right, gap = (int)(ws[i].Box.H * 1.2);
                // «$» отдельным словом — цифры в следующем.
                if (t == "$" && i + 1 < ws.Count && ws[i + 1].Box.X - right <= gap)
                {
                    i++;
                    t += TextUtil.DigitLike(ws[i].Text);
                    right = ws[i].Box.Right;
                }
                var head = t[1..];
                bool ok = head.Length > 0 && head.All(char.IsAsciiDigit);
                var digits = ok ? head : "";
                var text = t;
                for (int j = i + 1; ok && j < ws.Count; j++)
                {
                    if (ws[j].Box.X - right > gap) break;
                    var next = TextUtil.DigitLike(ws[j].Text);
                    if (next.StartsWith('$') || !next.Any(char.IsAsciiDigit)) break;
                    // Рядом кусок числа, но не «три цифры» — OCR ошибся: лучше не знать баланс, чем записать в 1000 раз меньше.
                    if (next.Length != 3 || !next.All(char.IsAsciiDigit) || head.Length > 3) { ok = false; text += " " + next; break; }
                    digits += next; text += " " + next; right = ws[j].Box.Right; i = j;
                }
                amounts.Add((x, ok && digits.Length <= 9 && int.TryParse(digits, out var v) ? v : null, text));
            }
        }
        if (amounts.Count == 0) return null;
        var best = amounts.MaxBy(a => a.X);
        // Самая правая сумма (баланс) не разобралась — не подставляем вместо неё левую ($0 корзины).
        if (best.Value == null)
        {
            rec.Trace.Add($"деньги: {string.Join(" | ", amounts.Select(a => a.Text))} → не разобрал");
            return null;
        }
        rec.Trace.Add($"деньги: {string.Join(" | ", amounts.Select(a => a.Text))} → {best.Value}");
        return best.Value;
    }

    /// <summary>Только для проверки: не читать название, узнавать пушку лишь по картинке.</summary>
    public static bool DisableNameOcr { get; set; }

    Weapon? FindWeapon(Frame f, List<OcrLineText> lines, RectI zone, Recognition rec)
    {
        var texts = new List<string>();
        foreach (var line in lines)
        {
            var inside = line.Words.Where(w => zone.Contains(w.Box.CenterX, w.Box.CenterY)).ToList();
            if (inside.Count == 0) continue;
            texts.Add(string.Join(" ", inside.Select(w => w.Text)));
            texts.AddRange(inside.Select(w => w.Text));
        }
        // Латинское название надёжнее читает английский OCR. Короткие слова («M4», «MP5») OCR
        // пропускает, если они мелкие и у края — поэтому крупно (×3) и с полями из фона.
        var big = Padded(f.Crop(zone), 3, 24);
        foreach (var line in ocr.English(big).Concat(ocr.Russian(big)))
        {
            texts.Add(line.Text);
            texts.AddRange(line.Words.Select(w => w.Text));
        }

        Weapon? best = null;
        double bestSim = 0;
        int bestLen = 0;
        string bestText = "";
        foreach (var t in texts.Distinct())
        {
            // Сравниваем и с латинским названием (GALIL, TCX-SPR), и с русским (АК-74, СВД, ПКМ).
            var lat = TextUtil.Latinize(t);
            var cyr = TextUtil.NormRu(t);
            foreach (var w in data.Weapons)
            {
                foreach (var (ocrKey, key) in new[] { (lat, w.Key), (cyr, w.KeyRu) })
                {
                    if (ocrKey.Length < 2 || key.Length == 0) continue;
                    double s = TextUtil.Similarity(ocrKey, key);
                    if (s > bestSim || (s == bestSim && best != null && key.Length > bestLen))
                    {
                        bestSim = s; best = w; bestText = t; bestLen = key.Length;
                    }
                }
            }
        }
        rec.WeaponText = bestText;
        rec.Trace.Add($"подписи в ячейке: [{string.Join(" | ", texts.Distinct())}] → {best?.Name} ({bestSim:0.00})");
        // Для коротких названий одна ошибка OCR — уже другая пушка (M4 / M14), поэтому строже.
        double need = best != null && bestLen <= 3 ? 0.99 : 0.6;
        return bestSim >= need ? best : null;
    }

    /// <summary>Увеличить картинку и обложить полями цвета фона — так OCR видит короткие слова.</summary>
    static Frame Padded(Frame src, int scale, int pad)
    {
        var up = src.Resize(src.W * scale, src.H * scale);
        var dst = new Frame(up.W + 2 * pad, up.H + 2 * pad);
        var (r, g, b) = src.Rgb(0, 0);
        for (int i = 0; i < dst.Px.Length; i += 4)
        {
            dst.Px[i] = (byte)b; dst.Px[i + 1] = (byte)g; dst.Px[i + 2] = (byte)r; dst.Px[i + 3] = 255;
        }
        for (int y = 0; y < up.H; y++)
            Buffer.BlockCopy(up.Px, y * up.W * 4, dst.Px, ((y + pad) * dst.W + pad) * 4, up.W * 4);
        return dst;
    }

    /// <summary>
    /// Запасной путь, если название не прочиталось: сравнить картинку пушки в выбранной ячейке
    /// экипировки с иконками пушек. Ячейка находится над кнопками «БОЕПРИПАСЫ / МОДУЛИ».
    /// </summary>
    Weapon? WeaponByIcon(Frame f, RectI button, Recognition rec)
    {
        int bw = button.W, bh = button.H;
        var slot = RectI.FromLTRB(button.X - (int)(1.8 * bw), button.Y - (int)(9.6 * bh),
                                  button.Right + (int)(0.45 * bw), button.Y - (int)(1.1 * bh)).Intersect(f.Bounds);
        if (slot.W < 40 || slot.H < 20) return null;
        int sw = slot.W, sh = slot.H;
        // Не сравниваем ценник (справа сверху), мини-иконку магазина (справа снизу) и подпись (снизу).
        bool Allowed(int x, int y) =>
            x >= 3 && y >= 3 && x < sw - 3 && y < sh - 3 &&
            !(x > sw * 0.68 && y < sh * 0.27) && !(x > sw * 0.82 && y > sh * 0.55) && y < sh * 0.82;

        var pool = data.Weapons.Where(w => w.Slots.Length > 0 || w.Attr.Count > 0).ToList();
        var (best, score, second) = icons.Match(f, slot, Allowed, pool, w => w.Icon, IconMatcher.WeaponFits);
        rec.Trace.Add($"пушка по картинке {slot}: {best?.Name ?? "?"} ({score:0.00}, следующая {second:0.00})");
        if (DisableNameOcr)
            rec.Trace.Add("  " + string.Join(", ", pool
                .Select(w => (w.Name, s: icons.Match(f, slot, Allowed, [w], x => x.Icon, IconMatcher.WeaponFits).Score))
                .OrderByDescending(t => t.s).Take(6).Select(t => $"{t.Name} {t.s:0.00}")));
        if (best == null || score < 0.5 || score - second < 0.03) { lastIconWeapon = null; return null; }
        // Похожие силуэты (семейство M4) дают отрыв в сотые доли: такой ответ берём, только если
        // он повторился на двух кадрах подряд. Ошибка с пушкой испортила бы и все обвесы.
        bool sure = score - second >= 0.08 || lastIconWeapon == best;
        lastIconWeapon = best;
        if (!sure) rec.Trace.Add("  отрыв мал — жду подтверждения на следующем кадре");
        return sure ? best : null;
    }

    Weapon? lastIconWeapon;

    /// <summary>Яркость букв: 95-й перцентиль яркости в рамке слова.</summary>
    static int TextBrightness(Frame f, RectI r)
    {
        r = r.Intersect(f.Bounds);
        if (r.IsEmpty) return 0;
        var hist = new int[256];
        int n = 0;
        for (int y = r.Y; y < r.Bottom; y++)
            for (int x = r.X; x < r.Right; x++)
            {
                var (cr, cg, cb) = f.Rgb(x, y);
                hist[(cr * 77 + cg * 150 + cb * 29) >> 8]++;
                n++;
            }
        for (int v = 255, acc = 0; v >= 0; v--)
            if ((acc += hist[v]) >= n * 0.05) return v;
        return 0;
    }

    static double OrangeFraction(Frame f, RectI r)
    {
        r = r.Intersect(f.Bounds);
        if (r.IsEmpty) return 0;
        int hits = 0, n = 0;
        for (int y = r.Y; y < r.Bottom; y += 2)
            for (int x = r.X; x < r.Right; x += 2)
            {
                var (cr, cg, cb) = f.Rgb(x, y);
                if (IsOrange(cr, cg, cb)) hits++;
                n++;
            }
        return n == 0 ? 0 : (double)hits / n;
    }

    /// <summary>Зелёные ценники в полосе ячеек (связные области зелёного цвета).</summary>
    static List<RectI> GreenTags(Frame f, RectI strip, int h)
    {
        var result = new List<RectI>();
        if (strip.IsEmpty) return result;
        var seen = new bool[strip.W * strip.H];
        var stack = new Stack<(int, int)>();
        for (int y = 0; y < strip.H; y++)
        {
            for (int x = 0; x < strip.W; x++)
            {
                if (seen[y * strip.W + x]) continue;
                seen[y * strip.W + x] = true;
                var (r, g, b) = f.Rgb(strip.X + x, strip.Y + y);
                if (!IsGreen(r, g, b)) continue;

                int minX = x, maxX = x, minY = y, maxY = y, count = 0;
                stack.Push((x, y));
                while (stack.Count > 0)
                {
                    var (cx, cy) = stack.Pop();
                    count++;
                    minX = Math.Min(minX, cx); maxX = Math.Max(maxX, cx);
                    minY = Math.Min(minY, cy); maxY = Math.Max(maxY, cy);
                    foreach (var (nx, ny) in new[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) })
                    {
                        if (nx < 0 || ny < 0 || nx >= strip.W || ny >= strip.H || seen[ny * strip.W + nx]) continue;
                        seen[ny * strip.W + nx] = true;
                        var (r2, g2, b2) = f.Rgb(strip.X + nx, strip.Y + ny);
                        if (IsGreen(r2, g2, b2)) stack.Push((nx, ny));
                    }
                }
                int bw = maxX - minX + 1, bh = maxY - minY + 1;
                bool shape = bh >= 0.9 * h && bh <= 2.8 * h && bw >= 1.3 * bh && bw <= 5 * bh;
                if (shape && count >= bw * bh * 0.45)
                    result.Add(new RectI(strip.X + minX, strip.Y + minY, bw, bh));
            }
        }
        return result;
    }

    /// <summary>
    /// По ценнику находит ячейку: ценник прижат к правому верхнему углу, снизу у ячейки
    /// сплошная полоса (серая или оранжевая у выбранной). Возвращает область картинки и верх полосы.
    /// </summary>
    static (RectI Content, int BarTop)? CellFromTag(Frame f, RectI tag)
    {
        int th = tag.H;
        int right = tag.Right, top = tag.Y;
        int xa = tag.X - Math.Max(2, th / 6), xb = tag.X - tag.W / 2, xc = tag.Right - 3;
        int barTop = -1;
        for (int y = tag.Bottom + th; y < Math.Min(f.H - 3, tag.Y + 6 * th); y++)
        {
            if (IsBarRow(f, y, xa, xb, xc) && IsBarRow(f, y + 1, xa, xb, xc) && IsBarRow(f, y + 2, xa, xb, xc))
            {
                barTop = y;
                break;
            }
        }

        int left;
        if (barTop > 0)
        {
            var bar = f.Rgb(xa, barTop + 2);
            left = xa;
            while (left > 0 && ColorDist(f.Rgb(left - 1, barTop + 2), bar) < 30) left--;
        }
        else
        {
            barTop = top + (int)Math.Round(3.2 * th);
            left = right - (int)Math.Round(3.7 * th);
        }

        int w = right - left, ch = barTop - top;
        if (w < 2.5 * th || w > 6 * th || ch < 2 * th || ch > 5 * th)
        {
            barTop = top + (int)Math.Round(3.2 * th);
            left = right - (int)Math.Round(3.7 * th);
        }
        var content = RectI.FromLTRB(left, top, right, barTop).Intersect(f.Bounds);
        return content.IsEmpty ? null : (content, barTop);
    }

    static bool IsBarRow(Frame f, int y, int xa, int xb, int xc)
    {
        if (xa < 0 || xb < 0 || xc >= f.W || y >= f.H) return false;
        var a = f.Rgb(xa, y); var b = f.Rgb(xb, y); var c = f.Rgb(xc, y);
        if (ColorDist(a, b) > 18 || ColorDist(a, c) > 18) return false;
        int mx = Math.Max(a.R, Math.Max(a.G, a.B)), mn = Math.Min(a.R, Math.Min(a.G, a.B));
        bool gray = mx - mn < 20 && mx >= 60 && mx <= 175;
        return gray || IsOrange(a.R, a.G, a.B);
    }

    static int ColorDist((int R, int G, int B) a, (int R, int G, int B) b) =>
        Math.Max(Math.Abs(a.R - b.R), Math.Max(Math.Abs(a.G - b.G), Math.Abs(a.B - b.B)));

    /// <summary>Цена с ценника: сначала из общего OCR, а если она ни к одному обвесу не подходит — перечитать крупно.</summary>
    int? ReadPrice(Frame f, List<OcrWord> words, RectI tag, Func<int, bool> plausible)
    {
        var inside = words.Where(w => tag.Inflate(2, 2).Contains(w.Box.CenterX, w.Box.CenterY)).Select(w => w.Text);
        var first = TextUtil.ParsePrice(string.Concat(inside));
        if (first is int p && plausible(p)) return p;
        var again = ReadPriceCrop(f, tag);
        return again is int q && plausible(q) ? q : first ?? again;
    }

    int? ReadPriceCrop(Frame f, RectI tag)
    {
        var crop = f.Crop(tag.Inflate(2, 2));
        crop = crop.Resize(crop.W * 3, crop.H * 3);
        var text = string.Concat(ocr.English(crop).Select(l => l.Text)).TrimStart();
        // Знак доллара OCR читает как «S» — его отрезаем до замены S→5, иначе «$350» станет 5350.
        if (text.Length > 0 && text[0] is '$' or 'S' or '§') text = text[1..];
        return TextUtil.ParsePrice(TextUtil.DigitLike(text).Replace('S', '5'));
    }

    /// <summary>Продавец показывает только то, что продаётся и явно подходит к пушке.</summary>
    static bool Sellable(Attachment a, Weapon? w) =>
        a.InVendor && (w == null || (a.Fits?.Contains(w.Slug) ?? false));

    void Identify(Frame f, RectI content, RectI tag, CellHit hit, Recognition rec)
    {
        // Только то, что продаётся и подходит к пушке: заводские детали без цены и без списка
        // совместимости продавец не показывает.
        var pool = data.Attachments.Values.Where(a => Sellable(a, rec.Weapon)).ToList();
        if (pool.Count == 0) pool = data.Attachments.Values.Where(a => a.InVendor).ToList();
        // $0 — стрельбище, там всё бесплатно: цена ничего не говорит, узнаём по картинке.
        var byPrice = hit.Price is int p and > 0 ? pool.Where(a => a.Prices.Contains(p)).ToList() : [];

        // Маска: не сравниваем ценник, значок количества слева сверху и рамку.
        var tagLocal = tag.Inflate(2, 2).Offset(-content.X, -content.Y);
        int badgeW = (int)(content.W * 0.3), badgeH = (int)(content.H * 0.28);
        bool Allowed(int x, int y) =>
            x >= 2 && y >= 2 && x < content.W - 2 && y < content.H - 1 &&
            !tagLocal.Contains(x, y) && !(x < badgeW && y < badgeH);

        var candidates = byPrice.Count > 0 ? byPrice : pool;
        if (byPrice.Count == 1)
        {
            var (_, sc, _) = icons.Match(f, content, Allowed, byPrice);
            hit.Score = sc;
            // Цена совпала, а картинка совсем другая — скорее всего ячейку закрыло или цена прочиталась не та.
            if (sc >= 0.3 || double.IsNegativeInfinity(sc))
            {
                hit.Att = byPrice[0];
                hit.How = "цена";
            }
            else hit.How = $"цена есть, картинка не та ({sc:0.00})";
        }
        else
        {
            var (best, score, second) = icons.Match(f, content, Allowed, candidates);
            hit.Score = score;
            bool sure = byPrice.Count > 0 ? best != null && score >= 0.3 : best != null && score >= 0.55 && score - second >= 0.03;
            if (sure)
            {
                hit.Att = best;
                hit.How = byPrice.Count > 0 ? $"цена+картинка из {byPrice.Count}" : $"картинка из {candidates.Count}";
            }
            else hit.How = $"не уверен (картинка {score:0.00}/{second:0.00})";
        }
        rec.Trace.Add($"ячейка {content}: цена {hit.Price?.ToString() ?? "?"} → {hit.Att?.Name ?? "?"} [{hit.How}, сходство {hit.Score:0.00}]");
    }
}
