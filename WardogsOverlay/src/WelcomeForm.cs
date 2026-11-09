using System.Text;
using WardogsOverlay.Ui;

namespace WardogsOverlay;

/// <summary>Сведения о системе для отчёта о проблеме и для проверки в окне первого запуска.</summary>
static class Diagnostics
{
    public static (bool Ru, bool En) OcrLanguages()
    {
        var langs = Windows.Media.Ocr.OcrEngine.AvailableRecognizerLanguages.Select(l => l.LanguageTag.ToLowerInvariant()).ToList();
        return (langs.Any(l => l.StartsWith("ru")), langs.Any(l => l.StartsWith("en")));
    }

    /// <summary>Нашлась ли игра и как она запущена: в окне, на весь экран, свёрнута.</summary>
    public static (bool Found, string Text, bool Warn) CheckGame(Settings s)
    {
        var game = new GameWindow(s);
        if (!game.Locate()) return (false, L.T("Игра не найдена — запусти Wardogs и нажми «Проверить» ещё раз.", "Game not found — start Wardogs and press Check again."), true);
        if (game.IsMinimized) return (true, L.T("Игра найдена, но свёрнута — разверни её, чтобы проверить размер окна.", "Game found but minimized — restore it to check the window size."), true);
        var r = game.ClientRect();
        var screen = Screen.FromRectangle(new Rectangle(r.X, r.Y, Math.Max(1, r.W), Math.Max(1, r.H))).Bounds;
        bool full = r.W >= screen.Width && r.H >= screen.Height;
        return full
            ? (true, L.T($"Игра найдена: {r.W}×{r.H} на весь экран. Проверь, что в игре выбран «Оконный без рамки», а не «Полный экран» — иначе снимок экрана будет чёрным (оверлей сам предупредит).",
                         $"Game found: {r.W}×{r.H} full screen. Make sure the game uses Borderless windowed, not Fullscreen — otherwise screen capture is black (the overlay will warn you)."), false)
            : (true, L.T($"Игра найдена: окно {r.W}×{r.H}. Всё в порядке.", $"Game found: {r.W}×{r.H} window. All good."), false);
    }

    public static string SystemInfo(Settings s, Scanner? scanner, GameData? data, IReadOnlyCollection<char> busyKeys)
    {
        var sb = new StringBuilder();
        var (ru, en) = OcrLanguages();
        sb.AppendLine($"Wardogs Overlay {Updater.CurrentText}");
        sb.AppendLine($"Windows {Environment.OSVersion.Version}, .NET {Environment.Version}, {(Environment.Is64BitProcess ? "x64" : "x86")}");
        sb.AppendLine($"Data folder: {(AppPaths.Portable ? "next to the exe" : "%LOCALAPPDATA%\\WardogsOverlay")}");
        sb.AppendLine($"OCR languages: ru={ru}, en={en}");
        foreach (var sc in Screen.AllScreens)
            sb.AppendLine($"Screen: {sc.Bounds.Width}x{sc.Bounds.Height} at {sc.Bounds.X},{sc.Bounds.Y}{(sc.Primary ? " (primary)" : "")}");
        sb.AppendLine($"UI language: {s.Language}, panel scale {s.OverlayScale:0.##}, hide from capture {s.HideFromCapture}");
        sb.AppendLine($"Game: {scanner?.GameStatus ?? "-"}, search {(scanner?.Enabled ?? false ? "on" : "off")}, panel: {scanner?.PlaqueStatus ?? "-"}, capture black: {scanner?.CaptureBlack ?? false}");
        sb.AppendLine($"Busy hotkeys: {(busyKeys.Count == 0 ? "none" : string.Join(", ", busyKeys.Select(k => "Ctrl+Shift+" + k)))}");
        if (data != null) sb.AppendLine($"MetaForge data: {data.Version}, patch {data.PatchTitle} {data.PatchBuild}, weapons {data.Weapons.Count}, attachments {data.Attachments.Count}");
        sb.AppendLine($"Last data check (UTC): {GameData.LastCheck:O}");
        return sb.ToString();
    }
}

/// <summary>
/// Окно первого запуска: язык программы, проверка игры и распознавания, как пользоваться.
/// Открывается само при первом запуске и по ссылке «Помощь» в главном окне.
/// </summary>
sealed class WelcomeForm : Form
{
    readonly Settings settings;
    readonly Action onLanguage;
    float D => DeviceDpi / 96f;
    int Sc(float px) => (int)Math.Round(px * D);

    public WelcomeForm(Settings settings, Action onLanguage)
    {
        this.settings = settings;
        this.onLanguage = onLanguage;
        Theme.Load();
        Icon = AppIcon.Get();
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.UiFont(14);
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        Theme.DarkTitleBar(this);
        _ = Handle;
        Build();
    }

    Label Lbl(string text, float px, int weight = 400, Color? color = null) => new()
    {
        Text = text, UseMnemonic = false, AutoSize = true, BackColor = Color.Transparent, ForeColor = color ?? Theme.Text,
        Font = Theme.UiFont(px, weight), Margin = new Padding(0, 0, 0, Sc(6)), MaximumSize = new Size(Sc(520), 0),
    };

