namespace WardogsOverlay;

/// <summary>
/// На что ушли деньги, когда баланс у продавца упал. Ищем по всему каталогу то, что стоит ровно
/// столько (цена, открытие, открытие с покупкой, несколько пачек патронов или гранат), и из подходящих
/// берём тот предмет, чьё название сейчас на экране (продавец показывает выбранный предмет крупно).
/// Не получилось однозначно — показываем пару вариантов, а не «что-то купил».
/// С Сезона 3 у продавца бывают скидки (догоняющая для низких уровней и для отстающей фракции) — тогда точной
/// цены нет, и предмет принимаем, только если его название или картинка над ценником на экране.
/// </summary>
static class PurchaseMatcher
{
    sealed record Option(string What, int Cost, string Kind, string NameRu, string NameEn, int Priority);

    /// <summary>Самая большая скидка продавца, которую ещё считаем правдоподобной.</summary>
    const double MaxDiscount = 0.5;

    public static (string What, string Kind) Attribute(GameData? data, int spent, Weapon? w, List<Attachment> now,
                                                       string? prevWeapon, List<Attachment> prevAtts, IReadOnlyList<string> screen,
                                                       Func<List<DbItem>, DbItem?>? byIcon = null, bool limited = false)
    {
        string open = L.T("открытие: ", "unlock: ");
        var options = new List<Option>();
        void Add(string ru, string en, int price, Unlock? unlock, string kind, int priority, int maxPacks = 1, string? unit = null)
        {
            string name = L.Ru ? ru : en;
            if (price > 0)
                for (int k = 1; k <= maxPacks; k++)
                    options.Add(new Option(k == 1 ? name : $"{name} ×{k}{unit}", price * k, kind, ru, en, priority));
            if (unlock is { Cash: > 0 } u)
            {
                options.Add(new Option(open + name, u.Cash, "unlock", ru, en, priority));
                if (price > 0) options.Add(new Option(open + name, u.Cash + price, "unlock", ru, en, priority));
            }
        }

        // Самое вероятное — то, что на экране: текущая пушка и её обвесы.
        if (w != null) Add(L.WeaponRu(w.Name), w.Name, w.Price, w.Unlock, "weapon", 3);
        foreach (var a in now) Add(L.AttachmentRu(a.Name), a.Name, a.ShopPrice, a.Unlock, "attachments", 3);
        if (data != null)
        {
            // Патроны к текущей пушке — частая мелкая покупка.
            if (w != null)
                foreach (var am in data.Items.Where(i => i.Tab == "ammunition" && i.CalibreKey == w.CalibreKey && i.Price > 0 && i.InVendor && !(limited && Season3.LimitedExcludes(i))))
                    Add(L.Name(am), am.Name, am.Price!.Value, null, "other", 2, maxPacks: 10, unit: L.T(" пачки", " packs"));
            // Весь каталог продавца — по одной штуке (иначе пачки разных патронов дают десятки совпадений).
            // В режиме Limited часть каталога не продаётся (HP/AP, броня 3–4, ударная техника…) — не предлагаем её.
            foreach (var it in data.Items.Where(i => (i.InVendor || i.Unlock is { Cash: > 0 }) && !(limited && Season3.LimitedExcludes(i))))
            {
                string kind = it.Tab switch { "weapons" => "weapon", "attachments" => "attachments", _ => "other" };
                Add(L.Name(it), it.Name, it.Price ?? 0, it.Unlock, kind, 1);
            }
        }

        // Скидка продавца: дешевле каталожной цены (не больше чем вдвое), и это подтверждает экран —
        // название предмета или (если разрешено) картинка над ценником на потраченную сумму.
        (string What, string Kind)? Discounted(bool icon)
        {
            var named = options.Where(o => o.Kind != "unlock" && o.Cost > spent && spent >= o.Cost * (1 - MaxDiscount))
                               .Select(o => (o, s: ScreenScore(o, screen))).Where(t => t.s >= 0.8)
                               .OrderByDescending(t => t.s).ThenByDescending(t => t.o.Priority).ThenBy(t => t.o.Cost - spent).ToList();
            if (named.Count > 0) return (WithDiscount(named[0].o.What, named[0].o.Cost, spent), named[0].o.Kind);
            if (!icon || byIcon == null || data == null) return null;
            var cands = data.Items.Where(i => i.Price > spent && spent >= i.Price * (1 - MaxDiscount) && i.Icon.Length > 0 && i.InVendor
                                              && !(limited && Season3.LimitedExcludes(i))).ToList();
            return cands.Count > 0 && byIcon(cands) is { } item
                ? (WithDiscount(L.Name(item), item.Price!.Value, spent), item.Tab switch { "weapons" => "weapon", "attachments" => "attachments", _ => "other" })
                : null;
        }

        var hits = options.Where(o => o.Cost == spent).ToList();
        if (hits.Count == 0)
        {
            // Два обвеса разом (быстрые покупки между двумя показаниями баланса).
            var atts = options.Where(o => o.Kind == "attachments" && o.Priority == 3).ToList();
            for (int i = 0; i < atts.Count; i++)
                for (int j = i + 1; j < atts.Count; j++)
                    if (atts[i].Cost + atts[j].Cost == spent) return ($"{atts[i].What}, {atts[j].What}", "attachments");
            if (Discounted(icon: true) is { } d) return d;
            var added = w != null && prevWeapon == w.Slug ? now.Where(a => prevAtts.All(x => x.Slug != a.Slug)).Select(L.Name).ToList() : [];
            return added.Count > 0 ? (string.Join(", ", added), "attachments")
                                   : (L.T("не нашёл в каталоге ничего ровно за эту сумму", "nothing in the catalogue costs exactly this"), "other");
        }

        // Название подходящего предмета на экране — это он.
        var onScreen = hits.Select(o => (o, s: ScreenScore(o, screen))).Where(t => t.s >= 0.8).OrderByDescending(t => t.s).ThenByDescending(t => t.o.Priority).ToList();
        if (onScreen.Count > 0) return (onScreen[0].o.What, onScreen[0].o.Kind);
        // Со скидкой сумма часто совпадает с полной ценой чего-то другого — название на экране важнее такого совпадения.
        if (Discounted(icon: false) is { } byName) return byName;

        // Ценник ровно на эту сумму виден на экране — сравниваем картинку над ним с иконками кандидатов.
        if (byIcon != null && data != null)
        {
            var cands = data.Items.Where(i => i.Price == spent && i.Icon.Length > 0 && i.InVendor && !(limited && Season3.LimitedExcludes(i))).ToList();
            if (cands.Count > 0 && byIcon(cands) is { } item)
                return (L.Name(item), item.Tab switch { "weapons" => "weapon", "attachments" => "attachments", _ => "other" });
        }

        // На экране не нашли: сначала то, что связано с текущей пушкой, потом весь каталог.
        int top = hits.Max(o => o.Priority);
        var best = hits.Where(o => o.Priority == top).DistinctBy(o => o.What).ToList();
        // Только по цене в каталоге, без подтверждения на экране — честно пишем «вероятно».
        if (best.Count == 1) return (top == 1 ? L.T("вероятно: ", "probably: ") + best[0].What : best[0].What, best[0].Kind);
        string kindAll = best.Select(o => o.Kind).Distinct().Count() == 1 ? best[0].Kind : "other";
        var shown = best.Take(3).Select(o => o.What).ToList();
        return (L.T("одно из: ", "one of: ") + string.Join(" · ", shown) + (best.Count > 3 ? L.T($" и ещё {best.Count - 3}", $" and {best.Count - 3} more") : ""), kindAll);
    }

