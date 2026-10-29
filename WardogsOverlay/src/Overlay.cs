using System.Drawing.Drawing2D;
using System.Drawing.Text;
using static WardogsOverlay.Native;

namespace WardogsOverlay;

enum ItemState { Normal, Added, Removed, Unknown }
sealed record PlaqueItem(string Slot, string Name, ItemState State);

sealed class OverlayModel
{
    public required Weapon Weapon { get; init; }
    public required BuildStats Now { get; init; }
    public BuildStats? Before { get; init; }
    public required List<PlaqueItem> Items { get; init; }
    public required List<Attachment> Current { get; init; }
    public SlotOptions? Options { get; init; }
    public BestBuild? Best { get; init; }
    public bool MoveMode { get; set; }
    /// <summary>Только главные статы: отдача, разброс, прицеливание, зум.</summary>
    public bool Compact { get; init; }
    /// <summary>Строка экономики: «сессия +$12 400 · $830/мин».</summary>
    public string? Money { get; set; }

    public static OverlayModel Build(Weapon w, List<CellHit> cells, List<Attachment>? snapshot,
                                     GameData? data = null, string? selectedSlot = null, Settings? s = null)
    {
        var atts = cells.Where(c => c.Att != null).Select(c => c.Att!).ToList();
        var items = new List<PlaqueItem>();
        foreach (var c in cells)
        {
            if (c.Att == null)
            {
                items.Add(new PlaqueItem("?", c.Price is int p and > 0 ? L.T($"не узнал обвес (${p})", $"unknown attachment (${p})") : L.T("не узнал обвес", "unknown attachment"), ItemState.Unknown));
                continue;
            }
            bool added = snapshot != null && snapshot.All(x => x.Slug != c.Att.Slug);
            var name = L.Name(c.Att);
            if (c.Att.StateNote != null) name += L.T(" (сложены)", $" ({c.Att.StateNote.ToLowerInvariant()})");
            items.Add(new PlaqueItem(Stats.SlotName(c.Att), name, added ? ItemState.Added : ItemState.Normal));
        }
        if (snapshot != null)
            foreach (var x in snapshot.Where(x => atts.All(a => a.Slug != x.Slug)))
                items.Add(new PlaqueItem(Stats.SlotName(x), L.Name(x), ItemState.Removed));

        SlotOptions? options = null;
        BestBuild? best = null;
        if (data != null && s != null)
        {
            if (s.ShowSlotOptions && selectedSlot != null)
                options = Builds.Options(data, w, atts, selectedSlot, Math.Clamp(s.SlotOptionsMax, 3, 15));
            if (s.BestPriority is Priority p)
                best = Builds.Best(data, w, atts, p, s.BestBudget > 0 ? s.BestBudget : null);
        }

        return new OverlayModel
        {
            Weapon = w,
            Now = Stats.Compute(w, atts),
            Before = snapshot != null ? Stats.Compute(w, snapshot) : null,
            Items = items,
            Current = atts,
            Options = options,
            Best = best,
            Compact = s?.CompactMode ?? false,
        };
    }
}

/// <summary>
/// Рисует плашку по макету редизайна. Размеры заданы для 1080p и масштабируются коэффициентом u.
/// Шрифты — встроенные Onest и JetBrains Mono, в пикселях (масштаб задаёт ScaleTransform).
/// </summary>
static class OverlayRenderer
{
    public static readonly Color Bg = Color.FromArgb(19, 21, 24);
    static readonly Color Label = Color.FromArgb(215, 218, 221);
    static readonly Color ChipBg = Color.FromArgb(29, 32, 36);
    static readonly Color CurrentRow = Color.FromArgb(34, 37, 42);

    const float Pad = 18;
    const float HeaderH = 18 + 34 + 10 + 24 + 14, ItemH = 22, RowH = 28, OptH = 30, BestItemH = 24, BestRowH = 24;
    public const float Radius = 18;

    static float Width(OverlayModel m) => m.Options != null || m.Best != null ? 420 : 400;

    static List<StatDef> Rows(OverlayModel m) =>
        Stats.Defs.Where(d => m.Now.Changed(d) || (m.Before?.Changed(d) ?? false))
                  .Where(d => !m.Compact || d.Kind == StatKind.Zoom || Stats.Weight(d, Priority.Balance) >= 1)
                  .ToList();

