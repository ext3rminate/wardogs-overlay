using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace WardogsOverlay.Ui;

/// <summary>Основа своих элементов: двойная буферизация, масштаб по DPI, перерисовка при изменении размера.</summary>
abstract class UiControl : Control
{
    protected UiControl()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        ForeColor = Theme.Text;
    }

    /// <summary>Множитель DPI (1 при 96 dpi).</summary>
    protected float D => DeviceDpi / 96f;
    protected float S(float px) => px * D;

    protected override void OnPaint(PaintEventArgs e)
    {
        Theme.Smooth(e.Graphics);
        Draw(e.Graphics);
    }

    protected abstract void Draw(Graphics g);
}

/// <summary>Карточка: скруглённая панель с тонкой рамкой. Дети лежат внутри с отступами.</summary>
class Card : Panel
{
    public Color Fill { get; set; } = Theme.Panel;
    public Color Border { get; set; } = Theme.Line;
    public float Radius { get; set; } = 14;

    public Card()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Theme.Panel;
        Padding = new Padding(16);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        g.Clear(Theme.BackOf(Parent));
        float d = DeviceDpi / 96f;
        var r = new RectangleF(0, 0, Width, Height);
        Theme.FillRound(g, Fill, r, Radius * d);
        Theme.StrokeRound(g, Border, r, Radius * d);
    }
}

/// <summary>Переключатель вкл/выкл (вместо галочки).</summary>
class Toggle : UiControl
{
    bool on;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Checked { get => on; set { if (on == value) return; on = value; Invalidate(); CheckedChanged?.Invoke(this, EventArgs.Empty); } }
    public event EventHandler? CheckedChanged;

    public Toggle()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.CheckButton;
        Size = new Size(44, 26);
    }

    protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Space or Keys.Enter) Checked = !Checked; base.OnKeyDown(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0, 0, Width, Height);
        float h = Height;
        Theme.FillRound(g, on ? Theme.Accent : Theme.Line2, r, h / 2);
        float k = h - S(6);
        float x = on ? Width - S(3) - k : S(3);
        using (var b = new SolidBrush(Theme.Bg)) g.FillEllipse(b, x, S(3), k, k);
        if (Focused) Theme.StrokeRound(g, Theme.Alpha(Theme.Text, 120), r, h / 2, S(1.5f));
    }
}

/// <summary>Сегментный переключатель (вместо выпадающего списка): кнопки одной строкой.</summary>
class Segmented : UiControl
{
    string[] items = [];
    int selected;
    int hover = -1;

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string[] Items { get => items; set { items = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex { get => selected; set { if (selected == value) return; selected = value; Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty); } }
    public event EventHandler? SelectedIndexChanged;
    public Color Well { get; set; } = Theme.Bg;

    public Segmented()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
        Font = Theme.UiFont(12, 600);
        Height = 42;
    }

    int Hit(Point p) => items.Length == 0 ? -1 : Math.Clamp((int)((p.X - S(3)) / ((Width - S(6)) / items.Length)), 0, items.Length - 1);
    protected override void OnMouseMove(MouseEventArgs e) { int h = Hit(e.Location); if (h != hover) { hover = h; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { int h = Hit(e.Location); if (h >= 0) SelectedIndex = h; Focus(); base.OnMouseDown(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Left && selected > 0) SelectedIndex--;
        if (e.KeyCode == Keys.Right && selected < items.Length - 1) SelectedIndex++;
        base.OnKeyDown(e);
    }
    protected override bool IsInputKey(Keys k) => k is Keys.Left or Keys.Right || base.IsInputKey(k);

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0, 0, Width, Height);
        Theme.FillRound(g, Well, r, S(11));
        Theme.StrokeRound(g, Focused ? Theme.Line2 : Theme.Line, r, S(11));
        if (items.Length == 0) return;
        float w = (Width - S(6)) / items.Length;
        for (int i = 0; i < items.Length; i++)
        {
            var cell = new RectangleF(S(3) + i * w + (i > 0 ? S(1.5f) : 0), S(3), w - S(1.5f), Height - S(6));
            if (i == selected) Theme.FillRound(g, Theme.Line, cell, S(8));
            else if (i == hover) Theme.FillRound(g, Theme.Alpha(Theme.Line, 120), cell, S(8));
            Theme.Draw(g, items[i], Font, i == selected ? Theme.Text : Theme.Muted, cell, Theme.Align.Center);
        }
    }
}

