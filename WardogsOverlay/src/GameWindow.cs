using System.Text;
using static WardogsOverlay.Native;

namespace WardogsOverlay;

/// <summary>
/// Ищет окно игры по классу и заголовку. Процесс игры не открывается — только сведения об окне,
/// которые Windows отдаёт любой программе (как панель задач).
/// </summary>
sealed class GameWindow(Settings settings)
{
    readonly uint ownPid = (uint)Environment.ProcessId;
    IntPtr hwnd;
    long nextSearch;

    public IntPtr Handle => hwnd;

    public bool Locate()
    {
        if (hwnd != IntPtr.Zero && IsWindow(hwnd)) return true;
        hwnd = IntPtr.Zero;
        long now = Environment.TickCount64;
        if (now < nextSearch) return false;
        nextSearch = now + 2000;

        var title = new StringBuilder(256);
        var cls = new StringBuilder(256);
        EnumWindows((h, _) =>
        {
            if (!IsWindowVisible(h)) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == ownPid) return true;
            if (!string.IsNullOrEmpty(settings.WindowClass))
            {
                cls.Clear();
                GetClassName(h, cls, cls.Capacity);
                if (!cls.ToString().Equals(settings.WindowClass, StringComparison.OrdinalIgnoreCase)) return true;
            }
            title.Clear();
            GetWindowText(h, title, title.Capacity);
            var t = title.ToString();
            foreach (var part in settings.WindowTitleContains)
            {
                if (t.Contains(part, StringComparison.OrdinalIgnoreCase)) { hwnd = h; return false; }
            }
            return true;
        }, IntPtr.Zero);
        return hwnd != IntPtr.Zero;
    }

    public bool IsForeground => hwnd != IntPtr.Zero && GetForegroundWindow() == hwnd;
    public bool IsMinimized => hwnd != IntPtr.Zero && IsIconic(hwnd);

    /// <summary>Клиентская область окна в экранных координатах (физические пиксели).</summary>
    public RectI ClientRect()
    {
        if (!GetClientRect(hwnd, out var r)) return default;
        var p = new POINT();
        ClientToScreen(hwnd, ref p);
        return new RectI(p.X, p.Y, r.Right - r.Left, r.Bottom - r.Top);
    }
}
