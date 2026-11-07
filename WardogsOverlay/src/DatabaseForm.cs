#pragma warning disable CS8604, CS8620 // пустые ячейки таблиц (null) — норма
using System.Drawing.Drawing2D;
using WardogsOverlay.Ui;

namespace WardogsOverlay;

/// <summary>
/// Окно «База» по макету редизайна: боковая навигация и восемь страниц — пушки (со сравнением), обвесы,
/// броня, патроны, прогресс, сборка под бюджет, экономика, патч. Данные — MetaForge, деньги — с экрана продавца.
/// </summary>
sealed class DatabaseForm : Form
{
    readonly GameData data;
    readonly Economy economy;
    readonly NavRail rail = new();
    readonly Panel host = new();
    readonly Control?[] pages = new Control?[Keys.Length];
    static readonly string[] Keys = ["weapons", "attachments", "armour", "ammo", "combat", "progress", "loadout", "economy", "patch"];
    Action? economyRefresh;

    float D => DeviceDpi / 96f;
    int Sc(float px) => (int)Math.Round(px * D);

    /// <summary>Убрать и освободить дочерние элементы. Dispose прямо в foreach по Controls пропускает
    /// каждый второй (коллекция меняется на ходу), и они копятся с каждым обновлением.</summary>
    static void ClearControls(Control parent)
    {
        var old = parent.Controls.Cast<Control>().ToArray();
        parent.Controls.Clear();
        foreach (var c in old) c.Dispose();
    }

    public DatabaseForm(GameData data, Economy economy, Settings? settings = null, string? startTab = null)
    {
        this.data = data;
        this.economy = economy;
        this.settings = settings;
        Theme.Load();
        Text = L.T("Wardogs — база и инструменты", "Wardogs — database & tools");
        Icon = AppIcon.Get();
        BackColor = Theme.Bg;
        ForeColor = Theme.Text;
        Font = Theme.UiFont(14);
        AutoScaleMode = AutoScaleMode.None;
        StartPosition = FormStartPosition.CenterScreen;
        Theme.DarkTitleBar(this);
        _ = Handle;
        var area = Screen.PrimaryScreen!.WorkingArea;
        Size = new Size(Math.Min(Sc(1320), area.Width - Sc(40)), Math.Min(Sc(880), area.Height - Sc(40)));
        MinimumSize = new Size(Math.Min(Sc(980), area.Width - Sc(40)), Math.Min(Sc(560), area.Height - Sc(40)));

        rail.Dock = DockStyle.Left;
        rail.Width = Sc(264);
        rail.Title = L.T("Wardogs · База", "Wardogs · Data");
        rail.Footer = $"MetaForge · {data.Version}\n{data.PatchTitle}";
        rail.Items =
        [
            new(L.T("Пушки", "Weapons"), IconKind.Crosshair), new(L.T("Обвесы", "Attachments"), IconKind.Sliders),
            new(L.T("Броня", "Armour"), IconKind.Shield), new(L.T("Патроны", "Ammo"), IconKind.Bullet),
            new(L.T("Бой: урон и TTK", "Combat: damage & TTK"), IconKind.Crosshair),
            new(L.T("Прогресс", "Progression"), IconKind.Steps), new(L.T("Сборка под бюджет", "Budget loadout"), IconKind.Layers),
            new(L.T("Экономика", "Economy"), IconKind.Chart), new(L.T("Патч", "Patch"), IconKind.Doc),
        ];
        rail.SelectedIndexChanged += (_, _) => ShowPage(rail.SelectedIndex);

        host.Dock = DockStyle.Fill;
        host.BackColor = Theme.Bg;
        host.Padding = new Padding(Sc(28), Sc(24), Sc(28), Sc(20));
        Controls.Add(host);
        Controls.Add(rail);

        int start = Math.Max(0, Array.IndexOf(Keys, startTab ?? "weapons"));
        rail.SelectedIndex = start;
        ShowPage(start);
        economy.Changed += OnEconomy;
        DpiChanged += (_, _) => BeginInvoke(RebuildForDpi);
    }

    void RebuildForDpi()
    {
        if (IsDisposed) return;
        SuspendLayout();
        economyRefresh = null;
        host.Controls.Clear();
        for (int i = 0; i < pages.Length; i++)
        {
            pages[i]?.Dispose();
            pages[i] = null;
        }
        rail.Width = Sc(264);
        host.Padding = new Padding(Sc(28), Sc(24), Sc(28), Sc(20));
        var area = Screen.FromControl(this).WorkingArea;
        MinimumSize = new Size(Math.Min(Sc(980), area.Width - Sc(40)), Math.Min(Sc(560), area.Height - Sc(40)));
        ShowPage(rail.SelectedIndex);
        ResumeLayout(true);
    }

    /// <summary>Открыть страницу по номеру (для проверки).</summary>
    public void SelectPage(int i) => rail.SelectedIndex = i;
    public int CurrentPage => rail.SelectedIndex;
    readonly Settings? settings;
    public static IReadOnlyList<string> PageKeys => Keys;