/// <summary>Фильтры-таблетки: один выбранный, переносятся на новую строку.</summary>
class ChipBar : UiControl
{
    string[] items = [];
    int selected;
    readonly List<RectangleF> boxes = [];

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public string[] Items { get => items; set { items = value; Layout2(); Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex { get => selected; set { if (selected == value) return; selected = value; Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty); } }
    public event EventHandler? SelectedIndexChanged;

    public ChipBar()
    {
        Cursor = Cursors.Hand;
        Font = Theme.UiFont(13, 500);
        Height = 36;
    }

    protected override void OnResize(EventArgs e) { Layout2(); base.OnResize(e); }

    void Layout2()
    {
        boxes.Clear();
        if (Width <= 0) return;
        using var g = CreateGraphics();
        float x = 0, y = 0, h = S(34), gap = S(8);
        foreach (var it in items)
        {
            float w = Theme.Measure(g, it, Font).Width + S(28);
            if (x > 0 && x + w > Width) { x = 0; y += h + gap; }
            boxes.Add(new RectangleF(x, y, w, h));
            x += w + gap;
        }
        int need = (int)Math.Ceiling(y + h + 1);
        if (Height != need) Height = need;
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        for (int i = 0; i < boxes.Count; i++) if (boxes[i].Contains(e.Location)) SelectedIndex = i;
        base.OnMouseDown(e);
    }

    protected override void Draw(Graphics g)
    {
        for (int i = 0; i < boxes.Count && i < items.Length; i++)
        {
            var b = boxes[i];
            bool on = i == selected;
            if (on) Theme.FillRound(g, Theme.Mix(Theme.Accent, Theme.Bg, 0.14), b, b.Height / 2);
            Theme.StrokeRound(g, on ? Theme.Mix(Theme.Accent, Theme.Bg, 0.4) : Theme.Line, b, b.Height / 2);
            Theme.Draw(g, items[i], Font, on ? Theme.Accent : Theme.TextSoft, b, Theme.Align.Center);
        }
    }
}

enum ButtonKind { Primary, Secondary, Ghost }

/// <summary>Кнопка: основная (янтарная), вторичная (с рамкой), призрачная.</summary>
class UiButton : UiControl
{
    bool hover, down;
    public ButtonKind Kind { get; set; } = ButtonKind.Secondary;
    public IconKind? Icon { get; set; }

    public UiButton()
    {
        TabStop = true;
        Cursor = Cursors.Hand;
        Font = Theme.UiFont(14, 600);
        Height = 46;
        AccessibleRole = AccessibleRole.PushButton;
    }

    protected override void OnTextChanged(EventArgs e) { Invalidate(); base.OnTextChanged(e); }
    protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = down = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode is Keys.Space or Keys.Enter) OnClick(EventArgs.Empty); base.OnKeyDown(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    public int PreferredWidth(int? dpi = null)
    {
        using var g = CreateGraphics();
        float target = dpi ?? DeviceDpi;
        return (int)Math.Ceiling(Theme.Measure(g, Text, Font).Width * target / DeviceDpi + (Icon != null ? 58 : 34) * target / 96f);
    }

    protected override void Draw(Graphics g)
    {
        var r = new RectangleF(0, 0, Width, Height);
        float rad = S(12);
        Color fg;
        switch (Kind)
        {
            case ButtonKind.Primary:
                Theme.FillRound(g, down ? Theme.Mix(Theme.Accent, Color.Black, 0.85) : hover ? Theme.Mix(Theme.Accent, Color.White, 0.9) : Theme.Accent, r, rad);
                fg = Theme.AccentInk;
                break;
            case ButtonKind.Secondary:
                Theme.FillRound(g, hover ? Theme.Selected : Theme.Raised, r, rad);
                Theme.StrokeRound(g, Theme.Line2, r, rad);
                fg = Theme.Text;
                break;
            default:
                if (hover) Theme.FillRound(g, Theme.Raised, r, rad);
                Theme.StrokeRound(g, Theme.Line2, r, rad);
                fg = Theme.Text;
                break;
        }
        if (Focused) Theme.StrokeRound(g, Theme.Alpha(Theme.Text, 140), r, rad, S(1.5f));
        float textW = Theme.Measure(g, Text, Font).Width;
        float iconW = Icon != null ? S(26) : 0;
        float x = (Width - textW - iconW) / 2;
        if (Icon is IconKind ic) Icons.Draw(g, ic, new RectangleF(x, (Height - S(18)) / 2, S(18), S(18)), fg, S(2));
        Theme.Draw(g, Text, Font, fg, new RectangleF(x + iconW, 0, Math.Max(textW + S(12), Width - x - iconW), Height));
    }
}

/// <summary>Поле ввода: скруглённая рамка, необязательная приставка («$», лупа).</summary>
class TextField : Panel
{
    public TextBox Box { get; } = new();
    readonly string prefix;
    readonly bool searchIcon;

