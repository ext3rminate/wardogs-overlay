using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace WardogsOverlay;

/// <summary>
/// «Сообщить о проблеме»: собирает в zip журнал, настройки, сведения о системе и последние снимки
/// для отладки, открывает папку с архивом и страницу нового issue на GitHub. Ничего не отправляет сам:
/// архив пользователь прикладывает к issue, если захочет.
/// </summary>
static class ProblemReport
{
    const string IssuesUrl = "https://github.com/ext3rminate/wardogs-overlay/issues/new";

    public static string Create(string systemInfo)
    {
        var dir = Path.Combine(AppPaths.Root, "reports");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"report_{DateTime.Now:yyyyMMdd_HHmmss}.zip");
        using (var zip = ZipFile.Open(path, ZipArchiveMode.Create))
        {
            Add(zip, "system.txt", systemInfo);
            if (File.Exists(Log.FilePath)) Add(zip, "overlay.log", Redact(string.Join(Environment.NewLine, ReadTail(Log.FilePath, 3000))));
            if (File.Exists(Settings.FilePath)) Add(zip, "settings.json", File.ReadAllText(Settings.FilePath));
            // Последние снимки для отладки (кадр игры и ход распознавания) — по ним видно, что не узналось.
            var debug = Path.Combine(AppPaths.Root, "debug");
            if (Directory.Exists(debug))
                foreach (var f in new DirectoryInfo(debug).GetFiles().OrderByDescending(f => f.LastWriteTimeUtc).Take(6))
                    zip.CreateEntryFromFile(f.FullName, "debug/" + f.Name, CompressionLevel.Optimal);
        }
        Log.Info("Отчёт о проблеме собран: " + path);
        return path;
    }

    /// <summary>Открыть папку с архивом и страницу issue (в адресе — только версии программы и Windows).</summary>
    public static void Open(string zipPath)
    {
        Process.Start("explorer.exe", $"/select,\"{zipPath}\"");
        var body = $"**Что случилось:**\n\n\n**Что ожидалось:**\n\n\n---\nWardogs Overlay {Updater.CurrentText} · Windows {Environment.OSVersion.Version}\n" +
                   L.T("Приложите архив из открывшейся папки (перетащите его сюда).", "Attach the archive from the opened folder (drag it here).");
        var url = $"{IssuesUrl}?title={Uri.EscapeDataString(L.T("Проблема: ", "Problem: "))}&body={Uri.EscapeDataString(body)}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    static void Add(ZipArchive zip, string name, string text)
    {
        var e = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
        w.Write(text);
    }

    static IEnumerable<string> ReadTail(string path, int lines)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var sr = new StreamReader(fs);
        var q = new Queue<string>();
        while (sr.ReadLine() is { } l)
        {
            q.Enqueue(l);
            if (q.Count > lines) q.Dequeue();
        }
        return q;
    }

    /// <summary>Пути с именем пользователя Windows в журнале — заменяем на %USERPROFILE%.</summary>
    static string Redact(string s)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return profile.Length > 3 ? s.Replace(profile, "%USERPROFILE%", StringComparison.OrdinalIgnoreCase) : s;
    }
}
