using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;

namespace WardogsOverlay;

/// <summary>Новая версия на GitHub: что скачать и что в ней нового.</summary>
sealed record UpdateInfo(Version Version, string Tag, string Notes, string PageUrl, string ExeName, string ExeUrl, long ExeSize, string SumsUrl);

/// <summary>
/// Обновление из GitHub Releases. Скачивается отдельный exe из релиза, сверяется с SHA256SUMS.txt,
/// запущенный exe переименовывается в .old (Windows это позволяет), новый встаёт на его место,
/// и программа перезапускается. Старый файл удаляется при следующем запуске.
/// </summary>
static class Updater
{
    const string Repo = "ext3rminate/wardogs-overlay";
    public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";
    /// <summary>Для проверки без настоящего релиза: адрес, отдающий JSON в формате GitHub releases/latest.</summary>
    static string Api => Environment.GetEnvironmentVariable("WARDOGS_UPDATE_API") is { Length: > 0 } url
        ? url : "https://api.github.com/repos/" + Repo + "/releases/latest";

    public static Version Current { get; } = Normalize(typeof(Updater).Assembly.GetName().Version ?? new Version(0, 0));
    public static string CurrentText => $"{Current.Major}.{Current.Minor}.{Current.Build}";

    static string ExePath => Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "WardogsOverlay.exe");
    static string OldPath => Path.ChangeExtension(ExePath, ".old.exe");
    static string NewPath => Path.ChangeExtension(ExePath, ".new.exe");

    static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(0, v.Build));

    static HttpClient Http()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"WardogsOverlay/{CurrentText}");
        return http;
    }

    /// <summary>Есть ли версия новее текущей. null — нет (или релиз без нужных файлов).</summary>
    public static async Task<UpdateInfo?> CheckAsync()
    {
        using var http = Http();
        http.Timeout = TimeSpan.FromSeconds(20);
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        using var doc = JsonDocument.Parse(await http.GetStringAsync(Api));
        var root = doc.RootElement;
        if (root.TryGetProperty("draft", out var d) && d.ValueKind == JsonValueKind.True) return null;
        if (root.TryGetProperty("prerelease", out var p) && p.ValueKind == JsonValueKind.True) return null;
        var tag = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "";
        if (!TryParseTag(tag, out var version) || version <= Current) return null;

        string? exeUrl = null, sumsUrl = null, exeName = null;
        long exeSize = 0;
        if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            foreach (var a in assets.EnumerateArray())
            {
                var name = a.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                var url = a.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                if (url == null) continue;
                if (name.Equals("SHA256SUMS.txt", StringComparison.OrdinalIgnoreCase)) sumsUrl = url;
                else if (name.StartsWith("WardogsOverlay-", StringComparison.OrdinalIgnoreCase) && name.EndsWith("-win-x64.exe", StringComparison.OrdinalIgnoreCase))
                {
                    exeUrl = url; exeName = name;
                    exeSize = a.TryGetProperty("size", out var s) && s.TryGetInt64(out var sz) ? sz : 0;
                }
            }
        if (exeUrl == null || sumsUrl == null || exeName == null) return null;
        var notes = root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "";
        var page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? ReleasesPage : ReleasesPage;
        return new UpdateInfo(version, tag, notes, page, exeName, exeUrl, exeSize, sumsUrl);
    }

    /// <summary>«v1.2.3» → 1.2.3. Предварительные версии («v1.2.3-beta») не ставим сами.</summary>
    public static bool TryParseTag(string tag, out Version version)
    {
        version = new Version(0, 0);
        var s = tag.TrimStart('v', 'V');
        if (s.Contains('-') || !Version.TryParse(s, out var v)) return false;
        version = Normalize(v);
        return true;
    }

    /// <summary>Можно ли писать в папку программы (из Program Files — нельзя, тогда обновлять вручную).</summary>
    public static bool CanSelfUpdate()
    {
        try
        {
            var probe = Path.Combine(AppContext.BaseDirectory, ".write-test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return Environment.ProcessPath != null;
        }
        catch { return false; }
    }

    /// <summary>Скачать, проверить и поставить новую версию. После успеха нужно вызвать <see cref="Restart"/>.</summary>
    public static async Task InstallAsync(UpdateInfo info, Action<string> report)
    {
        using var http = Http();
        report(L.T("проверяю подпись релиза…", "checking release checksums…"));
        var sums = await http.GetStringAsync(info.SumsUrl);
        var expected = sums.Split('\n')
            .Select(l => l.Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries))
            .Where(parts => parts.Length == 2 && parts[1].Trim().TrimStart('*') == info.ExeName)
            .Select(parts => parts[0].ToLowerInvariant()).FirstOrDefault()
            ?? throw new InvalidDataException(L.T("в SHA256SUMS.txt нет файла " + info.ExeName, "SHA256SUMS.txt has no entry for " + info.ExeName));

        File.Delete(NewPath);
        try
        {
            using (var resp = await http.GetAsync(info.ExeUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                resp.EnsureSuccessStatusCode();
                long total = resp.Content.Headers.ContentLength ?? info.ExeSize;
                await using var src = await resp.Content.ReadAsStreamAsync();
                await using var dst = File.Create(NewPath);
                var buf = new byte[81920];
                long done = 0;
                int read, lastPct = -1;
                while ((read = await src.ReadAsync(buf)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, read));
                    done += read;
                    int pct = total > 0 ? (int)(done * 100 / total) : -1;
                    if (pct != lastPct && pct >= 0)
                    {
                        lastPct = pct;
                        report(L.T($"скачиваю {pct}%", $"downloading {pct}%"));
                    }
                }
            }

            string actual;
            await using (var f = File.OpenRead(NewPath)) actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(f));
            if (actual != expected)
                throw new InvalidDataException(L.T("скачанный файл не совпадает с контрольной суммой релиза", "the downloaded file does not match the release checksum"));

            // Запущенный exe нельзя перезаписать, но можно переименовать.
            report(L.T("ставлю…", "installing…"));
            File.Delete(OldPath);
            File.Move(ExePath, OldPath);
            try { File.Move(NewPath, ExePath); }
            catch
            {
                File.Move(OldPath, ExePath);
                throw;
            }
            Log.Info($"Обновление {CurrentText} → {info.Tag} установлено");
        }
        finally
        {
            try { File.Delete(NewPath); } catch { }
        }
    }

    /// <summary>Запустить новую версию; она дождётся, пока эта закроется.</summary>
    public static void Restart(bool hidden) =>
        Process.Start(new ProcessStartInfo(ExePath, hidden ? "--after-update --hidden" : "--after-update")
                      { UseShellExecute = false, WorkingDirectory = AppContext.BaseDirectory });

    /// <summary>Убрать старый exe после обновления (он мог быть ещё занят, поэтому несколько попыток).</summary>
    public static void CleanupOld()
    {
        Task.Run(async () =>
        {
            for (int i = 0; i < 10 && (File.Exists(OldPath) || File.Exists(NewPath)); i++)
            {
                try { File.Delete(OldPath); File.Delete(NewPath); }
                catch { await Task.Delay(1000); }
            }
        });
    }

    /// <summary>Первые строки описания релиза для окна — без разметки Markdown.</summary>
    public static string ShortNotes(UpdateInfo info, int maxLines = 4)
    {
        var lines = info.Notes.Replace("\r", "").Split('\n')
            .Select(l => l.Trim())
            .SkipWhile(l => !l.StartsWith("- ") && !l.StartsWith("* "))
            .TakeWhile(l => l.StartsWith("- ") || l.StartsWith("* "))
            .Select(l => "• " + l[2..].Replace("`", ""))
            .ToList();
        // В описании сначала английская часть, потом русская — для русского интерфейса берём русскую.
        if (L.Ru)
        {
            var ruStart = info.Notes.IndexOf("## Русский", StringComparison.Ordinal);
            if (ruStart >= 0)
            {
                var ru = ShortNotes(info with { Notes = info.Notes[(ruStart + 10)..] }, int.MaxValue);
                if (ru.Length > 0) lines = ru.Split('\n').ToList();
            }
        }
        var shown = lines.Take(maxLines).ToList();
        if (lines.Count > maxLines) shown.Add(L.T($"…и ещё {lines.Count - maxLines}", $"…and {lines.Count - maxLines} more"));
        return string.Join("\n", shown);
    }
}