    public TextField(string prefix = "", bool searchIcon = false, string placeholder = "")
    {
        this.prefix = prefix;
        this.searchIcon = searchIcon;
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        BackColor = Theme.Bg;
        Height = 42;
        Box.BorderStyle = BorderStyle.None;
        Box.BackColor = Theme.Bg;
        Box.ForeColor = Theme.Text;
        Box.Font = Theme.UiFont(14);
        Box.PlaceholderText = placeholder;
        Controls.Add(Box);
        Cursor = Cursors.IBeam;
        Click += (_, _) => Box.Focus();
        Box.GotFocus += (_, _) => Invalidate();
        Box.LostFocus += (_, _) => Invalidate();
    }

    public Color Fill { get => BackColor; set { BackColor = value; Box.BackColor = value; } }

    protected override void OnLayout(LayoutEventArgs e)
    {
        base.OnLayout(e);
        float d = DeviceDpi / 96f;
        int left = (int)(14 * d) + (searchIcon ? (int)(24 * d) : 0) + (prefix.Length > 0 ? (int)(14 * d) : 0);
        Box.Location = new Point(left, (Height - Box.Height) / 2);
        Box.Width = Math.Max(1, Width - left - (int)(12 * d));
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        PerformLayout();
        Invalidate();
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        var g = e.Graphics;
        Theme.Smooth(g);
        g.Clear(Theme.BackOf(Parent));
        float d = DeviceDpi / 96f;
        var r = new RectangleF(0, 0, Width, Height);
        Theme.FillRound(g, BackColor, r, 11 * d);
        Theme.StrokeRound(g, Box.Focused ? Theme.Faint : Theme.Line2, r, 11 * d);
        float x = 14 * d;
        if (searchIcon) { Icons.Draw(g, IconKind.Search, new RectangleF(x, (Height - 16 * d) / 2, 16 * d, 16 * d), Theme.Faint, 2 * d); x += 24 * d; }
        if (prefix.Length > 0)
        {
            using var f = Theme.MonoFont(Box.Font.SizeInPoints / 0.75f, 600);
            Theme.Draw(g, prefix, f, Theme.Faint, new RectangleF(x, 0, 20 * d, Height));
        }
    }
}

/// <summary>Ползунок: дорожка, заливка акцентом, ручка. Тащить мышью или стрелками.</summary>
class Slider : UiControl
{
    int value = 50;
    bool drag;
    public int Minimum { get; set; }
    public int Maximum { get; set; } = 100;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value { get => value; set { value = Math.Clamp(value, Minimum, Maximum); if (this.value == value) return; this.value = value; Invalidate(); ValueChanged?.Invoke(this, EventArgs.Empty); } }
    public event EventHandler? ValueChanged;

    public Slider() { TabStop = true; Cursor = Cursors.Hand; Height = 22; AccessibleRole = AccessibleRole.Slider; }

    void SetFrom(int x)
    {
        float k = S(9), w = Width - 2 * k;
        Value = Minimum + (int)Math.Round(Math.Clamp((x - k) / w, 0, 1) * (Maximum - Minimum));
    }
    protected override void OnMouseDown(MouseEventArgs e) { drag = true; Focus(); SetFrom(e.X); base.OnMouseDown(e); }
    protected override void OnMouseMove(MouseEventArgs e) { if (drag) SetFrom(e.X); base.OnMouseMove(e); }
    protected override void OnMouseUp(MouseEventArgs e) { drag = false; base.OnMouseUp(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Left or Keys.Down) Value -= 5;
        if (e.KeyCode is Keys.Right or Keys.Up) Value += 5;
        base.OnKeyDown(e);
    }
    protected override bool IsInputKey(Keys k) => k is Keys.Left or Keys.Right or Keys.Up or Keys.Down || base.IsInputKey(k);

