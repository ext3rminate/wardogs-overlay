namespace WardogsOverlay;

static class Log
{
    static readonly object Gate = new();
    static readonly Queue<string> Recent = new();
    public static string FilePath => Path.Combine(AppContext.BaseDirectory, "overlay.log");
    public static bool Echo { get; set; }

    public static void Info(string msg)
    {
        var line = $"{DateTime.Now:HH:mm:ss} {msg}";
        lock (Gate)
        {
            Recent.Enqueue(line);
            while (Recent.Count > 200) Recent.Dequeue();
            try
            {
                var fi = new FileInfo(FilePath);
                if (fi.Exists && fi.Length > 1_000_000) File.Move(FilePath, FilePath + ".old", true);
                File.AppendAllText(FilePath, $"{DateTime.Now:yyyy-MM-dd} {line}{Environment.NewLine}");
            }
            catch { /* журнал не должен ронять программу */ }
        }
        if (Echo) Console.WriteLine(line);
    }

    public static string[] Tail(int n)
    {
        lock (Gate) return Recent.Skip(Math.Max(0, Recent.Count - n)).ToArray();
    }
}
