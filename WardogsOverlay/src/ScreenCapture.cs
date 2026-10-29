using System.Runtime.InteropServices;
using static WardogsOverlay.Native;

namespace WardogsOverlay;

/// <summary>
/// Снимок области экрана через BitBlt — так же работают «Ножницы», OBS (захват экрана), Discord.
/// Буфер DIB переиспользуется, пока не меняется размер области.
/// </summary>
sealed class ScreenCapture : IDisposable
{
    IntPtr memDc, dib, oldObj, bits;
    int bw, bh;

    public Frame Grab(RectI r)
    {
        Ensure(r.W, r.H);
        IntPtr screen = GetDC(IntPtr.Zero);
        try { BitBlt(memDc, 0, 0, r.W, r.H, screen, r.X, r.Y, SRCCOPY); }
        finally { ReleaseDC(IntPtr.Zero, screen); }

        var f = new Frame(r.W, r.H);
        Marshal.Copy(bits, f.Px, 0, f.Px.Length);
        return f;
    }

    void Ensure(int w, int h)
    {
        if (w == bw && h == bh && dib != IntPtr.Zero) return;
        Free();
        IntPtr screen = GetDC(IntPtr.Zero);
        try
        {
            memDc = CreateCompatibleDC(screen);
            var bmi = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = w, biHeight = -h, biPlanes = 1, biBitCount = 32,
            };
            dib = CreateDIBSection(screen, ref bmi, 0, out bits, IntPtr.Zero, 0);
            oldObj = SelectObject(memDc, dib);
            bw = w; bh = h;
        }
        finally { ReleaseDC(IntPtr.Zero, screen); }
    }

    void Free()
    {
        if (memDc != IntPtr.Zero)
        {
            SelectObject(memDc, oldObj);
            DeleteDC(memDc);
            memDc = IntPtr.Zero;
        }
        if (dib != IntPtr.Zero) { DeleteObject(dib); dib = IntPtr.Zero; }
        bw = bh = 0;
    }

    public void Dispose() => Free();
}