    protected override void Draw(Graphics g)
    {
        float k = S(9), cy = Height / 2f, w = Width - 2 * k;
        float t = Maximum == Minimum ? 0 : (float)(value - Minimum) / (Maximum - Minimum);
        Theme.FillRound(g, Theme.Track, new RectangleF(k, cy - S(3), w, S(6)), S(3));
        Theme.FillRound(g, Theme.Accent, new RectangleF(k, cy - S(3), w * t, S(6)), S(3));
        float x = k + w * t;
        using (var b = new SolidBrush(Theme.Text)) g.FillEllipse(b, x - S(8), cy - S(8), S(16), S(16));
        if (Focused) using (var p = new Pen(Theme.Accent, S(2))) g.DrawEllipse(p, x - S(9), cy - S(9), S(18), S(18));
    }
}

enum IconKind { Crosshair, Sliders, Shield, Bullet, Steps, Layers, Chart, Doc, Search, Map, List, Chevron }

/// <summary>Линейные иконки, нарисованные примитивами (как в макете).</summary>
static class Icons
{
    public static void Draw(Graphics g, IconKind kind, RectangleF r, Color c, float width)
    {
        var st = g.Save();
        g.TranslateTransform(r.X, r.Y);
        g.ScaleTransform(r.Width / 24f, r.Height / 24f);
        using var pen = new Pen(c, width * 24f / r.Width) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        void L(float x1, float y1, float x2, float y2) => g.DrawLine(pen, x1, y1, x2, y2);
        switch (kind)
        {
            case IconKind.Crosshair:
                g.DrawEllipse(pen, 5, 5, 14, 14);
                L(12, 2, 12, 7); L(12, 17, 12, 22); L(2, 12, 7, 12); L(17, 12, 22, 12);
                break;
            case IconKind.Sliders:
                L(4, 7, 13, 7); L(17, 7, 20, 7); L(4, 12, 7, 12); L(11, 12, 20, 12); L(4, 17, 15, 17); L(19, 17, 20, 17);
                L(15, 5, 15, 9); L(9, 10, 9, 14); L(17, 15, 17, 19);
                break;
            case IconKind.Shield:
                g.DrawPolygon(pen, new PointF[] { new(12, 3), new(19, 6), new(19, 12), new(16, 18), new(12, 21), new(8, 18), new(5, 12), new(5, 6) });
                break;
            case IconKind.Bullet:
                g.DrawLine(pen, 9, 21, 15, 21); g.DrawLine(pen, 9, 21, 9, 9); g.DrawLine(pen, 15, 21, 15, 9);
                g.DrawArc(pen, 9, 6, 6, 6, 180, 180); L(9, 14, 15, 14);
                break;
            case IconKind.Steps:
                g.DrawLines(pen, new PointF[] { new(4, 20), new(8, 20), new(8, 15), new(12, 15), new(12, 10), new(16, 10), new(16, 5), new(20, 5) });
                break;
            case IconKind.Layers:
                g.DrawPolygon(pen, new PointF[] { new(12, 3), new(21, 8), new(12, 13), new(3, 8) });
                g.DrawLines(pen, new PointF[] { new(3, 13), new(12, 18), new(21, 13) });
                break;
            case IconKind.Chart:
                L(4, 20, 4, 10); L(10, 20, 10, 4); L(16, 20, 16, 12); L(3, 20, 21, 20);
                break;
            case IconKind.Doc:
                g.DrawPolygon(pen, new PointF[] { new(7, 3), new(14, 3), new(19, 8), new(19, 21), new(7, 21) });
                g.DrawLines(pen, new PointF[] { new(14, 3), new(14, 8), new(19, 8) });
                L(10, 13, 16, 13); L(10, 17, 16, 17);
                break;
            case IconKind.Search:
                g.DrawEllipse(pen, 4, 4, 14, 14); L(16.5f, 16.5f, 20, 20);
                break;
            case IconKind.Map:
                g.DrawPolygon(pen, new PointF[] { new(3, 6), new(9, 4), new(15, 6), new(21, 4), new(21, 18), new(15, 20), new(9, 18), new(3, 20) });
                L(9, 4, 9, 18); L(15, 6, 15, 20);
                break;
            case IconKind.List:
                L(4, 6, 20, 6); L(4, 12, 20, 12); L(4, 18, 14, 18);
                break;
            case IconKind.Chevron:
                g.DrawLines(pen, new PointF[] { new(6, 9), new(12, 15), new(18, 9) });
                break;
        }
        g.Restore(st);
    }
}

/// <summary>Боковая навигация окна «База»: логотип, пункты с иконками, подпись внизу.</summary>
class NavRail : UiControl
{
    public record Item(string Label, IconKind Icon);
    Item[] items = [];
    int selected, hover = -1;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Item[] Items { get => items; set { items = value; Invalidate(); } }
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SelectedIndex { get => selected; set { if (selected == value) return; selected = value; Invalidate(); SelectedIndexChanged?.Invoke(this, EventArgs.Empty); } }
    public event EventHandler? SelectedIndexChanged;
    public string Title { get; set; } = "";
    public string Footer { get; set; } = "";
    readonly Font itemFont = Theme.UiFont(16, 500), titleFont = Theme.UiFont(16, 700), footFont = Theme.UiFont(12);

