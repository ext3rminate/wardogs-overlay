using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace WardogsOverlay;

readonly record struct RectI(int X, int Y, int W, int H)
{
    public int Right => X + W;
    public int Bottom => Y + H;
    public int CenterX => X + W / 2;
    public int CenterY => Y + H / 2;
    public bool IsEmpty => W <= 0 || H <= 0;

    public static RectI FromLTRB(int l, int t, int r, int b) => new(l, t, r - l, b - t);
    public bool Contains(int x, int y) => x >= X && y >= Y && x < Right && y < Bottom;
    public RectI Offset(int dx, int dy) => new(X + dx, Y + dy, W, H);
    public RectI Inflate(int dx, int dy) => FromLTRB(X - dx, Y - dy, Right + dx, Bottom + dy);

    public RectI Intersect(RectI o)
    {
        int l = Math.Max(X, o.X), t = Math.Max(Y, o.Y), r = Math.Min(Right, o.Right), b = Math.Min(Bottom, o.Bottom);
        return r > l && b > t ? FromLTRB(l, t, r, b) : default;
    }

    public RectI Union(RectI o) => IsEmpty ? o : o.IsEmpty ? this
        : FromLTRB(Math.Min(X, o.X), Math.Min(Y, o.Y), Math.Max(Right, o.Right), Math.Max(Bottom, o.Bottom));

    public RectI Scale(double k) => FromLTRB((int)Math.Floor(X * k), (int)Math.Floor(Y * k),
                                             (int)Math.Ceiling(Right * k), (int)Math.Ceiling(Bottom * k));

    public override string ToString() => $"[{X},{Y} {W}x{H}]";
}

/// <summary>Картинка BGRA, 4 байта на пиксель.</summary>
sealed class Frame
{
    public int W { get; }
    public int H { get; }
    public byte[] Px { get; }

    public Frame(int w, int h) : this(w, h, new byte[w * h * 4]) { }
    public Frame(int w, int h, byte[] px) { W = w; H = h; Px = px; }

    public RectI Bounds => new(0, 0, W, H);

    public (int R, int G, int B) Rgb(int x, int y)
    {
        int i = (y * W + x) * 4;
        return (Px[i + 2], Px[i + 1], Px[i]);
    }

    public Frame Crop(RectI r)
    {
        r = r.Intersect(Bounds);
        var f = new Frame(Math.Max(r.W, 1), Math.Max(r.H, 1));
        if (r.IsEmpty) return f;
        for (int y = 0; y < r.H; y++)
            Buffer.BlockCopy(Px, ((r.Y + y) * W + r.X) * 4, f.Px, y * r.W * 4, r.W * 4);
        return f;
    }

    /// <summary>Уменьшение — усреднение по сетке выборок, увеличение — билинейная интерполяция.</summary>
    public Frame Resize(int nw, int nh)
    {
        var dst = new Frame(nw, nh);
        double sx = (double)W / nw, sy = (double)H / nh;
        if (sx <= 1 && sy <= 1)
        {
            for (int y = 0; y < nh; y++)
            {
                double fy = Math.Clamp((y + 0.5) * sy - 0.5, 0, H - 1);
                int y0 = (int)fy, y1 = Math.Min(y0 + 1, H - 1);
                double wy = fy - y0;
                for (int x = 0; x < nw; x++)
                {
                    double fx = Math.Clamp((x + 0.5) * sx - 0.5, 0, W - 1);
                    int x0 = (int)fx, x1 = Math.Min(x0 + 1, W - 1);
                    double wx = fx - x0;
                    int d = (y * nw + x) * 4;
                    for (int c = 0; c < 4; c++)
                    {
                        double top = Px[(y0 * W + x0) * 4 + c] * (1 - wx) + Px[(y0 * W + x1) * 4 + c] * wx;
                        double bot = Px[(y1 * W + x0) * 4 + c] * (1 - wx) + Px[(y1 * W + x1) * 4 + c] * wx;
                        dst.Px[d + c] = (byte)(top * (1 - wy) + bot * wy + 0.5);
                    }
                }
            }
            return dst;
        }

        int nsx = Math.Clamp((int)Math.Ceiling(sx), 1, 4), nsy = Math.Clamp((int)Math.Ceiling(sy), 1, 4);
        int n = nsx * nsy;
        Span<int> acc = stackalloc int[4];
        for (int y = 0; y < nh; y++)
        {
            for (int x = 0; x < nw; x++)
            {
                acc.Clear();
                for (int j = 0; j < nsy; j++)
                {
                    int syi = Math.Min((int)((y + (j + 0.5) / nsy) * sy), H - 1);
                    int row = syi * W;
                    for (int i = 0; i < nsx; i++)
                    {
                        int sxi = Math.Min((int)((x + (i + 0.5) / nsx) * sx), W - 1);
                        int s = (row + sxi) * 4;
                        acc[0] += Px[s]; acc[1] += Px[s + 1]; acc[2] += Px[s + 2]; acc[3] += Px[s + 3];
                    }
                }
                int d = (y * nw + x) * 4;
                dst.Px[d] = (byte)(acc[0] / n); dst.Px[d + 1] = (byte)(acc[1] / n);
                dst.Px[d + 2] = (byte)(acc[2] / n); dst.Px[d + 3] = (byte)(acc[3] / n);
            }
        }
        return dst;
    }

