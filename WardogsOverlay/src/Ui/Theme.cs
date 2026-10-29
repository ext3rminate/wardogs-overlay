using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace WardogsOverlay.Ui;

/// <summary>
/// Оформление: цвета и шрифты из макета редизайна. Шрифты Onest и JetBrains Mono (OFL) встроены в программу:
/// для GDI+ — через PrivateFontCollection, для обычных элементов WinForms (GDI) — через AddFontMemResourceEx.
/// </summary>
static class Theme
{
    public static readonly Color Bg = Color.FromArgb(13, 14, 16);
    public static readonly Color Rail = Color.FromArgb(18, 20, 23);
    public static readonly Color Panel = Color.FromArgb(21, 23, 26);
    public static readonly Color Raised = Color.FromArgb(27, 30, 34);
    public static readonly Color Header = Color.FromArgb(24, 26, 30);
    public static readonly Color Selected = Color.FromArgb(30, 33, 38);
    public static readonly Color Line = Color.FromArgb(38, 42, 47);
    public static readonly Color LineSoft = Color.FromArgb(34, 38, 43);
    public static readonly Color Line2 = Color.FromArgb(49, 54, 60);
    public static readonly Color Track = Color.FromArgb(35, 39, 44);
    public static readonly Color Chip = Color.FromArgb(31, 34, 39);
    public static readonly Color Text = Color.FromArgb(236, 238, 240);
    public static readonly Color TextSoft = Color.FromArgb(201, 205, 210);
    public static readonly Color Muted = Color.FromArgb(161, 167, 174);
    public static readonly Color Faint = Color.FromArgb(133, 139, 146);
    public static readonly Color Ghost = Color.FromArgb(92, 98, 106);
    public static readonly Color Accent = Color.FromArgb(245, 165, 36);
    public static readonly Color AccentInk = Color.FromArgb(26, 18, 4);
    public static readonly Color Good = Color.FromArgb(87, 211, 140);
    public static readonly Color GoodText = Color.FromArgb(111, 224, 160);
    public static readonly Color Bad = Color.FromArgb(255, 111, 97);
    public static readonly Color BadText = Color.FromArgb(255, 142, 131);
    public static readonly Color Blue = Color.FromArgb(142, 187, 255);

    public static Color Alpha(Color c, int a) => Color.FromArgb(a, c);
    /// <summary>Цвет поверх фона с прозрачностью — для «подсвеченных» плашек на непрозрачной основе.</summary>
    public static Color Mix(Color top, Color bottom, double a) => Color.FromArgb(
        (int)(top.R * a + bottom.R * (1 - a)), (int)(top.G * a + bottom.G * (1 - a)), (int)(top.B * a + bottom.B * (1 - a)));

    // ---------- шрифты ----------

    static readonly PrivateFontCollection Fonts = new();
    static readonly List<GCHandle> Pinned = [];
    static bool loaded;

    public static FontFamily Ui { get; private set; } = FontFamily.GenericSansSerif;
    public static FontFamily UiMedium { get; private set; } = FontFamily.GenericSansSerif;
    public static FontFamily UiSemi { get; private set; } = FontFamily.GenericSansSerif;
    public static FontFamily Mono { get; private set; } = FontFamily.GenericMonospace;
    public static FontFamily MonoMedium { get; private set; } = FontFamily.GenericMonospace;

