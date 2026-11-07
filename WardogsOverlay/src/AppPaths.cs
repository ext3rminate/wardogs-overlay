namespace WardogsOverlay;

/// <summary>
/// Где хранить настройки, базу, журнал и снимки. Обычно — рядом с программой (её можно носить
/// на флешке и обновлять распаковкой поверх). Если туда писать нельзя (Program Files и т.п.) —
/// %LOCALAPPDATA%\WardogsOverlay, а то, что уже лежало рядом с программой, переносится туда.
/// </summary>
static class AppPaths
{
    public static string Root { get; } = Pick();

    /// <summary>Данные лежат рядом с программой, а не в профиле пользователя.</summary>
    public static bool Portable => string.Equals(Root.TrimEnd('\\'), AppContext.BaseDirectory.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);

    static string Pick()
    {
        var exeDir = AppContext.BaseDirectory;
        // WARDOGS_READONLY=1 — для проверки: вести себя так, будто в папку программы писать нельзя.
        bool forceReadOnly = Environment.GetEnvironmentVariable("WARDOGS_READONLY") == "1";
        if (!forceReadOnly && Writable(exeDir)) return exeDir;
        var alt = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WardogsOverlay");
        try
        {
            Directory.CreateDirectory(alt);
            Migrate(exeDir, alt);
        }
        catch { /* без переноса — просто начнём с чистого листа */ }
        return alt;
    }

    static bool Writable(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, $".write-test-{Environment.ProcessId}");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Настройки и скачанная база из папки программы — в новую папку (если там ещё пусто).</summary>
    static void Migrate(string from, string to)
    {
        var settings = Path.Combine(from, "settings.json");
        if (File.Exists(settings) && !File.Exists(Path.Combine(to, "settings.json")))
            File.Copy(settings, Path.Combine(to, "settings.json"));
        var data = Path.Combine(from, "data");
        if (!Directory.Exists(data) || Directory.Exists(Path.Combine(to, "data"))) return;
        foreach (var file in Directory.EnumerateFiles(data, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(to, "data", Path.GetRelativePath(data, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }
}