    static List<StatDef> BestRows(OverlayModel m) => m.Best == null ? [] :
        Stats.Defs.Where(d => d.Better != Better.Neutral && Math.Abs(Diff(d, m.Now, m.Best.Stats)) >= 0.005)
                  .OrderByDescending(d => Stats.Weight(d, m.Best.Priority) * Math.Abs(Diff(d, m.Now, m.Best.Stats)))
                  .Take(5).OrderBy(d => Array.IndexOf(Stats.Defs, d)).ToList();

    static double Diff(StatDef d, BuildStats a, BuildStats b) =>
        d.Kind == StatKind.Seconds ? b.Value(d) - a.Value(d) : b.Delta(d) - a.Delta(d);

    static IEnumerable<Attachment> BestItems(OverlayModel m) =>
        m.Best == null ? [] : m.Best.Atts.Where(a => a.SlotKey is not ("Sight" or "Magazine" or "CantedSight"));

    static IEnumerable<Attachment> BestRemoved(OverlayModel m) =>
        m.Best == null ? [] : m.Current.Where(c => c.SlotKey is not ("Sight" or "Magazine" or "CantedSight")
                                                   && m.Best.Atts.All(b => b.SlotKey != c.SlotKey));

    static float StatsHeight(OverlayModel m) =>
        12 + (m.Before != null ? 24 : 0) + Math.Max(1, Rows(m).Count) * RowH + 12;

    public static Size Measure(OverlayModel m, float u)
    {
        float h = HeaderH + 4 + m.Items.Count * ItemH + 14 + 1 + StatsHeight(m);
        if (m.Options != null) h += 1 + 12 + 24 + 20 + m.Options.Rows.Count * OptH + 10;
        if (m.Best != null)
            h += 1 + 12 + 24 + Math.Max(1, BestItems(m).Count() + BestRemoved(m).Count()) * BestItemH + 8 + Math.Max(1, BestRows(m).Count) * BestRowH + 10;
        h += 1 + 10 + (m.Money != null ? 22 : 0) + 18 + 14;
        if (m.MoveMode) h += 22;
        return new Size((int)Math.Ceiling(Width(m) * u), (int)Math.Ceiling(h * u));
    }

    static Font Px(FontFamily fam, float px, FontStyle st = FontStyle.Regular) => new(fam, px, st, GraphicsUnit.Pixel);

