using Windows.Graphics.Imaging;

namespace WardogsOverlay;

/// <summary>
/// Узнаёт обвес по картинке в ячейке: иконки MetaForge — те же текстуры, что рисует игра.
/// Иконка вписывается в ячейку с перебором масштаба и сдвига, сходство — нормированная корреляция.
/// Иконки декодируются лениво и только для подходящих к пушке обвесов.
/// </summary>
sealed class IconMatcher(GameData data)
{
    const int Target = 40;                       // высота, к которой приводится ячейка
    static readonly double[] Fits = [0.80, 0.88, 0.96, 1.04];
    /// <summary>Пушка в ячейке экипировки нарисована мельче и не по центру по высоте — перебираем шире.</summary>
    public static readonly double[] WeaponFits = [0.62, 0.70, 0.78, 0.86, 0.94];
    const int Shift = 4;

    sealed record IconImg(int W, int H, float[] Ga, float[] A); // яркость с учётом альфы, альфа

    readonly Dictionary<string, IconImg?> cache = [];

    public (Attachment? Best, double Score, double Second) Match(Frame frame, RectI content, Func<int, int, bool> allowed,
                                                                IEnumerable<Attachment> pool) =>
        Match(frame, content, allowed, pool, a => a.Icon, Fits);

    /// <summary>Лучшая по сходству картинка из набора (обвесы или пушки).</summary>
    public (T? Best, double Score, double Second) Match<T>(Frame frame, RectI content, Func<int, int, bool> allowed,
                                                         IEnumerable<T> pool, Func<T, string> iconOf, double[] fits) where T : class
    {
        var (cg, cm, cw, ch) = Sample(frame, content, allowed);
        float bg = Background(cg, cm, cw, ch);

        T? best = null;
        double bestScore = double.NegativeInfinity, second = double.NegativeInfinity;
        foreach (var a in pool)
        {
            var icon = Get(iconOf(a));
            if (icon == null) continue;
            double s = Score(cg, cm, cw, ch, bg, icon, fits);
            if (s > bestScore) { second = bestScore; bestScore = s; best = a; }
            else if (s > second) second = s;
        }
        return (best, bestScore, second);
    }

    /// <summary>
    /// Пустая ячейка рисует контур-силуэт типа обвеса. Сравниваем контуры ячейки с контурами иконок
    /// (край альфа-канала) — грубо, но чтобы отличить дуло от рукояти, хватает.
    /// </summary>
    public double MatchEdges(Frame frame, RectI content, Func<int, int, bool> allowed, IEnumerable<Attachment> pool)
    {
        var (cg, cm, cw, ch) = Sample(frame, content, allowed);
        var ce = Edges(cg, cw, ch);
        double best = double.NegativeInfinity;
        foreach (var a in pool)
        {
            var icon = Get(a.Icon);
            if (icon == null) continue;
            foreach (var f in Fits)
            {
                double s = Math.Min((double)cw / icon.W, (double)ch / icon.H) * f;
                int rw = Math.Max(1, (int)Math.Round(icon.W * s)), rh = Math.Max(1, (int)Math.Round(icon.H * s));
                var alpha = RenderAlpha(icon, rw, rh);
                var ie = Edges(alpha, rw, rh);
                for (int dy = -Shift; dy <= Shift; dy += 2)
                    for (int dx = -Shift; dx <= Shift; dx += 2)
                    {
                        double ncc = Ncc(ce, cm, cw, ch, ie, rw, rh, (cw - rw) / 2 + dx, (ch - rh) / 2 + dy, 0);
                        if (ncc > best) best = ncc;
                    }
            }
        }
        return best;
    }

