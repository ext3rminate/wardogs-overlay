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
    Segmented segLang = null!, segPrio = null!, segTarget = null!, segDist = null!, segMine = null!;
    Toggle tKill = null!, tLife = null!;
    static readonly int[] KillDistances = [10, 50, 100, 200, 300];
    Toggle tSearch = null!, tOptions = null!, tCompact = null!, tMoney = null!, tHide = null!, tTray = null!, tAuto = null!;
    Label lblUpdVal = null!, lblKeysWarn = null!;
    bool blackHintShown;
    Label? lblBannerStatus;
    UiButton btnCheck = null!;
    UiButton? btnInstall;
    // Обновление: что нашли на GitHub и что с ним сейчас происходит.
    UpdateInfo? update;
    string? updateError;
    bool checkingUpdate, installing, updateChecked;
    string? autoFailed;
    readonly System.Windows.Forms.Timer updTimer = new() { Interval = 60_000 };
    long nextUpdateCheck, nextDataCheck;
    bool refreshingData, patchBannerShown;
    Label lblSearchVal = null!, lblGameVal = null!, lblPlaqueVal = null!, lblMoneyVal = null!, lblBase = null!, lblScaleVal = null!, lblOpacityVal = null!;
    PaintBox gameDot = null!;
    Slider slScale = null!, slOpacity = null!;
    TextField budget = null!;
    LinkLabel lnkUpdate = null!;
    readonly ToolStripMenuItem miOpen = new(), miToggle = new(), miDb = new(), miReport = new(), miExit = new();

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
        miReport.Click += (_, _) => ReportProblem();
        menu.Items.AddRange([miOpen, miDb, miToggle, miReport, new ToolStripSeparator(), miExit]);
        tray.ContextMenuStrip = menu;
        tray.DoubleClick += (_, _) => ShowFromTray();
        overlay.BusyKeysChanged += () => { if (IsHandleCreated) BeginInvoke(UpdateKeysWarn); };
        tray.BalloonTipClicked += (_, _) =>
        {
            if (recognizer?.Data is { } d && d.PatchBuild.Length > 0 && settings.SeenPatchBuild != d.PatchBuild)
            {
                settings.SeenPatchBuild = d.PatchBuild;
                settings.Save();
                Rebuild();
                OpenDatabase(DatabaseForm.PageKeys.Count - 1);
            }
            else if (update != null) ShowFromTray();
        };
        ApplyTexts();

        settings.Changed += () =>
        {
            if (!IsHandleCreated) return;
            // Патч посмотрели в окне базы — убираем баннер «Вышел патч».
            if (patchBannerShown && settings.SeenPatchBuild == recognizer?.Data.PatchBuild) BeginInvoke(Rebuild);
            else BeginInvoke(LoadSettingsToUi);
        };
        DpiChanged += (_, _) => BeginInvoke(Rebuild);
    }

    // ---------- разметка ----------

    Label Lbl(string text, float px, int weight = 400, Color? color = null, bool mono = false) => new()
    {
        Text = text, UseMnemonic = false, AutoSize = true, BackColor = Color.Transparent, ForeColor = color ?? Theme.Text,
        Font = mono ? Theme.MonoFont(Math.Max(14, px), weight) : Theme.UiFont(Math.Max(14, px), weight), Margin = Padding.Empty,
    };

    Label Section(string text) => Lbl(text.ToUpperInvariant(), 13, 600, Theme.Muted);

    void BuildLayout()
    {
        lblBannerStatus = null;
        btnInstall = null;
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
        if (update != null && settings.SkippedUpdate != update.Tag) col.Controls.Add(UpdateBanner(inner));
        patchBannerShown = recognizer?.Data is { PatchBuild.Length: > 0 } pd && settings.SeenPatchBuild != null && settings.SeenPatchBuild != pd.PatchBuild;
        if (patchBannerShown) col.Controls.Add(PatchBanner(inner, recognizer!.Data));

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

        // --- карточка «Бой и экономика»
        var fight = CardColumn(inner);
        fight.Body.Controls.Add(Section(L.T("Бой и экономика на плашке", "Combat & economy on the panel")));
        tKill = ToggleRow(fight.Body, inner, L.T("Выстрелы до убийства", "Shots to kill"),
            L.T("Грудь и голова против брони и шлема выбранного уровня, лучший патрон", "Chest and head against the chosen armour and helmet, best ammo"),
            v => settings.ShowKill = v);
        string none = L.T("Нет", "None");
        segTarget = SegRow(fight.Body, inner, L.T("Броня противника", "Enemy armour"), [none, "1", "2", "3", "4"],
            i => settings.TargetArmour = i);
        segDist = SegRow(fight.Body, inner, L.T("Дистанция", "Distance"), KillDistances.Select(d => d + L.T(" м", " m")).ToArray(),
            i => settings.TargetDistance = KillDistances[i]);
        tLife = ToggleRow(fight.Body, inner, L.T("Цена жизни", "Life cost"),
            L.T("Что теряешь без реанимации и сколько раз купишь снова", "What you lose without a revive, and how many rebuys you can afford"),
            v => settings.ShowLifeCost = v);
        segMine = SegRow(fight.Body, inner, L.T("Моя броня и шлем", "My armour and helmet"), [none, "1", "2", "3", "4"],
            i => settings.MyArmour = i);
        col.Controls.Add(fight.Card);

        // --- карточка «Обновления»
        var upd = CardColumn(inner);
        upd.Body.Controls.Add(Section(L.T("Обновления", "Updates")));
        tAuto = ToggleRow(upd.Body, inner, L.T("Обновлять автоматически", "Update automatically"),
            L.T("Ставлю новую версию с GitHub, пока игра свёрнута", "Installs new GitHub releases while the game is in the background"),
            v => { settings.AutoUpdate = v; if (v) BeginInvoke(MaybeAutoInstall); });
        var updRow = new TableLayoutPanel { ColumnCount = 2, Width = inner - Sc(36), Height = Sc(44), BackColor = Color.Transparent, Margin = new Padding(0, Sc(4), 0, 0) };
        updRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        updRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        lblUpdVal = Lbl("", 13, 400, Theme.Muted);
        lblUpdVal.Anchor = AnchorStyles.Left;
        updRow.Controls.Add(lblUpdVal, 0, 0);
        btnCheck = new UiButton { Kind = ButtonKind.Ghost, Text = L.T("Проверить", "Check now"), Height = Sc(38), Font = Theme.UiFont(13, 500), Margin = Padding.Empty, Anchor = AnchorStyles.Right };
        btnCheck.Width = btnCheck.PreferredWidth(DeviceDpi);
        btnCheck.Click += async (_, _) => await CheckForUpdate(manual: true);
        updRow.Controls.Add(btnCheck, 1, 0);
        upd.Body.Controls.Add(updRow);
        col.Controls.Add(upd.Card);

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
        lblKeysWarn = Lbl("", 13, 500, Theme.BadText);
        lblKeysWarn.MaximumSize = new Size(inner - Sc(36), 0);
        lblKeysWarn.Margin = new Padding(0, Sc(8), 0, 0);
        keys.Body.Controls.Add(lblKeysWarn);
        UpdateKeysWarn();
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
                Text = text, UseMnemonic = false, AutoSize = true, LinkColor = Theme.Accent, ActiveLinkColor = Color.FromArgb(255, 194, 97), VisitedLinkColor = Theme.Accent,
                LinkBehavior = LinkBehavior.HoverUnderline, Font = Theme.UiFont(12, 500), BackColor = Theme.Bg, Margin = new Padding(Sc(12), Sc(4), 0, 0),
            };
            l.LinkClicked += (_, _) => a();
            return l;
        }
        lnkUpdate = Link(L.T("Обновить базу", "Update data"), async () => await RefreshData(manual: true));
        links.Controls.AddRange([lnkUpdate, Link(L.T("Журнал", "Log"), ShowLog),
            Link(L.T("Папка", "Folder"), () => Process.Start("explorer.exe", AppPaths.Root))]);
        foot.Controls.Add(links, 1, 0);
        col.Controls.Add(foot);
        var help = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Bg, Margin = new Padding(0, Sc(2), 0, 0), Anchor = AnchorStyles.Right };
        help.Controls.AddRange([Link(L.T("Помощь и проверка игры", "Help & game check"), ShowWelcome),
                                Link(L.T("Сообщить о проблеме", "Report a problem"), ReportProblem)]);
        var helpRow = new FlowLayoutPanel { Width = inner, Height = Sc(26), FlowDirection = FlowDirection.RightToLeft, BackColor = Theme.Bg, Margin = Padding.Empty };
        helpRow.Controls.Add(help);
        col.Controls.Add(helpRow);

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

    /// <summary>Подпись слева, сегменты справа — выбор из нескольких значений в карточке.</summary>
    Segmented SegRow(FlowLayoutPanel body, int inner, string title, string[] items, Action<int> apply)
    {
        int w = inner - Sc(36);
        var row = new TableLayoutPanel { ColumnCount = 2, Width = w, Height = Sc(50), BackColor = Theme.Panel, Margin = Padding.Empty };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Sc(56) * items.Length));
        var l = Lbl(title, 14);
        l.Anchor = AnchorStyles.Left;
        row.Controls.Add(l, 0, 0);
        var seg = new Segmented { Items = items, Size = new Size(Sc(56) * items.Length, Sc(38)), Anchor = AnchorStyles.Right, Margin = Padding.Empty };
        seg.SelectedIndexChanged += (_, _) => { if (loading || seg.SelectedIndex < 0) return; apply(seg.SelectedIndex); settings.NotifyChanged(); };
        row.Controls.Add(seg, 1, 0);
        body.Controls.Add(row);
        return seg;
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

    void OpenDatabase() => OpenDatabase(null);

    void OpenDatabase(int? page)
    {
        if (recognizer == null) return;
        if (dbForm == null || dbForm.IsDisposed)
        {
            dbForm = new DatabaseForm(recognizer.Data, economy, settings);
            if (page is int pg) dbForm.SelectPage(pg);
            dbForm.Show();
        }
        else if (page is int pg) dbForm.SelectPage(pg);
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
        miReport.Text = L.T("Сообщить о проблеме…", "Report a problem…");
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
        tAuto.Checked = settings.AutoUpdate;
        tKill.Checked = settings.ShowKill;
        tLife.Checked = settings.ShowLifeCost;
        segTarget.SelectedIndex = settings.TargetArmour;
        segMine.SelectedIndex = settings.MyArmour;
        segDist.SelectedIndex = Math.Max(0, Array.IndexOf(KillDistances, KillDistances.OrderBy(d => Math.Abs(d - settings.TargetDistance)).First()));
        RefreshUpdateUi();
        segPrio.SelectedIndex = settings.BestPriority switch { Priority.Balance => 1, Priority.Recoil => 2, Priority.Handling => 3, _ => 0 };
        if (!budget.Box.Focused) budget.Box.Text = settings.BestBudget > 0 ? settings.BestBudget.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU")) : "0";
        loading = false;
    }

    // ---------- жизненный цикл ----------

    protected override void SetVisibleCore(bool value)
    {
        // Запуск «в трей»: окно не показывается. Handle уже создан в конструкторе (ради DPI),
        // поэтому первый показ отмечаем флагом, а не по IsHandleCreated.
        if (value && !firstShowHandled)
        {
            firstShowHandled = true;
            if ((settings.StartMinimized || StartHidden) && !Preview)
            {
                value = false;
                BeginInvoke(StartAsync);
            }
        }
        base.SetVisibleCore(value);
    }

    /// <summary>Только для проверки внешнего вида: окно показывается, но поиск и база не запускаются.</summary>
    public bool Preview { get; init; }
    /// <summary>Перезапуск после фонового автообновления: окно не открываем, только сообщаем в трее.</summary>
    public bool StartHidden { get; init; }
    bool firstShowHandled;

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!Preview) StartAsync();
        if (!Preview && settings.IsNew) BeginInvoke(ShowWelcome);
    }

    /// <summary>Только для проверки внешнего вида: прокрутить окно до низа.</summary>
    public void PreviewScrollToEnd() => scroll.AutoScrollPosition = new Point(0, scroll.DisplayRectangle.Height);

    void ShowWelcome()
    {
        if (!Visible) ShowFromTray();
        using var w = new WelcomeForm(settings, () => { dbForm?.Close(); BeginInvoke(Rebuild); });
        w.ShowDialog(this);
    }

    void UpdateKeysWarn()
    {
        if (lblKeysWarn == null || lblKeysWarn.IsDisposed) return;
        var busy = overlay.BusyKeys;
        lblKeysWarn.Text = busy.Count == 0 ? "" : L.T(
            $"Заняты другой программой: {string.Join(", ", busy.Select(k => "Ctrl+Shift+" + k))}. Освободи их в той программе (запись экрана, мессенджер…).",
            $"Taken by another program: {string.Join(", ", busy.Select(k => "Ctrl+Shift+" + k))}. Free them in that program (recorder, messenger…).");
        lblKeysWarn.Visible = busy.Count > 0;
    }

    /// <summary>Собрать архив для отчёта (журнал, настройки, сведения, снимки) и открыть страницу issue.</summary>
    void ReportProblem()
    {
        try
        {
            var info = Diagnostics.SystemInfo(settings, scanner, recognizer?.Data, overlay.BusyKeys);
            var zip = ProblemReport.Create(info);
            ProblemReport.Open(zip);
        }
        catch (Exception ex)
        {
            Log.Info("Отчёт не собрался: " + ex.Message);
            MessageBox.Show(this, L.T("Не получилось собрать отчёт: ", "Could not build the report: ") + ex.Message, "Wardogs overlay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
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
                // База от прошлой версии — докачиваем иконки пушек и журнал патчей. Не вышло (нет сети) —
                // работаем на том, что уже скачано, а не закрываемся.
                lblBase.Text = L.T("докачиваю недостающее…", "downloading missing data…");
                try
                {
                    await Task.Run(() => GameData.DownloadAsync(s => BeginInvoke(() => lblBase.Text = s)));
                    data = GameData.Load();
                }
                catch (Exception ex) { Log.Info("Докачать базу не удалось, работаю на скачанной: " + ex.Message); }
            }
            var ocr = new OcrService();
            recognizer = new Recognizer(ocr) { Data = data };
            if (settings.SeenPatchBuild == null && data.PatchBuild.Length > 0)
            {
                // Первый запуск: текущий патч не «новый», баннер покажем со следующего.
                settings.SeenPatchBuild = data.PatchBuild;
                settings.Save();
            }
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
        if (StartHidden)
            tray.ShowBalloonTip(4000, L.T($"Wardogs оверлей обновлён до {Updater.CurrentText}", $"Wardogs overlay updated to {Updater.CurrentText}"),
                L.T("Работает в трее, как и раньше.", "Running in the tray as before."), ToolTipIcon.Info);

        // Обновления: при запуске и потом раз в 6 часов; автоустановка ждёт, пока игра уйдёт с переднего плана.
        updTimer.Tick += async (_, _) =>
        {
            if (Environment.TickCount64 >= nextDataCheck) await RefreshData(manual: false);
            if (Environment.TickCount64 >= nextUpdateCheck) await CheckForUpdate(manual: false);
            else MaybeAutoInstall();
        };
        updTimer.Start();
        if (settings.SeenPatchBuild != recognizer.Data.PatchBuild) Rebuild(); // патч вышел, пока программа была закрыта
        // База: при запуске, если давно не сверяли (> 1 ч), и потом раз в 6 часов.
        if (DateTime.UtcNow - GameData.LastCheck > TimeSpan.FromHours(1)) await RefreshData(manual: false);
        else nextDataCheck = Environment.TickCount64 + 6 * 3600_000L;
        await CheckForUpdate(manual: false);
    }

    // ---------- обновление базы ----------

    /// <summary>
    /// Сверить базу с MetaForge и, если там новее, подменить её на ходу (без перезапуска).
    /// Вышел новый патч игры — баннер в окне и уведомление в трее.
    /// </summary>
    async Task RefreshData(bool manual)
    {
        if (refreshingData || recognizer == null) return;
        refreshingData = true;
        nextDataCheck = Environment.TickCount64 + 6 * 3600_000L;
        lnkUpdate.Enabled = false;
        var old = recognizer.Data;
        try
        {
            void Report(string s) { if (manual && IsHandleCreated) BeginInvoke(() => lblBase.Text = s); }
            bool changed = await Task.Run(() => GameData.DownloadAsync(Report));
            if (changed)
            {
                var fresh = await Task.Run(GameData.Load);
                recognizer.Data = fresh;
                Log.Info($"База обновлена: {fresh.Version}, патч {fresh.PatchBuild}");
                scanner?.Refresh();
                if (dbForm is { IsDisposed: false })
                {
                    // Окно базы держит старые данные — открываем заново на той же странице.
                    var page = dbForm.CurrentPage;
                    dbForm.Close();
                    OpenDatabase(page);
                }
                if (fresh.PatchBuild.Length > 0 && fresh.PatchBuild != old.PatchBuild)
                {
                    Log.Info($"Новый патч игры: {fresh.PatchTitle} ({fresh.PatchBuild})");
                    Rebuild();
                    tray.ShowBalloonTip(6000, L.T($"Вышел патч: {fresh.PatchTitle}", $"New game patch: {fresh.PatchTitle}"),
                        PatchSummary(fresh) + L.T(" База уже обновлена — нажми, чтобы посмотреть.", " Data is already updated — click to see."), ToolTipIcon.Info);
                }
            }
            else if (manual) lblBase.Text = L.T("База актуальна — на metaforge.app ничего нового.", "Data is up to date — nothing new on metaforge.app.");
        }
        catch (Exception ex)
        {
            Log.Info("Обновление базы не удалось: " + ex.Message);
            if (manual)
                MessageBox.Show(this, L.T("Не получилось обновить базу: ", "Could not update data: ") + ex.Message, "Wardogs overlay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            refreshingData = false;
            lnkUpdate.Enabled = true;
            if (!manual || lblBase.Text.StartsWith(L.T("Скачиваю", "Downloading"))) RefreshStatus();
        }
    }

    static string PatchSummary(GameData d)
    {
        int added = d.Patch.Count(p => p.Added), changed = d.Patch.Count(p => !p.Added && !p.Removed), removed = d.Patch.Count(p => p.Removed);
        var s = L.T($"{added} новых, {changed} изменено", $"{added} added, {changed} changed");
        if (removed > 0) s += L.T($", {removed} удалено", $", {removed} removed");
        return s + ".";
    }

    Card PatchBanner(int inner, GameData d)
    {
        var card = new Card { Width = inner, Height = Sc(100), Padding = new Padding(Sc(18), Sc(14), Sc(18), Sc(14)), Margin = new Padding(0, 0, 0, Sc(14)), Border = Theme.Good };
        var body = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Panel, Margin = Padding.Empty, Dock = DockStyle.Top,
        };
        card.Controls.Add(body);
        body.SizeChanged += (_, _) => card.Height = body.Height + card.Padding.Vertical;
        body.Controls.Add(Lbl(L.T($"Вышел патч: {d.PatchTitle}", $"New game patch: {d.PatchTitle}"), 16, 700, Theme.Good));
        var info = Lbl($"{d.PatchDate} · {PatchSummary(d)} " + L.T("База уже обновлена — цифры на плашке актуальны.", "Data is already updated — panel numbers are current."), 13, 400, Theme.Muted);
        info.MaximumSize = new Size(inner - Sc(36), 0);
        info.Margin = new Padding(0, Sc(6), 0, Sc(10));
        body.Controls.Add(info);
        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Panel, Margin = Padding.Empty };
        void Seen()
        {
            settings.SeenPatchBuild = d.PatchBuild;
            settings.Save();
            Rebuild();
        }
        UiButton Btn(string text, ButtonKind kind, Action click)
        {
            var b = new UiButton { Kind = kind, Text = text, Height = Sc(40), Font = Theme.UiFont(13, 600), Margin = new Padding(0, 0, Sc(8), 0) };
            b.Width = b.PreferredWidth(DeviceDpi);
            b.Click += (_, _) => click();
            buttons.Controls.Add(b);
            return b;
        }
        Btn(L.T("Что изменилось", "What changed"), ButtonKind.Primary, () => { Seen(); OpenDatabase(DatabaseForm.PageKeys.Count - 1); });
        Btn(L.T("Скрыть", "Dismiss"), ButtonKind.Ghost, Seen);
        body.Controls.Add(buttons);
        return card;
    }

    // ---------- обновление программы ----------

    async Task CheckForUpdate(bool manual)
    {
        if (checkingUpdate || installing) return;
        checkingUpdate = true;
        nextUpdateCheck = Environment.TickCount64 + 6 * 3600_000L;
        updateError = null;
        RefreshUpdateUi();
        try
        {
            var found = await Task.Run(Updater.CheckAsync);
            updateChecked = true;
            bool isNew = found != null && found.Tag != update?.Tag;
            update = found;
            if (manual && found != null && settings.SkippedUpdate == found.Tag)
            {
                // «Проверить» руками — показываем и пропущенную ранее версию.
                settings.SkippedUpdate = null;
                settings.Save();
            }
            if (isNew)
            {
                Log.Info($"Доступно обновление {found!.Tag}");
                Rebuild(); // баннер вверху окна
                if (!Visible && !settings.AutoUpdate && settings.SkippedUpdate != found.Tag)
                    tray.ShowBalloonTip(5000, L.T($"Доступна версия {found.Version.ToString(3)}", $"Version {found.Version.ToString(3)} is available"),
                        L.T("Нажми, чтобы открыть окно и обновиться.", "Click to open the window and update."), ToolTipIcon.Info);
            }
        }
        catch (Exception ex)
        {
            Log.Info("Проверка обновлений не удалась: " + ex.Message);
            if (manual) updateError = L.T("не удалось проверить: ", "check failed: ") + ex.Message;
        }
        finally
        {
            checkingUpdate = false;
            RefreshUpdateUi();
        }
        MaybeAutoInstall();
    }

    /// <summary>Автообновление: ставим, только когда игра не на переднем плане (не перезапускаемся посреди матча).</summary>
    void MaybeAutoInstall()
    {
        if (!settings.AutoUpdate || update == null || installing || checkingUpdate) return;
        if (settings.SkippedUpdate == update.Tag || autoFailed == update.Tag || scanner?.GameForeground == true || !Updater.CanSelfUpdate()) return;
        _ = InstallUpdate(auto: true);
    }

    async Task InstallUpdate(bool auto = false)
    {
        if (update == null || installing) return;
        if (!Updater.CanSelfUpdate())
        {
            // Папка только для чтения (например, Program Files) — обновить можно только вручную.
            Process.Start(new ProcessStartInfo(update.PageUrl) { UseShellExecute = true });
            return;
        }
        installing = true;
        updateError = null;
        RefreshUpdateUi();
        try
        {
            await Task.Run(() => Updater.InstallAsync(update, s => BeginInvoke(() => SetUpdateProgress(s))));
            economy.Save();
            // Сам обновился в фоне — новая версия тоже стартует в трее, а не открывает окно поверх всего.
            Updater.Restart(hidden: auto && !Visible);
            ExitApp();
        }
        catch (Exception ex)
        {
            Log.Info("Обновление не удалось: " + ex);
            updateError = L.T("не удалось обновить: ", "update failed: ") + ex.Message;
            installing = false;
            // Сам больше не пробую эту версию (иначе попытка каждую минуту) — кнопка «Обновить» остаётся.
            autoFailed = update.Tag;
            RefreshUpdateUi();
        }
    }

    void SetUpdateProgress(string s)
    {
        if (lblBannerStatus != null) lblBannerStatus.Text = s;
        lblUpdVal.Text = s;
    }

    void RefreshUpdateUi()
    {
        if (lblUpdVal == null) return;
        string text;
        if (installing) text = L.T("устанавливаю обновление…", "installing the update…");
        else if (checkingUpdate) text = L.T("проверяю GitHub…", "checking GitHub…");
        else if (updateError != null) text = updateError;
        else if (update != null) text = L.T($"доступна {update.Version.ToString(3)} (сейчас {Updater.CurrentText})", $"{update.Version.ToString(3)} available (current {Updater.CurrentText})");
        else if (updateChecked) text = L.T($"версия {Updater.CurrentText} — последняя", $"version {Updater.CurrentText} — up to date");
        else text = L.T($"версия {Updater.CurrentText}", $"version {Updater.CurrentText}");
        lblUpdVal.Text = text;
        lblUpdVal.ForeColor = updateError != null ? Theme.BadText : update != null ? Theme.Accent : Theme.Muted;
        lblUpdVal.MaximumSize = new Size(Math.Max(Sc(120), (lblUpdVal.Parent?.Width ?? Sc(400)) - btnCheck.Width - Sc(12)), 0);
        btnCheck.Enabled = !checkingUpdate && !installing;
        if (btnInstall != null) btnInstall.Enabled = !installing;
        if (lblBannerStatus != null && installing) lblBannerStatus.Text = text;
    }

    Card UpdateBanner(int inner)
    {
        var u = update!;
        var card = new Card { Width = inner, Height = Sc(100), Padding = new Padding(Sc(18), Sc(14), Sc(18), Sc(14)), Margin = new Padding(0, 0, 0, Sc(14)), Border = Theme.Accent };
        var body = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Panel, Margin = Padding.Empty, Dock = DockStyle.Top,
        };
        card.Controls.Add(body);
        body.SizeChanged += (_, _) => card.Height = body.Height + card.Padding.Vertical;

        body.Controls.Add(Lbl(L.T($"Доступна версия {u.Version.ToString(3)}", $"Version {u.Version.ToString(3)} is available"), 16, 700, Theme.Accent));
        var notes = Updater.ShortNotes(u);
        if (notes.Length > 0)
        {
            var n = Lbl(notes, 13, 400, Theme.Muted);
            n.MaximumSize = new Size(inner - Sc(36), 0);
            n.Margin = new Padding(0, Sc(6), 0, 0);
            body.Controls.Add(n);
        }
        lblBannerStatus = Lbl(Updater.CanSelfUpdate()
                ? L.T($"Сейчас {Updater.CurrentText}. Программа перезапустится сама, настройки и история сохранятся.",
                      $"Current {Updater.CurrentText}. The app restarts by itself; settings and history are kept.")
                : L.T("Папка программы только для чтения — скачай новую версию со страницы релиза.",
                      "The app folder is read-only — download the new version from the release page."), 12, 400, Theme.Faint);
        lblBannerStatus.MaximumSize = new Size(inner - Sc(36), 0);
        lblBannerStatus.Margin = new Padding(0, Sc(8), 0, Sc(10));
        body.Controls.Add(lblBannerStatus);

        var buttons = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Panel, Margin = Padding.Empty };
        UiButton Btn(string text, ButtonKind kind, Func<Task> click)
        {
            var b = new UiButton { Kind = kind, Text = text, Height = Sc(40), Font = Theme.UiFont(13, 600), Margin = new Padding(0, 0, Sc(8), 0) };
            b.Width = b.PreferredWidth(DeviceDpi);
            b.Click += async (_, _) => await click();
            buttons.Controls.Add(b);
            return b;
        }
        btnInstall = Btn(Updater.CanSelfUpdate() ? L.T("Обновить", "Update") : L.T("Открыть релиз", "Open release"), ButtonKind.Primary, () => InstallUpdate());
        Btn(L.T("Что нового", "What's new"), ButtonKind.Secondary, () =>
        {
            Process.Start(new ProcessStartInfo(u.PageUrl) { UseShellExecute = true });
            return Task.CompletedTask;
        });
        Btn(L.T("Пропустить версию", "Skip this version"), ButtonKind.Ghost, () =>
        {
            settings.SkippedUpdate = u.Tag;
            settings.Save();
            Rebuild();
            return Task.CompletedTask;
        });
        body.Controls.Add(buttons);
        return card;
    }

    void RefreshStatus()
    {
        var s = scanner;
        loading = true;
        tSearch.Checked = s?.Enabled ?? true;
        loading = false;
        lblSearchVal.Text = s == null ? L.T("Запуск…", "Starting…") : s.Enabled ? L.T("Идёт", "Running") : L.T("Выключен", "Off");
        var game = s?.GameStatus ?? "—";
        if (s?.CaptureBlack == true)
        {
            // Эксклюзивный полноэкранный режим: Windows не отдаёт программам картинку игры.
            game = L.T("снимок чёрный — включи «Оконный без рамки»", "capture is black — use Borderless windowed");
            if (!blackHintShown)
            {
                blackHintShown = true;
                tray.ShowBalloonTip(8000, L.T("Оверлей не видит игру", "The overlay can't see the game"),
                    L.T("Снимок экрана чёрный: похоже, игра в режиме «Полный экран». Включи в настройках игры «Оконный без рамки».",
                        "Screen capture is black: the game seems to be in Fullscreen mode. Switch it to Borderless windowed in the game settings."), ToolTipIcon.Warning);
            }
        }
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
        updTimer.Stop();
        tray.Visible = false;
        scanner?.Dispose();
        economy.Save();
        Log.Info("Выход");
        Application.Exit();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) { tray.Dispose(); updTimer.Dispose(); }
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