    public NavRail() { TabStop = true; Cursor = Cursors.Hand; BackColor = Theme.Rail; }

    RectangleF ItemRect(int i) => new(S(12), S(66) + i * S(46), Width - S(24), S(42));
    int Hit(Point p) { for (int i = 0; i < items.Length; i++) if (ItemRect(i).Contains(p)) return i; return -1; }
    protected override void OnMouseMove(MouseEventArgs e) { int h = Hit(e.Location); if (h != hover) { hover = h; Invalidate(); } base.OnMouseMove(e); }
    protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { int h = Hit(e.Location); if (h >= 0) SelectedIndex = h; base.OnMouseDown(e); }
    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Up && selected > 0) SelectedIndex--;
        if (e.KeyCode == Keys.Down && selected < items.Length - 1) SelectedIndex++;
        base.OnKeyDown(e);
    }
    protected override bool IsInputKey(Keys k) => k is Keys.Up or Keys.Down || base.IsInputKey(k);

    protected override void Draw(Graphics g)
    {
        using var textScale = Theme.LogicalText(g, DeviceDpi);
        g.Clear(Theme.Rail);
        using (var p = new Pen(Theme.LineSoft)) g.DrawLine(p, Width - 1, 0, Width - 1, Height);
        Icons.Draw(g, IconKind.Crosshair, new RectangleF(S(20), S(22), S(22), S(22)), Theme.Accent, S(2));
        Theme.Draw(g, Title, titleFont, Theme.Text, new RectangleF(S(52), S(18), Width - S(60), S(30)));
        for (int i = 0; i < items.Length; i++)
        {
            var r = ItemRect(i);
            bool on = i == selected;
            if (on) Theme.FillRound(g, Theme.Selected, r, S(10));
            else if (i == hover) Theme.FillRound(g, Theme.Alpha(Theme.Selected, 140), r, S(10));
            var c = on ? Theme.Text : Theme.Muted;
            Icons.Draw(g, items[i].Icon, new RectangleF(r.X + S(12), r.Y + (r.Height - S(18)) / 2, S(18), S(18)), c, S(1.8f));
            Theme.Draw(g, items[i].Label, itemFont, c, new RectangleF(r.X + S(41), r.Y, r.Width - S(44), r.Height));
        }
        if (Footer.Length > 0)
        {
            var lines = Footer.Split('\n');
            for (int i = 0; i < lines.Length; i++)
                Theme.Draw(g, lines[i], footFont, Theme.Faint, new RectangleF(S(22), Height - S(54) + i * S(16), Width - S(30), S(16)));
        }
    }
}

/// <summary>Двухстрочная ячейка таблицы: название и подпись под ним.</summary>
sealed record TwoLine(string Title, string Sub) : IComparable
{
    public int CompareTo(object? o) => string.Compare(Title, (o as TwoLine)?.Title, StringComparison.CurrentCulture);
    public override string ToString() => Title;
}

/// <summary>Цветная ячейка: текст и его цвет (например, время убийства).</summary>
sealed record Tinted(double Value, string Text, Color Color) : IComparable
{
    public int CompareTo(object? o) => Value.CompareTo((o as Tinted)?.Value ?? double.MinValue);
    public override string ToString() => Text;
}

