using System.Diagnostics;
using WardogsOverlay.Ui;

namespace WardogsOverlay;

/// <summary>
/// Главное окно по макету редизайна: плитки состояния, крупные действия, карточки настроек.
/// Крестик сворачивает в трей — программа продолжает работать.
/// </summary>
sealed class MainForm : Form
{
    readonly Settings settings;
    readonly OverlayForm overlay;
    readonly NotifyIcon tray;
    readonly Economy economy = new();
    Scanner? scanner;
    Recognizer? recognizer;
    DatabaseForm? dbForm;
    bool exiting, trayHintShown, loading = true, started;

    // Элементы пересоздаются при смене языка и DPI.
    Panel scroll = null!;
    Segmented segLang = null!, segPrio = null!;
    Toggle tSearch = null!, tOptions = null!, tCompact = null!, tMoney = null!, tHide = null!, tTray = null!;
    Label lblSearchVal = null!, lblGameVal = null!, lblPlaqueVal = null!, lblMoneyVal = null!, lblBase = null!, lblScaleVal = null!, lblOpacityVal = null!;
    PaintBox gameDot = null!;
    Slider slScale = null!, slOpacity = null!;
    TextField budget = null!;
    LinkLabel lnkUpdate = null!;
    readonly ToolStripMenuItem miOpen = new(), miToggle = new(), miDb = new(), miExit = new();

    float D => DeviceDpi / 96f;
    int Sc(float px) => (int)Math.Round(px * D);

    public MainForm(Settings settings, OverlayForm overlay)
    {
        this.settings = settings;
        this.overlay = overlay;
        Theme.Load();
        Icon = AppIcon.Get();
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.UiFont(14);
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.FixedSingle;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Theme.DarkTitleBar(this);

        // DeviceDpi достоверен только после создания HWND. Иначе геометрия
        // строится для 96 DPI, а дочерние элементы получают DPI монитора.
        _ = Handle;

        BuildLayout();
        LoadSettingsToUi();

        tray = new NotifyIcon { Icon = AppIcon.Get(), Text = "Wardogs overlay", Visible = true };
        var menu = DarkMenu.Create();
        miOpen.Click += (_, _) => ShowFromTray();
        miToggle.Click += (_, _) => scanner?.SetEnabled(!scanner.Enabled);
        miDb.Click += (_, _) => OpenDatabase();
        miExit.Click += (_, _) => ExitApp();
        menu.Items.AddRange([miOpen, miDb, miToggle, new ToolStripSeparator(), miExit]);
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowFromTray();
        ApplyTexts();

        settings.Changed += () => { if (IsHandleCreated) BeginInvoke(LoadSettingsToUi); };
        DpiChanged += (_, _) => BeginInvoke(Rebuild);
    }

    // ---------- разметка ----------

    Label Lbl(string text, float px, int weight = 400, Color? color = null, bool mono = false) => new()
    {
        Text = text, AutoSize = true, BackColor = Color.Transparent, ForeColor = color ?? Theme.Text,
        Font = mono ? Theme.MonoFont(Math.Max(14, px), weight) : Theme.UiFont(Math.Max(14, px), weight), Margin = Padding.Empty,
    };

    Label Section(string text) => Lbl(text.ToUpperInvariant(), 13, 600, Theme.Muted);