    public static void Draw(Graphics g, OverlayModel m, float u)
    {
        Ui.Theme.Load();
        Ui.Theme.Smooth(g);
        var size = Measure(m, u);
        g.Clear(Bg);
        g.ScaleTransform(u, u);
        float width = Width(m), height = size.Height / u;
        var T = Ui.Theme.Text; var Muted = Ui.Theme.Muted; var Faint = Ui.Theme.Faint;
        var Accent = Ui.Theme.Accent; var Good = Ui.Theme.Good; var Bad = Ui.Theme.Bad;

        using var fTitle = Px(Ui.Theme.Ui, 28, FontStyle.Bold);
        using var fPill = Px(Ui.Theme.UiSemi, 11, Ui.Theme.UiSemi == Ui.Theme.Ui ? FontStyle.Bold : FontStyle.Regular);
        using var fChip = Px(Ui.Theme.MonoMedium, 12);
        using var fSlot = Px(Ui.Theme.UiSemi, 10, Ui.Theme.UiSemi == Ui.Theme.Ui ? FontStyle.Bold : FontStyle.Regular);
        using var fItem = Px(Ui.Theme.Ui, 14);
        using var fItemStrike = Px(Ui.Theme.Ui, 14, FontStyle.Strikeout);
        using var fRow = Px(Ui.Theme.Ui, 14);
        using var fVal = Px(Ui.Theme.Mono, 13, FontStyle.Bold);
        using var fOpt = Px(Ui.Theme.Ui, 13);
        using var fOptMono = Px(Ui.Theme.MonoMedium, 12);
        using var fHead = Px(Ui.Theme.Ui, 11, FontStyle.Bold);
        using var fCol = Px(Ui.Theme.Mono, 10, FontStyle.Bold);
        using var fFoot = Px(Ui.Theme.Ui, 11);
        using var fFootB = Px(Ui.Theme.UiSemi, 11, Ui.Theme.UiSemi == Ui.Theme.Ui ? FontStyle.Bold : FontStyle.Regular);
        using var fMoney = Px(Ui.Theme.MonoMedium, 12);

        void Txt(string s, Font f, Color c, float x, float y, float w, float h, Ui.Theme.Align a = Ui.Theme.Align.Left) =>
            Ui.Theme.Draw(g, s, f, c, new RectangleF(x, y, w, h), a);
        void Sep(float y) { using var p = new Pen(Ui.Theme.Line, 1); g.DrawLine(p, 0, y, width, y); }

        // рамка карточки (окно скруглено регионом, рамку рисуем сами)
        Ui.Theme.StrokeRound(g, Color.FromArgb(42, 46, 51), new RectangleF(0, 0, width, height), Radius);

        float x0 = Pad, inner = width - 2 * Pad, y = 18;
        var w = m.Weapon;

        // --- шапка
        Txt(L.Name(w).ToUpperInvariant(), fTitle, T, x0, y, inner - 120, 34);
        var cls = Stats.ClassName(w.Sub);
        float pillW = Ui.Theme.Measure(g, cls, fPill).Width + 20;
        var pill = new RectangleF(width - Pad - pillW, y + 7, pillW, 22);
        Ui.Theme.FillRound(g, Ui.Theme.Mix(Accent, Bg, 0.14), pill, 11);
        Txt(cls, fPill, Accent, pill.X, pill.Y, pill.Width, pill.Height, Ui.Theme.Align.Center);
        y += 34 + 10;

        var chips = new List<string>();
        if (w.Damage is int dmg) chips.Add(L.T($"Урон {dmg}", $"Dmg {dmg}"));
        if (w.Rpm is int rpm) chips.Add(L.T($"{rpm} в/мин", $"{rpm} rpm"));
        if (w.Range is int rng) chips.Add(L.T($"{rng} м", $"{rng} m"));
        chips.Add("ADS " + Stats.Sec(m.Now.Base(Stats.Def("AimTime"))));
        float cx = x0;
        foreach (var c in chips)
        {
            float cw = Ui.Theme.Measure(g, c, fChip).Width + 16;
            Ui.Theme.FillRound(g, ChipBg, new RectangleF(cx, y, cw, 24), 7);
            Txt(c, fChip, Ui.Theme.TextSoft, cx, y, cw, 24, Ui.Theme.Align.Center);
            cx += cw + 6;
        }
        y += 24 + 14;

        // --- обвесы
        y += 4;
        foreach (var it in m.Items)
        {
            var (font, color, prefix) = it.State switch
            {
                ItemState.Added => (fItem, Accent, "→ "),
                ItemState.Removed => (fItemStrike, Ui.Theme.Ghost, ""),
                ItemState.Unknown => (fItem, Ui.Theme.Ghost, ""),
                _ => (fItem, T, ""),
            };
            Txt(it.Slot, fSlot, it.State == ItemState.Removed ? Ui.Theme.Ghost : Faint, x0, y, 74, ItemH);
            Txt(prefix + it.Name, font, color, x0 + 84, y, inner - 84, ItemH);
            y += ItemH;
        }
        y += 14;
        Sep(y); y += 1;

        // --- статы
        y += 12;
        var rows = Rows(m);
        if (m.Before != null)
        {
            Txt(L.T("С ЗАПОМНЕННОЙ СБОРКОЙ", "VS SAVED BUILD"), fHead, Accent, x0, y, inner, 20);
            Txt(L.T("БЫЛО", "WAS"), fCol, Faint, x0, y, inner - 112, 20, Ui.Theme.Align.Right);
            Txt(L.T("СЕЙЧАС", "NOW"), fCol, Faint, x0, y, inner, 20, Ui.Theme.Align.Right);
            y += 24;
        }
        if (rows.Count == 0) { Txt(L.T("Обвесы не меняют статы", "Attachments don't change stats"), fRow, Muted, x0, y, inner, RowH); y += RowH; }
        foreach (var d in rows)
        {
            if (m.Before != null) CompareRow(d, y);
            else StatRow(d, y);
            y += RowH;
        }
        y += 12;

        // --- что поставить
        if (m.Options is SlotOptions o)
        {
            Sep(y); y += 1 + 12;
            Txt(o.SlotLabel + L.T(" · ЧТО ПОСТАВИТЬ", " · OPTIONS"), fHead, Accent, x0, y, inner, 20);
            Txt(L.T("итог сборки", "build total"), fFoot, Faint, x0, y, inner, 20, Ui.Theme.Align.Right);
            y += 24;
            float colW = 44, priceW = 48, capW = o.ShowCapacity ? 34 : 0;
            float statsX = width - Pad - o.Columns.Count * colW;
            float priceX = statsX - capW - priceW;
            Txt(L.T("ОБВЕС", "ITEM"), fCol, Faint, x0, y, priceX - x0, 18);
            Txt(L.T("ЦЕНА", "PRICE"), fCol, Faint, priceX, y, priceW, 18, Ui.Theme.Align.Right);
            if (o.ShowCapacity) Txt(L.T("ПАТР", "RNDS"), fCol, Faint, priceX + priceW, y, capW, 18, Ui.Theme.Align.Right);
            for (int i = 0; i < o.Columns.Count; i++)
                Txt(o.Columns[i].Short.ToUpperInvariant(), fCol, Faint, statsX + i * colW, y, colW, 18, Ui.Theme.Align.Right);
            y += 20;
            var cur = o.Rows.FirstOrDefault(r => r.Current);
            foreach (var r in o.Rows)
            {
                if (r.Current) Ui.Theme.FillRound(g, CurrentRow, new RectangleF(x0 - 6, y, inner + 12, OptH), 9);
                var name = r.Att != null ? L.ShortName(r.Att) : L.T("без обвеса", "none");
                Txt((r.Current ? "● " : "") + name, fOpt, r.Current ? Accent : T, x0, y, priceX - x0 - 4, OptH);
                Txt(r.Att != null ? $"${r.Att.ShopPrice}" : "—", fOptMono, Muted, priceX, y, priceW, OptH, Ui.Theme.Align.Right);
                if (o.ShowCapacity) Txt(r.Att?.Capacity?.ToString() ?? "—", fOptMono, Muted, priceX + priceW, y, capW, OptH, Ui.Theme.Align.Right);
                for (int i = 0; i < o.Columns.Count; i++)
                {
                    var d = o.Columns[i];
                    var color = T;
                    if (cur != null && !r.Current)
                    {
                        int good = Stats.Goodness(d, Diff(d, cur.Stats, r.Stats));
                        color = good > 0 ? Good : good < 0 ? Bad : Muted;
                    }
                    Txt(Fmt(d, r.Stats, true), fOptMono, color, statsX + i * colW, y, colW, OptH, Ui.Theme.Align.Right);
                }
                y += OptH;
            }
            y += 10;
        }

        // --- подбор
        if (m.Best is BestBuild b)
        {
            Sep(y); y += 1 + 12;
            Txt(L.T("ПОДБОР · ", "BEST · ") + Builds.PriorityName(b.Priority), fHead, Accent, x0, y, inner, 20);
            var cost = L.T($"обвесы ${b.Cost}", $"attachments ${b.Cost}") + (b.Budget is int bud ? L.T($" / ${bud}", $" / ${bud}") : "");
            Txt(cost, fFoot, Faint, x0, y, inner, 20, Ui.Theme.Align.Right);
            y += 24;
            int n = 0;
            foreach (var a in BestItems(m))
            {
                bool same = m.Current.Any(c => c.Slug == a.Slug);
                Txt(Stats.SlotName(a), fSlot, Faint, x0, y, 74, BestItemH);
                Txt((same ? "" : "→ ") + L.ShortName(a), fItem, same ? T : Accent, x0 + 84, y, inner - 140, BestItemH);
                Txt($"${a.ShopPrice}", fOptMono, Muted, x0, y, inner, BestItemH, Ui.Theme.Align.Right);
                y += BestItemH; n++;
            }
            foreach (var c in BestRemoved(m))
            {
                Txt(Stats.SlotName(c), fSlot, Ui.Theme.Ghost, x0, y, 74, BestItemH);
                Txt(L.ShortName(c) + L.T(" — снять", " — remove"), fItemStrike, Ui.Theme.Ghost, x0 + 84, y, inner - 84, BestItemH);
                y += BestItemH; n++;
            }
            if (n == 0) { Txt(L.T("Текущая сборка уже лучшая", "Current build is already the best"), fItem, Muted, x0, y, inner, BestItemH); y += BestItemH; }
            y += 8;
            var br = BestRows(m);
            if (br.Count == 0) { Txt(L.T("Статы те же", "Same stats"), fOpt, Muted, x0, y, inner, BestRowH); y += BestRowH; }
            foreach (var d in br)
            {
                double diff = Diff(d, m.Now, b.Stats);
                int good = Stats.Goodness(d, diff);
                var color = good > 0 ? Good : good < 0 ? Bad : Muted;
                Txt(d.Label, fOpt, Label, x0, y, inner - 150, BestRowH);
                Txt(Fmt(d, m.Now), fOptMono, Faint, x0, y, inner - 96, BestRowH, Ui.Theme.Align.Right);
                Txt("→", fOptMono, Ui.Theme.Ghost, width - Pad - 92, y, 16, BestRowH);
                Txt(Fmt(d, b.Stats) + (good > 0 ? " ▲" : good < 0 ? " ▼" : ""), fOptMono, color, x0, y, inner, BestRowH, Ui.Theme.Align.Right);
                y += BestRowH;
            }
            y += 10;
        }

        // --- подвал
        Sep(y); y += 1 + 10;
        if (m.Money != null)
        {
            Txt(L.T("сессия", "session"), fMoney, Muted, x0, y, inner, 20);
            var money = m.Money.Replace(L.T("сессия ", "session "), "");
            Txt(money, fMoney, money.StartsWith('−') ? Ui.Theme.BadText : Good, x0, y, inner, 20, Ui.Theme.Align.Right);
            y += 22;
        }
        float hx = x0;
        foreach (var (k, what) in new[] { ("S", m.Before != null ? L.T("сброс", "reset") : L.T("сравнить", "compare")), ("B", L.T("подбор", "best")), ("H", L.T("варианты", "options")) })
        {
            Txt(k, fFootB, Ui.Theme.TextSoft, hx, y, 12, 18);
            float ww = Ui.Theme.Measure(g, what, fFoot).Width + 4;
            Txt(what, fFoot, Faint, hx + 11, y, ww + 4, 18);
            hx += 11 + ww + 14;
        }
        Txt("Ctrl+Shift · metaforge.app", fFoot, Faint, x0, y, inner, 18, Ui.Theme.Align.Right);
        y += 18;
        if (m.MoveMode)
        {
            Txt(L.T("Перетащи плашку мышью · Ctrl+Shift+M — готово", "Drag the panel · Ctrl+Shift+M — done"), fFoot, Accent, x0, y + 6, inner, 16);
            using var pen = new Pen(Accent, 2) { DashStyle = DashStyle.Dash };
            using var path = Ui.Theme.Round(new RectangleF(1, 1, width - 2, height - 2), Radius);
            g.DrawPath(pen, path);
        }

        void StatRow(StatDef d, float ry)
        {
            var now = m.Now;
            double delta = now.Delta(d);
            int good = Stats.Goodness(d, delta);
            var color = good > 0 ? Good : good < 0 ? Bad : Muted;
            string label = d.Label, value;
            double rel;
            switch (d.Kind)
            {
                case StatKind.Zoom:
                    Txt(label, fRow, T, x0, ry, inner - 60, RowH);
                    Txt(Stats.Zoom(now.Value(d)), fVal, T, x0, ry, inner, RowH, Ui.Theme.Align.Right);
                    return;
                case StatKind.Seconds:
                    label = L.T("Прицел. ", "ADS ") + $"{Stats.SecShort(now.Base(d))}→{Stats.Sec(now.Value(d))}";
                    value = Stats.SecDelta(delta); rel = delta / now.Base(d); break;
                default:
                    value = Stats.Pct(delta); rel = delta; break;
            }
            Txt(label, fRow, Label, x0, ry, inner - 180, RowH);
            float bw = 96, bx = width - Pad - 68 - 10 - bw, cy = ry + RowH / 2, mid = bx + bw / 2;
            Ui.Theme.FillRound(g, Ui.Theme.Track, new RectangleF(bx, cy - 3, bw, 6), 3);
            using (var tick = new SolidBrush(Color.FromArgb(74, 80, 87))) g.FillRectangle(tick, mid - 1, cy - 5, 2, 10);
            float len = (float)(Math.Min(1, Math.Abs(rel) / 0.5) * bw / 2);
            len = Math.Max(len, 2);
            Ui.Theme.FillRound(g, color, new RectangleF(rel < 0 ? mid - len : mid, cy - 3, len, 6), 3);
            Txt(value, fVal, color, x0, ry, inner, RowH, Ui.Theme.Align.Right);
        }

        void CompareRow(StatDef d, float ry)
        {
            var before = m.Before!;
            double diff = Diff(d, before, m.Now);
            int good = Stats.Goodness(d, diff);
            var color = good > 0 ? Good : good < 0 ? Bad : Muted;
            bool same = Math.Abs(diff) < 0.005;
            Txt(d.Label, fRow, same ? Muted : Label, x0, ry, inner - 170, RowH);
            Txt(Fmt(d, before), fOptMono, Faint, x0, ry, inner - 112, RowH, Ui.Theme.Align.Right);
            Txt("→", fOptMono, Ui.Theme.Ghost, width - Pad - 106, ry, 16, RowH);
            Txt(Fmt(d, m.Now) + (good > 0 ? " ▲" : good < 0 ? " ▼" : ""), fVal, same ? Muted : color, x0, ry, inner, RowH, Ui.Theme.Align.Right);
        }
    }