    /// <summary>Негатив: Windows OCR заметно лучше читает тёмный текст на светлом фоне.</summary>
    public Frame Inverted()
    {
        var f = new Frame(W, H);
        for (int i = 0; i < Px.Length; i += 4)
        {
            f.Px[i] = (byte)(255 - Px[i]); f.Px[i + 1] = (byte)(255 - Px[i + 1]); f.Px[i + 2] = (byte)(255 - Px[i + 2]);
            f.Px[i + 3] = 255;
        }
        return f;
    }

    public byte[] Gray()
    {
        var g = new byte[W * H];
        for (int i = 0, p = 0; i < g.Length; i++, p += 4)
            g[i] = (byte)((Px[p + 2] * 77 + Px[p + 1] * 150 + Px[p] * 29) >> 8);
        return g;
    }

    public static Frame FromBitmap(Bitmap bmp)
    {
        var f = new Frame(bmp.Width, bmp.Height);
        var data = bmp.LockBits(new Rectangle(0, 0, bmp.Width, bmp.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < f.H; y++)
                Marshal.Copy(data.Scan0 + y * data.Stride, f.Px, y * f.W * 4, f.W * 4);
        }
        finally { bmp.UnlockBits(data); }
        return f;
    }

    public Bitmap ToBitmap()
    {
        var bmp = new Bitmap(W, H, PixelFormat.Format32bppArgb);
        var data = bmp.LockBits(new Rectangle(0, 0, W, H), ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
        try
        {
            for (int y = 0; y < H; y++)
            {
                Marshal.Copy(Px, y * W * 4, data.Scan0 + y * data.Stride, W * 4);
            }
        }
        finally { bmp.UnlockBits(data); }
        return bmp;
    }

    /// <summary>Грубый отпечаток области: средняя яркость в сетке 48×24. Для отслеживания изменений меню.</summary>
    public float[] Signature()
    {
        const int gw = 48, gh = 24;
        var sig = new float[gw * gh];
        int stepX = Math.Max(1, W / (gw * 4)), stepY = Math.Max(1, H / (gh * 4));
        var cnt = new int[gw * gh];
        for (int y = 0; y < H; y += stepY)
        {
            int cy = Math.Min(y * gh / H, gh - 1);
            for (int x = 0; x < W; x += stepX)
            {
                int cx = Math.Min(x * gw / W, gw - 1);
                int p = (y * W + x) * 4;
                sig[cy * gw + cx] += (Px[p + 2] * 77 + Px[p + 1] * 150 + Px[p] * 29) >> 8;
                cnt[cy * gw + cx]++;
            }
        }
        for (int i = 0; i < sig.Length; i++) if (cnt[i] > 0) sig[i] /= cnt[i];
        return sig;
    }

    public static float SignatureDiff(float[] a, float[] b)
    {
        if (a.Length != b.Length) return float.MaxValue;
        float s = 0;
        for (int i = 0; i < a.Length; i++) s += Math.Abs(a[i] - b[i]);
        return s / a.Length;
    }
}