    static (float[] G, bool[] M, int W, int H) Sample(Frame frame, RectI content, Func<int, int, bool> allowed)
    {
        int ch = Target, cw = Math.Max(8, (int)Math.Round(content.W * (double)Target / content.H));
        var cg = new float[cw * ch];
        var cm = new bool[cw * ch];
        double sx = (double)content.W / cw, sy = (double)content.H / ch;
        for (int y = 0; y < ch; y++)
        {
            for (int x = 0; x < cw; x++)
            {
                int x0 = content.X + (int)(x * sx), x1 = content.X + Math.Max((int)((x + 1) * sx), (int)(x * sx) + 1);
                int y0 = content.Y + (int)(y * sy), y1 = content.Y + Math.Max((int)((y + 1) * sy), (int)(y * sy) + 1);
                double sum = 0; int n = 0, ok = 0;
                for (int yy = y0; yy < y1; yy++)
                    for (int xx = x0; xx < x1; xx++)
                    {
                        if (xx < 0 || yy < 0 || xx >= frame.W || yy >= frame.H) continue;
                        var (r, g, b) = frame.Rgb(xx, yy);
                        sum += (r * 77 + g * 150 + b * 29) / 256.0;
                        n++;
                        if (allowed(xx - content.X, yy - content.Y)) ok++;
                    }
                cg[y * cw + x] = n > 0 ? (float)(sum / n) : 0;
                cm[y * cw + x] = n > 0 && ok * 4 >= n * 3;
            }
        }
        return (cg, cm, cw, ch);
    }

    static float[] Edges(float[] g, int w, int h)
    {
        var e = new float[w * h];
        for (int y = 1; y < h - 1; y++)
            for (int x = 1; x < w - 1; x++)
            {
                int p = y * w + x;
                e[p] = Math.Abs(g[p + 1] - g[p - 1]) + Math.Abs(g[p + w] - g[p - w]);
            }
        return e;
    }

    /// <summary>Нормированная корреляция ячейки с картинкой rw×rh, сдвинутой на (ox, oy); снаружи — фон bg.</summary>
    static double Ncc(float[] cg, bool[] cm, int cw, int ch, float[] img, int rw, int rh, int ox, int oy, float bg)
    {
        double sa = 0, sb = 0, saa = 0, sbb = 0, sab = 0; int n = 0;
        for (int y = 0; y < ch; y++)
        {
            int iy = y - oy;
            for (int x = 0; x < cw; x++)
            {
                int p = y * cw + x;
                if (!cm[p]) continue;
                int ix = x - ox;
                double a = cg[p];
                double b = ix >= 0 && iy >= 0 && ix < rw && iy < rh ? img[iy * rw + ix] : bg;
                sa += a; sb += b; saa += a * a; sbb += b * b; sab += a * b; n++;
            }
        }
        if (n < 50) return double.NegativeInfinity;
        double va = saa - sa * sa / n, vb = sbb - sb * sb / n;
        if (va <= 1e-6 || vb <= 1e-6) return double.NegativeInfinity;
        return (sab - sa * sb / n) / Math.Sqrt(va * vb);
    }

    static float[] RenderAlpha(IconImg icon, int rw, int rh)
    {
        var dst = new float[rw * rh];
        double kx = (double)icon.W / rw, ky = (double)icon.H / rh;
        for (int y = 0; y < rh; y++)
            for (int x = 0; x < rw; x++)
            {
                int sx = Math.Min((int)((x + 0.5) * kx), icon.W - 1), sy = Math.Min((int)((y + 0.5) * ky), icon.H - 1);
                dst[y * rw + x] = icon.A[sy * icon.W + sx] * 255f;
            }
        return dst;
    }

    /// <summary>
    /// Цвет фона ячейки — самый частый (фон однородный). Края брать нельзя: туда попадает
    /// оранжевая рамка выбранной ячейки, и фон получается ярким.
    /// </summary>
    static float Background(float[] g, bool[] m, int w, int h)
    {
        var hist = new int[64];
        for (int i = 0; i < g.Length; i++)
            if (m[i]) hist[Math.Clamp((int)g[i] / 4, 0, 63)]++;
        int best = 0;
        for (int b = 1; b < 64; b++)
            if (hist[b] > hist[best]) best = b;
        if (hist[best] == 0) return 25;
        double sum = 0; int n = 0;
        for (int i = 0; i < g.Length; i++)
            if (m[i] && (int)g[i] / 4 == best) { sum += g[i]; n++; }
        return (float)(sum / n);
    }