    static string Fmt(StatDef d, BuildStats s, bool compact = false) => d.Kind switch
    {
        StatKind.Zoom => s.Changed(d) ? Stats.Zoom(s.Value(d)) : "—",
        StatKind.Seconds => compact ? Stats.SecShort(s.Value(d)) : Stats.Sec(s.Value(d)),
        _ => Stats.Pct(s.Delta(d)),
    };
}
/// <summary>
/// Окно плашки: поверх всех, прозрачно для мыши, не забирает фокус у игры, нет в панели задач.
/// Горячие клавиши — RegisterHotKey (без хуков клавиатуры) и только пока игра на переднем плане.
/// </summary>
sealed class OverlayForm : Form
{
    public enum Hotkey { Toggle = 1, Snapshot, Debug, Best, Options, Move }

    static readonly (Hotkey Id, char Key, bool NeedsPlaque)[] Keys =
    [
        (Hotkey.Toggle, 'O', false), (Hotkey.Debug, 'D', false),
        (Hotkey.Snapshot, 'S', true), (Hotkey.Best, 'B', true), (Hotkey.Options, 'H', true), (Hotkey.Move, 'M', true),
    ];

    readonly Settings settings;
    readonly HashSet<Hotkey> registered = [];
    OverlayModel? model;
    float scale = 1;
    bool gameForeground, moveMode;
    Point dragStart;
    Rectangle dragBounds;
    bool dragging;