    public static void Load()
    {
        if (loaded) return;
        loaded = true;
        var asm = typeof(Theme).Assembly;
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase)))
        {
            using var s = asm.GetManifestResourceStream(name)!;
            var bytes = new byte[s.Length];
            s.ReadExactly(bytes);
            var h = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            Pinned.Add(h);
            Fonts.AddMemoryFont(h.AddrOfPinnedObject(), bytes.Length);
            uint n = 0;
            AddFontMemResourceEx(h.AddrOfPinnedObject(), (uint)bytes.Length, IntPtr.Zero, ref n);
        }
        FontFamily? Find(params string[] names) =>
            names.Select(n => Fonts.Families.FirstOrDefault(f => f.Name.Equals(n, StringComparison.OrdinalIgnoreCase))).FirstOrDefault(f => f != null);
        Ui = Find("Onest") ?? new FontFamily("Segoe UI");
        UiMedium = Find("Onest Medium") ?? Ui;
        UiSemi = Find("Onest SemiBold") ?? Ui;
        Mono = Find("JetBrains Mono") ?? new FontFamily("Consolas");
        MonoMedium = Find("JetBrains Mono Medium") ?? Mono;
    }

    /// <summary>Шрифт интерфейса. size — в пикселях макета (при 96 dpi), weight — 400/500/600/700.</summary>
    public static Font UiFont(float px, int weight = 400) => weight switch
    {
        >= 700 => new Font(Ui, px * 0.75f, FontStyle.Bold),
        >= 600 => new Font(UiSemi, px * 0.75f, UiSemi == Ui ? FontStyle.Bold : FontStyle.Regular),
        >= 500 => new Font(UiMedium, px * 0.75f),
        _ => new Font(Ui, px * 0.75f),
    };

    public static Font MonoFont(float px, int weight = 500) => weight switch
    {
        >= 600 => new Font(Mono, px * 0.75f, FontStyle.Bold),
        >= 500 => new Font(MonoMedium, px * 0.75f),
        _ => new Font(Mono, px * 0.75f),
    };

    // ---------- рисование ----------

    public static GraphicsPath Round(RectangleF r, float radius)
    {
        var p = new GraphicsPath();
        float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
        if (d <= 0.5f) { p.AddRectangle(r); return p; }
        p.AddArc(r.X, r.Y, d, d, 180, 90);
        p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
        p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
        p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
        p.CloseFigure();
        return p;
    }

    public static void FillRound(Graphics g, Color c, RectangleF r, float radius)
    {
        using var b = new SolidBrush(c);
        using var p = Round(r, radius);
        g.FillPath(b, p);
    }

    public static void StrokeRound(Graphics g, Color c, RectangleF r, float radius, float width = 1)
    {
        using var pen = new Pen(c, width);
        using var p = Round(new RectangleF(r.X + width / 2, r.Y + width / 2, r.Width - width, r.Height - width), radius);
        g.DrawPath(pen, p);
    }

    public static void Smooth(Graphics g)
    {
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    }

    static readonly StringFormat Near = new() { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Center, Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
    static readonly StringFormat Far = new() { Alignment = StringAlignment.Far, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };
    static readonly StringFormat Center = new() { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center, FormatFlags = StringFormatFlags.NoWrap };

    public enum Align { Left, Right, Center }

    // Шрифты внутри Painter/ячеек не принадлежат Control.Font, поэтому WinForms
    // не масштабирует их при смене DPI. Контекст задаёт им тот же масштаб,
    // что и геометрии; обычные Control.Font уже масштабированы WinForms.
    [ThreadStatic] static float logicalTextScale;
    public static IDisposable LogicalText(Graphics g, int dpi) => new TextScaleScope(dpi / g.DpiY);

    sealed class TextScaleScope : IDisposable
    {
        readonly float previous = logicalTextScale;
        public TextScaleScope(float scale) => logicalTextScale = scale;
        public void Dispose() => logicalTextScale = previous;
    }

    static Font? ScaledTextFont(Font f) => logicalTextScale > 0 && Math.Abs(logicalTextScale - 1) > 0.001f
        ? new Font(f.FontFamily, f.Size * logicalTextScale, f.Style, f.Unit) : null;

    public static void Draw(Graphics g, string s, Font f, Color c, RectangleF r, Align a = Align.Left)
    {
        using var b = new SolidBrush(c);
        using var scaled = ScaledTextFont(f);
        g.DrawString(s, scaled ?? f, b, r, a switch { Align.Right => Far, Align.Center => Center, _ => Near });
    }

    /// <summary>Фон ближайшего непрозрачного предка — чтобы скруглённые углы не чернели на прозрачных панелях.</summary>
    public static Color BackOf(Control? c)
    {
        for (var p = c; p != null; p = p.Parent)
            if (p.BackColor.A == 255) return p.BackColor;
        return Bg;
    }

    public static SizeF Measure(Graphics g, string s, Font f)
    {
        using var scaled = ScaledTextFont(f);
        return g.MeasureString(s, scaled ?? f, int.MaxValue, StringFormat.GenericTypographic);
    }

    /// <summary>Тёмные полосы прокрутки и рамки стандартных элементов (Windows 10/11).</summary>
    public static void DarkScrollbars(Control c)
    {
        if (c.IsHandleCreated) SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
        else c.HandleCreated += (_, _) => SetWindowTheme(c.Handle, "DarkMode_Explorer", null);
    }

    public static void DarkTitleBar(Form f)
    {
        void Apply() { int on = 1; DwmSetWindowAttribute(f.Handle, 20, ref on, sizeof(int)); }
        if (f.IsHandleCreated) Apply(); else f.HandleCreated += (_, _) => Apply();
    }

    [DllImport("gdi32.dll")] static extern IntPtr AddFontMemResourceEx(IntPtr pbFont, uint cbFont, IntPtr pdv, ref uint pcFonts);
    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)] static extern int SetWindowTheme(IntPtr hwnd, string? app, string? idList);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
}