    void BuildLayout()
    {
        int W = Sc(620);
        scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg, Padding = new Padding(Sc(20), Sc(16), Sc(20), Sc(20)) };
        Theme.DarkScrollbars(scroll);
        var col = new TableLayoutPanel
        {
            ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Top,
            BackColor = Theme.Bg, Margin = Padding.Empty, Padding = Padding.Empty,
        };
        col.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, W - Sc(40)));
        int inner = W - Sc(40);

        // --- шапка: логотип, название, язык
        var head = new TableLayoutPanel { ColumnCount = 3, Width = inner, Height = Sc(52), Margin = new Padding(0, 0, 0, Sc(14)), BackColor = Theme.Bg };
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Sc(52)));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        head.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Sc(96)));
        var logo = new PaintBox { Size = new Size(Sc(40), Sc(40)), Margin = new Padding(0, Sc(6), 0, 0) };
        logo.Painter = (g, b) =>
        {
            g.Clear(Theme.Bg);
            var r = new RectangleF(0, 0, b.Width, b.Height);
            Theme.FillRound(g, Theme.Raised, r, b.S(11));
            Theme.StrokeRound(g, Theme.Line, r, b.S(11));
            Icons.Draw(g, IconKind.Crosshair, new RectangleF(b.S(9), b.S(9), b.S(22), b.S(22)), Theme.Accent, b.S(2));
        };
        var titles = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(Sc(4), Sc(6), 0, 0), BackColor = Theme.Bg };
        titles.Controls.Add(Lbl("Wardogs Overlay", 17, 700));
        titles.Controls.Add(Lbl(L.T("Бафы и дебафы обвесов прямо в меню сборки", "Attachment buffs and debuffs right in the build menu"), 12, 400, Theme.Muted));
        segLang = new Segmented { Items = ["RU", "EN"], Size = new Size(Sc(92), Sc(38)), Margin = new Padding(0, Sc(7), 0, 0), Well = Theme.Panel };
        segLang.SelectedIndexChanged += (_, _) =>
        {
            if (loading) return;
            var lang = segLang.SelectedIndex == 1 ? Lang.En : Lang.Ru;
            if (lang == L.Current) return;
            settings.Language = lang;
            L.Current = lang;
            dbForm?.Close();
            BeginInvoke(Rebuild);
            settings.NotifyChanged();
        };
        head.Controls.Add(logo, 0, 0); head.Controls.Add(titles, 1, 0); head.Controls.Add(segLang, 2, 0);
        col.Controls.Add(head);

        // --- плитки состояния
        var tiles = new TableLayoutPanel { ColumnCount = 2, RowCount = 2, Width = inner, Height = Sc(82) * 2 + Sc(10), Margin = new Padding(0, 0, 0, Sc(14)), BackColor = Theme.Bg };
        tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        tiles.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        tiles.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
        Card Tile(string caption, Label value, Control? right = null, Control? lead = null)
        {
            var c = new Card { Dock = DockStyle.Fill, Margin = new Padding(0, 0, Sc(5), Sc(10)), Padding = new Padding(Sc(14), Sc(12), Sc(14), Sc(10)) };
            var cap = Lbl(caption, 12, 400, Theme.Muted);
            cap.Location = new Point(Sc(14), Sc(14));
            c.Controls.Add(cap);
            int vx = Sc(14);
            if (lead != null) { lead.Location = new Point(Sc(14), Sc(48)); c.Controls.Add(lead); vx += Sc(16); }
            value.Location = new Point(vx, Sc(42));
            value.AutoSize = false;
            value.AutoEllipsis = true;
            value.Height = Sc(28);
            c.Controls.Add(value);
            c.Layout += (_, _) => value.Width = Math.Max(1, c.Width - vx - Sc(14));
            if (right != null)
            {
                right.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                c.Controls.Add(right);
                c.Layout += (_, _) => right.Location = new Point(c.Width - right.Width - Sc(14), Sc(11));
            }
            return c;
        }
        tSearch = new Toggle { Size = new Size(Sc(44), Sc(26)) };
        tSearch.CheckedChanged += (_, _) => { if (!loading && scanner != null && scanner.Enabled != tSearch.Checked) scanner.SetEnabled(tSearch.Checked); };
        lblSearchVal = Lbl("—", 15, 600);
        lblGameVal = Lbl("—", 15, 600);
        lblPlaqueVal = Lbl("—", 15, 600);
        lblMoneyVal = Lbl("—", 15, 600, Theme.Good, mono: true);
        gameDot = new PaintBox { Size = new Size(Sc(8), Sc(8)), BackColor = Theme.Panel };
        gameDot.Painter = (g, b) =>
        {
            g.Clear(Theme.Panel);
            using var br = new SolidBrush(scanner?.GameFound == true ? Theme.Good : Theme.Ghost);
            g.FillEllipse(br, 0, 0, b.Width - 1, b.Height - 1);
        };
        tiles.Controls.Add(Tile(L.T("Поиск меню", "Menu search"), lblSearchVal, tSearch), 0, 0);
        var gameTile = Tile(L.T("Игра", "Game"), lblGameVal, lead: gameDot);
        gameTile.Margin = new Padding(Sc(5), 0, 0, Sc(10));
        tiles.Controls.Add(gameTile, 1, 0);
        var plaqueTile = Tile(L.T("Плашка", "Panel"), lblPlaqueVal);
        plaqueTile.Margin = new Padding(0, 0, Sc(5), 0);
        tiles.Controls.Add(plaqueTile, 0, 1);
        var moneyTile = Tile(L.T("Деньги за сессию", "Session money"), lblMoneyVal);
        moneyTile.Margin = new Padding(Sc(5), 0, 0, 0);
        tiles.Controls.Add(moneyTile, 1, 1);
        col.Controls.Add(tiles);

        // --- главные действия
        var actions = new TableLayoutPanel { ColumnCount = 2, Width = inner, Height = Sc(48), Margin = new Padding(0, 0, 0, Sc(14)), BackColor = Theme.Bg };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var btnDb = new UiButton { Kind = ButtonKind.Primary, Text = L.T("База и сборки", "Database & builds"), Icon = IconKind.List, Dock = DockStyle.Fill, Margin = new Padding(0, 0, Sc(5), 0) };
        btnDb.Click += (_, _) => OpenDatabase();
        var btnMaps = new UiButton { Kind = ButtonKind.Secondary, Text = L.T("Карты", "Maps"), Icon = IconKind.Map, Dock = DockStyle.Fill, Margin = new Padding(Sc(5), 0, 0, 0) };
        btnMaps.Click += (_, _) => MapsMenu().Show(btnMaps, new Point(0, btnMaps.Height + Sc(4)));
        actions.Controls.Add(btnDb, 0, 0); actions.Controls.Add(btnMaps, 1, 0);
        col.Controls.Add(actions);

        // --- карточка «Плашка»
        var plaque = CardColumn(inner);
        plaque.Body.Controls.Add(Section(L.T("Плашка", "Panel")));
        (slScale, lblScaleVal) = SliderRow(plaque.Body, inner, L.T("Размер", "Size"), 60, 160, v =>
        {
            settings.OverlayScale = v / 100.0;
            lblScaleVal.Text = $"{v}%";
        });
        (slOpacity, lblOpacityVal) = SliderRow(plaque.Body, inner, L.T("Непрозрачность", "Opacity"), 50, 100, v =>
        {
            settings.Opacity = v / 100.0;
            lblOpacityVal.Text = $"{v}%";
            overlay.ApplyOpacity();
        });
        tOptions = ToggleRow(plaque.Body, inner, L.T("Варианты для выбранного слота", "Options for the selected slot"),
            L.T("Таблица «что поставить» на плашке · Ctrl+Shift+H", "“What to fit” table on the panel · Ctrl+Shift+H"), v => settings.ShowSlotOptions = v);
        tCompact = ToggleRow(plaque.Body, inner, L.T("Компактный режим", "Compact mode"),
            L.T("Только отдача, разброс, прицеливание и зум", "Only recoil, spread, ADS and zoom"), v => settings.CompactMode = v);
        tMoney = ToggleRow(plaque.Body, inner, L.T("Заработок внизу плашки", "Earnings at the bottom"),
            L.T("Сессия +$… · $…/мин", "Session +$… · $…/min"), v => settings.ShowMoney = v);
        tHide = ToggleRow(plaque.Body, inner, L.T("Скрывать от записи и стрима", "Hide from recordings and streams"),
            L.T("Плашку не видно в OBS и Discord", "The panel is invisible to OBS and Discord"), v => { settings.HideFromCapture = v; overlay.ApplyCaptureAffinity(); });
        tTray = ToggleRow(plaque.Body, inner, L.T("Запускать свёрнутым в трей", "Start minimized to tray"),
            L.T("Окно не открывается при старте", "The window does not open at startup"), v => settings.StartMinimized = v);
        var reset = new UiButton { Kind = ButtonKind.Ghost, Text = L.T("Вернуть плашку на место", "Reset panel position"), Height = Sc(40), Font = Theme.UiFont(13, 500), Margin = new Padding(0, Sc(10), 0, 0) };
        reset.Width = reset.PreferredWidth(DeviceDpi);
        reset.Click += (_, _) => { settings.OverlayOffsetX = settings.OverlayOffsetY = 0; settings.NotifyChanged(); };
        plaque.Body.Controls.Add(reset);
        col.Controls.Add(plaque.Card);

        // --- карточка «Подбор сборки»
        var best = CardColumn(inner);
        var bestHead = new TableLayoutPanel { ColumnCount = 2, Width = inner - Sc(36), Height = Sc(22), BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, Sc(12)) };
        bestHead.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bestHead.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        bestHead.Controls.Add(Section(L.T("Подбор сборки", "Best build")), 0, 0);
        bestHead.Controls.Add(Lbl("Ctrl+Shift+B", 11, 500, Theme.Faint, mono: true), 1, 0);
        best.Body.Controls.Add(bestHead);
        segPrio = new Segmented
        {
            Items = [L.T("Выкл", "Off"), L.T("Баланс", "Balance"), L.T("Отдача", "Recoil"), L.T("Прицел", "ADS")],
            Size = new Size(inner - Sc(36), Sc(42)), Margin = new Padding(0, 0, 0, Sc(14)),
        };
        segPrio.SelectedIndexChanged += (_, _) =>
        {
            if (loading) return;
            settings.BestPriority = segPrio.SelectedIndex switch { 1 => Priority.Balance, 2 => Priority.Recoil, 3 => Priority.Handling, _ => null };
            settings.NotifyChanged();
        };
        best.Body.Controls.Add(segPrio);
        var budgetRow = new TableLayoutPanel { ColumnCount = 2, Width = inner - Sc(36), Height = Sc(42), BackColor = Color.Transparent, Margin = new Padding(0, 0, 0, Sc(10)) };
        budgetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        budgetRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Sc(140)));
        var bl = Lbl(L.T("Бюджет на обвесы", "Attachment budget"), 14);
        bl.Anchor = AnchorStyles.Left;
        budgetRow.Controls.Add(bl, 0, 0);
        budget = new TextField("$") { Dock = DockStyle.Fill, Margin = Padding.Empty };
        budget.Box.Font = Theme.MonoFont(14);
        budget.Box.TextAlign = HorizontalAlignment.Right;
        budget.Box.TextChanged += (_, _) =>
        {
            if (loading) return;
            var digits = new string(budget.Box.Text.Where(char.IsDigit).ToArray());
            settings.BestBudget = digits.Length is > 0 and < 8 ? int.Parse(digits) : 0;
            settings.NotifyChanged();
        };
        budgetRow.Controls.Add(budget, 1, 0);
        best.Body.Controls.Add(budgetRow);
        var budgetHint = Lbl(L.T("Прицел и магазин не трогаю — это дело вкуса. 0 — без ограничения.",
                                       "Optic and magazine are left alone — that's taste. 0 — no limit."), 12, 400, Theme.Faint);
        budgetHint.MaximumSize = new Size(inner - Sc(36), 0);
        best.Body.Controls.Add(budgetHint);
        col.Controls.Add(best.Card);

        // --- карточка «Клавиши в игре»
        var keys = CardColumn(inner);
        keys.Body.Controls.Add(Section(L.T("Клавиши в игре", "In-game keys")));
        var keyGrid = new PaintBox { Size = new Size(inner - Sc(36), Sc(108)), BackColor = Theme.Panel, Margin = new Padding(0, Sc(12), 0, Sc(8)) };
        var keyList = new[]
        {
            ("O", L.T("поиск вкл/выкл", "search on/off")), ("S", L.T("запомнить сборку", "save build")),
            ("B", L.T("подбор сборки", "best build")), ("H", L.T("варианты слота", "slot options")),
            ("M", L.T("двигать плашку", "move panel")), ("D", L.T("снимок для отладки", "debug snapshot")),
        };
        var kf = Theme.MonoFont(12, 600);
        var lf = Theme.UiFont(13);
        keyGrid.Painter = (g, b) =>
        {
            g.Clear(Theme.Panel);
            float cw = b.Width / 2f, rh = b.S(36);
            for (int i = 0; i < keyList.Length; i++)
            {
                float x = (i % 2) * cw, y = (i / 2) * rh;
                var cap = new RectangleF(x, y + b.S(4), b.S(28), b.S(28));
                Theme.FillRound(g, Theme.Raised, cap, b.S(7));
                Theme.StrokeRound(g, Theme.Line2, cap, b.S(7));
                using (var p = new Pen(Theme.Line2, b.S(2))) g.DrawLine(p, cap.X + b.S(4), cap.Bottom - b.S(1), cap.Right - b.S(4), cap.Bottom - b.S(1));
                Theme.Draw(g, keyList[i].Item1, kf, Theme.Text, cap, Theme.Align.Center);
                Theme.Draw(g, keyList[i].Item2, lf, Theme.Muted, new RectangleF(x + b.S(38), y, cw - b.S(40), b.S(36)));
            }
        };
        keys.Body.Controls.Add(keyGrid);
        var keysHint = Lbl(L.T("Ctrl+Shift + клавиша. Работают только пока игра на переднем плане.",
                                       "Ctrl+Shift + key. Only while the game is focused."), 12, 400, Theme.Faint);
        keysHint.MaximumSize = new Size(inner - Sc(36), 0);
        keys.Body.Controls.Add(keysHint);
        col.Controls.Add(keys.Card);

        // --- подвал
        var foot = new TableLayoutPanel { ColumnCount = 2, Width = inner, Height = Sc(28), BackColor = Theme.Bg, Margin = new Padding(0, Sc(2), 0, 0) };
        foot.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        foot.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        lblBase = Lbl("—", 12, 400, Theme.Faint);
        lblBase.Anchor = AnchorStyles.Left;
        foot.Controls.Add(lblBase, 0, 0);
        var links = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Bg, Margin = Padding.Empty };
        LinkLabel Link(string text, Action a)
        {
            var l = new LinkLabel
            {
                Text = text, AutoSize = true, LinkColor = Theme.Accent, ActiveLinkColor = Color.FromArgb(255, 194, 97), VisitedLinkColor = Theme.Accent,
                LinkBehavior = LinkBehavior.HoverUnderline, Font = Theme.UiFont(12, 500), BackColor = Theme.Bg, Margin = new Padding(Sc(12), Sc(4), 0, 0),
            };
            l.LinkClicked += (_, _) => a();
            return l;
        }
        lnkUpdate = Link(L.T("Обновить базу", "Update data"), async () => await UpdateDatabase());
        links.Controls.AddRange([lnkUpdate, Link(L.T("Журнал", "Log"), ShowLog),
            Link(L.T("Папка", "Folder"), () => Process.Start("explorer.exe", AppContext.BaseDirectory))]);
        foot.Controls.Add(links, 1, 0);
        col.Controls.Add(foot);

        scroll.Controls.Add(col);
        Controls.Add(scroll);

        var area = Screen.FromControl(this).WorkingArea;
        ClientSize = new Size(W + SystemInformation.VerticalScrollBarWidth, Math.Min(Sc(1180), area.Height - Sc(60)));
    }

    (Card Card, FlowLayoutPanel Body) CardColumn(int inner)
    {
        // Ширина карточки фиксирована, высота — по содержимому (автоматический размер сжал бы и ширину).
        var card = new Card { Width = inner, Height = Sc(100), Padding = new Padding(Sc(18)), Margin = new Padding(0, 0, 0, Sc(14)) };
        var body = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Panel, Margin = Padding.Empty, Dock = DockStyle.Top,
        };
        card.Controls.Add(body);
        body.SizeChanged += (_, _) => card.Height = body.Height + card.Padding.Vertical;
        return (card, body);
    }

    (Slider, Label) SliderRow(FlowLayoutPanel body, int inner, string label, int min, int max, Action<int> apply)
    {
        int w = inner - Sc(36);
        var row = new TableLayoutPanel { ColumnCount = 2, Width = w, Height = Sc(24), BackColor = Theme.Panel, Margin = new Padding(0, Sc(14), 0, Sc(4)) };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.Controls.Add(Lbl(label, 14), 0, 0);
        var val = Lbl("", 13, 400, Theme.Muted, mono: true);
        row.Controls.Add(val, 1, 0);
        body.Controls.Add(row);
        var s = new Slider { Minimum = min, Maximum = max, Width = w, Height = Sc(24), Margin = new Padding(0, 0, 0, Sc(4)), BackColor = Theme.Panel };
        s.ValueChanged += (_, _) => { apply(s.Value); if (!loading) settings.NotifyChanged(); };
        body.Controls.Add(s);
        return (s, val);
    }

    Toggle ToggleRow(FlowLayoutPanel body, int inner, string title, string hint, Action<bool> apply)
    {
        int w = inner - Sc(36);
        var row = new PaintBox { Width = w, Height = Sc(62), BackColor = Theme.Panel, Margin = Padding.Empty };
        var tf = Theme.UiFont(15);
        var hf = Theme.UiFont(13);
        row.Painter = (g, b) =>
        {
            g.Clear(Theme.Panel);
            using (var p = new Pen(Theme.Line)) g.DrawLine(p, 0, 0, b.Width, 0);
            Theme.Draw(g, title, tf, Theme.Text, new RectangleF(0, b.S(10), b.Width - b.S(60), b.S(22)));
            Theme.Draw(g, hint, hf, Theme.Faint, new RectangleF(0, b.S(32), b.Width - b.S(60), b.S(18)));
        };
        var t = new Toggle { Size = new Size(Sc(44), Sc(26)), Location = new Point(w - Sc(44), Sc(18)) };
        t.CheckedChanged += (_, _) => { if (loading) return; apply(t.Checked); settings.NotifyChanged(); };
        row.Controls.Add(t);
        body.Controls.Add(row);
        return t;
    }

    /// <summary>Смена языка или DPI: собрать окно заново.</summary>
    void Rebuild()
    {
        SuspendLayout();
        var old = Controls.Cast<Control>().ToList();
        Controls.Clear();
        foreach (var c in old) c.Dispose();
        BuildLayout();
        LoadSettingsToUi();
        ApplyTexts();
        RefreshStatus();
        ResumeLayout(true);
    }

    void OpenDatabase()
    {
        if (recognizer == null) return;
        if (dbForm == null || dbForm.IsDisposed)
        {
            dbForm = new DatabaseForm(recognizer.Data, economy);
            dbForm.Show();
        }
        else
        {
            if (dbForm.WindowState == FormWindowState.Minimized) dbForm.WindowState = FormWindowState.Normal;
            dbForm.Activate();
        }
    }

    static ContextMenuStrip MapsMenu()
    {
        var m = DarkMenu.Create();
        void Add(string text, string url) =>
            m.Items.Add(text, null, (_, _) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }));
        Add("Bakurani", "https://metaforge.app/wardogs/map/bakurani");
        Add("Ozeti", "https://metaforge.app/wardogs/map/ozeti");
        Add("Zestafona", "https://metaforge.app/wardogs/map/zestafona");
        m.Items.Add(new ToolStripSeparator());
        Add(L.T("Все карты (MetaForge)", "All maps (MetaForge)"), "https://metaforge.app/wardogs/map");
        Add(L.T("Статус серверов", "Server status"), "https://metaforge.app/wardogs/server-status");
        return m;
    }

    void ApplyTexts()
    {
        Text = L.T("Wardogs оверлей", "Wardogs overlay");
        miOpen.Text = L.T("Открыть", "Open");
        miDb.Text = L.T("База и сборки…", "Database & builds…");
        miToggle.Text = L.T("Поиск вкл/выкл", "Search on/off");
        miExit.Text = L.T("Выход", "Exit");
    }

    void LoadSettingsToUi()
    {
        loading = true;
        segLang.SelectedIndex = L.Ru ? 0 : 1;
        slScale.Value = Math.Clamp((int)Math.Round(settings.OverlayScale * 100), slScale.Minimum, slScale.Maximum);
        slOpacity.Value = Math.Clamp((int)Math.Round(settings.Opacity * 100), slOpacity.Minimum, slOpacity.Maximum);
        lblScaleVal.Text = $"{slScale.Value}%";
        lblOpacityVal.Text = $"{slOpacity.Value}%";
        tOptions.Checked = settings.ShowSlotOptions;
        tCompact.Checked = settings.CompactMode;
        tMoney.Checked = settings.ShowMoney;
        tHide.Checked = settings.HideFromCapture;
        tTray.Checked = settings.StartMinimized;
        segPrio.SelectedIndex = settings.BestPriority switch { Priority.Balance => 1, Priority.Recoil => 2, Priority.Handling => 3, _ => 0 };
        if (!budget.Box.Focused) budget.Box.Text = settings.BestBudget > 0 ? settings.BestBudget.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) : "0";
        loading = false;
    }

    // ---------- жизненный цикл ----------

    protected override void SetVisibleCore(bool value)
    {
        // Запуск «в трей»: окно создаётся, но не показывается.
        if (!IsHandleCreated && settings.StartMinimized)
        {
            CreateHandle();
            value = false;
            BeginInvoke(StartAsync);
        }
        base.SetVisibleCore(value);
    }

    /// <summary>Только для проверки внешнего вида: окно показывается, но поиск и база не запускаются.</summary>
    public bool Preview { get; init; }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!Preview) StartAsync();
    }

    async void StartAsync()
    {
        if (started) return;
        started = true;
        RefreshStatus();
        try
        {
            if (!GameData.IsDownloaded)
            {
                lblBase.Text = L.T("первый запуск — скачиваю с metaforge.app…", "first run — downloading from metaforge.app…");
                await Task.Run(() => GameData.DownloadAsync(s => BeginInvoke(() => lblBase.Text = s)));
            }
            var data = GameData.Load();
            if (data.NeedsUpdate)
            {
                // База от прошлой версии — докачиваем иконки пушек и журнал патчей.
                lblBase.Text = L.T("докачиваю недостающее…", "downloading missing data…");
                await Task.Run(() => GameData.DownloadAsync(s => BeginInvoke(() => lblBase.Text = s)));
                data = GameData.Load();
            }
            var ocr = new OcrService();
            recognizer = new Recognizer(ocr) { Data = data };
        }
        catch (Exception ex)
        {
            Log.Info("Запуск не удался: " + ex);
            MessageBox.Show(this, ex.Message, "Wardogs overlay", MessageBoxButtons.OK, MessageBoxIcon.Error);
            ExitApp();
            return;
        }

        scanner = new Scanner(settings, recognizer, overlay, economy);
        economy.Changed += () => { if (IsHandleCreated) BeginInvoke(RefreshStatus); };
        scanner.StatusChanged += () => { if (IsHandleCreated) BeginInvoke(RefreshStatus); };
        scanner.Start();
        Log.Info($"База от {recognizer.Data.Version}: пушек {recognizer.Data.Weapons.Count}, обвесов {recognizer.Data.Attachments.Count}");
        RefreshStatus();
    }

    void RefreshStatus()
    {
        var s = scanner;
        loading = true;
        tSearch.Checked = s?.Enabled ?? true;
        loading = false;
        lblSearchVal.Text = s == null ? L.T("Запуск…", "Starting…") : s.Enabled ? L.T("Идёт", "Running") : L.T("Выключен", "Off");
        var game = s?.GameStatus ?? "—";
        lblGameVal.Text = game.Length > 0 ? char.ToUpper(game[0]) + game[1..] : game;
        gameDot.Invalidate();
        lblPlaqueVal.Text = s?.PlaqueStatus ?? "—";
        var money = economy.Summary();
        lblMoneyVal.Text = money.Replace(L.T("сессия ", "session "), "");
        lblMoneyVal.ForeColor = money.Contains('−') ? Theme.BadText : economy.Current.Points.Count >= 2 ? Theme.Good : Theme.Muted;
        if (recognizer != null)
            lblBase.Text = L.T($"База MetaForge от {recognizer.Data.Version} · {recognizer.Data.Weapons.Count} пушек · {recognizer.Data.Attachments.Count} обвесов",
                               $"MetaForge data {recognizer.Data.Version} · {recognizer.Data.Weapons.Count} weapons · {recognizer.Data.Attachments.Count} attachments");
        tray.Text = s == null ? "Wardogs overlay" : $"Wardogs — {(s.Enabled ? s.PlaqueStatus : L.T("поиск выключен", "search off"))}".Truncate(63);
    }

    async Task UpdateDatabase()
    {
        lnkUpdate.Enabled = false;
        try
        {
            await Task.Run(() => GameData.DownloadAsync(s => BeginInvoke(() => lblBase.Text = s)));
            if (recognizer != null)
            {
                recognizer.Data = GameData.Load();
                Log.Info($"База обновлена: {recognizer.Data.Version}");
                scanner?.Refresh();
            }
        }
        catch (Exception ex)
        {
            Log.Info("Обновление базы не удалось: " + ex.Message);
            MessageBox.Show(this, L.T("Не получилось обновить базу: ", "Could not update data: ") + ex.Message, "Wardogs overlay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            lnkUpdate.Enabled = true;
            RefreshStatus();
        }
    }

    void ShowLog()
    {
        var f = new Form
        {
            Text = L.T("Журнал событий", "Event log"), Icon = AppIcon.Get(), Size = new Size(Sc(860), Sc(540)),
            StartPosition = FormStartPosition.CenterParent, BackColor = Theme.Bg, Padding = new Padding(Sc(12)),
        };
        Theme.DarkTitleBar(f);
        var box = new TextBox
        {
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, WordWrap = false, Dock = DockStyle.Fill,
            Font = Theme.MonoFont(12, 400), BackColor = Theme.Panel, ForeColor = Theme.TextSoft, BorderStyle = BorderStyle.None,
            Text = string.Join(Environment.NewLine, Log.Tail(200)),
        };
        Theme.DarkScrollbars(box);
        f.Controls.Add(box);
        f.Shown += (_, _) => { box.SelectionStart = box.TextLength; box.ScrollToCaret(); };
        f.Show(this);
    }

    void ShowFromTray()
    {
        Show();
        WindowState = FormWindowState.Normal;
        Activate();
    }

    protected override void OnFormClosing(FormClosingEventArgs e)
    {
        if (!exiting && e.CloseReason == CloseReason.UserClosing)
        {
            e.Cancel = true;
            Hide();
            if (!trayHintShown)
            {
                trayHintShown = true;
                tray.ShowBalloonTip(3000, L.T("Wardogs оверлей работает", "Wardogs overlay is running"),
                    L.T("Свернул в трей. Выход — правый клик по значку.", "Minimized to tray. Right-click the icon to exit."), ToolTipIcon.Info);
            }
            return;
        }
        base.OnFormClosing(e);
    }

    void ExitApp()
    {
        exiting = true;
        tray.Visible = false;
        scanner?.Dispose();
        economy.Save();
        Log.Info("Выход");
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) tray.Dispose();
        base.Dispose(disposing);
    }
}

static class AppIcon
{
    static Icon? icon;

    public static Icon Get()
    {
        if (icon != null) return icon;
        using var s = typeof(AppIcon).Assembly.GetManifestResourceStream("WardogsOverlay.app.ico");
        icon = s != null ? new Icon(s) : SystemIcons.Application;
        return icon;
    }
}

static class StringExt
{
    public static string Truncate(this string s, int max) => s.Length <= max ? s : s[..(max - 1)] + "…";
}