    public event Action<Hotkey>? HotkeyPressed;
    /// <summary>Плашку перетащили: на сколько пикселей сдвинуть относительно места по умолчанию.</summary>
    public event Action<int, int>? Moved;

    public OverlayForm(Settings settings)
    {
        this.settings = settings;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = OverlayRenderer.Bg;
        DoubleBuffered = true;
        AutoScaleMode = AutoScaleMode.None;
        Text = "Wardogs overlay";
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TOPMOST;
            return cp;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyOpacity();
        ApplyCaptureAffinity();
    }

    /// <summary>Плашка не попадает в снимки экрана: ни в наши (не мешает распознаванию), ни в запись.</summary>
    public void ApplyCaptureAffinity() =>
        SetWindowDisplayAffinity(Handle, settings.HideFromCapture ? WDA_EXCLUDEFROMCAPTURE : 0);

    /// <summary>
    /// Прозрачность задаём сами и всегда. Окно со стилем WS_EX_LAYERED (нужен, чтобы клики проходили
    /// в игру) без SetLayeredWindowAttributes невидимо, а WinForms при Opacity = 100% её не вызывает.
    /// </summary>
    public void ApplyOpacity()
    {
        byte alpha = (byte)Math.Round(Math.Clamp(settings.Opacity, 0.3, 1.0) * 255);
        SetLayeredWindowAttributes(Handle, 0, alpha, LWA_ALPHA);
    }