    void Build()
    {
        SuspendLayout();
        foreach (var c in Controls.Cast<Control>().ToArray()) c.Dispose();
        Controls.Clear();
        Text = L.T("Wardogs Overlay — первый запуск", "Wardogs Overlay — getting started");
        var col = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(Sc(28), Sc(22), Sc(28), Sc(22)), BackColor = Theme.Bg, Location = Point.Empty,
        };
        col.Controls.Add(Lbl("Wardogs Overlay", 24, 700));
        col.Controls.Add(Lbl(L.T("Пара шагов — и плашка с характеристиками появится прямо в меню сборки.",
                                 "A couple of steps and the stats panel will appear right in the build menu."), 13, 400, Theme.Muted));

        void Section(string title) { var l = Lbl(title.ToUpperInvariant(), 12, 600, Theme.Faint); l.Margin = new Padding(0, Sc(16), 0, Sc(8)); col.Controls.Add(l); }

        Section(L.T("1. Язык программы", "1. App language"));
        var lang = new Segmented { Items = ["Русский", "English"], SelectedIndex = L.Ru ? 0 : 1, Size = new Size(Sc(240), Sc(40)), Well = Theme.Panel, Margin = new Padding(0, 0, 0, Sc(4)) };
        lang.SelectedIndexChanged += (_, _) =>
        {
            var l = lang.SelectedIndex == 1 ? Lang.En : Lang.Ru;
            if (l == L.Current) return;
            settings.Language = l;
            L.Current = l;
            settings.NotifyChanged();
            onLanguage();
            BeginInvoke(Build);
        };
        col.Controls.Add(lang);

        Section(L.T("2. Игра", "2. The game"));
        col.Controls.Add(Lbl(L.T("В настройках экрана игры выбери «Оконный без рамки». Язык игры — русский или английский, дополнительно ничего ставить не нужно.",
                                 "In the game's display settings choose Borderless windowed. The game can be in English or Russian; nothing else to install."), 13, 400, Theme.TextSoft));
        var (ru, en) = Diagnostics.OcrLanguages();
        col.Controls.Add(Lbl(ru || en
            ? L.T($"Распознавание текста Windows: {(ru && en ? "русский и английский" : ru ? "русский" : "английский")} ✓", $"Windows text recognition: {(ru && en ? "English and Russian" : en ? "English" : "Russian")} ✓")
            : L.T("В Windows нет языков распознавания текста — добавь английский или русский в «Параметры → Время и язык».",
                  "Windows has no text recognition languages — add English or Russian in Settings → Time & language."),
            13, 400, ru || en ? Theme.Good : Theme.BadText));
        var result = Lbl("", 13, 500, Theme.Muted);
        var check = new UiButton { Kind = ButtonKind.Secondary, Text = L.T("Проверить игру", "Check the game"), Height = Sc(40), Font = Theme.UiFont(13, 600), Margin = new Padding(0, Sc(4), 0, Sc(6)) };
        check.Width = check.PreferredWidth(DeviceDpi);
        check.Click += (_, _) =>
        {
            var (found, text, warn) = Diagnostics.CheckGame(settings);
            result.Text = text;
            result.ForeColor = !found || warn ? Theme.Accent : Theme.Good;
        };
        col.Controls.Add(check);
        col.Controls.Add(result);

        Section(L.T("3. Как пользоваться", "3. How to use it"));
        col.Controls.Add(Lbl(L.T("Открой в игре Продавец → Экипировка → Модули (ATTC.) — рядом появится плашка.", "In the game open Vendor → Equipment → Attachments (ATTC.) — the panel appears next to it."), 13, 400, Theme.TextSoft));
        col.Controls.Add(Lbl(L.T("Ctrl+Shift+O — поиск вкл/выкл · Ctrl+Shift+M — двигать плашку · Ctrl+Shift+D — снимок, если что-то не узнаётся.",
                                 "Ctrl+Shift+O — search on/off · Ctrl+Shift+M — move the panel · Ctrl+Shift+D — snapshot if something isn't recognized."), 13, 400, Theme.Muted));
        col.Controls.Add(Lbl(L.T("Закрытое окно сворачивается в трей у часов. Если что-то не работает — «Сообщить о проблеме» внизу главного окна.",
                                 "Closing the window minimizes it to the tray. If something doesn't work, use Report a problem at the bottom of the main window."), 13, 400, Theme.Muted));

        var done = new UiButton { Kind = ButtonKind.Primary, Text = L.T("Готово", "Done"), Height = Sc(44), Font = Theme.UiFont(14, 600), Margin = new Padding(0, Sc(16), 0, 0) };
        done.Width = Math.Max(Sc(160), done.PreferredWidth(DeviceDpi));
        done.Click += (_, _) => Close();
        col.Controls.Add(done);

        Controls.Add(col);
        ResumeLayout(true);
        ClientSize = new Size(Sc(580), col.PreferredSize.Height);
        AcceptButton = null;
    }
}
