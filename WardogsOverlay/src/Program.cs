using System.Runtime.InteropServices;
using System.Text;

namespace WardogsOverlay;

static class Program
{
    [STAThread]
    static int Main(string[] args)
    {
        Native.SetProcessDpiAwarenessContext(Native.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);

        if (args.Length > 0 && args[0] == "--test")
        {
            AttachConsole(-1); // вывод в консоль, из которой запустили
            Console.OutputEncoding = Encoding.UTF8;
            return TestMode.Run(args.Skip(1).ToArray());
        }

        var settings = Settings.Load();
        L.Current = settings.Language;

        // Второй экземпляр не нужен: две плашки и двойные горячие клавиши. После обновления новая
        // версия стартует, пока старая ещё закрывается, — ждём её.
        bool afterUpdate = args.Contains("--after-update");
        using var single = new Mutex(false, @"Local\WardogsOverlay.SingleInstance");
        bool first;
        try { first = single.WaitOne(afterUpdate ? 20_000 : 0); }
        catch (AbandonedMutexException) { first = true; }
        if (!first)
        {
            MessageBox.Show(L.T("Wardogs оверлей уже запущен — значок в трее рядом с часами.",
                                "Wardogs overlay is already running — see the tray icon next to the clock."),
                            "Wardogs overlay", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return 0;
        }

        Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.ThreadException += (_, e) => Log.Info("Ошибка интерфейса: " + e.Exception);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Log.Info("Фатальная ошибка: " + e.ExceptionObject);

        Log.Info(afterUpdate ? $"Запуск после обновления, версия {Updater.CurrentText}" : $"Запуск, версия {Updater.CurrentText}");
        Updater.CleanupOld();
        Ui.Theme.Load();
        using var overlay = new OverlayForm(settings);
        _ = overlay.Handle; // окно плашки создаётся сразу, но скрыто
        using var main = new MainForm(settings, overlay) { StartHidden = args.Contains("--hidden") };
        Application.Run(main);
        return 0;
    }

    [DllImport("kernel32.dll")] static extern bool AttachConsole(int pid);
}