/// <summary>
/// Таблица в стиле макета: тёмная, строки 60 px, моноширинные числа справа, шапка капителью,
/// двухстрочные названия, эффекты — цветными таблетками (столбец с Tag = StatDef и значением double).
/// </summary>
class StyledGrid : DataGridView
{
    readonly Font mono = Theme.MonoFont(16), monoHead = Theme.MonoFont(12, 600), title = Theme.UiFont(17, 600), sub = Theme.UiFont(13, 500), small = Theme.UiFont(14, 500);
    int metricsDpi;

    public StyledGrid()
    {
        DoubleBuffered = true;
        Dock = DockStyle.Fill;
        ReadOnly = true;
        AllowUserToAddRows = AllowUserToDeleteRows = AllowUserToResizeRows = false;
        AllowUserToResizeColumns = true;
        RowHeadersVisible = false;
        SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        MultiSelect = true;
        BackgroundColor = Theme.Panel;
        BorderStyle = BorderStyle.None;
        GridColor = Theme.LineSoft;
        CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
        ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
        EnableHeadersVisualStyles = false;
        ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
        DefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.Panel, ForeColor = Theme.Text, SelectionBackColor = Theme.Selected, SelectionForeColor = Theme.Text,
            Font = mono, Padding = new Padding(8, 0, 8, 0),
        };
        ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
        {
            BackColor = Theme.Header, ForeColor = Theme.Faint, SelectionBackColor = Theme.Header, SelectionForeColor = Theme.Faint,
            Font = monoHead, Padding = new Padding(8, 0, 8, 0),
        };
        Theme.DarkScrollbars(this);
        _ = Handle;
        // Высоту строк задаём сразу: строки добавляются раньше, чем создаётся окно таблицы.
        ApplyDpiMetrics();
    }

    void ApplyDpiMetrics()
    {
        float d = DeviceDpi / 96f;
        RowTemplate.Height = (int)Math.Round(60 * d);
        foreach (DataGridViewRow row in Rows) row.Height = RowTemplate.Height;
        ColumnHeadersHeight = (int)Math.Round(40 * d);
        using var g = CreateGraphics();
        // DataGridViewCellStyle.Font, как и шрифты Painter, не является Control.Font.
        float scale = DeviceDpi / g.DpiY;
        var oldBody = DefaultCellStyle.Font;
        var oldHead = ColumnHeadersDefaultCellStyle.Font;
        DefaultCellStyle.Font = new Font(mono.FontFamily, mono.Size * scale, mono.Style);
        ColumnHeadersDefaultCellStyle.Font = new Font(monoHead.FontFamily, monoHead.Size * scale, monoHead.Style);
        DefaultCellStyle.Padding = ColumnHeadersDefaultCellStyle.Padding = new Padding((int)(8 * d), 0, (int)(8 * d), 0);
        if (oldBody != mono) oldBody?.Dispose();
        if (oldHead != monoHead) oldHead?.Dispose();
        if (metricsDpi > 0 && metricsDpi != DeviceDpi)
            foreach (DataGridViewColumn col in Columns)
                col.MinimumWidth = (int)Math.Round(col.MinimumWidth * (double)DeviceDpi / metricsDpi);
        metricsDpi = DeviceDpi;
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        ApplyDpiMetrics();
    }

    public DataGridViewTextBoxColumn AddCol(string header, float weight = 1, bool right = true, object? tag = null, int min = 72)
    {
        // Запас под крупные числа, валюту, отступы и индикатор сортировки.
        if (right) min = Math.Max(min, 104);
        min = (int)(min * DeviceDpi / 96f);
        using var g = CreateGraphics();
        using var textScale = Theme.LogicalText(g, DeviceDpi);
        min = Math.Max(min, (int)Math.Ceiling(Theme.Measure(g, header.ToUpperInvariant(), monoHead).Width + 34 * DeviceDpi / 96f));
        var c = new DataGridViewTextBoxColumn
        {
            HeaderText = header.ToUpperInvariant(), FillWeight = weight * 100, MinimumWidth = min, Tag = tag,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill, SortMode = DataGridViewColumnSortMode.Automatic,
        };
        c.DefaultCellStyle.Alignment = right ? DataGridViewContentAlignment.MiddleRight : DataGridViewContentAlignment.MiddleLeft;
        c.HeaderCell.Style.Alignment = c.DefaultCellStyle.Alignment;
        Columns.Add(c);
        return c;
    }

    protected override void OnCellPainting(DataGridViewCellPaintingEventArgs e)
    {
        if (e.RowIndex < 0 || e.ColumnIndex < 0) { base.OnCellPainting(e); return; }
        var v = e.Value;
        var col = Columns[e.ColumnIndex];
        bool special = v is TwoLine || v is Tinted || (col.Tag is StatDef && v is double) || (col.Tag as string) == "small";
        if (!special) { base.OnCellPainting(e); return; }

        var g = e.Graphics!;
        using var textScale = Theme.LogicalText(g, DeviceDpi);
        e.PaintBackground(e.CellBounds, true);
        Theme.Smooth(g);
        var r = e.CellBounds;
        float d = DeviceDpi / 96f;
        var inner = new RectangleF(r.X + 8 * d, r.Y, r.Width - 16 * d, r.Height - 1);
        switch (v)
        {
            case TwoLine t:
                Theme.Draw(g, t.Title, title, Theme.Text, new RectangleF(inner.X, inner.Y + inner.Height / 2 - 19 * d, inner.Width, 20 * d));
                Theme.Draw(g, t.Sub, sub, Theme.Faint, new RectangleF(inner.X, inner.Y + inner.Height / 2 + 1 * d, inner.Width, 16 * d));
                break;
            case Tinted tt:
                Theme.Draw(g, tt.Text, mono, tt.Color, inner, Theme.Align.Right);
                break;
            case double x when col.Tag is StatDef def:
                int good = Stats.Goodness(def, x);
                var txt = Stats.Pct(x);
                var size = Theme.Measure(g, txt, mono);
                float pw = Math.Max(54 * d, size.Width + 14 * d), ph = 26 * d;
                var pill = new RectangleF(r.X + (r.Width - pw) / 2, r.Y + (r.Height - ph) / 2, pw, ph);
                var bg = good > 0 ? Theme.Mix(Theme.Good, Theme.Panel, 0.12) : good < 0 ? Theme.Mix(Theme.Bad, Theme.Panel, 0.13) : Theme.Chip;
                Theme.FillRound(g, bg, pill, 7 * d);
                using (var f = Theme.MonoFont(14))
                    Theme.Draw(g, txt, f, good > 0 ? Theme.GoodText : good < 0 ? Theme.BadText : Theme.Muted, pill, Theme.Align.Center);
                break;
            default:
                Theme.Draw(g, v?.ToString() ?? "", small, Theme.Muted, inner);
                break;
        }
        e.Handled = true;
    }
}