    public void SetGameForeground(bool on)
    {
        gameForeground = on;
        UpdateHotkeys();
    }

    void UpdateHotkeys()
    {
        foreach (var (id, key, needsPlaque) in Keys)
        {
            bool want = gameForeground && (!needsPlaque || Visible);
            if (want == registered.Contains(id)) continue;
            if (want && RegisterHotKey(Handle, (int)id, MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT, key)) registered.Add(id);
            else if (!want) { UnregisterHotKey(Handle, (int)id); registered.Remove(id); }
        }
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == WM_HOTKEY)
        {
            var id = (Hotkey)(int)m.WParam;
            if (id == Hotkey.Move) SetMoveMode(!moveMode);
            else HotkeyPressed?.Invoke(id);
        }
        base.WndProc(ref m);
    }

    /// <summary>Режим перетаскивания: плашка начинает ловить мышь, остальное время она «прозрачна» для кликов.</summary>
    void SetMoveMode(bool on)
    {
        moveMode = on;
        const int GWL_EXSTYLE = -20;
        long ex = GetWindowLongPtr(Handle, GWL_EXSTYLE).ToInt64();
        ex = on ? ex & ~WS_EX_TRANSPARENT : ex | WS_EX_TRANSPARENT;
        SetWindowLongPtr(Handle, GWL_EXSTYLE, new IntPtr(ex));
        if (model != null)
        {
            model.MoveMode = on;
            var size = OverlayRenderer.Measure(model, scale);
            Size = size;
            using var path = Ui.Theme.Round(new RectangleF(0, 0, size.Width, size.Height), OverlayRenderer.Radius * scale);
            Region = new Region(path);
        }
        Cursor = on ? Cursors.SizeAll : Cursors.Default;
        Invalidate();
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        if (!moveMode || e.Button != MouseButtons.Left) return;
        dragging = true;
        dragStart = Cursor.Position;
        dragBounds = Bounds;
        Capture = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        if (!dragging) return;
        var p = Cursor.Position;
        Location = new Point(dragBounds.X + p.X - dragStart.X, dragBounds.Y + p.Y - dragStart.Y);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        if (!dragging) return;
        dragging = false;
        Capture = false;
        Moved?.Invoke(Location.X - dragBounds.X, Location.Y - dragBounds.Y);
    }

    /// <summary>Показать плашку. x, y — желаемый левый верхний угол; area — окно игры на экране.</summary>
    public void Present(OverlayModel m, int x, int y, float u, RectI area)
    {
        if (dragging) return;
        m.MoveMode = moveMode;
        model = m;
        scale = u;
        var size = OverlayRenderer.Measure(m, u);
        x = Math.Clamp(x, area.X, Math.Max(area.X, area.Right - size.Width));
        y = Math.Clamp(y, area.Y, Math.Max(area.Y, area.Bottom - size.Height));
        var bounds = new Rectangle(x, y, size.Width, size.Height);
        if (Bounds != bounds)
        {
            Bounds = bounds;
            // Скруглённые углы карточки: окно обрезается по контуру.
            using var path = Ui.Theme.Round(new RectangleF(0, 0, size.Width, size.Height), OverlayRenderer.Radius * u);
            Region = new Region(path);
        }
        if (!Visible)
        {
            Show();
            Log.Info($"Плашка на экране: {bounds.X},{bounds.Y} {bounds.Width}x{bounds.Height}");
        }
        // Игра при активации может подняться выше — возвращаем плашку наверх, не забирая фокус.
        SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        Invalidate();
        UpdateHotkeys();
    }

    public void Conceal()
    {
        if (moveMode) SetMoveMode(false);
        if (Visible) Hide();
        model = null;
        UpdateHotkeys();
    }

    protected override void OnPaintBackground(PaintEventArgs e) { }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (model == null) { e.Graphics.Clear(OverlayRenderer.Bg); return; }
        OverlayRenderer.Draw(e.Graphics, model, scale);
    }

    const uint LWA_ALPHA = 0x2, SWP_NOSIZE = 0x1, SWP_NOMOVE = 0x2, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    static readonly IntPtr HWND_TOPMOST = new(-1);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetLayeredWindowAttributes(IntPtr h, uint key, byte alpha, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern IntPtr SetWindowLongPtr(IntPtr h, int i, IntPtr v);
}