    void OnEconomy() { if (IsHandleCreated && !IsDisposed) BeginInvoke(() => economyRefresh?.Invoke()); }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        economy.Changed -= OnEconomy;
        base.OnFormClosed(e);
    }

    void ShowPage(int i)
    {
        pages[i] ??= i switch
        {
            0 => WeaponsPage(), 1 => AttachmentsPage(), 2 => ArmourPage(), 3 => AmmoPage(),
            4 => CombatPage(), 5 => ProgressPage(), 6 => LoadoutPage(), 7 => EconomyPage(), _ => PatchPage(),
        };
        host.SuspendLayout();
        host.Controls.Clear();
        host.Controls.Add(pages[i]);
        host.ResumeLayout();
    }

    // ---------- общие элементы ----------

    Label Lbl(string text, float px, int weight = 400, Color? color = null, bool mono = false) => new()
    {
        Text = text, AutoSize = true, BackColor = Color.Transparent, ForeColor = color ?? Theme.Text,
        Font = mono ? Theme.MonoFont(px, weight) : Theme.UiFont(px, weight), Margin = Padding.Empty,
    };

    /// <summary>Шапка страницы: крупный заголовок, подпись и элементы справа.</summary>
    Control Header(string title, string subtitle, params Control[] right)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, BackColor = Theme.Bg, Margin = Padding.Empty };
        t.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        t.Controls.Add(Lbl(title, 26, 700));
        var sub = Lbl(subtitle, 13, 400, Theme.Muted);
        sub.Margin = new Padding(Sc(2), Sc(4), 0, 0);
        t.Controls.Add(sub);
        t.SizeChanged += (_, _) => sub.MaximumSize = new Size(Math.Max(1, t.ClientSize.Width - Sc(4)), 0);
        if (right.Length > 0)
        {
            // GrowAndShrink: иначе высота, посчитанная при узком окне (кнопки в столбик), так и остаётся пустым местом.
            var r = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = true, BackColor = Theme.Bg, Margin = new Padding(0, Sc(12), 0, 0) };
            foreach (var c in right) { c.Margin = new Padding(0, 0, Sc(10), Sc(4)); r.Controls.Add(c); }
            t.Controls.Add(r);
        }
        // Высоту считаем сами по ширине страницы: автоматическая запоминает высоту, посчитанную
        // ещё до раскладки (подзаголовок в столбик), и под шапкой остаётся пустое место.
        t.ParentChanged += (_, _) =>
        {
            if (t.Parent is not Control parent) return;
            void Fit()
            {
                int w = parent.ClientSize.Width - parent.Padding.Horizontal;
                if (w <= 0) return;
                sub.MaximumSize = new Size(Math.Max(1, w - Sc(4)), 0);
                t.AutoSize = false;
                t.Height = t.GetPreferredSize(new Size(w, 0)).Height;
            }
            parent.SizeChanged += (_, _) => Fit();
            Fit();
        };
        return t;
    }

    Control Gap(int px) => new Panel { Dock = DockStyle.Top, Height = Sc(px), BackColor = Theme.Bg };

    /// <summary>Таблица внутри скруглённой карточки (и необязательная полоса снизу).</summary>
    Card GridCard(StyledGrid grid, Control? bottom = null)
    {
        var card = new Card { Dock = DockStyle.Fill, Padding = new Padding(1), Radius = 16 };
        card.Controls.Add(grid);
        if (bottom != null) card.Controls.Add(bottom);
        grid.SizeChanged += (_, _) =>
        {
            // Скругляем верх таблицы, чтобы не вылезала за углы карточки.
            using var p = new GraphicsPath();
            float r = Sc(15);
            p.AddArc(0, 0, r * 2, r * 2, 180, 90);
            p.AddArc(grid.Width - r * 2, 0, r * 2, r * 2, 270, 90);
            p.AddLine(grid.Width, r, grid.Width, grid.Height);
            p.AddLine(grid.Width, grid.Height, 0, grid.Height);
            p.CloseFigure();
            grid.Region = new Region(p);
        };
        return card;
    }

    Panel Page() => new() { Dock = DockStyle.Fill, BackColor = Theme.Bg };

    static bool Match(string q, params string[] texts) =>
        q.Length == 0 || texts.Any(t => t.Contains(q, StringComparison.OrdinalIgnoreCase));

    static double? Effect(Attachment a, string attr)
    {
        var mods = a.Mods.Where(m => m.Attr == attr && m.Op == "multiply").ToList();
        return mods.Count == 0 ? null : mods.Aggregate(1.0, (acc, m) => acc * m.Value) - 1;
    }

    /// <summary>Окно с текстом (подробности о предмете) в тёмной теме.</summary>
    void ShowText(string title, IEnumerable<string> lines)
    {
        var f = new Form
        {
            Text = title, Icon = AppIcon.Get(), BackColor = Theme.Bg, Size = new Size(Sc(680), Sc(520)),
            StartPosition = FormStartPosition.CenterParent, Padding = new Padding(Sc(14)),
        };
        Theme.DarkTitleBar(f);
        var box = new TextBox
        {
            Multiline = true, ReadOnly = true, Dock = DockStyle.Fill, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
            BackColor = Theme.Panel, ForeColor = Theme.TextSoft, Font = Theme.UiFont(14), Text = string.Join(Environment.NewLine, lines),
        };
        Theme.DarkScrollbars(box);
        f.Controls.Add(box);
        f.Show(this);
    }

    IEnumerable<string> PatchLines(string id)
    {
        var p = data.Patch.FirstOrDefault(x => x.Id == id);
        if (p == null) yield break;
        yield return "";
        yield return L.T($"Изменения в патче «{data.PatchTitle}» ({data.PatchDate}):", $"Changes in «{data.PatchTitle}» ({data.PatchDate}):");
        if (p.Added) yield return L.T("  новый предмет", "  new item");
        foreach (var c in p.Changes) yield return $"  {L.ChangeLabel(c)}: {c.From} → {c.To}";
    }

    // ---------- пушки ----------

    Control WeaponsPage()
    {
        var page = Page();
        var grid = new StyledGrid();
        grid.AddCol(L.T("Пушка", "Weapon"), 2.4f, right: false, min: 240);
        grid.AddCol(L.T("Урон", "Dmg"), 0.6f); grid.AddCol(L.T("В/мин", "RPM"), 0.6f); grid.AddCol(L.T("Дальн", "Range"), 0.6f);
        grid.AddCol(L.T("Пуля", "Vel."), 0.6f); grid.AddCol("ADS, " + L.T("с", "s"), 0.6f); grid.AddCol(L.T("Подброс", "Climb"), 0.7f);
        grid.AddCol(L.T("Выстр.", "Shots"), 0.6f); grid.AddCol(L.T("Убийство", "TTK"), 0.8f); grid.AddCol(L.T("Цена", "Price"), 0.8f, min: 90);
        grid.AddCol(L.T("Открывается", "Unlock"), 1.8f, right: false, tag: "small", min: 170);
        grid.Columns[3].DefaultCellStyle.ForeColor = grid.Columns[4].DefaultCellStyle.ForeColor = Theme.Muted;

        var classes = data.Weapons.Select(w => w.Sub).Distinct().OrderBy(s => s).ToList();
        static string Sentence(string s) => s.Length <= 3 ? s : char.ToUpper(s[0]) + s[1..].ToLowerInvariant();
        var chips = new ChipBar { Dock = DockStyle.Top, Items = classes.Select(c => Sentence(Stats.ClassName(c))).Prepend(L.T("Все", "All")).ToArray() };
        var search = new TextField(searchIcon: true, placeholder: L.T("Найти пушку…", "Find a weapon…")) { Width = Sc(260), Height = Sc(42) };
        var selected = Lbl("", 13, 400, Theme.Muted);
        var compare = new UiButton { Kind = ButtonKind.Primary, Text = L.T("Сравнить бок о бок", "Compare side by side"), Height = Sc(40), Font = Theme.UiFont(13, 600) };
        compare.Width = compare.PreferredWidth(DeviceDpi);

        void Fill()
        {
            grid.Rows.Clear();
            foreach (var w in data.Weapons.Where(w => w.InVendor))
            {
                if (chips.SelectedIndex > 0 && w.Sub != classes[chips.SelectedIndex - 1]) continue;
                if (!Match(search.Box.Text, w.Name, L.Name(w))) continue;
                var ttk = Combat.TtkChest(data, w, 2);
                object? t = ttk is { } x ? new Tinted(x.Seconds, Combat.Seconds(x.Seconds), x.Seconds <= 0.3 ? Theme.Good : x.Seconds >= 0.53 ? Theme.BadText : Theme.Text) : null;
                int i = grid.Rows.Add(new TwoLine(L.Name(w), Stats.ClassName(w.Sub)), w.Damage, w.Rpm, w.Range, w.Velocity,
                    w.Attr.GetValueOrDefault("AimTime").ToString("0.00"), w.Climb10?.ToString("0.0"), ttk?.Shots, t,
                    w.Price > 0 ? L.Money(w.Price) : "—", L.UnlockText(w.Unlock));
                grid.Rows[i].Tag = w;
            }
        }
        void UpdateSelected()
        {
            var names = grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<Weapon>().Select(L.Name).ToList();
            selected.Text = names.Count == 0 ? L.T("Выдели 2–4 пушки (Ctrl + клик)", "Select 2–4 weapons (Ctrl + click)")
                                             : L.T("Выбрано: ", "Selected: ") + string.Join(", ", names);
        }
        chips.SelectedIndexChanged += (_, _) => Fill();
        search.Box.TextChanged += (_, _) => Fill();
        grid.SelectionChanged += (_, _) => UpdateSelected();
        grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && grid.Rows[e.RowIndex].Tag is Weapon w) ShowWeapon(w); };
        compare.Click += (_, _) => CompareWeapons(grid.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Tag).OfType<Weapon>().Take(4).ToList());

        var bar = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = Sc(60), ColumnCount = 2, BackColor = Theme.Header, Padding = new Padding(Sc(16), Sc(10), Sc(16), Sc(10)) };
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        bar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        selected.Anchor = AnchorStyles.Left;
        bar.Controls.Add(selected, 0, 0);
        bar.Controls.Add(compare, 1, 0);

        page.Controls.Add(GridCard(grid, bar));
        page.Controls.Add(Gap(16));
        page.Controls.Add(chips);
        page.Controls.Add(Gap(14));
        page.Controls.Add(Header(L.T("Пушки", "Weapons"),
            L.T("Без обвесов. Убийство — очередь в грудь обычными патронами через бронежилет 2 ур. (≈)",
                "Without attachments. TTK — burst to the chest with FMJ through level 2 armour (≈)"), search));
        Fill();
        UpdateSelected();
        return page;
    }

    void CompareWeapons(List<Weapon> ws)
    {
        if (ws.Count < 2)
        {
            MessageBox.Show(this, L.T("Выдели в таблице 2–4 пушки (Ctrl + клик).", "Select 2–4 weapons in the table (Ctrl + click)."), Text);
            return;
        }
        var metrics = new List<(string Name, Func<Weapon, double?> Get, bool LowerBetter, string Fmt)>
        {
            (L.T("Урон", "Damage"), w => w.Damage, false, "0"),
            (L.T("Скорострельность, в/мин", "Rate of fire"), w => w.Rpm, false, "0"),
            (L.T("Эффективная дальность, м", "Effective range, m"), w => w.Range, false, "0"),
            (L.T("Скорость пули, м/с", "Muzzle velocity"), w => w.Velocity, false, "0"),
            (L.T("Точность, MOA", "Accuracy, MOA"), w => w.Moa, true, "0.00"),
            (L.T("Время прицеливания, с", "ADS time, s"), w => w.Attr.GetValueOrDefault("AimTime"), true, "0.00"),
            (L.T("Скорость доставания", "Equip speed"), w => w.Attr.GetValueOrDefault("EquipSpeedMultiplier", 1), false, "0.00×"),
            (L.T("Подброс за 10 выстрелов, °", "Climb over 10 rounds, °"), w => w.Climb10, true, "0.0"),
            (L.T("Увод вбок за 10 выстрелов, °", "Drift over 10 rounds, °"), w => w.Drift10, true, "0.00"),
            (L.T("Выстрелов в грудь (без брони)", "Chest shots (no armour)"), w => Combat.TtkChest(data, w, 0)?.Shots, true, "0"),
            (L.T("Выстрелов в грудь (броня 2)", "Chest shots (armour 2)"), w => Combat.TtkChest(data, w, 2)?.Shots, true, "0"),
            (L.T("Время убийства (броня 2), с", "TTK (armour 2), s"), w => Combat.TtkChest(data, w, 2)?.Seconds, true, "0.00"),
            (L.T("Множитель в голову", "Head multiplier"), w => w.Hit.GetValueOrDefault("head"), false, "0.00×"),
            (L.T("Магазин", "Magazine"), w => w.MagSize, false, "0"),
            (L.T("Цена", "Price"), w => w.Price > 0 ? w.Price : null, true, "$#,0"),
        };
        var g = new StyledGrid { MultiSelect = false };
        g.AddCol(L.T("Характеристика", "Stat"), 1.8f, right: false, min: 200);
        foreach (var w in ws) g.AddCol(L.Name(w), 1f);
        foreach (var (name, get, lower, fmt) in metrics)
        {
            var vals = ws.Select(get).ToList();
            var present = vals.Where(v => v != null).Select(v => v!.Value).ToList();
            double? best = present.Count >= 2 && present.Distinct().Count() > 1 ? (lower ? present.Min() : present.Max()) : null;
            var cells = new List<object> { new TwoLine(name, "") };
            foreach (var v in vals)
                cells.Add(v is double x ? new Tinted(x, x.ToString(fmt), x == best ? Theme.Good : Theme.Text) : "—");
            g.Rows.Add(cells.ToArray());
        }
        foreach (DataGridViewColumn c in g.Columns) c.SortMode = DataGridViewColumnSortMode.NotSortable;
        var f = new Form
        {
            Text = L.T("Сравнение пушек", "Weapon comparison"), Icon = AppIcon.Get(), BackColor = Theme.Bg,
            Size = new Size(Sc(320 + 170 * ws.Count), Sc(820)), StartPosition = FormStartPosition.CenterParent, Padding = new Padding(Sc(16)),
        };
        Theme.DarkTitleBar(f);
        var note = Lbl(L.T("Зелёным — лучшее значение. Без обвесов.", "Green — best value. Without attachments."), 12, 400, Theme.Faint);
        note.Dock = DockStyle.Bottom;
        note.Padding = new Padding(0, Sc(8), 0, 0);
        f.Controls.Add(GridCard(g));
        f.Controls.Add(note);
        f.Show(this);
    }

    void ShowWeapon(Weapon w)
    {
        var item = data.Items.FirstOrDefault(i => i.Slug == w.Slug);
        var fits = data.Attachments.Values.Where(a => a.InVendor && (a.Fits?.Contains(w.Slug) ?? false)).GroupBy(a => Stats.SlotName(a.SlotKey)).ToList();
        var text = new List<string>
        {
            $"{L.Name(w)} — {Stats.ClassName(w.Sub)}", "", item?.Description.Replace("\n", Environment.NewLine) ?? "", "",
            L.T("Режимы огня: ", "Fire modes: ") + string.Join(", ", w.FireModes),
            L.T("Открывается: ", "Unlock: ") + L.UnlockText(w.Unlock), "", L.T("Обвесы:", "Attachments:"),
        };
        foreach (var g in fits) text.Add($"  {g.Key}: " + string.Join(", ", g.Select(L.ShortName)));
        text.AddRange(PatchLines(w.Id));
        ShowText(L.Name(w), text);
    }

    // ---------- обвесы ----------

    Control AttachmentsPage()
    {
        var page = Page();
        var effects = new[] { "VerticalRecoilMultiplier", "HorizontalRecoilMultiplier", "SpreadMultiplier", "AimTime", "EquipSpeedMultiplier",
                              "Character.WeaponSway.StabilizationTimeMultiplier" };
        var grid = new StyledGrid();
        grid.AddCol(L.T("Обвес", "Attachment"), 3.2f, right: false, min: 300);
        grid.AddCol(L.T("Цена", "Price"), 0.6f); grid.AddCol(L.T("Зум", "Zoom"), 0.5f);
        foreach (var e in effects) grid.AddCol(Stats.Def(e).Short, 0.7f, tag: Stats.Def(e));
        grid.AddCol(L.T("Открывается", "Unlock"), 1.4f, right: false, tag: "small", min: 130);
        grid.Columns[2].DefaultCellStyle.ForeColor = Theme.Muted;

        var vendor = data.Attachments.Values.Where(a => a.InVendor).ToList();
        var slots = new[] { "", "Sight", "Underbarrel", "Muzzle", "Magazine" };
        var weapons = data.Weapons.Where(w => w.InVendor && w.Slots.Length > 0).OrderBy(w => L.Name(w)).ToList();
        Weapon? weapon = null;
        var seg = new Segmented { Dock = DockStyle.Left, Width = Sc(560), Height = Sc(42), Well = Theme.Panel };
        var segRow = new Panel { Dock = DockStyle.Top, Height = Sc(42), BackColor = Theme.Bg };
        segRow.Controls.Add(seg);
        var pick = new UiButton { Kind = ButtonKind.Secondary, Height = Sc(42), Font = Theme.UiFont(14, 500) };
        var search = new TextField(searchIcon: true, placeholder: L.T("Найти обвес…", "Find an attachment…")) { Width = Sc(230), Height = Sc(42), Fill = Theme.Panel };

        void UpdatePick()
        {
            pick.Text = L.T("К пушке: ", "Weapon: ") + (weapon != null ? L.Name(weapon) : L.T("любая", "any")) + "  ▾";
            pick.Width = pick.PreferredWidth(DeviceDpi);
            var counts = slots.Select(s => vendor.Count(a => (s == "" || a.SlotKey == s) && (weapon == null || (a.Fits?.Contains(weapon.Slug) ?? false)))).ToArray();
            string[] names = [L.T("Все", "All"), Stats.SlotName("Sight"), Stats.SlotName("Underbarrel"), Stats.SlotName("Muzzle"), Stats.SlotName("Magazine")];
            seg.Items = names.Select((n, i) => $"{char.ToUpper(n[0])}{n[1..].ToLowerInvariant()}  {counts[i]}").ToArray();
        }
        void Fill()
        {
            grid.Rows.Clear();
            foreach (var a in vendor.OrderBy(a => a.SlotKey).ThenBy(a => L.Name(a)))
            {
                if (seg.SelectedIndex > 0 && a.SlotKey != slots[seg.SelectedIndex]) continue;
                if (weapon != null && !(a.Fits?.Contains(weapon.Slug) ?? false)) continue;
                if (!Match(search.Box.Text, a.Name, L.Name(a))) continue;
                var row = new List<object?> { new TwoLine(L.Name(a), Stats.SlotName(a)), L.Money(a.ShopPrice), a.Zoom is double z ? z.ToString("0.##") + "×" : "—" };
                row.AddRange(effects.Select(e => (object?)Effect(a, e)));
                row.Add(L.UnlockText(a.Unlock));
                int i = grid.Rows.Add(row.ToArray());
                grid.Rows[i].Tag = a;
            }
        }
        pick.Click += (_, _) =>
        {
            var m = DarkMenu.Create();
            m.Items.Add(L.T("Любая пушка", "Any weapon"), null, (_, _) => { weapon = null; UpdatePick(); Fill(); });
            m.Items.Add(new ToolStripSeparator());
            foreach (var w in weapons) m.Items.Add(L.Name(w), null, (_, _) => { weapon = w; UpdatePick(); Fill(); });
            m.Show(pick, new Point(0, pick.Height + Sc(4)));
        };
        seg.SelectedIndexChanged += (_, _) => Fill();
        search.Box.TextChanged += (_, _) => Fill();
        grid.CellDoubleClick += (_, e) =>
        {
            if (e.RowIndex < 0 || grid.Rows[e.RowIndex].Tag is not Attachment a) return;
            var item = data.Items.FirstOrDefault(i => i.Slug == a.Slug);
            var lines = new List<string> { L.Name(a), "", item?.Description.Replace("\n", Environment.NewLine) ?? "", "" };
            foreach (var mod in a.Mods)
            {
                var d = Stats.Defs.FirstOrDefault(x => x.Attr == mod.Attr);
                if (d == null) continue;
                lines.Add($"  {d.Label}: " + (mod.Op switch { "multiply" => Stats.Pct(mod.Value - 1), "add" => "+" + mod.Value.ToString("0.00"), _ => "= " + mod.Value.ToString("0.##") }));
            }
            lines.Add("");
            lines.Add(L.T("Подходит к: ", "Fits: ") + string.Join(", ", data.Weapons.Where(w => a.Fits?.Contains(w.Slug) ?? false).Select(L.Name)));
            lines.Add(L.T("Открывается: ", "Unlock: ") + L.UnlockText(a.Unlock));
            lines.AddRange(PatchLines(a.Id));
            ShowText(L.Name(a), lines);
        };

        page.Controls.Add(GridCard(grid));
        page.Controls.Add(Gap(16));
        page.Controls.Add(segRow);
        page.Controls.Add(Gap(14));
        page.Controls.Add(Header(L.T("Обвесы", "Attachments"),
            L.T("Изменение стата одним числом. Зелёный — лучше, красный — хуже", "Stat change as one number. Green — better, red — worse"), pick, search));
        UpdatePick();
        Fill();
        return page;
    }

    // ---------- броня ----------

    Control ArmourPage()
    {
        var page = Page();
        page.AutoScroll = true;
        Theme.DarkScrollbars(page);
        var col = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, BackColor = Theme.Bg };
        col.Controls.Add(Header(L.T("Броня", "Armour"),
            L.T("Насколько броня снижает урон по закрытым зонам. Бронебойные патроны пробивают её лучше",
                "How much armour cuts damage to covered zones. AP rounds penetrate it better")));
        var big = Theme.MonoFont(34, 600); var name = Theme.UiFont(15, 600); var sm = Theme.UiFont(12); var mono = Theme.MonoFont(14, 600);
        var badgeF = Theme.MonoFont(11, 600); var zoneF = Theme.UiFont(11);

        foreach (var (title, kind) in new[] { (L.T("Бронежилеты", "Body armour"), "armor"), (L.T("Шлемы", "Helmets"), "helmet") })
        {
            var sec = Lbl(title.ToUpperInvariant(), 13, 600, Theme.Muted);
            sec.Margin = new Padding(0, Sc(18), 0, Sc(10));
            col.Controls.Add(sec);
            var grid = new TableLayoutPanel { ColumnCount = 4, Height = Sc(300), Dock = DockStyle.Top, BackColor = Theme.Bg, Margin = Padding.Empty };
            for (int i = 0; i < 4; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
            double max = kind == "armor" ? 300 : 200;
            for (int lvl = 1; lvl <= 4; lvl++)
            {
                var item = data.Items.FirstOrDefault(i => i.Slug == $"level-{lvl}-{kind}");
                data.Armour.TryGetValue($"level-{lvl}-{kind}", out var info);
                int level = lvl;
                var card = new PaintBox { Dock = DockStyle.Fill, Margin = new Padding(lvl == 1 ? 0 : Sc(6), 0, lvl == 4 ? 0 : Sc(6), 0) };
                card.Painter = (g, b) =>
                {
                    g.Clear(Theme.Bg);
                    var r = new RectangleF(0, 0, b.Width, b.Height);
                    Theme.FillRound(g, Theme.Panel, r, b.S(16));
                    Theme.StrokeRound(g, Theme.Line, r, b.S(16));
                    float x = b.S(16), w = b.Width - b.S(32), y = b.S(16);
                    var badge = new RectangleF(x, y, b.S(52), b.S(24));
                    Theme.FillRound(g, level == 4 ? Theme.Accent : level == 3 ? Theme.Mix(Theme.Accent, Theme.Panel, 0.14) : Theme.Chip, badge, b.S(7));
                    Theme.Draw(g, L.T($"УР. {level}", $"LV. {level}"), badgeF, level == 4 ? Theme.AccentInk : level == 3 ? Theme.Accent : Theme.TextSoft, badge, Theme.Align.Center);
                    Theme.Draw(g, item?.Price is int pr ? L.Money(pr) : "—", mono, Theme.Text, new RectangleF(x, y, w, b.S(24)), Theme.Align.Right);
                    y += b.S(36);
                    Theme.Draw(g, item != null ? L.Name(item) : "—", name, Theme.Text, new RectangleF(x, y, w, b.S(22)));
                    y += b.S(30);
                    var red = $"{info?.Reduction ?? 0}%";
                    Theme.Draw(g, red, big, Theme.Text, new RectangleF(x - b.S(3), y, w, b.S(44)));
                    float rw = Theme.Measure(g, red, big).Width;
                    Theme.Draw(g, L.T("меньше урона", "less damage"), sm, Theme.Muted, new RectangleF(x + rw + b.S(4), y + b.S(14), w - rw, b.S(24)));
                    y += b.S(54);
                    Theme.Draw(g, L.T("Прочность", "Durability"), sm, Theme.Muted, new RectangleF(x, y, w, b.S(18)));
                    using (var mf = Theme.MonoFont(12)) Theme.Draw(g, (info?.Durability ?? 0).ToString(), mf, Theme.Text, new RectangleF(x, y, w, b.S(18)), Theme.Align.Right);
                    y += b.S(24);
                    Theme.FillRound(g, Theme.Track, new RectangleF(x, y, w, b.S(6)), b.S(3));
                    Theme.FillRound(g, Theme.Accent, new RectangleF(x, y, (float)(w * (info?.Durability ?? 0) / max), b.S(6)), b.S(3));
                    y += b.S(18);
                    float zx = x;
                    foreach (var z in info?.Covers ?? [])
                    {
                        var zt = L.Zone(z);
                        float zw = Theme.Measure(g, zt, zoneF).Width + b.S(14);
                        if (zx + zw > x + w) { zx = x; y += b.S(26); }
                        Theme.FillRound(g, Theme.Chip, new RectangleF(zx, y, zw, b.S(22)), b.S(6));
                        Theme.Draw(g, zt, zoneF, Theme.TextSoft, new RectangleF(zx, y, zw, b.S(22)), Theme.Align.Center);
                        zx += zw + b.S(5);
                    }
                    float fy = b.Height - b.S(40);
                    using (var p = new Pen(Theme.LineSoft)) g.DrawLine(p, x, fy, x + w, fy);
                    Theme.Draw(g, L.UnlockText(item?.Unlock), sm, Theme.Muted, new RectangleF(x, fy + b.S(6), w - b.S(50), b.S(28)));
                    using (var mf = Theme.MonoFont(12)) Theme.Draw(g, item?.Weight is double kg ? kg.ToString("0.0") + L.T(" кг", " kg") : "", mf, Theme.Muted, new RectangleF(x, fy + b.S(6), w, b.S(28)), Theme.Align.Right);
                };
                grid.Controls.Add(card, lvl - 1, 0);
            }
            col.Controls.Add(grid);
        }

        var special = data.Items.Where(i => i.Tab == "armor" && i.Price > 0 && !i.Slug.StartsWith("level-")).OrderBy(i => i.Price).ToList();
        if (special.Count > 0)
        {
            var sec = Lbl(L.T("ОСОБОЕ", "SPECIAL"), 13, 600, Theme.Muted);
            sec.Margin = new Padding(0, Sc(18), 0, Sc(10));
            col.Controls.Add(sec);
            var list = new PaintBox { Dock = DockStyle.Top, Height = Sc(12 + 44 * special.Count) };
            var rowF = Theme.UiFont(14, 600); var mf2 = Theme.MonoFont(13); var smf = Theme.UiFont(12);
            list.Painter = (g, b) =>
            {
                g.Clear(Theme.Bg);
                var r = new RectangleF(0, 0, b.Width, b.Height);
                Theme.FillRound(g, Theme.Panel, r, b.S(16));
                Theme.StrokeRound(g, Theme.Line, r, b.S(16));
                float y = b.S(6), x = b.S(16), w = b.Width - b.S(32);
                for (int i = 0; i < special.Count; i++)
                {
                    var it = special[i];
                    data.Armour.TryGetValue(it.Slug, out var info);
                    if (i > 0) using (var p = new Pen(Theme.LineSoft)) g.DrawLine(p, x, y, x + w, y);
                    Theme.Draw(g, L.Name(it), rowF, Theme.Text, new RectangleF(x, y, w * 0.4f, b.S(44)));
                    Theme.Draw(g, info != null ? $"{info.Reduction}%" : "—", mf2, Theme.Muted, new RectangleF(x + w * 0.42f, y, w * 0.1f, b.S(44)));
                    Theme.Draw(g, info != null ? info.Durability.ToString() : "—", mf2, Theme.Muted, new RectangleF(x + w * 0.52f, y, w * 0.1f, b.S(44)));
                    Theme.Draw(g, L.Money(it.Price ?? 0), mf2, Theme.Text, new RectangleF(x + w * 0.62f, y, w * 0.12f, b.S(44)));
                    Theme.Draw(g, L.UnlockText(it.Unlock), smf, Theme.Muted, new RectangleF(x + w * 0.76f, y, w * 0.24f, b.S(44)));
                    y += b.S(44);
                }
            };
            col.Controls.Add(list);
        }
        page.Controls.Add(col);
        return page;
    }

    // ---------- патроны ----------

    Control AmmoPage()
    {
        var page = Page();
        page.AutoScroll = true;
        Theme.DarkScrollbars(page);
        var col = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, BackColor = Theme.Bg };
        col.Controls.Add(Header(L.T("Патроны", "Ammo"),
            L.T("Цена за 10 штук. Трассирующие стоят так же, но открываются отдельно", "Price per 10. Tracers cost the same but unlock separately")));

        var ammo = data.Items.Where(i => i.Tab == "ammunition" && i.Price > 0 && i.Amount > 0 && i.RoundType != null).ToList();
        var groups = ammo.GroupBy(i => i.Calibre ?? "?").OrderBy(g => g.Key).ToList();
        var tagF = Theme.MonoFont(10, 600); var calF = Theme.MonoFont(18, 600); var rowF = Theme.UiFont(14); var subF = Theme.UiFont(11); var priceF = Theme.MonoFont(14, 600);
        (string Tag, Color Bg, Color Fg) Kind(string rt) =>
            rt.Contains("Armor Piercing") || rt.Contains("Slug") ? ("AP", Theme.Mix(Theme.Blue, Theme.Panel, 0.15), Theme.Blue)
            : rt.Contains("Flesh") || rt.Contains("HP") ? ("HP", Theme.Mix(Theme.Accent, Theme.Panel, 0.14), Theme.Accent)
            : rt.Contains("Buck") ? ("BUCK", Theme.Chip, Theme.TextSoft) : ("FMJ", Theme.Chip, Theme.TextSoft);

        var grid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 3, AutoSize = true, BackColor = Theme.Bg, Margin = new Padding(0, Sc(16), 0, 0) };
        for (int i = 0; i < 3; i++) grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
        int n = 0;
        foreach (var gr in groups)
        {
            var rows = gr.Where(i => !i.Name.Contains("(T)")).OrderBy(i => i.Price).ToList();
            if (rows.Count == 0) continue;
            var tracers = gr.Where(i => i.Name.Contains("(T)")).ToList();
            var cal = gr.Key;
            var card = new PaintBox { Dock = DockStyle.Fill, Height = Sc(64 + 48 * rows.Count), Margin = new Padding(n % 3 == 0 ? 0 : Sc(6), 0, n % 3 == 2 ? 0 : Sc(6), Sc(12)) };
            card.Painter = (g, b) =>
            {
                g.Clear(Theme.Bg);
                var r = new RectangleF(0, 0, b.Width, b.Height);
                Theme.FillRound(g, Theme.Panel, r, b.S(16));
                Theme.StrokeRound(g, Theme.Line, r, b.S(16));
                float x = b.S(16), w = b.Width - b.S(32), y = b.S(14);
                Theme.Draw(g, cal, calF, Theme.Text, new RectangleF(x, y, w, b.S(28)));
                y += b.S(40);
                foreach (var it in rows)
                {
                    using (var p = new Pen(Theme.LineSoft)) g.DrawLine(p, x, y, x + w, y);
                    var (tag, bg, fg) = Kind(it.RoundType ?? "");
                    var tr = new RectangleF(x, y + b.S(13), b.S(42), b.S(20));
                    Theme.FillRound(g, bg, tr, b.S(6));
                    Theme.Draw(g, tag, tagF, fg, tr, Theme.Align.Center);
                    var label = L.Ru ? L.AmmoRu(it.RoundType ?? "") : it.RoundType ?? "";
                    Theme.Draw(g, char.ToUpper(label[0]) + label[1..], rowF, Theme.Text, new RectangleF(x + b.S(54), y + b.S(4), w - b.S(120), b.S(22)));
                    var tracer = tracers.FirstOrDefault(t => t.RoundType == it.RoundType);
                    Theme.Draw(g, L.T("трассирующие: ", "tracers: ") + (tracer != null ? L.UnlockText(tracer.Unlock) : "—"), subF, Theme.Faint,
                               new RectangleF(x + b.S(54), y + b.S(25), w - b.S(120), b.S(18)));
                    Theme.Draw(g, L.Money((int)Math.Round(it.Price!.Value * 10.0 / it.Amount!.Value)), priceF, Theme.Text, new RectangleF(x, y, w, b.S(48)), Theme.Align.Right);
                    y += b.S(48);
                }
            };
            grid.Controls.Add(card, n % 3, n / 3);
            n++;
        }
        col.Controls.Add(grid);
        page.Controls.Add(col);
        return page;
    }

    // ---------- прогресс ----------

    Control ProgressPage()
    {
        var page = Page();
        var items = data.Items.Where(i => i.Unlock is { Level: > 0 } && i.Unlock.Role is { } r && r != "None").ToList();
        var chips = new ChipBar { Dock = DockStyle.Top, Items = L.Roles.Select(L.Role).ToArray() };
        // У каждой роли свой уровень (карьера и классы качаются независимо) — храним и показываем по роли.
        var levels = settings?.RoleLevels ?? [];
        int level = 1;
        var lvlBox = new TextField { Width = Sc(76), Height = Sc(42), Fill = Theme.Panel };
        lvlBox.Box.Font = Theme.MonoFont(15, 600);
        lvlBox.Box.TextAlign = HorizontalAlignment.Center;
        lvlBox.Margin = new Padding(Sc(6), 0, Sc(6), 0);
        var minus = new UiButton { Kind = ButtonKind.Ghost, Text = "−", Size = new Size(Sc(42), Sc(42)), Font = Theme.UiFont(18) };
        var plus = new UiButton { Kind = ButtonKind.Ghost, Text = "+", Size = new Size(Sc(42), Sc(42)), Font = Theme.UiFont(18) };
        var stepper = new FlowLayoutPanel { AutoSize = true, WrapContents = false, BackColor = Theme.Bg };
        var cap = Lbl("", 13, 400, Theme.Muted);
        cap.Margin = new Padding(0, Sc(12), Sc(10), 0);
        stepper.Controls.AddRange([cap, minus, lvlBox, plus]);
        bool settingBox = false;
        void SetLevel(int v, bool save)
        {
            level = Math.Clamp(v, 1, 200);
            settingBox = true;
            if (lvlBox.Box.Text != level.ToString()) lvlBox.Box.Text = level.ToString();
            settingBox = false;
            if (!save) return;
            levels[L.Roles[chips.SelectedIndex]] = level;
            settings?.NotifyChanged();
        }

        var timeline = new PaintBox { Dock = DockStyle.Top };
        var scroller = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg };
        Theme.DarkScrollbars(scroller);
        var summary = new PaintBox { Dock = DockStyle.Right, Width = Sc(300) };
        var nameF = Theme.UiFont(14, 500); var secF = Theme.UiFont(11); var costF = Theme.MonoFont(13); var nodeF = Theme.MonoFont(13, 600);
        var sumTitle = Theme.UiFont(12, 600); var sumBig = Theme.MonoFont(30, 600); var sumSm = Theme.UiFont(13);
        List<(int Level, List<DbItem> Items)> rows = [];

        void Fill()
        {
            var role = L.Roles[chips.SelectedIndex];
            rows = items.Where(i => i.Unlock!.Role == role).GroupBy(i => i.Unlock!.Level).OrderBy(g => g.Key)
                        .Select(g => (g.Key, g.ToList())).ToList();
            timeline.Height = Sc(16) + rows.Sum(r => Sc(20 + 26 * r.Items.Count));
            cap.Text = L.T($"Мой уровень · {L.Role(role)}", $"My level · {L.Role(role)}");
            timeline.Invalidate();
            summary.Invalidate();
        }
        timeline.Painter = (g, b) =>
        {
            g.Clear(Theme.Bg);
            var card = new RectangleF(0, 0, b.Width - b.S(2), b.Height);
            Theme.FillRound(g, Theme.Panel, card, b.S(16));
            Theme.StrokeRound(g, Theme.Line, card, b.S(16));
            float y = b.S(8), x = b.S(18);
            for (int i = 0; i < rows.Count; i++)
            {
                var (lvl, its) = rows[i];
                float h = b.S(20 + 26 * its.Count);
                bool done = lvl <= level, current = lvl == level, next = lvl > level && lvl <= level + 5;
                float cx = x + b.S(18);
                using (var p = new Pen(Theme.Line, b.S(2)))
                {
                    if (i > 0) g.DrawLine(p, cx, y, cx, y + b.S(10));
                    if (i < rows.Count - 1) g.DrawLine(p, cx, y + b.S(46), cx, y + h);
                }
                var node = new RectangleF(cx - b.S(18), y + b.S(10), b.S(36), b.S(36));
                using (var br = new SolidBrush(current ? Theme.Accent : next ? Theme.Mix(Theme.Accent, Theme.Panel, 0.14) : Theme.Chip)) g.FillEllipse(br, node);
                if (next) using (var p = new Pen(Theme.Mix(Theme.Accent, Theme.Panel, 0.4))) g.DrawEllipse(p, node);
                Theme.Draw(g, lvl.ToString(), nodeF, current ? Theme.AccentInk : next ? Theme.Accent : Theme.Muted, node, Theme.Align.Center);
                float ty = y + b.S(12);
                var textColor = done && !current ? Theme.Faint : Theme.Text;
                foreach (var it in its)
                {
                    var nm = L.Name(it);
                    float tx = x + b.S(52);
                    Theme.Draw(g, nm, nameF, textColor, new RectangleF(tx, ty, b.Width * 0.5f, b.S(24)));
                    float nw = Math.Min(Theme.Measure(g, nm, nameF).Width, b.Width * 0.5f);
                    var sec = L.TabName(it.Tab);
                    float sw = Theme.Measure(g, sec, secF).Width + b.S(14);
                    var sr = new RectangleF(tx + nw + b.S(12), ty + b.S(2), sw, b.S(20));
                    Theme.FillRound(g, Theme.Chip, sr, b.S(6));
                    Theme.Draw(g, sec, secF, Theme.Muted, sr, Theme.Align.Center);
                    Theme.Draw(g, it.Unlock!.Cash > 0 ? L.Money(it.Unlock.Cash) : "—", costF, next ? Theme.Accent : Theme.Muted,
                               new RectangleF(x, ty, b.Width - x - b.S(20), b.S(24)), Theme.Align.Right);
                    ty += b.S(26);
                }
                y += h;
            }
        };
        summary.Painter = (g, b) =>
        {
            g.Clear(Theme.Bg);
            var next = rows.Where(r => r.Level > level && r.Level <= level + 5).SelectMany(r => r.Items.Select(i => (r.Level, i))).ToList();
            int sum = next.Sum(n => n.i.Unlock!.Cash);
            float h = b.S(150 + 26 * Math.Min(next.Count, 8));
            var card = new RectangleF(b.S(16), 0, b.Width - b.S(16), h);
            Theme.FillRound(g, Theme.Panel, card, b.S(16));
            Theme.StrokeRound(g, Theme.Line, card, b.S(16));
            float x = card.X + b.S(18), w = card.Width - b.S(36), y = b.S(18);
            Theme.Draw(g, L.T("СЛЕДУЮЩИЕ 5 УРОВНЕЙ", "NEXT 5 LEVELS"), sumTitle, Theme.Muted, new RectangleF(x, y, w, b.S(20)));
            y += b.S(28);
            Theme.Draw(g, L.Money(sum), sumBig, Theme.Accent, new RectangleF(x, y, w, b.S(40)));
            y += b.S(42);
            Theme.Draw(g, L.T($"нужно на открытие {next.Count} предм.", $"to unlock {next.Count} items"), sumSm, Theme.Muted, new RectangleF(x, y, w, b.S(20)));
            y += b.S(30);
            using (var p = new Pen(Theme.Line)) g.DrawLine(p, x, y, x + w, y);
            y += b.S(10);
            foreach (var (lvl, it) in next.Take(8))
            {
                Theme.Draw(g, L.T($"Ур. {lvl}", $"Lv. {lvl}"), sumSm, Theme.Muted, new RectangleF(x, y, b.S(60), b.S(24)));
                Theme.Draw(g, L.Name(it), sumSm, Theme.Text, new RectangleF(x + b.S(60), y, w - b.S(60), b.S(24)), Theme.Align.Right);
                y += b.S(26);
            }
        };
        chips.SelectedIndexChanged += (_, _) => { SetLevel(levels.GetValueOrDefault(L.Roles[chips.SelectedIndex], 1), save: false); Fill(); };
        minus.Click += (_, _) => { SetLevel(level - 1, save: true); Fill(); };
        plus.Click += (_, _) => { SetLevel(level + 1, save: true); Fill(); };
        lvlBox.Box.TextChanged += (_, _) =>
        {
            if (settingBox || !int.TryParse(new string(lvlBox.Box.Text.Where(char.IsAsciiDigit).ToArray()), out var v) || v < 1) return;
            level = Math.Clamp(v, 1, 200);
            levels[L.Roles[chips.SelectedIndex]] = level;
            settings?.NotifyChanged();
            Fill();
        };
        SetLevel(levels.GetValueOrDefault(L.Roles[0], 1), save: false);

        scroller.Controls.Add(timeline);
        var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        body.Controls.Add(scroller);
        body.Controls.Add(summary);
        page.Controls.Add(body);
        page.Controls.Add(Gap(16));
        page.Controls.Add(chips);
        page.Controls.Add(Gap(14));
        page.Controls.Add(Header(L.T("Прогресс", "Progression"),
            L.T("Что открывается на каждом уровне роли. У карьеры и каждого класса свой уровень — укажи их, и «Сборка под бюджет» будет предлагать только открытое.",
                "What unlocks at each role level. Career and every class level up separately — set them and Budget loadout will only suggest what you have unlocked."), stepper));
        Fill();
        return page;
    }

    // ---------- сборка под бюджет ----------

    Control LoadoutPage()
    {
        var page = Page();
        var form = new Card { Dock = DockStyle.Left, Width = Sc(320), Padding = new Padding(Sc(18)), Radius = 16 };
        var stack = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, BackColor = Theme.Panel };
        form.Controls.Add(stack);
        int iw = Sc(320 - 36);
        Label Cap(string t) { var l = Lbl(t.ToUpperInvariant(), 12, 600, Theme.Muted); l.Margin = new Padding(0, Sc(4), 0, Sc(8)); return l; }

        stack.Controls.Add(Cap(L.T("Бюджет", "Budget")));
        var budget = new TextField("$") { Width = iw, Height = Sc(48), Margin = new Padding(0, 0, 0, Sc(8)) };
        budget.Box.Font = Theme.MonoFont(20, 600);
        budget.Box.Text = "6 000";
        stack.Controls.Add(budget);
        var quick = new TableLayoutPanel { ColumnCount = 3, RowCount = 1, Width = iw, Height = Sc(40), BackColor = Theme.Panel, Margin = new Padding(0, 0, 0, Sc(18)) };
        for (int i = 0; i < 3; i++) quick.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.3f));
        // Строка на всю высоту: иначе она растягивается под кнопку (46 по умолчанию) и низ кнопок обрезается.
        quick.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        foreach (var (v, i) in new[] { (3000, 0), (6000, 1), (10000, 2) })
        {
            var b = new UiButton { Kind = ButtonKind.Ghost, Text = L.Money(v), Dock = DockStyle.Fill, Height = Sc(40), Font = Theme.MonoFont(12), Margin = new Padding(i == 0 ? 0 : Sc(3), 0, i == 2 ? 0 : Sc(3), 0) };
            b.Click += (_, _) => budget.Box.Text = v.ToString("N0", System.Globalization.CultureInfo.GetCultureInfo("ru-RU"));
            quick.Controls.Add(b, i, 0);
        }
        stack.Controls.Add(quick);
        Segmented Seg(string title, string[] items, int sel)
        {
            stack.Controls.Add(Cap(title));
            var s = new Segmented { Items = items, Width = iw, Height = Sc(40), Margin = new Padding(0, 0, 0, Sc(16)) };
            s.SelectedIndex = sel;
            stack.Controls.Add(s);
            return s;
        }
        var armour = Seg(L.T("Броня и шлем", "Armour & helmet"), [L.T("Нет", "No"), "1", "2", "3", "4"], 1);
        var optic = Seg(L.T("Прицел", "Optic"), [L.T("Нет", "No"), L.T("Колл.", "Dot"), "2–4×", "6×+"], 1);
        var goal = Seg(L.T("Цель", "Goal"), [L.T("Баланс", "Balance"), L.T("Убийство", "TTK"), L.T("Отдача", "Recoil"), "ADS"], 0);
        var classes = data.Weapons.Where(w => w.InVendor && w.Price > 0 && w.Sub is not ("Pistol" or "Bow" or "Launcher" or "Other")).Select(w => w.Sub).Distinct().OrderBy(s => s).ToList();
        stack.Controls.Add(Cap(L.T("Класс", "Class")));
        string? cls = null;
        var clsBtn = new UiButton { Kind = ButtonKind.Ghost, Width = iw, Height = Sc(40), Font = Theme.UiFont(13, 500), Margin = new Padding(0, 0, 0, Sc(16)) };
        void ClsText() => clsBtn.Text = (cls == null ? L.T("Любой класс", "Any class") : Stats.ClassName(cls)) + "  ▾";
        ClsText();
        clsBtn.Click += (_, _) =>
        {
            var m = DarkMenu.Create();
            m.Items.Add(L.T("Любой класс", "Any class"), null, (_, _) => { cls = null; ClsText(); });
            foreach (var c in classes) m.Items.Add(Stats.ClassName(c), null, (_, _) => { cls = c; ClsText(); });
            m.Show(clsBtn, new Point(0, clsBtn.Height + Sc(4)));
        };
        stack.Controls.Add(clsBtn);
        // Только открытое: по уровням ролей из «Прогресса».
        var onlyRow = new TableLayoutPanel { ColumnCount = 2, Width = iw, Height = Sc(30), BackColor = Theme.Panel, Margin = new Padding(0, 0, 0, Sc(4)) };
        onlyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        onlyRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, Sc(48)));
        var onlyLbl = Lbl(L.T("Только открытое", "Unlocked only"), 13, 500);
        onlyLbl.Anchor = AnchorStyles.Left;
        var only = new Toggle { Size = new Size(Sc(44), Sc(26)), Anchor = AnchorStyles.Right, Checked = settings?.OnlyUnlocked ?? true };
        onlyRow.Controls.Add(onlyLbl, 0, 0);
        onlyRow.Controls.Add(only, 1, 0);
        stack.Controls.Add(onlyRow);
        var levelsHint = Lbl("", 11, 400, Theme.Faint);
        levelsHint.MaximumSize = new Size(iw, 0);
        levelsHint.Margin = new Padding(0, 0, 0, Sc(14));
        stack.Controls.Add(levelsHint);
        // Те же уровни, что видны в «Прогрессе»: не указанная роль — 1-й уровень.
        void LevelsHint()
        {
            if (!only.Checked || settings == null) { levelsHint.Text = L.T("Предлагаю всё, что есть у продавца.", "Suggesting everything the vendor sells."); return; }
            var lv = settings.EffectiveLevels();
            levelsHint.Text = L.T("По уровням: ", "By levels: ") + string.Join(" · ", L.Roles.Select(r => $"{L.Role(r)} {lv[r]}")) +
                (settings.RoleLevels.Count == 0 ? L.T(". Свои уровни укажи в «Прогрессе».", ". Set your levels in Progression.") : "");
        }
        LevelsHint();
        page.VisibleChanged += (_, _) => { if (page.Visible) LevelsHint(); };
        only.CheckedChanged += (_, _) =>
        {
            if (settings != null) { settings.OnlyUnlocked = only.Checked; settings.NotifyChanged(); }
            LevelsHint();
        };
        var find = new UiButton { Kind = ButtonKind.Primary, Text = L.T("Подобрать", "Find"), Width = iw, Height = Sc(46), Margin = new Padding(0, Sc(4), 0, 0) };
        stack.Controls.Add(find);

        var results = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, BackColor = Theme.Bg, Padding = new Padding(Sc(16), 0, 0, 0) };
        Theme.DarkScrollbars(results);
        var rName = Theme.UiFont(16, 700); var rSub = Theme.UiFont(11); var chipF = Theme.UiFont(12); var totalF = Theme.MonoFont(16, 600);
        var rankF = Theme.MonoFont(13, 600); var mLabel = Theme.UiFont(12); var mVal = Theme.MonoFont(12); var lineF = Theme.UiFont(13); var slotF = Theme.UiFont(10, 600);

        void Run()
        {
            var digits = new string(budget.Box.Text.Where(char.IsDigit).ToArray());
            var req = new Builds.LoadoutRequest
            {
                Budget = digits.Length is > 0 and < 9 ? int.Parse(digits) : 0, ArmourLevel = armour.SelectedIndex, EnemyArmour = 2,
                Sight = (Builds.SightBand)optic.SelectedIndex, Goal = (Builds.LoadoutGoal)goal.SelectedIndex,
                WeaponClass = cls,
                Levels = only.Checked ? settings?.EffectiveLevels() : null,
            };
            LevelsHint();
            // Броня выбранного уровня ещё не открыта — так и говорим, а не «ничего не влезает».
            var lockedGear = req.Levels == null || req.ArmourLevel == 0 ? null
                : new[] { $"level-{req.ArmourLevel}-armor", $"level-{req.ArmourLevel}-helmet" }
                    .Select(sl => data.Items.FirstOrDefault(i => i.Slug == sl))
                    .FirstOrDefault(i => i != null && !Builds.Unlocked(i.Unlock, req.Levels));
            List<Builds.Loadout> list = lockedGear != null ? [] : Builds.BestLoadouts(data, req, 6);
            results.SuspendLayout();
            ClearControls(results);
            int width = Math.Max(Sc(420), results.ClientSize.Width - Sc(36));
            if (list.Count == 0)
            {
                var none = Lbl(lockedGear != null
                    ? L.T($"{L.Name(lockedGear)} ещё не открыт по твоим уровням ({L.UnlockText(lockedGear.Unlock)}). Выбери броню пониже.",
                          $"{L.Name(lockedGear)} is not unlocked at your levels ({L.UnlockText(lockedGear.Unlock)}). Pick lower armour.")
                    : req.Levels != null
                        ? L.T("Ничего не подходит: не хватает бюджета или нужное ещё не открыто по твоим уровням. Увеличь бюджет, убери прицел или броню.",
                              "Nothing fits: the budget is too low or what's needed isn't unlocked at your levels. Raise the budget or drop the optic/armour.")
                        : L.T("Ничего не влезает в бюджет. Увеличь бюджет, убери прицел или броню.", "Nothing fits the budget. Raise it or drop the optic/armour."), 14, 400, Theme.Muted);
                results.Controls.Add(none);
            }
            double wT = list.Count > 0 ? list.Max(l => l.Ttk ?? 0) : 1, wC = list.Count > 0 ? list.Max(l => l.Climb ?? 0) : 1, wA = list.Count > 0 ? list.Max(l => l.Ads) : 1;
            for (int i = 0; i < list.Count; i++)
            {
                var l = list[i];
                bool open = i == 0;
                int lines = 1 + l.Atts.Count + (l.Armour != null ? 1 : 0) + (l.Helmet != null ? 1 : 0);
                int rank = i + 1;
                var box = new PaintBox { Width = width, Height = Sc(open ? 128 + 24 * lines : 112), Margin = new Padding(0, 0, 0, Sc(10)) };
                box.Painter = (g, b) =>
                {
                    g.Clear(Theme.Bg);
                    var r = new RectangleF(0, 0, b.Width, b.Height);
                    Theme.FillRound(g, open ? Theme.Mix(Theme.Raised, Theme.Panel, 0.5) : Theme.Panel, r, b.S(14));
                    Theme.StrokeRound(g, open ? Theme.Mix(Theme.Accent, Theme.Panel, 0.35) : Theme.Line, r, b.S(14));
                    float x = b.S(16), w = b.Width - b.S(32), y = b.S(14);
                    var rr = new RectangleF(x, y + b.S(4), b.S(30), b.S(30));
                    Theme.FillRound(g, open ? Theme.Accent : Theme.Chip, rr, b.S(9));
                    Theme.Draw(g, rank.ToString(), rankF, open ? Theme.AccentInk : Theme.TextSoft, rr, Theme.Align.Center);
                    Theme.Draw(g, L.Name(l.Weapon), rName, Theme.Text, new RectangleF(x + b.S(44), y, b.S(170), b.S(22)));
                    Theme.Draw(g, Stats.ClassName(l.Weapon.Sub), rSub, Theme.Faint, new RectangleF(x + b.S(44), y + b.S(22), b.S(170), b.S(16)));
                    float cx = x + b.S(220);
                    foreach (var a in l.Atts)
                    {
                        var t = L.ShortName(a);
                        float cw = Math.Min(Theme.Measure(g, t, chipF).Width + b.S(16), b.S(180));
                        if (cx + cw > x + w - b.S(90)) break;
                        Theme.FillRound(g, Theme.Chip, new RectangleF(cx, y + b.S(7), cw, b.S(24)), b.S(7));
                        Theme.Draw(g, t, chipF, Theme.TextSoft, new RectangleF(cx + b.S(2), y + b.S(7), cw - b.S(4), b.S(24)), Theme.Align.Center);
                        cx += cw + b.S(5);
                    }
                    Theme.Draw(g, L.Money(l.Cost), totalF, Theme.Text, new RectangleF(x, y + b.S(4), w, b.S(30)), Theme.Align.Right);
                    y += b.S(50);
                    var metrics = new[]
                    {
                        (L.T("Убийство", "TTK"), l.Ttk is double t1 ? Combat.Seconds(t1) : "—", l.Ttk ?? wT, wT),
                        (L.T("Подброс", "Climb"), l.Climb is double c1 ? c1.ToString("0.0") + "°" : "—", l.Climb ?? wC, wC),
                        ("ADS", Combat.Seconds(l.Ads), l.Ads, wA),
                    };
                    float mw = (w - b.S(28)) / 3;
                    for (int k = 0; k < 3; k++)
                    {
                        float mx = x + k * (mw + b.S(14));
                        Theme.Draw(g, metrics[k].Item1, mLabel, Theme.Muted, new RectangleF(mx, y, mw, b.S(18)));
                        Theme.Draw(g, metrics[k].Item2, mVal, Theme.Text, new RectangleF(mx, y, mw, b.S(18)), Theme.Align.Right);
                        Theme.FillRound(g, Theme.Track, new RectangleF(mx, y + b.S(24), mw, b.S(5)), b.S(3));
                        double frac = metrics[k].Item4 <= 0 ? 0 : Math.Clamp(1 - metrics[k].Item3 / metrics[k].Item4 + 0.2, 0.08, 1);
                        Theme.FillRound(g, Theme.Accent, new RectangleF(mx, y + b.S(24), (float)(mw * frac), b.S(5)), b.S(3));
                    }
                    if (!open) return;
                    y += b.S(46);
                    using (var p = new Pen(Theme.Line2)) g.DrawLine(p, x, y, x + w, y);
                    y += b.S(10);
                    void Line(string slot, string name, int price)
                    {
                        Theme.Draw(g, slot, slotF, Theme.Faint, new RectangleF(x, y, b.S(80), b.S(22)));
                        Theme.Draw(g, name, lineF, Theme.TextSoft, new RectangleF(x + b.S(84), y, w - b.S(160), b.S(22)));
                        using var mf = Theme.MonoFont(13);
                        Theme.Draw(g, L.Money(price), mf, Theme.Muted, new RectangleF(x, y, w, b.S(22)), Theme.Align.Right);
                        y += b.S(24);
                    }
                    Line(L.T("ПУШКА", "WEAPON"), L.Name(l.Weapon), l.Weapon.Price);
                    foreach (var a in l.Atts) Line(Stats.SlotName(a), L.Name(a), a.ShopPrice);
                    if (l.Armour != null) Line(L.T("БРОНЯ", "ARMOUR"), L.Name(l.Armour), l.Armour.Price ?? 0);
                    if (l.Helmet != null) Line(L.T("ШЛЕМ", "HELMET"), L.Name(l.Helmet), l.Helmet.Price ?? 0);
                };
                results.Controls.Add(box);
            }
            results.ResumeLayout();
        }
        find.Click += (_, _) => Run();
        results.SizeChanged += (_, _) => { foreach (Control c in results.Controls) if (c is PaintBox) c.Width = Math.Max(Sc(420), results.ClientSize.Width - Sc(36)); };

        var body = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Bg };
        body.Controls.Add(results);
        body.Controls.Add(form);
        page.Controls.Add(body);
        page.Controls.Add(Gap(14));
        page.Controls.Add(Header(L.T("Сборка под бюджет", "Budget loadout"),
            L.T("Пушка, прицел, обвесы и броня в заданную сумму. Магазин идёт с пушкой", "Weapon, optic, attachments and armour within a budget. Magazine comes with the weapon")));
        page.HandleCreated += (_, _) => BeginInvoke(Run);
        return page;
    }

    // ---------- экономика ----------

    Control EconomyPage()
    {
        var page = Page();
        var tiles = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 4, Height = Sc(104), BackColor = Theme.Bg };
        for (int i = 0; i < 4; i++) tiles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25));
        var kpi = new (string Label, Func<(string Value, string Sub, Color Color)> Get)[]
        {
            (L.T("Сейчас", "Now"), () => economy.Current.Points.Count == 0 ? ("—", L.T("нет показаний", "no readings"), Theme.Text)
                : (L.Money(economy.Current.Points[^1].Balance), L.T("старт ", "start ") + L.Money(economy.Current.Points[0].Balance), Theme.Text)),
            (L.T("Заработано", "Earned"), () => ("+" + L.Money(economy.Current.Earned), L.T("весь рост баланса", "all balance growth"), Theme.Good)),
            (L.T("Потрачено", "Spent"), () => ("−" + L.Money(economy.Current.Spent), L.T("закупы и покупки", "loadouts and purchases"), Theme.BadText)),
            (L.T("В минуту", "Per minute"), () => (L.Money((int)economy.Current.EarnedPerMinute), L.T($"за {(int)economy.Current.Duration.TotalMinutes} мин", $"over {(int)economy.Current.Duration.TotalMinutes} min"), Theme.Accent)),
        };
        var kl = Theme.UiFont(12); var kv = Theme.MonoFont(26, 600); var ks = Theme.UiFont(12);
        var tileBoxes = new List<PaintBox>();
        for (int i = 0; i < 4; i++)
        {
            int k = i;
            var t = new PaintBox { Dock = DockStyle.Fill, Margin = new Padding(k == 0 ? 0 : Sc(6), 0, k == 3 ? 0 : Sc(6), 0) };
            t.Painter = (g, b) =>
            {
                g.Clear(Theme.Bg);
                var r = new RectangleF(0, 0, b.Width, b.Height);
                Theme.FillRound(g, Theme.Panel, r, b.S(14));
                Theme.StrokeRound(g, Theme.Line, r, b.S(14));
                var (v, s, c) = kpi[k].Get();
                Theme.Draw(g, kpi[k].Label, kl, Theme.Muted, new RectangleF(b.S(16), b.S(14), b.Width - b.S(32), b.S(18)));
                Theme.Draw(g, v, kv, c, new RectangleF(b.S(14), b.S(34), b.Width - b.S(28), b.S(38)));
                Theme.Draw(g, s, ks, Theme.Faint, new RectangleF(b.S(16), b.S(74), b.Width - b.S(32), b.S(18)));
            };
            tiles.Controls.Add(t, k, 0);
            tileBoxes.Add(t);
        }

        var chart = new PaintBox { Dock = DockStyle.Top, Height = Sc(180) };
        var axisF = Theme.MonoFont(11); var titleF = Theme.UiFont(13, 600); var hintF = Theme.UiFont(12);
        chart.Painter = (g, b) =>
        {
            g.Clear(Theme.Bg);
            var card = new RectangleF(0, 0, b.Width, b.Height);
            Theme.FillRound(g, Theme.Panel, card, b.S(16));
            Theme.StrokeRound(g, Theme.Line, card, b.S(16));
            Theme.Draw(g, L.T("БАЛАНС ЗА СЕССИЮ", "SESSION BALANCE"), titleF, Theme.Muted, new RectangleF(b.S(18), b.S(16), b.Width, b.S(20)));
            var pts = economy.Current.Points.ToList();
            var plot = new RectangleF(b.S(90), b.S(50), b.Width - b.S(110), b.Height - b.S(90));
            if (pts.Count < 2)
            {
                Theme.Draw(g, L.T("График появится после двух показаний баланса — зайди к продавцу в начале и после боя.",
                                  "The chart appears after two balance readings — visit the vendor before and after a fight."), hintF, Theme.Faint, plot, Theme.Align.Center);
                return;
            }
            double min = pts.Min(p => p.Balance), max = pts.Max(p => p.Balance);
            if (max - min < 1) { max += 500; min = Math.Max(0, min - 500); }
            var t0 = pts[0].Time; double span = Math.Max(1, (pts[^1].Time - t0).TotalSeconds);
            PointF Map(DateTime t, double v) => new((float)(plot.X + (t - t0).TotalSeconds / span * plot.Width), (float)(plot.Bottom - (v - min) / (max - min) * plot.Height));
            using (var gp = new Pen(Theme.Track))
                for (int i = 0; i <= 3; i++)
                {
                    float y = plot.Bottom - i * plot.Height / 3f;
                    g.DrawLine(gp, plot.X, y, plot.Right, y);
                    Theme.Draw(g, L.Money((int)(min + (max - min) * i / 3)), axisF, Theme.Faint, new RectangleF(b.S(8), y - b.S(9), b.S(76), b.S(18)), Theme.Align.Right);
                }
            var line = new List<PointF>();
            for (int i = 0; i < pts.Count; i++)
            {
                if (i > 0) line.Add(Map(pts[i].Time, pts[i - 1].Balance));
                line.Add(Map(pts[i].Time, pts[i].Balance));
            }
            using (var area = new GraphicsPath())
            {
                area.AddLines(line.ToArray());
                area.AddLine(line[^1], new PointF(line[^1].X, plot.Bottom));
                area.AddLine(new PointF(line[^1].X, plot.Bottom), new PointF(line[0].X, plot.Bottom));
                area.CloseFigure();
                using var fill = new SolidBrush(Theme.Alpha(Theme.Accent, 31));
                g.FillPath(fill, area);
            }
            using (var pen = new Pen(Theme.Accent, b.S(2.5f)) { LineJoin = LineJoin.Round }) g.DrawLines(pen, line.ToArray());
            Theme.Draw(g, $"{t0:HH:mm}", axisF, Theme.Faint, new RectangleF(plot.X, plot.Bottom + b.S(8), b.S(60), b.S(18)));
            Theme.Draw(g, $"{pts[^1].Time:HH:mm}", axisF, Theme.Faint, new RectangleF(plot.Right - b.S(60), plot.Bottom + b.S(8), b.S(60), b.S(18)), Theme.Align.Right);
        };

        var grid = new StyledGrid { MultiSelect = false };
        grid.AddCol(L.T("Сессия", "Session"), 1.4f, right: false, min: 140);
        grid.AddCol(L.T("Длит.", "Length"), 0.7f); grid.AddCol(L.T("Заработано", "Earned"), 1f); grid.AddCol(L.T("Потрачено", "Spent"), 1f);
        grid.AddCol(L.T("Итог", "Net"), 1f); grid.AddCol(L.T("В минуту", "Per min"), 0.8f);
        grid.Columns[1].DefaultCellStyle.ForeColor = grid.Columns[3].DefaultCellStyle.ForeColor = Theme.Muted;

        // --- «Куда ушли деньги» и «Цель»
        var mid = new TableLayoutPanel { Dock = DockStyle.Top, ColumnCount = 2, Height = Sc(236), BackColor = Theme.Bg };
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        mid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        var spentBox = new PaintBox { Dock = DockStyle.Fill, Margin = new Padding(0, 0, Sc(7), 0) };
        var rowF = Theme.UiFont(13); var monoF = Theme.MonoFont(13, 600); var bigF = Theme.MonoFont(20, 600);
        spentBox.Painter = (g, b) =>
        {
            g.Clear(Theme.Bg);
            var r = new RectangleF(0, 0, b.Width, b.Height);
            Theme.FillRound(g, Theme.Panel, r, b.S(16));
            Theme.StrokeRound(g, Theme.Line, r, b.S(16));
            float x = b.S(18), w = b.Width - b.S(36), y = b.S(16);
            Theme.Draw(g, L.T("КУДА УШЛИ ДЕНЬГИ · СЕССИЯ", "WHERE THE MONEY WENT · SESSION"), titleF, Theme.Muted, new RectangleF(x, y, w, b.S(20)));
            y += b.S(30);
            var list = economy.Current.Purchases.ToList();
            if (list.Count == 0)
            {
                Theme.Draw(g, L.T("Траты появятся здесь: оверлей следит за балансом в бою и у продавца.",
                                  "Spending shows up here: the overlay watches the balance in matches and at the vendor."),
                           hintF, Theme.Faint, new RectangleF(x, y, w, b.Height - y - b.S(12)));
                return;
            }
            var kinds = new[] { (new[] { "kit" }, L.T("закупы", "loadouts")), (new[] { "attachments", "weapon" }, L.T("пушки и обвесы", "weapons & parts")),
                                (new[] { "unlock" }, L.T("открытия", "unlocks")), (new[] { "other" }, L.T("прочее", "other")) };
            float cx = x;
            foreach (var (k, label) in kinds)
            {
                int sum = list.Where(p => k.Contains(p.Kind)).Sum(p => p.Amount);
                Theme.Draw(g, L.Money(sum), monoF, sum > 0 ? Theme.Text : Theme.Faint, new RectangleF(cx, y + b.S(4), w / 4, b.S(24)));
                Theme.Draw(g, label, hintF, Theme.Faint, new RectangleF(cx, y + b.S(28), w / 4, b.S(18)));
                cx += w / 4;
            }
            y += b.S(56);
            foreach (var p in list.AsEnumerable().Reverse().Take(5))
            {
                using (var pen = new Pen(Theme.LineSoft)) g.DrawLine(pen, x, y, x + w, y);
                Theme.Draw(g, $"{p.Time:HH:mm}", axisF, Theme.Faint, new RectangleF(x, y + b.S(6), b.S(50), b.S(20)));
                Theme.Draw(g, "−" + L.Money(p.Amount), monoF, Theme.BadText, new RectangleF(x + b.S(52), y + b.S(5), b.S(100), b.S(20)));
                Theme.Draw(g, p.What, rowF, Theme.TextSoft, new RectangleF(x + b.S(150), y + b.S(5), w - b.S(150), b.S(20)));
                y += b.S(26);
            }
        };

        // Цель: что хочется открыть и сколько ещё играть при твоём темпе.
        var goals = data.Items.Where(i => i.Unlock is { Cash: > 0 }).OrderBy(i => i.Tab).ThenBy(i => i.Unlock!.Cash).ToList();
        DbItem? goal = goals.FirstOrDefault(i => i.Slug == settings?.GoalSlug);
        var goalPanel = new Panel { Dock = DockStyle.Fill, Margin = new Padding(Sc(7), 0, 0, 0), BackColor = Theme.Bg };
        var goalBox = new PaintBox { Dock = DockStyle.Fill };
        var goalPick = new UiButton { Kind = ButtonKind.Secondary, Height = Sc(36), Font = Theme.UiFont(13, 500), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        void UpdateGoalPick()
        {
            goalPick.Text = (goal == null ? L.T("Выбрать цель", "Pick a goal") : L.T("Сменить", "Change")) + "  ▾";
            goalPick.Width = goalPick.PreferredWidth(DeviceDpi);
            goalPick.Location = new Point(goalPanel.Width - goalPick.Width - Sc(16), Sc(12));
        }
        goalPanel.SizeChanged += (_, _) => UpdateGoalPick();
        goalPick.Click += (_, _) =>
        {
            var m = DarkMenu.Create();
            foreach (var tab in goals.GroupBy(i => i.Tab))
            {
                var sub = new ToolStripMenuItem(L.TabName(tab.Key));
                foreach (var it in tab)
                {
                    var item = it;
                    sub.DropDownItems.Add($"{L.Name(item)} — {L.Money(item.Unlock!.Cash)}", null, (_, _) =>
                    {
                        goal = item;
                        if (settings != null) { settings.GoalSlug = item.Slug; settings.NotifyChanged(); }
                        UpdateGoalPick();
                        goalBox.Invalidate();
                    });
                }
                m.Items.Add(sub);
            }
            m.Show(goalPick, new Point(0, goalPick.Height + Sc(4)));
        };
        goalBox.Painter = (g, b) =>
        {
            g.Clear(Theme.Bg);
            var r = new RectangleF(0, 0, b.Width, b.Height);
            Theme.FillRound(g, Theme.Panel, r, b.S(16));
            Theme.StrokeRound(g, Theme.Line, r, b.S(16));
            float x = b.S(18), w = b.Width - b.S(36), y = b.S(16);
            Theme.Draw(g, L.T("ЦЕЛЬ", "GOAL"), titleF, Theme.Muted, new RectangleF(x, y, w, b.S(20)));
            y += b.S(34);
            if (goal == null)
            {
                Theme.Draw(g, L.T("Выбери, что хочешь открыть (технику, пушку, обвес) — посчитаю, сколько не хватает и сколько ещё играть при твоём темпе.",
                                  "Pick what you want to unlock (a vehicle, weapon or attachment) — I'll show how much is missing and how long to play at your rate."),
                           hintF, Theme.Faint, new RectangleF(x, y, w, b.Height - y - b.S(12)));
                return;
            }
            var u = goal.Unlock!;
            int cost = u.Cash;
            Theme.Draw(g, L.Name(goal), Theme.UiFont(16, 700), Theme.Text, new RectangleF(x, y, w, b.S(24)));
            y += b.S(28);
            Theme.Draw(g, L.T($"Открыть: {L.Money(cost)} · {L.Role(u.Role)} {u.Level} ур.", $"Unlock: {L.Money(cost)} · {L.Role(u.Role)} level {u.Level}") +
                          (goal.Price is int pr and > 0 ? L.T($" · потом цена {L.Money(pr)}", $" · then {L.Money(pr)} each") : ""),
                       hintF, Theme.Muted, new RectangleF(x, y, w, b.S(20)));
            y += b.S(30);
            int balance = economy.LastBalance ?? 0;
            double frac = Math.Clamp(balance / (double)cost, 0, 1);
            var bar = new RectangleF(x, y, w, b.S(10));
            Theme.FillRound(g, Theme.Track, bar, b.S(5));
            if (frac > 0) Theme.FillRound(g, frac >= 1 ? Theme.Good : Theme.Accent, new RectangleF(x, y, Math.Max(b.S(10), w * (float)frac), b.S(10)), b.S(5));
            y += b.S(20);
            // Строки рисуем по одной: перенос в этом рисовании не работает.
            var lines = new List<(string Text, Color Color)>();
            if (balance >= cost) lines.Add((L.T($"Денег хватает: баланс {L.Money(balance)}.", $"You have enough: balance {L.Money(balance)}."), Theme.Good));
            else
            {
                int left = cost - balance;
                lines.Add((L.T($"Баланс {L.Money(balance)} — не хватает {L.Money(left)}.", $"Balance {L.Money(balance)} — {L.Money(left)} to go."), Theme.TextSoft));
                if (economy.AverageRate is double rate and > 0)
                {
                    var t = TimeSpan.FromMinutes(left / rate);
                    string eta = t.TotalHours >= 1 ? L.T($"{(int)t.TotalHours} ч {t.Minutes} мин", $"{(int)t.TotalHours} h {t.Minutes} min") : L.T($"{Math.Max(1, t.Minutes)} мин", $"{Math.Max(1, t.Minutes)} min");
                    lines.Add((L.T($"≈ {eta} игры при твоём темпе {L.Money((int)rate)}/мин (без трат).", $"≈ {eta} of play at your {L.Money((int)rate)}/min (no spending)."), Theme.Accent));
                }
                else lines.Add((L.T("Срок покажу, когда наберётся 10 минут истории заработка.", "Time estimate appears after 10 minutes of earnings history."), Theme.Faint));
            }
            lines.Add((L.T($"Ещё нужен уровень {u.Level} роли «{L.Role(u.Role)}».", $"Also needs {L.Role(u.Role)} level {u.Level}."), Theme.Faint));
            foreach (var (text, color) in lines)
            {
                Theme.Draw(g, text, hintF, color, new RectangleF(x, y, w, b.S(20)));
                y += b.S(24);
            }
        };
        goalPanel.Controls.Add(goalPick);
        goalPanel.Controls.Add(goalBox);
        goalPick.BringToFront();
        mid.Controls.Add(spentBox, 0, 0);
        mid.Controls.Add(goalPanel, 1, 0);

        void Refresh()
        {
            foreach (var t in tileBoxes) t.Invalidate();
            chart.Invalidate();
            spentBox.Invalidate();
            goalBox.Invalidate();
            UpdateGoalPick();
            grid.Rows.Clear();
            foreach (var h in economy.History)
                grid.Rows.Add(new TwoLine(h.Start.ToString("dd.MM, HH:mm"), ""), $"{(int)h.Duration.TotalHours}:{h.Duration.Minutes:00}",
                    L.Money(h.Earned), L.Money(h.Spent),
                    new Tinted(h.Net, (h.Net >= 0 ? "+" : "−") + L.Money(Math.Abs(h.Net)), h.Net >= 0 ? Theme.Good : Theme.BadText),
                    L.Money((int)h.EarnedPerMinute));
        }
        economyRefresh = Refresh;

        var newSession = new UiButton { Kind = ButtonKind.Secondary, Text = L.T("Новая сессия", "New session"), Height = Sc(42), Font = Theme.UiFont(13, 600) };
        newSession.Width = newSession.PreferredWidth(DeviceDpi);
        newSession.Click += (_, _) => economy.NewSession();

        page.Controls.Add(GridCard(grid));
        page.Controls.Add(Gap(14));
        page.Controls.Add(mid);
        page.Controls.Add(Gap(14));
        page.Controls.Add(chart);
        page.Controls.Add(Gap(14));
        page.Controls.Add(tiles);
        page.Controls.Add(Gap(16));
        page.Controls.Add(Header(L.T("Экономика", "Economy"),
            L.T("Баланс читается в реальном времени — в бою и у продавца. Рост — заработок, падение — траты",
                "Balance is read in real time — in matches and at the vendor. Growth is income, drops are spending"), newSession));
        Refresh();
        return page;
    }

    // ---------- бой ----------

    static readonly RoundLoad[] AllLoads = [RoundLoad.FMJ, RoundLoad.HollowPoint, RoundLoad.ArmorPiercing];
    static string LoadName(RoundLoad l) => l switch
    {
        RoundLoad.HollowPoint => L.T("HP · по плоти", "HP · flesh"),
        RoundLoad.ArmorPiercing => L.T("AP · бронебойные", "AP · armour-piercing"),
        _ => L.T("FMJ · обычные", "FMJ · standard"),
    };
    static string LoadShort(RoundLoad l) => l switch { RoundLoad.HollowPoint => "HP", RoundLoad.ArmorPiercing => "AP", _ => "FMJ" };

    /// <summary>
    /// Калькулятор: сколько выстрелов и времени до убийства по зоне, броне, шлему, дистанции и патронам —
    /// и какие патроны выгоднее брать против такой брони (с ценой магазина).
    /// </summary>
    Control CombatPage()
    {
        var page = Page();
        var weapons = data.Weapons.Where(w => w.InVendor && w.Rpm != null && w.Sub != "Launcher" && w.CalibreKey.Length > 0)
                                  .OrderBy(w => L.Name(w)).ToList();
        // По умолчанию — последняя пушка, которую оверлей видел в меню.
        var weapon = settings?.MyWeapons.Select(s => weapons.FirstOrDefault(w => w.Slug == s)).LastOrDefault(w => w != null)
                     ?? weapons.FirstOrDefault(w => w.Slug == "ak74") ?? weapons.FirstOrDefault();
        if (weapon == null) { page.Controls.Add(Header(L.T("Бой", "Combat"), L.T("В базе нет баллистики.", "No ballistics in the data."))); return page; }

        var zones = new[] { ("head", L.T("Голова", "Head")), ("neck", L.T("Шея", "Neck")), ("torso_upper", L.T("Грудь", "Chest")),
                            ("torso_lower", L.T("Живот", "Stomach")), ("arm_upper", L.T("Руки", "Arms")), ("leg_thigh", L.T("Ноги", "Legs")) };
        int[] dists = [10, 50, 100, 200, 300, 500];
        var pick = new UiButton { Kind = ButtonKind.Secondary, Height = Sc(42), Font = Theme.UiFont(14, 500) };
        Segmented Seg(string[] items, int sel, int width) =>
            new() { Items = items, SelectedIndex = sel, Size = new Size(Sc(width), Sc(40)), Well = Theme.Panel, Margin = new Padding(0, Sc(4), Sc(18), 0) };
        string none = L.T("Нет", "None");
        var segBody = Seg([none, "1", "2", "3", "4"], 2, 250);
        var segHelmet = Seg([none, "1", "2", "3", "4"], 2, 250);
        var segDist = Seg(dists.Select(d => d + L.T(" м", " m")).ToArray(), 1, 400);
        var segZone = Seg(zones.Select(z => z.Item2).ToArray(), 2, 520);

        var controls = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, WrapContents = true, BackColor = Theme.Bg };
        void Group(string caption, Control c)
        {
            var g = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, WrapContents = false, BackColor = Theme.Bg, Margin = new Padding(0, 0, 0, Sc(10)) };
            g.Controls.Add(Lbl(caption.ToUpperInvariant(), 11, 600, Theme.Faint));
            g.Controls.Add(c);
            controls.Controls.Add(g);
        }
        Group(L.T("Бронежилет цели", "Target body armour"), segBody);
        Group(L.T("Шлем цели", "Target helmet"), segHelmet);
        Group(L.T("Дистанция", "Distance"), segDist);
        Group(L.T("Куда попадаешь", "Hit zone"), segZone);

        // Три карточки патронов + совет.
        var cards = new PaintBox { Dock = DockStyle.Top, Height = Sc(200) };
        var advice = Lbl("", 14, 500, Theme.Text);
        advice.Dock = DockStyle.Top;
        advice.Padding = new Padding(Sc(2), Sc(10), 0, Sc(6));
        var grid = new StyledGrid();
        grid.AddCol(L.T("Зона", "Zone"), 1.4f, right: false, min: 150);
        foreach (var l in AllLoads) grid.AddCol(LoadShort(l), 1f);

        var bigF = Theme.MonoFont(28, 700); var secF = Theme.MonoFont(15, 600); var capF = Theme.UiFont(13, 600);
        var smallF = Theme.UiFont(12); var badgeF = Theme.MonoFont(10, 600);
        List<(RoundLoad Load, Kill? Kill, int? Mag, DbItem? Item)> results = [];

        void Recalc()
        {
            int body = segBody.SelectedIndex, helmet = segHelmet.SelectedIndex;
            double dist = dists[Math.Max(0, segDist.SelectedIndex)];
            string zone = zones[Math.Max(0, segZone.SelectedIndex)].Item1;
            pick.Text = L.Name(weapon) + "  ▾";
            pick.Width = pick.PreferredWidth(DeviceDpi);
            results = AllLoads.Select(l => (l, Combat.Shots(data, weapon, zone, l, body, helmet, dist), Combat.MagazineCost(data, weapon, l), Combat.AmmoItem(data, weapon, l))).ToList();
            advice.Text = Advice(results, body, zone, helmet);
            cards.Invalidate();

            grid.Rows.Clear();
            foreach (var z in Combat.Zones)
            {
                var kills = AllLoads.Select(l => Combat.Shots(data, weapon, z, l, body, helmet, dist)).ToArray();
                double best = kills.Where(k => k != null).Select(k => k!.Seconds).DefaultIfEmpty(double.MaxValue).Min();
                var zn = L.Zone(z);
                var row = new List<object?> { char.ToUpper(zn[0]) + zn[1..] };
                row.AddRange(kills.Select(k => (object?)(k == null ? "—"
                    : new Tinted(k.Seconds, $"{k.Shots} · {Combat.Seconds(k.Seconds)}", Math.Abs(k.Seconds - best) < 1e-9 ? Theme.Good : Theme.Text))));
                grid.Rows.Add(row.ToArray());
            }
        }

        cards.Painter = (g, b) =>
        {
            g.Clear(Theme.Bg);
            if (results.Count == 0) return;
            var ok = results.Where(r => r.Kill != null).ToList();
            var bestLoad = ok.OrderBy(r => r.Kill!.Seconds).ThenBy(r => r.Mag ?? int.MaxValue).Select(r => (RoundLoad?)r.Load).FirstOrDefault();
            float gap = b.S(14), w = (b.Width - 2 * gap) / 3f;
            for (int i = 0; i < results.Count; i++)
            {
                var (load, kill, mag, item) = results[i];
                var r = new RectangleF(i * (w + gap), 0, w, b.Height - b.S(2));
                bool isBest = load == bestLoad;
                Theme.FillRound(g, Theme.Panel, r, b.S(14));
                Theme.StrokeRound(g, isBest ? Theme.Accent : Theme.Line, r, b.S(14));
                float x = r.X + b.S(18), y = r.Y + b.S(16), cw = r.Width - b.S(36);
                Theme.Draw(g, LoadName(load), capF, Theme.Muted, new RectangleF(x, y, cw, b.S(20)));
                if (isBest)
                {
                    var t = L.T("ЛУЧШЕ", "BEST");
                    float bw = Theme.Measure(g, t, badgeF).Width + b.S(14);
                    var br = new RectangleF(r.Right - b.S(18) - bw, y, bw, b.S(20));
                    Theme.FillRound(g, Theme.Accent, br, b.S(6));
                    Theme.Draw(g, t, badgeF, Theme.AccentInk, br, Theme.Align.Center);
                }
                y += b.S(30);
                if (kill == null)
                {
                    Theme.Draw(g, L.T("нет у этой пушки", "not available"), smallF, Theme.Faint, new RectangleF(x, y + b.S(10), cw, b.S(20)));
                    continue;
                }
                var shots = $"{kill.Shots}";
                Theme.Draw(g, shots, bigF, Theme.Text, new RectangleF(x, y, cw, b.S(40)));
                float sw = Theme.Measure(g, shots, bigF).Width;
                Theme.Draw(g, L.T(" выстр.", " shots"), smallF, Theme.Muted, new RectangleF(x + sw, y + b.S(16), cw, b.S(20)));
                Theme.Draw(g, Combat.Seconds(kill.Seconds), secF, isBest ? Theme.Accent : Theme.Text, new RectangleF(x, y + b.S(8), cw, b.S(30)), Theme.Align.Right);
                y += b.S(50);
                string per = kill.PerHit.ToString(kill.PerHit < 10 ? "0.0" : "0", L.Ru ? System.Globalization.CultureInfo.GetCultureInfo("ru-RU") : System.Globalization.CultureInfo.InvariantCulture);
                Theme.Draw(g, L.T($"{per} урона за попадание", $"{per} damage per hit") + (kill.Armoured ? L.T(" (через броню)", " (through armour)") : ""),
                           smallF, Theme.Muted, new RectangleF(x, y, cw, b.S(20)));
                y += b.S(24);
                Theme.Draw(g, L.T("Магазин: ", "Magazine: ") + (mag is int m ? L.Money(m) : "—"), smallF, Theme.Muted, new RectangleF(x, y, cw, b.S(20)));
                y += b.S(24);
                Theme.Draw(g, L.T("Открывается: ", "Unlock: ") + (item != null ? L.UnlockText(item.Unlock) : "—"), smallF, Theme.Faint, new RectangleF(x, y, cw, b.S(20)));
            }
        };

        pick.Click += (_, _) =>
        {
            var m = DarkMenu.Create();
            foreach (var w in weapons) m.Items.Add(L.Name(w), null, (_, _) => { weapon = w; Recalc(); });
            m.Show(pick, new Point(0, pick.Height + Sc(4)));
        };
        foreach (var s in new[] { segBody, segHelmet, segDist, segZone }) s.SelectedIndexChanged += (_, _) => Recalc();

        page.Controls.Add(grid);
        page.Controls.Add(Gap(12));
        page.Controls.Add(advice);
        page.Controls.Add(cards);
        page.Controls.Add(Gap(10));
        page.Controls.Add(controls);
        page.Controls.Add(Header(L.T("Бой: урон и время убийства", "Combat: damage & time to kill"),
            L.T("Выстрелы и время до убийства по зоне, броне, шлему, дистанции и патронам — та же модель, что в калькуляторе MetaForge. Время — с первого выстрела, вместе с полётом пули.",
                "Shots and time to kill by zone, armour, helmet, distance and ammo — the same model as MetaForge's calculator. Time counts from the first shot and includes bullet travel."),
            pick));
        Recalc();
        return page;
    }

    /// <summary>Совет по патронам: что быстрее убивает и стоит ли переплачивать.</summary>
    static string Advice(List<(RoundLoad Load, Kill? Kill, int? Mag, DbItem? Item)> res, int body, string zone, int helmet)
    {
        var ok = res.Where(r => r.Kill != null).ToList();
        if (ok.Count == 0) return "";
        var best = ok.OrderBy(r => r.Kill!.Seconds).ThenBy(r => r.Mag ?? int.MaxValue).First();
        var fmj = ok.FirstOrDefault(r => r.Load == RoundLoad.FMJ);
        bool armoured = best.Kill!.Armoured;
        string target = armoured ? (zone is "head" or "neck" ? L.T($"шлема {helmet} ур.", $"a level {helmet} helmet") : L.T($"брони {body} ур.", $"level {body} armour"))
                                 : L.T("незащищённой зоны", "an unprotected zone");
        if (ok.Count == 1) return L.T($"У этой пушки только обычные патроны: {best.Kill.Shots} выстр. за {Combat.Seconds(best.Kill.Seconds)}.",
                                      $"This weapon only has standard rounds: {best.Kill.Shots} shots in {Combat.Seconds(best.Kill.Seconds)}.");
        // Тот же результат дешевле — так и советуем.
        if (best.Load != RoundLoad.FMJ && fmj.Kill != null && fmj.Kill.Shots == best.Kill.Shots)
            return L.T($"Против {target} {LoadShort(best.Load)} не быстрее обычных — {fmj.Kill.Shots} выстр. Бери FMJ: магазин {L.Money(fmj.Mag ?? 0)} вместо {L.Money(best.Mag ?? 0)}.",
                       $"Against {target}, {LoadShort(best.Load)} is no faster than standard — {fmj.Kill.Shots} shots. Take FMJ: {L.Money(fmj.Mag ?? 0)} per magazine instead of {L.Money(best.Mag ?? 0)}.");
        var worst = ok.OrderByDescending(r => r.Kill!.Seconds).First();
        var s = L.T($"Против {target} лучше {LoadShort(best.Load)}: {best.Kill.Shots} выстр. за {Combat.Seconds(best.Kill.Seconds)}",
                    $"Against {target}, {LoadShort(best.Load)} is best: {best.Kill.Shots} shots in {Combat.Seconds(best.Kill.Seconds)}");
        if (worst.Load != best.Load && worst.Kill!.Shots > best.Kill.Shots)
            s += L.T($", а {LoadShort(worst.Load)} — {worst.Kill.Shots} выстр.", $", while {LoadShort(worst.Load)} needs {worst.Kill.Shots}.");
        else s += ".";
        if (best.Mag is int bm && fmj.Mag is int fm && best.Load != RoundLoad.FMJ) s += L.T($" Магазин {L.Money(bm)} (FMJ — {L.Money(fm)}).", $" Magazine {L.Money(bm)} (FMJ {L.Money(fm)}).");
        return s;
    }

    // ---------- патч ----------

    Control PatchPage()
    {
        var page = Page();
        // Фильтр по названию раздела: разные разделы базы («other», «keycards»…) могут называться одинаково.
        var tabsList = data.Patch.Select(p => L.TabName(p.Tab)).Distinct().OrderBy(t => t).ToList();
        // «Мои пушки» — то, что оверлей узнавал в меню: сами пушки и обвесы, которые к ним подходят.
        var mine = settings?.MyWeapons.ToHashSet() ?? [];
        var myIds = data.Weapons.Where(w => mine.Contains(w.Slug)).Select(w => w.Id)
            .Concat(data.Attachments.Values.Where(a => a.Fits?.Any(mine.Contains) == true).Select(a => a.Id)).ToHashSet();
        int fixedChips = mine.Count > 0 ? 2 : 1;
        var chipItems = new List<string> { L.T("Все", "All") };
        if (mine.Count > 0) chipItems.Add(L.T("Мои пушки", "My weapons"));
        var chips = new ChipBar { Dock = DockStyle.Top, Items = chipItems.Concat(tabsList).ToArray() };
        var search = new TextField(searchIcon: true, placeholder: L.T("Найти предмет…", "Find an item…")) { Width = Sc(240), Height = Sc(42), Fill = Theme.Panel };
        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Theme.Bg, WrapContents = true };
        Theme.DarkScrollbars(flow);
        var nameF = Theme.UiFont(15, 600); var tagF = Theme.UiFont(11); var whatF = Theme.UiFont(13); var valF = Theme.MonoFont(13); var newF = Theme.MonoFont(10, 600);

        string NameOf(PatchItem p)
        {
            var item = data.Items.FirstOrDefault(i => i.Id == p.Id);
            return item != null ? L.Name(item) : p.Name;
        }
        // Выгоднее игроку: дешевле, раньше открывается; хуже — наоборот. Остальное без оценки.
        int Verdict(PatchChange c)
        {
            var path = string.Join("/", c.Path);
            if (path is not ("price" or "unlock/level" or "unlock/cash")) return 0;
            if (!double.TryParse(c.From, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var a) ||
                !double.TryParse(c.To, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var b)) return 0;
            return b < a ? 1 : b > a ? -1 : 0;
        }
        string Val(PatchChange c, string v)
        {
            var path = string.Join("/", c.Path);
            if ((path is "price" or "unlock/cash") && int.TryParse(v, out var n)) return L.Money(n);
            return v.Length > 22 ? v[..20] + "…" : v;
        }

        void Fill()
        {
            flow.SuspendLayout();
            ClearControls(flow);
            int w = CardWidth();
            // Сверху — официальные заметки разработчиков к этому патчу (что не видно по данным: механики, исправления).
            if (chips.SelectedIndex == 0 && search.Box.Text.Trim().Length == 0)
                foreach (var post in PatchNews()) flow.Controls.Add(NewsCard(post, flow.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - Sc(14)));
            // Сначала то, что поменялось у знакомых предметов, потом удалённое и новинки.
            foreach (var p in data.Patch.OrderBy(p => p.Removed ? 1 : p.Added && p.Changes.Count == 0 ? 2 : 0))
            {
                if (chips.SelectedIndex == 1 && mine.Count > 0 && !myIds.Contains(p.Id)) continue;
                if (chips.SelectedIndex >= fixedChips && L.TabName(p.Tab) != tabsList[chips.SelectedIndex - fixedChips]) continue;
                var nm = NameOf(p);
                if (!Match(search.Box.Text, p.Name, nm)) continue;
                // Все изменения, без обрезки: у некоторых предметов их два десятка.
                var changes = p.Changes.Where(c => !c.Path.SequenceEqual(["description"]) && !c.Path.SequenceEqual(["flavor"])).ToList();
                if (!p.Added && !p.Removed && changes.Count == 0) continue;
                var item = p;
                var card = new PaintBox { Width = w, Height = Sc(58 + 32 * Math.Max(changes.Count, p.Added || p.Removed ? 0 : 1)), Margin = new Padding(0, 0, Sc(12), Sc(12)) };
                card.Painter = (g, b) =>
                {
                    g.Clear(Theme.Bg);
                    var r = new RectangleF(0, 0, b.Width, b.Height);
                    Theme.FillRound(g, Theme.Panel, r, b.S(14));
                    Theme.StrokeRound(g, Theme.Line, r, b.S(14));
                    float x = b.S(16), cw = b.Width - b.S(32), y = b.S(14);
                    float right = x + cw;
                    if (item.Added || item.Removed)
                    {
                        var badge = item.Added ? L.T("НОВОЕ", "NEW") : L.T("УДАЛЕНО", "REMOVED");
                        float bw = Math.Max(b.S(58), Theme.Measure(g, badge, newF).Width + b.S(14));
                        var nr = new RectangleF(right - bw, y + b.S(2), bw, b.S(20));
                        Theme.FillRound(g, item.Added ? Theme.Accent : Theme.BadText, nr, b.S(6));
                        Theme.Draw(g, badge, newF, item.Added ? Theme.AccentInk : Theme.Bg, nr, Theme.Align.Center);
                        right -= bw + b.S(8);
                    }
                    var tab = L.TabName(item.Tab);
                    float tw = Theme.Measure(g, tab, tagF).Width + b.S(16);
                    var tr = new RectangleF(right - tw, y + b.S(2), tw, b.S(20));
                    Theme.FillRound(g, Theme.Chip, tr, b.S(6));
                    Theme.Draw(g, tab, tagF, Theme.Muted, tr, Theme.Align.Center);
                    Theme.Draw(g, nm, nameF, Theme.Text, new RectangleF(x, y, right - tw - x - b.S(8), b.S(24)));
                    y += b.S(36);
                    foreach (var c in changes)
                    {
                        using (var pen = new Pen(Theme.LineSoft)) g.DrawLine(pen, x, y, x + cw, y);
                        Theme.Draw(g, L.ChangeLabel(c), whatF, Theme.Muted, new RectangleF(x, y, cw * 0.55f, b.S(32)));
                        int v = Verdict(c);
                        var to = Val(c, c.To);
                        float toW = Theme.Measure(g, to, valF).Width + b.S(6);
                        Theme.Draw(g, to, valF, v > 0 ? Theme.Good : v < 0 ? Theme.BadText : Theme.Text, new RectangleF(x, y, cw, b.S(32)), Theme.Align.Right);
                        Theme.Draw(g, Val(c, c.From) + "  →", valF, Theme.Faint, new RectangleF(x, y, cw - toW - b.S(4), b.S(32)), Theme.Align.Right);
                        y += b.S(32);
                    }
                };
                flow.Controls.Add(card);
            }
            flow.ResumeLayout();
        }
        chips.SelectedIndexChanged += (_, _) => Fill();
        search.Box.TextChanged += (_, _) => Fill();
        int CardWidth() => Math.Max(Sc(380), (flow.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - Sc(26)) / 2);
        flow.SizeChanged += (_, _) =>
        {
            int w = CardWidth();
            foreach (Control c in flow.Controls)
                c.Width = c is Card ? flow.ClientSize.Width - SystemInformation.VerticalScrollBarWidth - Sc(14) : w;
        };

        var counters = new PaintBox { Size = new Size(Sc(230), Sc(54)) };
        var cnt = Theme.MonoFont(20, 600); var cl = Theme.UiFont(12);
        counters.Painter = (g, b) =>
        {
            g.Clear(Theme.Bg);
            var vals = new[] { (data.Patch.Count(p => p.Added), L.T("новых", "added")), (data.Patch.Count(p => !p.Added && !p.Removed), L.T("изменено", "changed")) };
            for (int i = 0; i < 2; i++)
            {
                var r = new RectangleF(i * b.S(118), 0, b.S(110), b.Height);
                Theme.FillRound(g, Theme.Panel, r, b.S(12));
                Theme.StrokeRound(g, Theme.Line, r, b.S(12));
                Theme.Draw(g, vals[i].Item1.ToString(), cnt, Theme.Text, new RectangleF(r.X + b.S(14), b.S(4), r.Width, b.S(28)));
                Theme.Draw(g, vals[i].Item2, cl, Theme.Muted, new RectangleF(r.X + b.S(14), b.S(30), r.Width, b.S(18)));
            }
        };

        page.Controls.Add(flow);
        page.Controls.Add(Gap(16));
        page.Controls.Add(chips);
        page.Controls.Add(Gap(14));
        var sub = data.Patch.Count == 0
            ? L.T("Журнал патчей ещё не скачан — «Обновить базу» в главном окне", "Patch log not downloaded yet — «Update data» in the main window")
            : $"{data.PatchTitle} · {data.PatchDate}" + (data.PatchBuild.Length > 0 ? $" · {data.PatchBuild}" : "") +
              (data.Patch.Any(p => p.Removed) ? L.T($" · удалено: {data.Patch.Count(p => p.Removed)}", $" · removed: {data.Patch.Count(p => p.Removed)}") : "") +
              " · " + L.T("зелёным — выгоднее для тебя, красным — хуже", "green — better for you, red — worse");
        page.Controls.Add(Header(L.T("Патч", "Patch"), sub, search, counters));
        page.HandleCreated += (_, _) => BeginInvoke(Fill);
        // Открыли «Патч» — значит, новый патч увидели: баннер в главном окне больше не нужен.
        if (settings != null && data.PatchBuild.Length > 0 && settings.SeenPatchBuild != data.PatchBuild)
        {
            settings.SeenPatchBuild = data.PatchBuild;
            settings.NotifyChanged();
        }
        return page;
    }

    /// <summary>Записи разработчиков в Steam с выхода прошлого патча (не больше трёх), иначе последняя.</summary>
    IEnumerable<NewsPost> PatchNews()
    {
        if (data.News.Count == 0) return [];
        var since = DateTime.TryParse(data.PatchPreviousDate, System.Globalization.CultureInfo.InvariantCulture,
                                      System.Globalization.DateTimeStyles.None, out var d) ? d : DateTime.MinValue;
        var recent = data.News.Where(n => n.Date >= since).Take(3).ToList();
        return recent.Count > 0 ? recent : data.News.Take(1);
    }

    Card NewsCard(NewsPost post, int width)
    {
        var card = new Card { Width = Math.Max(Sc(380), width), Height = Sc(100), Padding = new Padding(Sc(18), Sc(14), Sc(18), Sc(14)), Margin = new Padding(0, 0, Sc(12), Sc(12)) };
        var body = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Theme.Panel, Margin = Padding.Empty, Dock = DockStyle.Top,
        };
        card.Controls.Add(body);
        body.SizeChanged += (_, _) => card.Height = body.Height + card.Padding.Vertical;
        Label L1(string text, float px, int weight, Color color) => new()
        {
            Text = text, AutoSize = true, BackColor = Color.Transparent, ForeColor = color, Font = Theme.UiFont(px, weight),
            Margin = new Padding(0, 0, 0, Sc(6)), MaximumSize = new Size(card.Width - Sc(36), 0),
        };
        body.Controls.Add(L1(L.T("ОФИЦИАЛЬНО · STEAM · ", "OFFICIAL · STEAM · ") + post.Date.ToString("d MMMM yyyy", L.Ru ? System.Globalization.CultureInfo.GetCultureInfo("ru-RU") : System.Globalization.CultureInfo.InvariantCulture).ToUpperInvariant(),
                             11, 600, Theme.Faint));
        body.Controls.Add(L1(post.Title, 16, 700, Theme.Text));
        // Анонсы бывают огромными — показываем начало, целиком — в Steam.
        var text = post.Text.Length > 1400 ? post.Text[..1400].TrimEnd() + "…" : post.Text;
        if (text.Length > 0) body.Controls.Add(L1(text, 13, 400, Theme.Muted));
        var link = new LinkLabel
        {
            Text = L.T("Читать целиком в Steam", "Read in full on Steam"), AutoSize = true, LinkColor = Theme.Accent, ActiveLinkColor = Theme.Accent,
            VisitedLinkColor = Theme.Accent, LinkBehavior = LinkBehavior.HoverUnderline, Font = Theme.UiFont(12, 500), BackColor = Color.Transparent,
            Margin = new Padding(0, Sc(2), 0, 0),
        };
        link.LinkClicked += (_, _) => { if (post.Url.StartsWith("https://")) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(post.Url) { UseShellExecute = true }); };
        body.Controls.Add(link);
        return card;
    }
}