    /// <summary>«Бронежилет 2-го уровня (скидка 20%)».</summary>
    static string WithDiscount(string what, int price, int spent)
    {
        int pct = (int)Math.Round(100.0 * (price - spent) / price);
        return pct > 0 ? what + L.T($" (скидка {pct}%)", $" ({pct}% off)") : what;
    }

    /// <summary>Насколько название предмета похоже на строку с экрана (строки и пары соседних строк).</summary>
    static double ScreenScore(Option o, IReadOnlyList<string> screen)
    {
        if (screen.Count == 0) return 0;
        var names = new[] { TextUtil.NormRu(o.NameRu), TextUtil.NormRu(o.NameEn) }.Where(n => n.Length >= 4).Distinct().ToList();
        double best = 0;
        for (int i = 0; i < screen.Count; i++)
        {
            foreach (var text in new[] { screen[i], i + 1 < screen.Count ? screen[i] + " " + screen[i + 1] : null })
            {
                if (text == null) continue;
                var t = TextUtil.NormRu(text);
                if (t.Length < 4) continue;
                foreach (var n in names)
                {
                    double s = TextUtil.Similarity(t, n);
                    if (t.Length >= 6 && (t.Contains(n) || n.Contains(t) && t.Length >= n.Length * 0.7)) s = Math.Max(s, 0.95);
                    best = Math.Max(best, s);
                }
            }
        }
        return best;
    }
}