/// <summary>Нарисованный блок: размер и содержимое задаёт делегат (карточки брони, патроны, рейтинг сборок…).</summary>
class PaintBox : UiControl
{
    public Action<Graphics, PaintBox>? Painter { get; set; }
    public new float S(float px) => base.S(px);
    public object? Data { get; set; }
    public PaintBox() { BackColor = Theme.Bg; }
    protected override void Draw(Graphics g)
    {
        using var textScale = Theme.LogicalText(g, DeviceDpi);
        Painter?.Invoke(g, this);
    }
}

/// <summary>Тёмное контекстное меню (трей, «Карты»).</summary>
static class DarkMenu
{
    sealed class Colors : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => Theme.Raised;
        public override Color ImageMarginGradientBegin => Theme.Raised;
        public override Color ImageMarginGradientMiddle => Theme.Raised;
        public override Color ImageMarginGradientEnd => Theme.Raised;
        public override Color MenuBorder => Theme.Line2;
        public override Color MenuItemBorder => Theme.Selected;
        public override Color MenuItemSelected => Theme.Selected;
        public override Color SeparatorDark => Theme.Line;
        public override Color SeparatorLight => Theme.Line;
    }

    sealed class Renderer() : ToolStripProfessionalRenderer(new Colors())
    {
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Enabled ? Theme.Text : Theme.Faint;
            base.OnRenderItemText(e);
        }
    }

    public static ContextMenuStrip Create()
    {
        var m = new ContextMenuStrip { Renderer = new Renderer(), Font = Theme.UiFont(13), ShowImageMargin = false, Padding = new Padding(4) };
        return m;
    }
}