    static double Score(float[] cg, bool[] cm, int cw, int ch, float bg, IconImg icon, double[] fits)
    {
        double best = double.NegativeInfinity;
        foreach (var f in fits)
        {
            double s = Math.Min((double)cw / icon.W, (double)ch / icon.H) * f;
            int rw = Math.Max(1, (int)Math.Round(icon.W * s)), rh = Math.Max(1, (int)Math.Round(icon.H * s));
            var comp = Render(icon, rw, rh, bg);
            for (int dy = -Shift; dy <= Shift; dy++)
                for (int dx = -Shift; dx <= Shift; dx++)
                {
                    double ncc = Ncc(cg, cm, cw, ch, comp, rw, rh, (cw - rw) / 2 + dx, (ch - rh) / 2 + dy, bg);
                    if (ncc > best) best = ncc;
                }
        }
        return best;
    }

    /// <summary>Иконка, уменьшенная до rw×rh и наложенная на фон ячейки (2×2 выборки на пиксель).</summary>
    static float[] Render(IconImg icon, int rw, int rh, float bg)
    {
        var dst = new float[rw * rh];
        double kx = (double)icon.W / rw, ky = (double)icon.H / rh;
        for (int y = 0; y < rh; y++)
        {
            for (int x = 0; x < rw; x++)
            {
                double ga = 0, a = 0;
                for (int j = 0; j < 2; j++)
                    for (int i = 0; i < 2; i++)
                    {
                        int sx = Math.Min((int)((x + (i + 0.5) / 2) * kx), icon.W - 1);
                        int sy = Math.Min((int)((y + (j + 0.5) / 2) * ky), icon.H - 1);
                        ga += icon.Ga[sy * icon.W + sx];
                        a += icon.A[sy * icon.W + sx];
                    }
                ga /= 4; a /= 4;
                dst[y * rw + x] = (float)(ga + (1 - a) * bg);
            }
        }
        return dst;
    }

    IconImg? Get(string icon)
    {
        if (string.IsNullOrEmpty(icon)) return null;
        if (cache.TryGetValue(icon, out var img)) return img;
        img = null;
        var path = data.IconFile(icon);
        if (path != null)
        {
            try { img = Decode(path); }
            catch (Exception e) { Log.Info($"Иконка {icon} не читается: {e.Message}"); }
        }
        cache[icon] = img;
        return img;
    }

    /// <summary>Для отладки: как иконка выглядит после декодирования (яркость×альфа на сером фоне).</summary>
    public static void Dump(string webp, string png)
    {
        var img = Decode(webp);
        var f = new Frame(img.W, img.H);
        for (int i = 0; i < img.W * img.H; i++)
        {
            byte v = (byte)Math.Clamp(img.Ga[i] + (1 - img.A[i]) * 60, 0, 255);
            f.Px[i * 4] = f.Px[i * 4 + 1] = f.Px[i * 4 + 2] = v;
            f.Px[i * 4 + 3] = 255;
        }
        using var bmp = f.ToBitmap();
        bmp.Save(png);
    }

    /// <summary>WebP декодирует сам Windows (кодек WIC), уменьшая до 96 px по большей стороне.</summary>
    static IconImg Decode(string path)
    {
        using var fs = File.OpenRead(path);
        using var ras = fs.AsRandomAccessStream();
        var dec = BitmapDecoder.CreateAsync(ras).AsTask().GetAwaiter().GetResult();
        double k = Math.Min(1.0, 96.0 / Math.Max(dec.PixelWidth, dec.PixelHeight));
        uint w = (uint)Math.Max(1, Math.Round(dec.PixelWidth * k)), h = (uint)Math.Max(1, Math.Round(dec.PixelHeight * k));
        var tr = new BitmapTransform { ScaledWidth = w, ScaledHeight = h, InterpolationMode = BitmapInterpolationMode.Fant };
        var pd = dec.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, tr,
                                       ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage)
                    .AsTask().GetAwaiter().GetResult();
        var px = pd.DetachPixelData();
        int n = (int)(w * h);
        var ga = new float[n];
        var al = new float[n];
        for (int i = 0; i < n; i++)
        {
            int p = i * 4;
            float a = px[p + 3] / 255f;
            al[i] = a;
            ga[i] = (px[p + 2] * 77 + px[p + 1] * 150 + px[p] * 29) / 256f * a;
        }
        return new IconImg((int)w, (int)h, ga, al);
    }
}
